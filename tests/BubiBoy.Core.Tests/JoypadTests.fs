module BubiBoy.Core.Tests.JoypadTests

open BubiBoy.Core
open Xunit

[<Fact>]
let ``writeP1 selects action buttons when bit five is clear`` () =
    let state = Joypad.writeP1 0x10uy Joypad.initial

    Assert.True(state.SelectAction)
    Assert.False(state.SelectDirection)
    Assert.Equal(0xDFuy, Joypad.readP1 state)

[<Fact>]
let ``readP1 clears low bits for selected pressed action buttons`` () =
    let state =
        Joypad.initial
        |> Joypad.writeP1 0x10uy
        |> Joypad.setButton Joypad.A true
        |> Joypad.setButton Joypad.Start true

    Assert.Equal(0xD6uy, Joypad.readP1 state)

[<Fact>]
let ``readP1 clears low bits for selected pressed direction buttons`` () =
    let state =
        Joypad.initial
        |> Joypad.writeP1 0x20uy
        |> Joypad.setButton Joypad.Left true
        |> Joypad.setButton Joypad.Up true

    Assert.Equal(0xE9uy, Joypad.readP1 state)
