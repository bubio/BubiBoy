namespace BubiBoy.App

open System

type internal LatestFrameMailbox<'T>() =
    let gate = obj ()
    let mutable pending: 'T option = None
    let mutable overwrittenFrames = 0L

    member _.Publish(value: 'T) =
        lock gate (fun () ->
            if pending.IsSome then
                overwrittenFrames <- overwrittenFrames + 1L

            pending <- Some value)

    member _.Take() =
        lock gate (fun () ->
            let value = pending
            let pendingCount = if pending.IsSome then 1 else 0
            pending <- None
            value, pendingCount, overwrittenFrames)

    member _.Clear() =
        lock gate (fun () ->
            pending <- None
            overwrittenFrames <- 0L)

    member _.Snapshot() =
        lock gate (fun () -> (if pending.IsSome then 1 else 0), overwrittenFrames)

type internal AudioPacing(initialTargetFrames: int, fallbackTargetFrames: int, timeProvider: TimeProvider) =
    do
        if initialTargetFrames <= 0 then
            invalidArg (nameof initialTargetFrames) "Initial audio target must be positive."

        if fallbackTargetFrames < initialTargetFrames then
            invalidArg
                (nameof fallbackTargetFrames)
                "Fallback audio target must not be smaller than the initial target."

    let mutable startedAt = timeProvider.GetTimestamp()
    let mutable baselineUnderruns = 0L
    let mutable targetFrames = initialTargetFrames

    member _.Reset(underrunFrames: int64) =
        startedAt <- timeProvider.GetTimestamp()
        baselineUnderruns <- underrunFrames
        targetFrames <- initialTargetFrames

    member _.Update(underrunFrames: int64) =
        if
            targetFrames = initialTargetFrames
            && underrunFrames > baselineUnderruns
            && timeProvider.GetElapsedTime(startedAt) >= TimeSpan.FromSeconds 1.0
        then
            targetFrames <- fallbackTargetFrames

        targetFrames

type internal IAnimationFrameScheduler =
    abstract TryRequestFrame: Action<TimeSpan> -> bool

type internal FramePresentationCoordinator(scheduler: IAnimationFrameScheduler, present: unit -> unit) =
    let mutable running = false
    let mutable animationPending = false
    let mutable generation = 0L

    let rec requestAnimationFrame () =
        if running && not animationPending then
            let requestedGeneration = generation

            animationPending <-
                scheduler.TryRequestFrame(
                    Action<TimeSpan>(fun timestamp -> onAnimationFrame requestedGeneration timestamp)
                )

        animationPending

    and onAnimationFrame requestedGeneration (_: TimeSpan) =
        if requestedGeneration = generation then
            animationPending <- false

            if running then
                present ()
                requestAnimationFrame () |> ignore

    member _.Start() =
        if not running then
            generation <- generation + 1L
            running <- true
            requestAnimationFrame () |> ignore

    member _.FallbackTick() =
        if running && not animationPending then
            present ()
            requestAnimationFrame () |> ignore

    member _.NeedsFallback = running && not animationPending

    member _.Stop() =
        running <- false
        generation <- generation + 1L
        animationPending <- false
