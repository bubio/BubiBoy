namespace BubiBoy.Core

/// Models the divider and programmable timer hardware.
module Timer =
    /// Holds internal timer counters that are not directly memory mapped.
    [<Struct>]
    type State = { Divider: uint16; TimaCounter: int }

    /// Contains the memory-mapped registers consumed and produced by a timer tick.
    [<Struct>]
    type Registers =
        { Div: byte
          Tima: byte
          Tma: byte
          Tac: byte
          InterruptFlags: byte }

    /// Contains the timer state and register values after a tick.
    [<Struct>]
    type TickResult = { State: State; Registers: Registers }

    /// The timer state after hardware reset.
    let initial = { Divider = 0us; TimaCounter = 0 }

    let private timerEnabled tac = tac &&& 0x04uy <> 0uy

    let private period tac =
        match tac &&& 0x03uy with
        | 0x00uy -> 1024
        | 0x01uy -> 16
        | 0x02uy -> 64
        | _ -> 256

    let internal div state = byte (state.Divider >>> 8)

    let internal resetDiv state = { state with Divider = 0us }

    /// Advances the timer by the specified number of CPU cycles.
    let tick cycles state registers =
        let divider = uint16 ((int state.Divider + cycles) &&& 0xFFFF)

        if not (timerEnabled registers.Tac) then
            { State = { state with Divider = divider }
              Registers =
                { registers with
                    Div = byte (divider >>> 8) } }
        else
            let timerPeriod = period registers.Tac
            let total = state.TimaCounter + cycles
            let increments = total / timerPeriod
            let remainder = total % timerPeriod

            let mutable tima = registers.Tima
            let mutable interruptFlags = registers.InterruptFlags

            for _ in 1..increments do
                if tima = 0xFFuy then
                    tima <- registers.Tma
                    interruptFlags <- Interrupt.request Interrupt.TimerBit interruptFlags
                else
                    tima <- tima + 1uy

            { State =
                { Divider = divider
                  TimaCounter = remainder }
              Registers =
                { registers with
                    Div = byte (divider >>> 8)
                    Tima = tima
                    InterruptFlags = interruptFlags } }
