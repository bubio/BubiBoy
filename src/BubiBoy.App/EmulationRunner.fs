namespace BubiBoy.App

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Avalonia.Threading
open BubiBoy.Audio
open BubiBoy.Core
open BubiBoy.RetroAchievements

type DequeuedFrame =
    { PendingFrame: int
      OverwrittenFrames: int64
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
        audioBufferFallbackTargetFrames: int,
        timeProvider: TimeProvider,
        retroAchievements: RaClient option
    ) =
    let pendingFrame = LatestFrameMailbox<Emulator.FrameResult>()

    let audioPacing =
        AudioPacing(audioBufferTargetFrames, audioBufferFallbackTargetFrames, timeProvider)

    let mutable emulationLoop: CancellationTokenSource option = None
    let mutable emulationTask: Task option = None

    member _.ClearFrames() = pendingFrame.Clear()

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
        let coreMilliseconds = stopwatch.Elapsed.TotalMilliseconds

        retroAchievements
        |> Option.iter (fun client ->
            try
                client.ProcessFrame result.Session
            with ex ->
                Debug.WriteLine $"RetroAchievements frame processing failed: {ex}"
                client.SetOffline "RetroAchievements frame processing failed.")

        stopwatch.Stop()
        let frameMilliseconds = stopwatch.Elapsed.TotalMilliseconds
        let retroAchievementsMilliseconds = frameMilliseconds - coreMilliseconds
        performanceCounters.RecordFrameTime frameMilliseconds
        performanceCounters.RecordEmulatedFrame()
        let writeResult = audioOutput.Enqueue(applyVolume result.AudioSamples)
        let diagnosticsAfter = audioOutput.Diagnostics()
        let frame = traceCounters.NextGeneratedFrame()

        PerfTrace.writeFrame
            perfTrace
            frame
            frameMilliseconds
            coreMilliseconds
            retroAchievementsMilliseconds
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

        pendingFrame.Publish result

        result

    member this.Start
        (getSession: unit -> Emulator.Session option, setSession: Emulator.Session -> unit, requestStop: unit -> unit)
        =
        let cts = new CancellationTokenSource()
        let token = cts.Token
        emulationLoop <- Some cts
        audioPacing.Reset(audioOutput.Diagnostics().UnderrunFrames)

        let task =
            Task.Run(
                (fun () ->
                    while not token.IsCancellationRequested do
                        match getSession () with
                        | None -> Thread.Sleep 1
                        | Some session ->
                            let diagnostics = audioOutput.Diagnostics()

                            if diagnostics.IsRunning && not token.IsCancellationRequested then
                                let targetFrames = audioPacing.Update diagnostics.UnderrunFrames

                                if diagnostics.BufferedFrames < targetFrames then
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
        let frame, pending, overwritten = pendingFrame.Take()

        { PendingFrame = pending
          OverwrittenFrames = overwritten
          Frame = frame }
