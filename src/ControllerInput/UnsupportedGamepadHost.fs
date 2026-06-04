namespace ControllerInput

open System
open System.Collections.Generic

type UnsupportedGamepadHost(reason: string) =
    let reason =
        if String.IsNullOrWhiteSpace reason then
            "No gamepad backend is available in this build."
        else
            reason.Trim()

    member _.Reason = reason

    interface GamepadHost with
        member _.Poll() = Array.Empty<GamepadSnapshot>() :> IReadOnlyList<GamepadSnapshot>

    interface IDisposable with
        member _.Dispose() = ()
