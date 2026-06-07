namespace BubiBoy.Core

/// Models the Game Boy joypad matrix and P1 register.
module Joypad =
    /// Identifies a Game Boy input button.
    type Button =
        | Right
        | Left
        | Up
        | Down
        | A
        | B
        | Select
        | Start

    /// Holds P1 selection lines and the set of pressed buttons.
    type State =
        { SelectAction: bool
          SelectDirection: bool
          Pressed: Set<Button> }

    /// The joypad state after hardware reset.
    let initial =
        { SelectAction = true
          SelectDirection = true
          Pressed = Set.empty }

    /// Applies a value written to the P1 selection register.
    let writeP1 value state =
        { state with
            SelectAction = value &&& 0x20uy = 0uy
            SelectDirection = value &&& 0x10uy = 0uy }

    /// Updates the pressed state of one button.
    let setButton button pressed state =
        if pressed then
            { state with
                Pressed = state.Pressed.Add button }
        else
            { state with
                Pressed = state.Pressed.Remove button }

    let private maskFor selected pressed button pressedMask =
        if selected && Set.contains button pressed then
            pressedMask
        else
            0x0Fuy

    /// Reads the active-low P1 register value for the selected button group.
    let readP1 state =
        let selectBits =
            0xC0uy
            ||| (if state.SelectAction then 0uy else 0x20uy)
            ||| (if state.SelectDirection then 0uy else 0x10uy)

        let actionBits =
            0x0Fuy
            &&& maskFor state.SelectAction state.Pressed A 0x0Euy
            &&& maskFor state.SelectAction state.Pressed B 0x0Duy
            &&& maskFor state.SelectAction state.Pressed Select 0x0Buy
            &&& maskFor state.SelectAction state.Pressed Start 0x07uy

        let directionBits =
            0x0Fuy
            &&& maskFor state.SelectDirection state.Pressed Right 0x0Euy
            &&& maskFor state.SelectDirection state.Pressed Left 0x0Duy
            &&& maskFor state.SelectDirection state.Pressed Up 0x0Buy
            &&& maskFor state.SelectDirection state.Pressed Down 0x07uy

        selectBits ||| (actionBits &&& directionBits)
