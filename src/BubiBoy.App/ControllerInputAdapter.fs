namespace BubiBoy.App

open BubiBoy.Core
open ControllerInput

module ControllerInputAdapter =
    let joypadButtonForControl mapping control =
        InputMapping.mapControllerControl mapping control

    let joypadButtonsForSnapshot mapping (snapshot: GamepadSnapshot) =
        snapshot.Pressed
        |> Seq.choose (joypadButtonForControl mapping)
        |> Set.ofSeq
