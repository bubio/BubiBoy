namespace BubiBoy.Core

module Lcd =
    type State =
        { Line: byte
          DotCounter: int }

    [<Literal>]
    let CyclesPerLine = 456

    [<Literal>]
    let LinesPerFrame = 154

    let initial =
        { Line = 0uy
          DotCounter = 0 }

    let resetLine state =
        { state with
            Line = 0uy
            DotCounter = 0 }

    let tick cycles state =
        let totalDots = state.DotCounter + cycles
        let advancedLines = totalDots / CyclesPerLine
        let dotCounter = totalDots % CyclesPerLine
        let line = (int state.Line + advancedLines) % LinesPerFrame

        { Line = byte line
          DotCounter = dotCounter }

