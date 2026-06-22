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

    let evaluate (client: RaClient option) operation =
        match client with
        | Some activeClient -> evaluateStatus activeClient.Snapshot.Status activeClient.CanPause operation
        | None -> OperationAllowed
