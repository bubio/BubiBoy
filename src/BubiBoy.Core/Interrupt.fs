namespace BubiBoy.Core

module Interrupt =
    [<Literal>]
    let VBlankBit = 0x01uy

    [<Literal>]
    let LcdStatBit = 0x02uy

    [<Literal>]
    let TimerBit = 0x04uy

    [<Literal>]
    let SerialBit = 0x08uy

    [<Literal>]
    let JoypadBit = 0x10uy

    let request (flag: byte) (interruptFlags: byte) =
        interruptFlags ||| flag
