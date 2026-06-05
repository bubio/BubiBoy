namespace BubiBoy.App

open System
open System.Threading
open BubiBoy.Audio

type RuntimePerformanceCounters() =
    let gate = obj ()
    let mutable displayedFrames = 0
    let mutable emulatedFrames = 0
    let mutable measuredDisplayFps = 0.0
    let mutable measuredEmulationFps = 0.0
    let mutable lastFrameMilliseconds = 0.0
    let mutable lastFpsSample = DateTime.UtcNow

    member _.RecordEmulatedFrame() =
        lock gate (fun () -> emulatedFrames <- emulatedFrames + 1)

    member _.Reset() =
        lock gate (fun () ->
            displayedFrames <- 0
            emulatedFrames <- 0
            measuredDisplayFps <- 0.0
            measuredEmulationFps <- 0.0
            lastFrameMilliseconds <- 0.0
            lastFpsSample <- DateTime.UtcNow)

    member _.RecordDisplayedFrame() =
        lock gate (fun () ->
            displayedFrames <- displayedFrames + 1

            let now = DateTime.UtcNow
            let elapsed = now - lastFpsSample

            if elapsed.TotalSeconds >= 1.0 then
                measuredDisplayFps <- float displayedFrames / elapsed.TotalSeconds
                measuredEmulationFps <- float emulatedFrames / elapsed.TotalSeconds
                displayedFrames <- 0
                emulatedFrames <- 0
                lastFpsSample <- now)

    member _.RecordFrameTime(elapsedMilliseconds) =
        lock gate (fun () -> lastFrameMilliseconds <- elapsedMilliseconds)

    member _.Snapshot() =
        lock gate (fun () -> measuredDisplayFps, measuredEmulationFps, lastFrameMilliseconds)

    member this.FormatDiagnostics(diagnostics: AudioHost.AudioDiagnostics) =
        let displayFps, emulationFps, frameMilliseconds = this.Snapshot()
        $"{DebugDisplay.formatPerformance displayFps emulationFps frameMilliseconds}\n{DebugDisplay.formatAudioDiagnostics diagnostics}"

type RuntimeTraceCounters() =
    let mutable generatedFrameCounter = 0
    let mutable displayTickCounter = 0
    let mutable displayedFrameCounter = 0
    let mutable lastDisplayTickMs = 0.0

    member _.NextGeneratedFrame() =
        Interlocked.Increment(&generatedFrameCounter)

    member _.NextDisplayTick(trace: PerfTrace.Trace option) =
        let tick = displayTickCounter + 1
        displayTickCounter <- tick

        let tickNow =
            match trace with
            | None -> 0.0
            | Some trace -> trace.Stopwatch.Elapsed.TotalMilliseconds

        let tickDelta =
            if lastDisplayTickMs = 0.0 then
                0.0
            else
                tickNow - lastDisplayTickMs

        lastDisplayTickMs <- tickNow
        tick, tickDelta

    member _.RecordDisplayedFrame() =
        displayedFrameCounter <- displayedFrameCounter + 1
        displayedFrameCounter

    member _.DisplayedFrameCount =
        displayedFrameCounter
