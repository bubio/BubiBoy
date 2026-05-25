namespace BubiBoy.Core

module Bus =
    type Memory =
        private
            { Cartridge: CartridgeMemory.CartridgeImage
              Vram: byte[]
              Wram: byte[]
              Oam: byte[]
              Io: byte[]
              Hram: byte[]
              Timer: Timer.State
              Lcd: Lcd.State
              Joypad: Joypad.State
              Apu: Apu.State
              InterruptEnable: byte }

    [<Literal>]
    let VramSize = 8 * 1024

    [<Literal>]
    let WramSize = 8 * 1024

    [<Literal>]
    let OamSize = 160

    [<Literal>]
    let IoSize = 128

    [<Literal>]
    let HramSize = 127

    let private initialIo () =
        let io = Array.zeroCreate<byte> IoSize
        io[0x00] <- 0xCFuy
        io[0x01] <- 0x00uy
        io[0x02] <- 0x7Euy
        io[0x04] <- 0x18uy
        io[0x07] <- 0xF8uy
        io[0x0F] <- 0xE1uy
        io[0x10] <- 0x80uy
        io[0x11] <- 0xBFuy
        io[0x12] <- 0xF3uy
        io[0x13] <- 0xFFuy
        io[0x14] <- 0xBFuy
        io[0x16] <- 0x3Fuy
        io[0x18] <- 0xFFuy
        io[0x19] <- 0xBFuy
        io[0x1A] <- 0x7Fuy
        io[0x1B] <- 0xFFuy
        io[0x1C] <- 0x9Fuy
        io[0x1D] <- 0xFFuy
        io[0x1E] <- 0xBFuy
        io[0x20] <- 0xFFuy
        io[0x23] <- 0xBFuy
        io[0x24] <- 0x77uy
        io[0x25] <- 0xF3uy
        io[0x26] <- 0xF1uy
        io[0x40] <- 0x91uy
        io[0x41] <- 0x80uy
        io[0x46] <- 0xFFuy
        io[0x47] <- 0xFCuy
        io[0x48] <- 0xFFuy
        io[0x49] <- 0xFFuy
        io

    let create cartridge =
        { Cartridge = cartridge
          Vram = Array.zeroCreate<byte> VramSize
          Wram = Array.zeroCreate<byte> WramSize
          Oam = Array.zeroCreate<byte> OamSize
          Io = initialIo ()
          Hram = Array.zeroCreate<byte> HramSize
          Timer = Timer.initial
          Lcd = Lcd.initial
          Joypad = Joypad.initial
          Apu = Apu.initial
          InterruptEnable = 0uy }

    let private unusableRead = 0xFFuy

    let private lcdEnabled memory =
        memory.Io[0x40] &&& 0x80uy <> 0uy

    let cartridge memory =
        memory.Cartridge

    let withCartridge cartridge memory =
        { memory with Cartridge = cartridge }

    let lcdState memory =
        memory.Lcd

    let rawIoByte index memory =
        memory.Io[index]

    let rawVramByte address memory =
        memory.Vram[address - 0x8000]

    let rawOamByte index memory =
        memory.Oam[index]

    let pendingAudioSamples memory =
        Apu.pendingSamples memory.Apu

    let drainAudioSamples memory =
        Apu.pendingSamples memory.Apu, { memory with Apu = Apu.clearPendingSamples memory.Apu }

    let withIoByte index value memory =
        let next = Array.copy memory.Io
        next[index] <- value
        { memory with Io = next }

    let withVramByte address value memory =
        let next = Array.copy memory.Vram
        next[address - 0x8000] <- value
        { memory with Vram = next }

    let withOamByte index value memory =
        let next = Array.copy memory.Oam
        next[index] <- value
        { memory with Oam = next }

    let private vramBlocked memory =
        lcdEnabled memory && memory.Lcd.Mode = Lcd.Transfer

    let private oamBlocked memory =
        lcdEnabled memory
        && match memory.Lcd.Mode with
           | Lcd.HBlank
           | Lcd.VBlank -> false
           | _ -> true

    let private stat memory =
        let raw = memory.Io[0x41] &&& 0xF8uy
        let coincidence =
            if memory.Lcd.Line = memory.Io[0x45] then
                0x04uy
            else
                0uy

        raw ||| coincidence ||| Lcd.modeBits memory.Lcd.Mode

    let private statInterruptSignal (memory: Memory) (lcd: Lcd.State) =
        let statRegister = memory.Io[0x41] &&& 0xF8uy
        let coincidenceSelected = statRegister &&& 0x40uy <> 0uy && lcd.Line = memory.Io[0x45]
        let modeSelected =
            match lcd.Mode with
            | Lcd.HBlank -> statRegister &&& 0x08uy <> 0uy
            | Lcd.VBlank -> statRegister &&& 0x10uy <> 0uy
            | Lcd.OamSearch -> statRegister &&& 0x20uy <> 0uy
            | Lcd.Transfer -> false

        coincidenceSelected || modeSelected

    let readByte (address: uint16) memory =
        let address = int address

        match address with
        | value when value <= 0x7FFF ->
            CartridgeMemory.readByte (uint16 value) memory.Cartridge
        | value when value >= 0x8000 && value <= 0x9FFF ->
            if vramBlocked memory then
                unusableRead
            else
                memory.Vram[value - 0x8000]
        | value when value >= 0xA000 && value <= 0xBFFF ->
            CartridgeMemory.readByte (uint16 value) memory.Cartridge
        | value when value >= 0xC000 && value <= 0xDFFF ->
            memory.Wram[value - 0xC000]
        | value when value >= 0xE000 && value <= 0xFDFF ->
            memory.Wram[value - 0xE000]
        | value when value >= 0xFE00 && value <= 0xFE9F ->
            if oamBlocked memory then
                unusableRead
            else
                memory.Oam[value - 0xFE00]
        | value when value >= 0xFEA0 && value <= 0xFEFF ->
            unusableRead
        | 0xFF00 ->
            Joypad.readP1 memory.Joypad
        | 0xFF04 ->
            Timer.div memory.Timer
        | 0xFF41 ->
            stat memory
        | 0xFF44 ->
            memory.Lcd.Line
        | value when value >= 0xFF00 && value <= 0xFF7F ->
            memory.Io[value - 0xFF00]
        | value when value >= 0xFF80 && value <= 0xFFFE ->
            memory.Hram[value - 0xFF80]
        | 0xFFFF ->
            memory.InterruptEnable
        | _ ->
            unusableRead

    let writeByte (address: uint16) (value: byte) memory =
        let address = int address

        match address with
        | addr when addr <= 0x7FFF ->
            { memory with Cartridge = CartridgeMemory.writeByte (uint16 addr) value memory.Cartridge }
        | addr when addr >= 0x8000 && addr <= 0x9FFF ->
            if vramBlocked memory then
                memory
            else
                memory.Vram[addr - 0x8000] <- value
                memory
        | addr when addr >= 0xA000 && addr <= 0xBFFF ->
            { memory with Cartridge = CartridgeMemory.writeByte (uint16 addr) value memory.Cartridge }
        | addr when addr >= 0xC000 && addr <= 0xDFFF ->
            memory.Wram[addr - 0xC000] <- value
            memory
        | addr when addr >= 0xE000 && addr <= 0xFDFF ->
            memory.Wram[addr - 0xE000] <- value
            memory
        | addr when addr >= 0xFE00 && addr <= 0xFE9F ->
            if oamBlocked memory then
                memory
            else
                memory.Oam[addr - 0xFE00] <- value
                memory
        | addr when addr >= 0xFEA0 && addr <= 0xFEFF ->
            memory
        | 0xFF00 ->
            { memory with Joypad = Joypad.writeP1 value memory.Joypad }
        | 0xFF04 ->
            memory.Io[0x04] <- 0uy
            { memory with Timer = Timer.resetDiv memory.Timer }
        | 0xFF41 ->
            memory.Io[0x41] <- value &&& 0xF8uy
            memory
        | 0xFF44 ->
            memory.Io[0x44] <- 0uy
            { memory with Lcd = Lcd.resetLine memory.Lcd }
        | 0xFF46 ->
            let sourceBase = uint16 value <<< 8

            for offset in 0 .. OamSize - 1 do
                memory.Oam[offset] <- readByte (sourceBase + uint16 offset) memory

            memory.Io[0x46] <- value
            memory
        | addr when addr >= 0xFF10 && addr <= 0xFF26 ->
            let nextIo, apu = Apu.writeRegister (addr - 0xFF00) value memory.Io memory.Apu
            { memory with Io = nextIo; Apu = apu }
        | addr when addr >= 0xFF00 && addr <= 0xFF7F ->
            memory.Io[addr - 0xFF00] <- value
            memory
        | addr when addr >= 0xFF80 && addr <= 0xFFFE ->
            memory.Hram[addr - 0xFF80] <- value
            memory
        | 0xFFFF ->
            { memory with InterruptEnable = value }
        | _ ->
            memory

    let tick cycles memory =
        let registers: Timer.Registers =
            { Div = readByte 0xFF04us memory
              Tima = readByte 0xFF05us memory
              Tma = readByte 0xFF06us memory
              Tac = readByte 0xFF07us memory
              InterruptFlags = readByte 0xFF0Fus memory }

        let timerResult = Timer.tick cycles memory.Timer registers
        let lcd =
            if lcdEnabled memory then
                Lcd.tick cycles memory.Lcd
            else
                Lcd.disabled memory.Lcd

        let mutable interruptFlags =
            if lcdEnabled memory && memory.Lcd.Line <> 144uy && lcd.Line = 144uy then
                Interrupt.request Interrupt.VBlankBit timerResult.Registers.InterruptFlags
            else
                timerResult.Registers.InterruptFlags

        let statSignal =
            if lcdEnabled memory then
                statInterruptSignal memory lcd
            else
                false

        if statSignal && not memory.Lcd.StatSignal then
            interruptFlags <- Interrupt.request Interrupt.LcdStatBit interruptFlags

        memory.Io[0x04] <- timerResult.Registers.Div
        memory.Io[0x05] <- timerResult.Registers.Tima
        memory.Io[0x06] <- timerResult.Registers.Tma
        memory.Io[0x07] <- timerResult.Registers.Tac
        memory.Io[0x0F] <- interruptFlags
        memory.Io[0x41] <- stat { memory with Lcd = lcd }
        memory.Io[0x44] <- lcd.Line

        let apu = Apu.tick cycles memory.Io memory.Apu
        memory.Io[0x26] <- Apu.statusRegister memory.Io apu

        { memory with
            Timer = timerResult.State
            Lcd = { lcd with StatSignal = statSignal }
            Apu = apu }

    let setButton button pressed memory =
        let wasPressed = Set.contains button memory.Joypad.Pressed
        let joypad = Joypad.setButton button pressed memory.Joypad
        let next = { memory with Joypad = joypad }

        if pressed && not wasPressed then
            let flags = readByte 0xFF0Fus next |> Interrupt.request Interrupt.JoypadBit
            writeByte 0xFF0Fus flags next
        else
            next
