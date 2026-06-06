namespace BubiBoy.App

open System
open System.Diagnostics
open Avalonia.Threading
open BubiBoy.Audio
open BubiBoy.Core

/// Dependencies used by the UI frame display timer.
type FrameDisplayDependencies =
    { IsRunning: unit -> bool
      DequeueFrame: unit -> DequeuedFrame
      UpdateFrame: Emulator.FrameResult -> unit
      UpdateDiagnostics: unit -> unit
      AudioDiagnostics: unit -> AudioHost.AudioDiagnostics }

/// Drives UI frame presentation and display-side performance tracing.
type FrameDisplayTimer
    (
        dependencies: FrameDisplayDependencies,
        performanceCounters: RuntimePerformanceCounters,
        traceCounters: RuntimeTraceCounters,
        perfTrace: PerfTrace.Trace option
    ) =
    let timer =
        DispatcherTimer(
            Interval =
                TimeSpan.FromMilliseconds(
                    1000.0 * float Hardware.CyclesPerFrame / float Hardware.DmgClockHz
                )
        )

    do
        timer.Tick.Add(fun _ ->
            if dependencies.IsRunning() then
                let tick, tickDelta = traceCounters.NextDisplayTick(perfTrace)
                let stopwatch = Stopwatch.StartNew()
                let dequeued = dependencies.DequeueFrame()

                match dequeued.Frame with
                | Some result ->
                    traceCounters.RecordDisplayedFrame() |> ignore
                    performanceCounters.RecordDisplayedFrame()
                    dependencies.UpdateFrame result
                | None ->
                    dependencies.UpdateDiagnostics()

                stopwatch.Stop()
                let diagnostics = dependencies.AudioDiagnostics()

                PerfTrace.writeDisplay
                    perfTrace
                    tick
                    stopwatch.Elapsed.TotalMilliseconds
                    tickDelta
                    traceCounters.DisplayedFrameCount
                    dequeued.QueueBefore
                    dequeued.QueueAfter
                    diagnostics.BufferedFrames
                    diagnostics.UnderrunFrames
                    diagnostics.DroppedFrames)

    /// Starts display timer ticks.
    member _.Start() =
        timer.Start()

    /// Stops display timer ticks.
    member _.Stop() =
        timer.Stop()
