namespace BubiBoy.Core

module Emulator =
    type StopReason =
        | StepLimitReached
        | FrameCompleted
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

    type FrameResult =
        { Session: Session
          Framebuffer: uint32[]
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
        let mutable remaining = maxSteps
        let mutable current = session
        let mutable stopReason = None

        while stopReason.IsNone do
            if remaining <= 0 then
                stopReason <- Some StepLimitReached
            else
                try
                    current <- step current
                    remaining <- remaining - 1
                with
                | Cpu.UnsupportedOpcode(opcode, pc) ->
                    stopReason <- Some(UnsupportedOpcode(opcode, pc))

        { Session = current
          StopReason = stopReason.Value }

    let runFrame maxSteps session =
        let targetCycles = session.TotalCycles + int64 Hardware.CyclesPerFrame
        let mutable remaining = maxSteps
        let mutable current = session
        let mutable stopReason = None

        while stopReason.IsNone do
            if current.TotalCycles >= targetCycles then
                stopReason <- Some FrameCompleted
            elif remaining <= 0 then
                stopReason <- Some StepLimitReached
            else
                try
                    current <- step current
                    remaining <- remaining - 1
                with
                | Cpu.UnsupportedOpcode(opcode, pc) ->
                    stopReason <- Some(UnsupportedOpcode(opcode, pc))

        { Session = current
          Framebuffer = Video.renderFrame current.Bus
          StopReason = stopReason.Value }
