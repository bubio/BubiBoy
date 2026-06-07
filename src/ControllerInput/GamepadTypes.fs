namespace ControllerInput

open System
open System.Collections.Generic

[<Struct>]
type GamepadId =
    val private rawValue: string

    new(value: string) =
        if String.IsNullOrWhiteSpace value then
            invalidArg (nameof value) "Gamepad id cannot be empty."

        { rawValue = value.Trim() }

    member this.Value = if isNull this.rawValue then String.Empty else this.rawValue

    override this.ToString() = this.Value

module GamepadId =
    let tryCreate value =
        try
            Some(GamepadId(value))
        with :? ArgumentException ->
            None

    let create value = GamepadId(value)

    let value (id: GamepadId) = id.Value

[<RequireQualifiedAccess>]
type GamepadControl =
    | DPadUp = 0
    | DPadDown = 1
    | DPadLeft = 2
    | DPadRight = 3
    | South = 4
    | East = 5
    | West = 6
    | North = 7
    | Start = 8
    | Select = 9
    | LeftShoulder = 10
    | RightShoulder = 11
    | LeftTrigger = 12
    | RightTrigger = 13
    | LeftStickUp = 14
    | LeftStickDown = 15
    | LeftStickLeft = 16
    | LeftStickRight = 17

module GamepadControl =
    let all =
        [ GamepadControl.DPadUp
          GamepadControl.DPadDown
          GamepadControl.DPadLeft
          GamepadControl.DPadRight
          GamepadControl.South
          GamepadControl.East
          GamepadControl.West
          GamepadControl.North
          GamepadControl.Start
          GamepadControl.Select
          GamepadControl.LeftShoulder
          GamepadControl.RightShoulder
          GamepadControl.LeftTrigger
          GamepadControl.RightTrigger
          GamepadControl.LeftStickUp
          GamepadControl.LeftStickDown
          GamepadControl.LeftStickLeft
          GamepadControl.LeftStickRight ]

    let storageName (control: GamepadControl) = control.ToString()

    let displayName control =
        match control with
        | GamepadControl.DPadUp -> "D-pad Up"
        | GamepadControl.DPadDown -> "D-pad Down"
        | GamepadControl.DPadLeft -> "D-pad Left"
        | GamepadControl.DPadRight -> "D-pad Right"
        | GamepadControl.South -> "A / ×"
        | GamepadControl.East -> "B / ○"
        | GamepadControl.West -> "X / □"
        | GamepadControl.North -> "Y / △"
        | GamepadControl.Start -> "Start"
        | GamepadControl.Select -> "Select"
        | GamepadControl.LeftShoulder -> "Left Shoulder"
        | GamepadControl.RightShoulder -> "Right Shoulder"
        | GamepadControl.LeftTrigger -> "Left Trigger"
        | GamepadControl.RightTrigger -> "Right Trigger"
        | GamepadControl.LeftStickUp -> "Left Stick Up"
        | GamepadControl.LeftStickDown -> "Left Stick Down"
        | GamepadControl.LeftStickLeft -> "Left Stick Left"
        | GamepadControl.LeftStickRight -> "Left Stick Right"
        | _ -> control.ToString()

    let tryParse (value: string) =
        let mutable control = Unchecked.defaultof<GamepadControl>

        if
            Enum.TryParse<GamepadControl>(value, true, &control)
            && (all |> List.contains control)
        then
            Some control
        else
            None

[<CLIMutable>]
type GamepadSnapshot =
    { Id: GamepadId
      Name: string
      Pressed: IReadOnlySet<GamepadControl> }

module GamepadSnapshot =
    let create id name (pressed: seq<GamepadControl>) =
        let safeName =
            if String.IsNullOrWhiteSpace name then
                "Unknown Controller"
            else
                name.Trim()

        { Id = id
          Name = safeName
          Pressed = HashSet<GamepadControl>(pressed) :> IReadOnlySet<GamepadControl> }
