namespace BubiBoy.App

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Avalonia.Threading
open BubiBoy.Audio
open BubiBoy.Core
open BubiBoy.RetroAchievements

type DequeuedFrame =
    { QueueBefore: int
      QueueAfter: int
      Frame: Emulator.FrameResult option }

type EmulationRunner
    (
        audioOutput: AudioHost.AudioDevice,
        applyVolume: Apu.Sample[] -> Apu.Sample[],
        applyInput: Emulator.Session -> Emulator.Session,
        performanceCounters: RuntimePerformanceCounters,
        traceCounters: RuntimeTraceCounters,
        perfTrace: PerfTrace.Trace option,
        maxStepsPerFrame: int,
        audioBufferTargetFrames: int,
        retroAchievements: RaClient option
    ) =
    let gate = obj ()
    let pendingFrames = Queue<Emulator.FrameResult>()
    let mutable emulationLoop: CancellationTokenSource option = None
    let mutable emulationTask: Task option = None

    member _.ClearFrames() =
        lock gate (fun () -> pendingFrames.Clear())

    member _.StopLoop() =
        match emulationLoop with
        | Some cts ->
            try
                cts.Cancel()
            with :? System.ObjectDisposedException ->
                ()

            emulationTask
            |> Option.iter (fun task ->
                try
                    task.Wait()
                with
                | :? AggregateException as ex ->
                    ex.Flatten().InnerExceptions
                    |> Seq.filter (fun inner ->
                        not (inner :? OperationCanceledException || inner :? TaskCanceledException))
                    |> Seq.iter (fun inner -> Debug.WriteLine $"Emulation loop failed: {inner}")
                | :? OperationCanceledException -> ())

            emulationLoop <- None
            emulationTask <- None
        | None -> ()

    member _.EnqueueFrameAudio(session: Emulator.Session) =
        let diagnosticsBefore = audioOutput.Diagnostics()
        let beforeSteps = session.Steps
        let beforeCycles = session.TotalCycles
        let stopwatch = Stopwatch.StartNew()
        let result = Emulator.runFrame maxStepsPerFrame session

        retroAchievements
        |> Option.iter (fun client ->
            try
                client.ProcessFrame result.Session
            with ex ->
                Debug.WriteLine $"RetroAchievements frame processing failed: {ex}"
                client.SetOffline "RetroAchievements frame processing failed.")

        stopwatch.Stop()
        performanceCounters.RecordEmulatedFrame()
        let writeResult = audioOutput.Enqueue(applyVolume result.AudioSamples)
        let diagnosticsAfter = audioOutput.Diagnostics()
        let frame = traceCounters.NextGeneratedFrame()

        PerfTrace.writeFrame
            perfTrace
            frame
            stopwatch.Elapsed.TotalMilliseconds
            (result.Session.Steps - beforeSteps)
            (result.Session.TotalCycles - beforeCycles)
            result.Session.Cpu.Registers.PC
            result.StopReason
            writeResult.AcceptedFrames
            writeResult.DroppedFrames
            diagnosticsBefore.BufferedFrames
            diagnosticsAfter.BufferedFrames
            diagnosticsAfter.UnderrunFrames
            diagnosticsAfter.DroppedFrames

        lock gate (fun () ->
            pendingFrames.Enqueue result

            while pendingFrames.Count > 30 do
                pendingFrames.Dequeue() |> ignore)

        result

    member this.Start
        (getSession: unit -> Emulator.Session option, setSession: Emulator.Session -> unit, requestStop: unit -> unit)
        =
        let cts = new CancellationTokenSource()
        let token = cts.Token
        emulationLoop <- Some cts

        let task =
            Task.Run(
                (fun () ->
                    while not token.IsCancellationRequested do
                        match getSession () with
                        | None -> Thread.Sleep 1
                        | Some session ->
                            let diagnostics = audioOutput.Diagnostics()

                            if diagnostics.IsRunning && not token.IsCancellationRequested then
                                if diagnostics.BufferedFrames < audioBufferTargetFrames then
                                    let result = this.EnqueueFrameAudio(applyInput session)
                                    setSession result.Session

                                    if result.StopReason <> Emulator.FrameCompleted then
                                        token.ThrowIfCancellationRequested()
                                        cts.Cancel()
                                        Dispatcher.UIThread.Post requestStop
                                else
                                    Thread.Sleep 1
                            elif not token.IsCancellationRequested then
                                Thread.Sleep 1

                            if token.IsCancellationRequested then
                                token.ThrowIfCancellationRequested()),
                token
            )

        emulationTask <- Some task

        task.ContinueWith(fun (_: Task) -> cts.Dispose()) |> ignore

    member _.DequeueFrame() =
        lock gate (fun () ->
            let queueBefore = pendingFrames.Count

            if pendingFrames.Count > 0 then
                let frame = pendingFrames.Dequeue()

                { QueueBefore = queueBefore
                  QueueAfter = pendingFrames.Count
                  Frame = Some frame }
            else
                { QueueBefore = queueBefore
                  QueueAfter = 0
                  Frame = None })
