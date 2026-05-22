namespace BubiBoy.Core

module Bus =
    type Memory =
        { Cartridge: CartridgeMemory.CartridgeImage
          Vram: byte[]
          Wram: byte[]
          Oam: byte[]
          Io: byte[]
          Hram: byte[]
          Timer: Timer.State
          Lcd: Lcd.State
          Joypad: Joypad.State
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

    let create cartridge =
        { Cartridge = cartridge
          Vram = Array.zeroCreate<byte> VramSize
          Wram = Array.zeroCreate<byte> WramSize
          Oam = Array.zeroCreate<byte> OamSize
          Io = Array.zeroCreate<byte> IoSize
          Hram = Array.zeroCreate<byte> HramSize
          Timer = Timer.initial
          Lcd = Lcd.initial
          Joypad = Joypad.initial
          InterruptEnable = 0uy }

    let private unusableRead = 0xFFuy

    let readByte (address: uint16) memory =
        let address = int address

        match address with
        | value when value <= 0x7FFF ->
            CartridgeMemory.readByte (uint16 value) memory.Cartridge
        | value when value >= 0x8000 && value <= 0x9FFF ->
            memory.Vram[value - 0x8000]
        | value when value >= 0xA000 && value <= 0xBFFF ->
            CartridgeMemory.readByte (uint16 value) memory.Cartridge
        | value when value >= 0xC000 && value <= 0xDFFF ->
            memory.Wram[value - 0xC000]
        | value when value >= 0xE000 && value <= 0xFDFF ->
            memory.Wram[value - 0xE000]
        | value when value >= 0xFE00 && value <= 0xFE9F ->
            memory.Oam[value - 0xFE00]
        | value when value >= 0xFEA0 && value <= 0xFEFF ->
            unusableRead
        | 0xFF00 ->
            Joypad.readP1 memory.Joypad
        | 0xFF04 ->
            Timer.div memory.Timer
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
            let next = Array.copy memory.Vram
            next[addr - 0x8000] <- value
            { memory with Vram = next }
        | addr when addr >= 0xA000 && addr <= 0xBFFF ->
            { memory with Cartridge = CartridgeMemory.writeByte (uint16 addr) value memory.Cartridge }
        | addr when addr >= 0xC000 && addr <= 0xDFFF ->
            let next = Array.copy memory.Wram
            next[addr - 0xC000] <- value
            { memory with Wram = next }
        | addr when addr >= 0xE000 && addr <= 0xFDFF ->
            let next = Array.copy memory.Wram
            next[addr - 0xE000] <- value
            { memory with Wram = next }
        | addr when addr >= 0xFE00 && addr <= 0xFE9F ->
            let next = Array.copy memory.Oam
            next[addr - 0xFE00] <- value
            { memory with Oam = next }
        | addr when addr >= 0xFEA0 && addr <= 0xFEFF ->
            memory
        | 0xFF00 ->
            { memory with Joypad = Joypad.writeP1 value memory.Joypad }
        | 0xFF04 ->
            let next = Array.copy memory.Io
            next[0x04] <- 0uy
            { memory with Io = next; Timer = Timer.resetDiv memory.Timer }
        | 0xFF44 ->
            let next = Array.copy memory.Io
            next[0x44] <- 0uy
            { memory with Io = next; Lcd = Lcd.resetLine memory.Lcd }
        | addr when addr >= 0xFF00 && addr <= 0xFF7F ->
            let next = Array.copy memory.Io
            next[addr - 0xFF00] <- value
            { memory with Io = next }
        | addr when addr >= 0xFF80 && addr <= 0xFFFE ->
            let next = Array.copy memory.Hram
            next[addr - 0xFF80] <- value
            { memory with Hram = next }
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
        let lcd = Lcd.tick cycles memory.Lcd
        let interruptFlags =
            if memory.Lcd.Line <> 144uy && lcd.Line = 144uy then
                Interrupt.request Interrupt.VBlankBit timerResult.Registers.InterruptFlags
            else
                timerResult.Registers.InterruptFlags

        let nextIo = Array.copy memory.Io
        nextIo[0x04] <- timerResult.Registers.Div
        nextIo[0x05] <- timerResult.Registers.Tima
        nextIo[0x06] <- timerResult.Registers.Tma
        nextIo[0x07] <- timerResult.Registers.Tac
        nextIo[0x0F] <- interruptFlags
        nextIo[0x44] <- lcd.Line

        { memory with
            Timer = timerResult.State
            Lcd = lcd
            Io = nextIo }

    let setButton button pressed memory =
        let wasPressed = Set.contains button memory.Joypad.Pressed
        let joypad = Joypad.setButton button pressed memory.Joypad
        let next = { memory with Joypad = joypad }

        if pressed && not wasPressed then
            let flags = readByte 0xFF0Fus next |> Interrupt.request Interrupt.JoypadBit
            writeByte 0xFF0Fus flags next
        else
            next
