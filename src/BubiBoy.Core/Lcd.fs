namespace BubiBoy.Core

module Lcd =
    type Mode =
        | HBlank
        | VBlank
        | OamSearch
        | Transfer

    type State =
        { Line: byte
          DotCounter: int
          Mode: Mode
          StatSignal: bool }

    [<Literal>]
    let CyclesPerLine = 456

    [<Literal>]
    let LinesPerFrame = 154

    let initial =
        { Line = 0uy
          DotCounter = 0
          Mode = OamSearch
          StatSignal = false }

    let modeBits mode =
        match mode with
        | HBlank -> 0uy
        | VBlank -> 1uy
        | OamSearch -> 2uy
        | Transfer -> 3uy

    let private modeFor line dotCounter =
        if line >= 144 then
            VBlank
        elif dotCounter < 80 then
            OamSearch
        elif dotCounter < 252 then
            Transfer
        else
            HBlank

    let resetLine state =
        { state with
            Line = 0uy
            DotCounter = 0
            Mode = OamSearch
            StatSignal = false }

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
