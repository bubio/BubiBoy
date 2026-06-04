namespace BubiBoy.App

open BubiBoy.Core
open ControllerInput

module ControllerInputAdapter =
    let joypadButtonForControl control =
        match control with
        | GamepadControl.DPadRight -> Some Joypad.Right
        | GamepadControl.DPadLeft -> Some Joypad.Left
        | GamepadControl.DPadUp -> Some Joypad.Up
        | GamepadControl.DPadDown -> Some Joypad.Down
        | GamepadControl.South -> Some Joypad.A
        | GamepadControl.East -> Some Joypad.B
        | GamepadControl.Select -> Some Joypad.Select
        | GamepadControl.Start -> Some Joypad.Start
        | _ -> None

    let joypadButtonsForSnapshot (snapshot: GamepadSnapshot) =
        snapshot.Pressed
        |> Seq.choose joypadButtonForControl
        |> Set.ofSeq
