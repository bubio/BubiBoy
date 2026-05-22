namespace BubiBoy.Core

module Emulator =
    type StopReason =
        | StepLimitReached
        | Halted
        | UnsupportedOpcode of opcode: byte * pc: uint16

    type Session =
        { Cpu: Cpu.State
          Bus: Bus.Memory
          TotalCycles: int64
          Steps: int }

    type RunResult =
        { Session: Session
          StopReason: StopReason }

    let createSession rom =
        rom
        |> CartridgeMemory.create
        |> Result.map (fun cartridge ->
            { Cpu = Cpu.initialState
              Bus = Bus.create cartridge
              TotalCycles = 0L
              Steps = 0 })

    let step session =
        let result = Cpu.step session.Cpu session.Bus
        let bus = Bus.tick result.Cycles result.Bus

        { Cpu = result.Cpu
          Bus = bus
          TotalCycles = session.TotalCycles + int64 result.Cycles
          Steps = session.Steps + 1 }

    let run maxSteps session =
        let rec loop remaining current =
            if current.Cpu.Halted then
                { Session = current
                  StopReason = Halted }
            elif remaining <= 0 then
                { Session = current
                  StopReason = StepLimitReached }
            else
                try
                    loop (remaining - 1) (step current)
                with
                | Cpu.UnsupportedOpcode(opcode, pc) ->
                    { Session = current
                      StopReason = UnsupportedOpcode(opcode, pc) }

        loop maxSteps session
