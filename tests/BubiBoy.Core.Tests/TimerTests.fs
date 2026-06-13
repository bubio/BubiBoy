module BubiBoy.Core.Tests.TimerTests

open BubiBoy.Core
open Xunit

let private registers div tima tma tac interruptFlags : Timer.Registers =
    { Div = div
      Tima = tima
      Tma = tma
      Tac = tac
      InterruptFlags = interruptFlags }

[<Fact>]
let ``tick advances divider high byte`` () =
    let registers = registers 0uy 0uy 0uy 0uy 0uy

    let result = Timer.tick 256 Timer.initial registers

    Assert.Equal(0x01uy, result.Registers.Div)
    Assert.Equal(256us, result.State.Divider)

[<Fact>]
let ``disabled timer does not increment TIMA`` () =
    let registers = registers 0uy 0x20uy 0x80uy 0x00uy 0uy

    let result = Timer.tick 4096 Timer.initial registers

    Assert.Equal(0x20uy, result.Registers.Tima)
    Assert.Equal(0uy, result.Registers.InterruptFlags)

[<Theory>]
[<InlineData(0x04uy, 1024)>]
[<InlineData(0x05uy, 16)>]
[<InlineData(0x06uy, 64)>]
[<InlineData(0x07uy, 256)>]
let ``enabled timer increments TIMA at selected period`` tac cycles =
    let registers = registers 0uy 0x10uy 0x80uy tac 0uy

    let result = Timer.tick cycles Timer.initial registers

    Assert.Equal(0x11uy, result.Registers.Tima)
    Assert.Equal(None, result.State.ReloadDelay)

[<Fact>]
let ``timer overflow reloads TMA and requests interrupt after four cycles`` () =
    let registers = registers 0uy 0xFFuy 0x77uy 0x05uy 0uy

    let overflowed = Timer.tick 16 Timer.initial registers
    let beforeReload = Timer.tick 3 overflowed.State overflowed.Registers
    let reloaded = Timer.tick 1 beforeReload.State beforeReload.Registers

    Assert.Equal(0uy, overflowed.Registers.Tima)
    Assert.Equal(0uy, beforeReload.Registers.Tima)
    Assert.Equal(0x77uy, reloaded.Registers.Tima)
    Assert.Equal(Interrupt.TimerBit, reloaded.Registers.InterruptFlags &&& Interrupt.TimerBit)

[<Fact>]
let ``resetting DIV on a high timer signal increments TIMA`` () =
    let state =
        { Timer.initial with
            Divider = 0x0008us }

    let result = Timer.resetDiv state (registers 0uy 0x20uy 0uy 0x05uy 0uy)

    Assert.Equal(0us, result.State.Divider)
    Assert.Equal(0x21uy, result.Registers.Tima)

[<Fact>]
let ``writing TIMA before pending reload cancels reload`` () =
    let overflowed =
        Timer.tick 16 Timer.initial (registers 0uy 0xFFuy 0x77uy 0x05uy 0uy)

    let written = Timer.writeTima 0x44uy overflowed.State overflowed.Registers
    let advanced = Timer.tick 4 written.State written.Registers

    Assert.Equal(0x44uy, advanced.Registers.Tima)
    Assert.Equal(0uy, advanced.Registers.InterruptFlags &&& Interrupt.TimerBit)

[<Fact>]
let ``disabling timer on a high selected divider bit increments TIMA`` () =
    let state =
        { Timer.initial with
            Divider = 0x0008us }

    let result = Timer.writeTac 0x00uy state (registers 0uy 0x20uy 0uy 0x05uy 0uy)

    Assert.Equal(0x21uy, result.Registers.Tima)
