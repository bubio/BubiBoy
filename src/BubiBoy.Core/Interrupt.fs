namespace BubiBoy.Core

/// Provides interrupt flag constants and request operations.
module Interrupt =
    /// The interrupt flag for vertical blanking.
    [<Literal>]
    let VBlankBit = 0x01uy

    /// The interrupt flag for LCD status events.
    [<Literal>]
    let LcdStatBit = 0x02uy

    /// The interrupt flag for timer overflow.
    [<Literal>]
    let TimerBit = 0x04uy

    /// The interrupt flag for serial transfers.
    [<Literal>]
    let SerialBit = 0x08uy

    /// The interrupt flag for joypad transitions.
    [<Literal>]
    let JoypadBit = 0x10uy

    /// Sets an interrupt flag in the IF register value.
    let request (flag: byte) (interruptFlags: byte) = interruptFlags ||| flag
