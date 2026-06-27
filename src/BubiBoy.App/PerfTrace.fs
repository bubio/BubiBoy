namespace BubiBoy.App

open System
open System.Diagnostics
open System.IO

module PerfTrace =
    type Trace =
        { Writer: StreamWriter
          DisplayWriter: StreamWriter
          FrameGate: obj
          DisplayGate: obj
          Stopwatch: Stopwatch }

    let createFromEnvironment () =
        let path = Environment.GetEnvironmentVariable("BUBIBOY_PERF_LOG")

        if String.IsNullOrWhiteSpace path then
            None
        else
            try
                let writer =
                    new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))

                writer.WriteLine(
                    "timeMs,frame,frameMs,coreMs,raMs,steps,cycles,pc,stop,acceptedAudio,enqueueDropped,bufferBefore,bufferAfter,underrunAfter,droppedAfter,gc0,gc1,gc2"
                )

                writer.Flush()
                let displayPath = Path.ChangeExtension(path, ".display.csv")

                let displayWriter =
                    new StreamWriter(File.Open(displayPath, FileMode.Create, FileAccess.Write, FileShare.Read))

                displayWriter.WriteLine(
                    "timeMs,tick,displayMs,tickDeltaMs,displayedFrame,pendingFrame,overwrittenFrames,bufferedAudio,underrun,dropped,gc0,gc1,gc2"
                )

                displayWriter.Flush()

                Some
                    { Writer = writer
                      DisplayWriter = displayWriter
                      FrameGate = obj ()
                      DisplayGate = obj ()
                      Stopwatch = Stopwatch.StartNew() }
            with ex ->
                eprintfn $"Could not create BUBIBOY_PERF_LOG '{path}': {ex.Message}"
                None

    let writeFrame
        trace
        frame
        frameMs
        coreMs
        raMs
        steps
        cycles
        pc
        stop
        acceptedAudio
        enqueueDropped
        bufferBefore
        bufferAfter
        underrunAfter
        droppedAfter
        =
        match trace with
        | None -> ()
        | Some trace ->
            lock trace.FrameGate (fun () ->
                trace.Writer.WriteLine(
                    $"{trace.Stopwatch.Elapsed.TotalMilliseconds:F3},{frame},{frameMs:F3},{coreMs:F3},{raMs:F3},{steps},{cycles},0x{pc:X4},{stop},{acceptedAudio},{enqueueDropped},{bufferBefore},{bufferAfter},{underrunAfter},{droppedAfter},{GC.CollectionCount 0},{GC.CollectionCount 1},{GC.CollectionCount 2}"
                ))

    let writeDisplay
        trace
        tick
        displayMs
        tickDeltaMs
        displayedFrame
        pendingFrame
        overwrittenFrames
        bufferedAudio
        underrun
        dropped
        =
        match trace with
        | None -> ()
        | Some trace ->
            lock trace.DisplayGate (fun () ->
                trace.DisplayWriter.WriteLine(
                    $"{trace.Stopwatch.Elapsed.TotalMilliseconds:F3},{tick},{displayMs:F3},{tickDeltaMs:F3},{displayedFrame},{pendingFrame},{overwrittenFrames},{bufferedAudio},{underrun},{dropped},{GC.CollectionCount 0},{GC.CollectionCount 1},{GC.CollectionCount 2}"
                ))

    let close trace =
        match trace with
        | None -> ()
        | Some trace ->
            lock trace.FrameGate (fun () -> trace.Writer.Dispose())
            lock trace.DisplayGate (fun () -> trace.DisplayWriter.Dispose())
