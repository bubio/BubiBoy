namespace BubiBoy.App

open System.Collections.Generic
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Avalonia.Threading
open BubiBoy.Audio
open BubiBoy.Core

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
        audioBufferTargetFrames: int
    ) =
    let gate = obj ()
    let pendingFrames = Queue<Emulator.FrameResult>()
    let mutable emulationLoop: CancellationTokenSource option = None

    member _.ClearFrames() =
        lock gate (fun () -> pendingFrames.Clear())

    member _.StopLoop() =
        match emulationLoop with
        | Some cts ->
            try
                cts.Cancel()
            with
            | :? System.ObjectDisposedException -> ()

            emulationLoop <- None
        | None -> ()

    member _.EnqueueFrameAudio(session: Emulator.Session) =
        let diagnosticsBefore = audioOutput.Diagnostics()
        let beforeSteps = session.Steps
        let beforeCycles = session.TotalCycles
        let stopwatch = Stopwatch.StartNew()
        let result = Emulator.runFrame maxStepsPerFrame session
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

    member this.FillAudioLead(token: CancellationToken, session: Emulator.Session, initialDiagnostics: AudioHost.AudioDiagnostics) =
        let stopwatch = Stopwatch.StartNew()
        let mutable current = session
        let mutable latest = None
        let mutable diagnostics = initialDiagnostics
        let mutable framesGenerated = 0
        let mutable keepGoing = diagnostics.IsRunning

        while
            keepGoing
            && not token.IsCancellationRequested
            && diagnostics.BufferedFrames < audioBufferTargetFrames do
            let result = this.EnqueueFrameAudio current
            current <- result.Session
            latest <- Some result
            framesGenerated <- framesGenerated + 1
            keepGoing <- result.StopReason = Emulator.FrameCompleted
            diagnostics <- audioOutput.Diagnostics()

        stopwatch.Stop()

        if framesGenerated > 0 then
            performanceCounters.RecordFrameTime(stopwatch.Elapsed.TotalMilliseconds / float framesGenerated)

        current, latest, keepGoing

    member this.PrimeAudioBuffer(getSession: unit -> Emulator.Session option, setSession: Emulator.Session -> unit) =
        match getSession() with
        | None -> ()
        | Some session ->
            let current, _, _ =
                this.FillAudioLead(CancellationToken.None, applyInput session, audioOutput.Diagnostics())

            setSession current

    member this.Start(getSession: unit -> Emulator.Session option, setSession: Emulator.Session -> unit, requestStop: unit -> unit) =
        let cts = new CancellationTokenSource()
        let token = cts.Token
        emulationLoop <- Some cts

        let task =
            Task.Run(
                (fun () ->
                    while not token.IsCancellationRequested do
                        match getSession() with
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
