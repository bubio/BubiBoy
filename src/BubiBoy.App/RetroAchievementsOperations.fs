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

type internal RaOperationDecision =
    | OperationAllowed
    | OperationDenied of message: string

module internal RetroAchievementsOperations =
    let private pauseDelayMessage framesRemaining =
        let seconds =
            Math.Ceiling(
                float framesRemaining * float Hardware.CyclesPerFrame
                / float Hardware.DmgClockHz
            )
            |> int
            |> max 1

        $"RetroAchievements requires {seconds} more second(s) before pausing."

    let evaluateStatus status canPause operation =
        match status, operation with
        | Active, Pause ->
            match canPause () with
            | PauseAllowed -> OperationAllowed
            | PauseDenied framesRemaining -> OperationDenied(pauseDelayMessage framesRemaining)
        | _ -> OperationAllowed

    let evaluateSnapshot snapshot canPause operation =
        match snapshot.Status, snapshot.HardcoreEnabled, operation with
        | Active, true, LoadState ->
            OperationDenied "Loading save states is unavailable while RetroAchievements Hardcore Mode is active."
        | Active, _, Pause ->
            match canPause () with
            | PauseAllowed -> OperationAllowed
            | PauseDenied framesRemaining -> OperationDenied(pauseDelayMessage framesRemaining)
        | _ -> OperationAllowed

    let evaluate (client: RaClient option) operation =
        match client with
        | Some activeClient -> evaluateSnapshot activeClient.Snapshot activeClient.CanPause operation
        | None -> OperationAllowed
