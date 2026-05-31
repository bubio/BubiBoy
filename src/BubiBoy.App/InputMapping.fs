namespace BubiBoy.App

open Avalonia.Input
open BubiBoy.Core

module InputMapping =
    let allJoypadButtons =
        [ Joypad.Right; Joypad.Left; Joypad.Up; Joypad.Down
          Joypad.A; Joypad.B; Joypad.Select; Joypad.Start ]

    let mapKey key =
        match key with
        | Key.Z -> Some Joypad.A
        | Key.X -> Some Joypad.B
        | Key.Back -> Some Joypad.Select
        | Key.Enter -> Some Joypad.Start
        | Key.Right -> Some Joypad.Right
        | Key.Left -> Some Joypad.Left
        | Key.Up -> Some Joypad.Up
        | Key.Down -> Some Joypad.Down
        | _ -> None
