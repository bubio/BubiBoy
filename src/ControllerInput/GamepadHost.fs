namespace ControllerInput

open System
open System.Collections.Generic

type GamepadHost =
    inherit IDisposable

    abstract Poll: unit -> IReadOnlyList<GamepadSnapshot>
