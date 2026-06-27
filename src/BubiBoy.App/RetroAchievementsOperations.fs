namespace BubiBoy.App

open System
open BubiBoy.Core
open BubiBoy.RetroAchievements

type internal RaControlledOperation =
    | Pause
    | SaveState
    | LoadState
    | Reset
    | ChangeGame
    | Rewind
    | SlowMotion
    | FrameAdvance
    | Cheats
    | InputPlayback
    | Debugger

type internal RaOperationDecision =
    | OperationAllowed
    | OperationDenied of message: string

module internal RetroAchievementsOperations =
    type private OperationPolicy =
        | Allowed
        | PauseControlled
        | HardcoreBlocked of description: string

    let private pauseDelayMessage framesRemaining =
        let seconds =
            Math.Ceiling(
                float framesRemaining * float Hardware.CyclesPerFrame
                / float Hardware.DmgClockHz
            )
            |> int
            |> max 1

        $"RetroAchievements requires {seconds} more second(s) before pausing."

    let private policy operation =
        match operation with
        | Pause -> PauseControlled
        | LoadState -> HardcoreBlocked "Loading save states"
        | Rewind -> HardcoreBlocked "Rewind"
        | SlowMotion -> HardcoreBlocked "Slow motion"
        | FrameAdvance -> HardcoreBlocked "Frame advance"
        | Cheats -> HardcoreBlocked "Cheats"
        | InputPlayback -> HardcoreBlocked "Input playback"
        | Debugger -> HardcoreBlocked "Debugger access"
        | SaveState
        | Reset
        | ChangeGame -> Allowed

    let private evaluateActiveSession hardcoreEnabled canPause operation =
        match policy operation with
        | Allowed -> OperationAllowed
        | PauseControlled ->
            match canPause () with
            | PauseAllowed -> OperationAllowed
            | PauseDenied framesRemaining -> OperationDenied(pauseDelayMessage framesRemaining)
        | HardcoreBlocked description when hardcoreEnabled ->
            OperationDenied $"{description} is unavailable while RetroAchievements Hardcore Mode is active."
        | HardcoreBlocked _ -> OperationAllowed

    let evaluateStatus status canPause operation =
        match status with
        | Active -> evaluateActiveSession false canPause operation
        | _ -> OperationAllowed

    let evaluateSnapshot snapshot canPause operation =
        match snapshot.Status with
        | Active -> evaluateActiveSession snapshot.HardcoreEnabled canPause operation
        | _ -> OperationAllowed

    let evaluate (client: RaClient option) operation =
        match client with
        | Some activeClient -> evaluateSnapshot activeClient.Snapshot activeClient.CanPause operation
        | None -> OperationAllowed

    let isAllowed client operation =
        match evaluate client operation with
        | OperationAllowed -> true
        | OperationDenied _ -> false
