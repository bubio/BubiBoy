namespace BubiBoy.App

open System
open System.Diagnostics
open System.IO

module PerfTrace =
    type Trace =
        { Writer: StreamWriter
          DisplayWriter: StreamWriter
          Gate: obj
          Stopwatch: Stopwatch }

    let createFromEnvironment () =
        let path = Environment.GetEnvironmentVariable("BUBIBOY_PERF_LOG")

        if String.IsNullOrWhiteSpace path then
            None
        else
            try
                let writer = new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                writer.WriteLine("timeMs,frame,frameMs,steps,cycles,pc,stop,acceptedAudio,enqueueDropped,bufferBefore,bufferAfter,underrunAfter,droppedAfter,gc0,gc1,gc2")
                writer.Flush()
                let displayPath = Path.ChangeExtension(path, ".display.csv")
                let displayWriter = new StreamWriter(File.Open(displayPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                displayWriter.WriteLine("timeMs,tick,displayMs,tickDeltaMs,displayedFrame,queueBefore,queueAfter,bufferedAudio,underrun,dropped,gc0,gc1,gc2")
                displayWriter.Flush()

                Some
                    { Writer = writer
                      DisplayWriter = displayWriter
                      Gate = obj ()
                      Stopwatch = Stopwatch.StartNew() }
            with ex ->
                eprintfn $"Could not create BUBIBOY_PERF_LOG '{path}': {ex.Message}"
                None

    let writeFrame trace frame frameMs steps cycles pc stop acceptedAudio enqueueDropped bufferBefore bufferAfter underrunAfter droppedAfter =
        match trace with
        | None -> ()
        | Some trace ->
            lock trace.Gate (fun () ->
                trace.Writer.WriteLine(
                    $"{trace.Stopwatch.Elapsed.TotalMilliseconds:F3},{frame},{frameMs:F3},{steps},{cycles},0x{pc:X4},{stop},{acceptedAudio},{enqueueDropped},{bufferBefore},{bufferAfter},{underrunAfter},{droppedAfter},{GC.CollectionCount 0},{GC.CollectionCount 1},{GC.CollectionCount 2}"
                )
                trace.Writer.Flush())

    let writeDisplay trace tick displayMs tickDeltaMs displayedFrame queueBefore queueAfter bufferedAudio underrun dropped =
        match trace with
        | None -> ()
        | Some trace ->
            lock trace.Gate (fun () ->
                trace.DisplayWriter.WriteLine(
                    $"{trace.Stopwatch.Elapsed.TotalMilliseconds:F3},{tick},{displayMs:F3},{tickDeltaMs:F3},{displayedFrame},{queueBefore},{queueAfter},{bufferedAudio},{underrun},{dropped},{GC.CollectionCount 0},{GC.CollectionCount 1},{GC.CollectionCount 2}"
                )
                trace.DisplayWriter.Flush())

    let close trace =
        match trace with
        | None -> ()
        | Some trace ->
            lock trace.Gate (fun () ->
                trace.Writer.Dispose()
                trace.DisplayWriter.Dispose())
