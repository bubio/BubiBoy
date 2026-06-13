namespace BubiBoy.Core

/// Models the divider and programmable timer hardware.
module Timer =
    /// Holds internal timer state that is not directly memory mapped.
    [<Struct>]
    type State =
        { Divider: uint16
          ReloadDelay: int option }

    /// Contains the memory-mapped registers consumed and produced by a timer tick.
    [<Struct>]
    type Registers =
        { Div: byte
          Tima: byte
          Tma: byte
          Tac: byte
          InterruptFlags: byte }

    /// Contains the timer state and register values after a tick or register write.
    [<Struct>]
    type TickResult = { State: State; Registers: Registers }

    /// The timer state after hardware reset.
    let initial = { Divider = 0us; ReloadDelay = None }

    let private timerEnabled tac = tac &&& 0x04uy <> 0uy

    let private selectedBit tac =
        match tac &&& 0x03uy with
        | 0x00uy -> 9
        | 0x01uy -> 3
        | 0x02uy -> 5
        | _ -> 7

    let private timerSignal divider tac =
        timerEnabled tac && (divider &&& (1us <<< selectedBit tac)) <> 0us

    let internal div state = byte (state.Divider >>> 8)

    let private incrementTima state registers =
        if state.ReloadDelay.IsSome then
            state, registers
        elif registers.Tima = 0xFFuy then
            { state with ReloadDelay = Some 4 }, { registers with Tima = 0uy }
        else
            state,
            { registers with
                Tima = registers.Tima + 1uy }

    let private advanceReload state registers =
        match state.ReloadDelay with
        | None -> state, registers
        | Some 1 ->
            { state with ReloadDelay = None },
            { registers with
                Tima = registers.Tma
                InterruptFlags = Interrupt.request Interrupt.TimerBit registers.InterruptFlags }
        | Some cycles ->
            { state with
                ReloadDelay = Some(cycles - 1) },
            registers

    /// Resets DIV and applies the timer's falling-edge increment behavior.
    let resetDiv state registers =
        let oldSignal = timerSignal state.Divider registers.Tac
        let reset = { state with Divider = 0us }

        if oldSignal then
            let state, registers = incrementTima reset registers

            { State = state
              Registers = { registers with Div = 0uy } }
        else
            { State = reset
              Registers = { registers with Div = 0uy } }

    /// Writes TIMA, cancelling a pending reload that has not completed.
    let writeTima value state registers =
        { State = { state with ReloadDelay = None }
          Registers = { registers with Tima = value } }

    /// Writes TAC and applies the timer's falling-edge increment behavior.
    let writeTac value state registers =
        let tac = value &&& 0x07uy
        let oldSignal = timerSignal state.Divider registers.Tac
        let newSignal = timerSignal state.Divider tac
        let registers = { registers with Tac = tac }

        if oldSignal && not newSignal then
            let state, registers = incrementTima state registers
            { State = state; Registers = registers }
        else
            { State = state; Registers = registers }

    /// Advances the timer by the specified number of CPU cycles.
    let tick cycles state registers =
        let mutable currentState = state
        let mutable currentRegisters = registers

        for _ in 1..cycles do
            let stateAfterReload, registersAfterReload =
                advanceReload currentState currentRegisters

            let oldSignal = timerSignal stateAfterReload.Divider registersAfterReload.Tac
            let divider = stateAfterReload.Divider + 1us
            let newSignal = timerSignal divider registersAfterReload.Tac

            currentState <-
                { stateAfterReload with
                    Divider = divider }

            currentRegisters <- registersAfterReload

            if oldSignal && not newSignal then
                let incrementedState, incrementedRegisters =
                    incrementTima currentState currentRegisters

                currentState <- incrementedState
                currentRegisters <- incrementedRegisters

        { State = currentState
          Registers =
            { currentRegisters with
                Div = div currentState } }
