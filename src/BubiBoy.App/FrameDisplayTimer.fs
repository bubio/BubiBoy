namespace BubiBoy.App

open System
open System.Diagnostics
open System.Threading
open Avalonia.Controls
open Avalonia.Threading
open BubiBoy.Audio
open BubiBoy.Core

/// Dependencies used by the UI frame display timer.
type internal FrameDisplayDependencies =
    { IsRunning: unit -> bool
      DequeueFrame: unit -> DequeuedFrame
      UpdateFrame: Emulator.FrameResult -> unit
      UpdateDiagnostics: unit -> unit
      AudioDiagnostics: unit -> AudioHost.AudioDiagnostics
      PumpServices: unit -> unit }

type internal TopLevelAnimationFrameScheduler(topLevel: TopLevel) =
    interface IAnimationFrameScheduler with
        member _.TryRequestFrame(callback) =
            try
                topLevel.RequestAnimationFrame callback
                true
            with
            | :? InvalidOperationException
            | :? ObjectDisposedException -> false

/// Drives UI frame presentation and display-side performance tracing.
type internal FrameDisplayTimer
    (
        dependencies: FrameDisplayDependencies,
        performanceCounters: RuntimePerformanceCounters,
        traceCounters: RuntimeTraceCounters,
        perfTrace: PerfTrace.Trace option,
        animationScheduler: IAnimationFrameScheduler
    ) =
    let serviceTimer = DispatcherTimer(Interval = TimeSpan.FromMilliseconds 100.0)
    let mutable fallbackPostPending = 0

    let present () =
        if dependencies.IsRunning() then
            let dequeued = dependencies.DequeueFrame()

            match dequeued.Frame with
            | Some result ->
                let tick, tickDelta = traceCounters.NextDisplayTick(perfTrace)
                let stopwatch = Stopwatch.StartNew()
                traceCounters.RecordDisplayedFrame() |> ignore
                performanceCounters.RecordDisplayedFrame()
                dependencies.UpdateFrame result
                stopwatch.Stop()
                let diagnostics = dependencies.AudioDiagnostics()

                PerfTrace.writeDisplay
                    perfTrace
                    tick
                    stopwatch.Elapsed.TotalMilliseconds
                    tickDelta
                    traceCounters.DisplayedFrameCount
                    dequeued.PendingFrame
                    dequeued.OverwrittenFrames
                    diagnostics.BufferedFrames
                    diagnostics.UnderrunFrames
                    diagnostics.DroppedFrames
            | None -> ()

    let coordinator = FramePresentationCoordinator(animationScheduler, present)

    let fallbackTimer =
        new Timer(
            (fun _ ->
                if Interlocked.CompareExchange(&fallbackPostPending, 1, 0) = 0 then
                    Dispatcher.UIThread.Post(fun () ->
                        Interlocked.Exchange(&fallbackPostPending, 0) |> ignore
                        coordinator.FallbackTick())),
            null,
            Timeout.Infinite,
            Timeout.Infinite
        )

    do serviceTimer.Tick.Add(fun _ -> dependencies.PumpServices())

    /// Starts display timer ticks.
    member _.Start() =
        serviceTimer.Start()
        coordinator.Start()
        fallbackTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds 8.0) |> ignore

    /// Stops display timer ticks.
    member _.Stop() =
        fallbackTimer.Change(Timeout.Infinite, Timeout.Infinite) |> ignore
        coordinator.Stop()
        serviceTimer.Stop()
