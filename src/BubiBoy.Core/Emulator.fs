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
          Framebuffer: uint32[]
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
              Framebuffer = Video.blankFrame ()
              TotalCycles = 0L
              Steps = 0 })

    let private lcdEnabled (bus: Bus.Memory) =
        Bus.readByte 0xFF40us bus &&& 0x80uy <> 0uy

    let private shouldRenderScanline (beforeBus: Bus.Memory) (afterBus: Bus.Memory) =
        lcdEnabled beforeBus
        && beforeBus.Lcd.Line < byte Hardware.ScreenHeight
        && beforeBus.Lcd.Mode <> Lcd.HBlank
        && ((afterBus.Lcd.Line = beforeBus.Lcd.Line && afterBus.Lcd.Mode = Lcd.HBlank)
            || afterBus.Lcd.Line <> beforeBus.Lcd.Line)

    let step session =
        let beforeBus = session.Bus
        let result = Cpu.step session.Cpu session.Bus
        let bus = Bus.tick result.Cycles result.Bus
        let framebuffer =
            if shouldRenderScanline beforeBus bus then
                let next = Array.copy session.Framebuffer
                Video.renderScanline (int beforeBus.Lcd.Line) bus next
                next
            else
                session.Framebuffer

        { Cpu = result.Cpu
          Bus = bus
          Framebuffer = framebuffer
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
          Framebuffer = current.Framebuffer
          StopReason = stopReason.Value }
