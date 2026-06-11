namespace BubiBoy.Core

/// Defines hardware-wide constants and operating modes.
module Hardware =
    /// The visible LCD width in pixels.
    [<Literal>]
    let ScreenWidth = 160

    /// The visible LCD height in pixels.
    [<Literal>]
    let ScreenHeight = 144

    /// The base DMG hardware clock frequency in hertz.
    [<Literal>]
    let DmgClockHz = 4_194_304

    /// The number of base hardware cycles in one complete frame.
    [<Literal>]
    let CyclesPerFrame = 70_224

    /// Identifies the active Game Boy hardware compatibility mode.
    type GameBoyMode =
        | Dmg
        | CgbCompatibility
        | Cgb
