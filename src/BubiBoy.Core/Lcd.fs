namespace BubiBoy.Core

/// Models LCD scanline timing and mode transitions.
module Lcd =
    /// Identifies the current LCD controller mode.
    type Mode =
        | HBlank
        | VBlank
        | OamSearch
        | Transfer

    /// Holds the current LCD timing and STAT interrupt signal state.
    [<Struct>]
    type State =
        { Line: byte
          DotCounter: int
          Mode: Mode
          StatSignal: bool }

    /// The number of hardware cycles in one scanline.
    [<Literal>]
    let CyclesPerLine = 456

    /// The total number of visible and vertical-blank lines in one frame.
    [<Literal>]
    let LinesPerFrame = 154

    /// The LCD state after hardware reset.
    let initial =
        { Line = 0uy
          DotCounter = 0
          Mode = OamSearch
          StatSignal = false }

    let internal modeBits mode =
        match mode with
        | HBlank -> 0uy
        | VBlank -> 1uy
        | OamSearch -> 2uy
        | Transfer -> 3uy

    let private modeFor line dotCounter =
        if line >= 144 then VBlank
        elif dotCounter < 80 then OamSearch
        elif dotCounter < 252 then Transfer
        else HBlank

    let internal resetLine state =
        { state with
            Line = 0uy
            DotCounter = 0
            Mode = OamSearch
            StatSignal = false }

    let internal disabled state =
        { state with
            Line = 0uy
            DotCounter = 0
            Mode = HBlank
            StatSignal = false }

    /// Advances LCD timing by the specified number of hardware cycles.
    let tick cycles state =
        let totalDots = state.DotCounter + cycles
        let advancedLines = totalDots / CyclesPerLine
        let dotCounter = totalDots % CyclesPerLine
        let line = (int state.Line + advancedLines) % LinesPerFrame
        let line = byte line

        { Line = line
          DotCounter = dotCounter
          Mode = modeFor (int line) dotCounter
          StatSignal = state.StatSignal }
