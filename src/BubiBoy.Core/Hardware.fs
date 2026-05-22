namespace BubiBoy.Core

module Hardware =
    [<Literal>]
    let ScreenWidth = 160

    [<Literal>]
    let ScreenHeight = 144

    [<Literal>]
    let DmgClockHz = 4_194_304

    [<Literal>]
    let CyclesPerFrame = 70_224

    type GameBoyMode =
        | Dmg
        | Cgb

