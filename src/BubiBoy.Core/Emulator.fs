namespace BubiBoy.Core

/// Coordinates CPU, bus, video, and audio state into deterministic emulator steps.
module Emulator =
    /// Explains why an emulator run loop stopped.
    type StopReason =
        | StepLimitReached
        | FrameCompleted
        | Halted
        | UnsupportedOpcode of opcode: byte * pc: uint16

    /// Holds all deterministic state needed to continue emulation.
    [<Struct>]
    type Session =
        { Cpu: Cpu.State
          Bus: Bus.Memory
          Framebuffer: uint32[]
          TotalCycles: int64
          Steps: int }

    /// Contains the session and stop reason produced by a bounded run.
    type RunResult =
        { Session: Session
          StopReason: StopReason }

    /// Contains the completed frame outputs and resulting session.
    type FrameResult =
        { Session: Session
          Framebuffer: uint32[]
          AudioSamples: Apu.Sample[]
          StopReason: StopReason }

    /// Creates an emulator session from a complete ROM image.
    let createSession rom =
        rom
        |> CartridgeMemory.create
        |> Result.map (fun cartridge ->
            let bus = Bus.create cartridge

            let cpu =
                match Bus.mode bus with
                | Hardware.Cgb ->
                    { Cpu.initialState with
                        Registers =
                            { Cpu.initialRegisters with
                                A = 0x11uy
                                F = 0x80uy } }
                | Hardware.Dmg
                | Hardware.CgbCompatibility -> Cpu.initialState

            { Bus = bus
              Framebuffer = Video.blankFrame ()
              TotalCycles = 0L
              Steps = 0
              Cpu = cpu })

    /// Creates a DMG emulator session that starts by executing a 256-byte boot ROM.
    let createSessionWithDmgBootRom bootRom rom =
        rom
        |> CartridgeMemory.create
        |> Result.bind (fun cartridge ->
            Bus.createWithDmgBootRom bootRom cartridge
            |> Result.map (fun bus ->
                { Bus = bus
                  Framebuffer = Video.blankFrame ()
                  TotalCycles = 0L
                  Steps = 0
                  Cpu = Cpu.powerOnState }))

    /// Creates a CGB emulator session that starts by executing a 2304-byte boot ROM.
    let createSessionWithCgbBootRom bootRom rom =
        rom
        |> CartridgeMemory.create
        |> Result.bind (fun cartridge ->
            Bus.createWithCgbBootRom bootRom cartridge
            |> Result.map (fun bus ->
                { Bus = bus
                  Framebuffer = Video.blankFrame ()
                  TotalCycles = 0L
                  Steps = 0
                  Cpu = Cpu.powerOnState }))

    let private lcdEnabled (bus: Bus.Memory) =
        Bus.readByte 0xFF40us bus &&& 0x80uy <> 0uy

    let private shouldRenderScanline (beforeBus: Bus.Memory) (afterBus: Bus.Memory) =
        let beforeLcd = Bus.lcdState beforeBus
        let afterLcd = Bus.lcdState afterBus

        lcdEnabled beforeBus
        && beforeLcd.Line < byte Hardware.ScreenHeight
        && beforeLcd.Mode <> Lcd.HBlank
        && ((afterLcd.Line = beforeLcd.Line && afterLcd.Mode = Lcd.HBlank)
            || afterLcd.Line <> beforeLcd.Line)

    /// Executes one CPU instruction and advances attached hardware.
    let step session =
        let beforeBus = session.Bus
        let result = Cpu.step session.Cpu session.Bus
        let bus = result.Bus

        let framebuffer =
            if shouldRenderScanline beforeBus bus then
                Video.renderScanlineReusable (int (Bus.lcdState beforeBus).Line) bus session.Framebuffer
                session.Framebuffer
            else
                session.Framebuffer

        { Cpu = result.Cpu
          Bus = bus
          Framebuffer = framebuffer
          TotalCycles =
            session.TotalCycles
            + int64 (Bus.hardwareCyclesForCpuCycles result.Cycles beforeBus)
          Steps = session.Steps + 1 }

    /// Executes at most the specified number of CPU instructions.
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
                with Cpu.UnsupportedOpcode(opcode, pc) ->
                    stopReason <- Some(UnsupportedOpcode(opcode, pc))

        { Session = current
          StopReason = stopReason.Value }

    /// Executes until one frame elapses or the instruction limit is reached.
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
                with Cpu.UnsupportedOpcode(opcode, pc) ->
                    stopReason <- Some(UnsupportedOpcode(opcode, pc))

        let audioSamples, bus = Bus.drainAudioSamples current.Bus

        { Session = { current with Bus = bus }
          Framebuffer = current.Framebuffer
          AudioSamples = audioSamples
          StopReason = stopReason.Value }
