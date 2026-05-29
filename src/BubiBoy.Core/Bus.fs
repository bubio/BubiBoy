namespace BubiBoy.Core

module Bus =
    type Memory =
        private
            { Cartridge: CartridgeMemory.CartridgeImage
              Mode: Hardware.GameBoyMode
              Vram: byte[]
              Wram: byte[]
              Oam: byte[]
              Io: byte[]
              Hram: byte[]
              VramBank: int
              WramBank: int
              BgPaletteRam: byte[]
              ObjPaletteRam: byte[]
              DoubleSpeed: bool
              SpeedSwitchPrepared: bool
              HdmaSource: uint16
              HdmaDestination: uint16
              HdmaRemaining: int
              HdmaActive: bool
              Timer: Timer.State
              Lcd: Lcd.State
              Joypad: Joypad.State
              Apu: Apu.State
              InterruptEnable: byte }

    [<Literal>]
    let VramBankSize = 8 * 1024

    [<Literal>]
    let VramSize = 2 * VramBankSize

    [<Literal>]
    let WramBankSize = 4 * 1024

    [<Literal>]
    let WramSize = 8 * WramBankSize

    [<Literal>]
    let OamSize = 160

    [<Literal>]
    let IoSize = 128

    [<Literal>]
    let HramSize = 127

    let private initialIo mode =
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
        match mode with
        | Hardware.Dmg -> ()
        | Hardware.Cgb ->
            io[0x4D] <- 0x7Euy
            io[0x4F] <- 0xFEuy
            io[0x51] <- 0xFFuy
            io[0x52] <- 0xFFuy
            io[0x53] <- 0xFFuy
            io[0x54] <- 0xFFuy
            io[0x55] <- 0xFFuy
            io[0x68] <- 0xC0uy
            io[0x6A] <- 0xC0uy
            io[0x6C] <- 0xFEuy
            io[0x70] <- 0xF8uy
        io

    let private modeForCartridge cartridge =
        match (CartridgeMemory.header cartridge).CgbSupport with
        | Cartridge.DmgOnly -> Hardware.Dmg
        | Cartridge.CgbEnhanced
        | Cartridge.CgbOnly -> Hardware.Cgb

    let create cartridge =
        let mode = modeForCartridge cartridge
        { Cartridge = cartridge
          Mode = mode
          Vram = Array.zeroCreate<byte> VramSize
          Wram = Array.zeroCreate<byte> WramSize
          Oam = Array.zeroCreate<byte> OamSize
          Io = initialIo mode
          Hram = Array.zeroCreate<byte> HramSize
          VramBank = 0
          WramBank = 1
          BgPaletteRam = Array.zeroCreate<byte> 64
          ObjPaletteRam = Array.zeroCreate<byte> 64
          DoubleSpeed = false
          SpeedSwitchPrepared = false
          HdmaSource = 0us
          HdmaDestination = 0us
          HdmaRemaining = 0
          HdmaActive = false
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

    let mode memory =
        memory.Mode

    let hardwareCyclesForCpuCycles cycles memory =
        if memory.DoubleSpeed then
            cycles / 2
        else
            cycles

    let withCartridge cartridge memory =
        { memory with Cartridge = cartridge }

    let lcdState memory =
        memory.Lcd

    let rawIoByte index memory =
        memory.Io[index]

    let rawVramByte address memory =
        memory.Vram[memory.VramBank * VramBankSize + address - 0x8000]

    let rawVramBankByte bank address memory =
        memory.Vram[(bank &&& 0x01) * VramBankSize + address - 0x8000]

    let rawOamByte index memory =
        memory.Oam[index]

    let rawBgPaletteByte index memory =
        memory.BgPaletteRam[index &&& 0x3F]

    let rawObjPaletteByte index memory =
        memory.ObjPaletteRam[index &&& 0x3F]

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
        next[memory.VramBank * VramBankSize + address - 0x8000] <- value
        { memory with Vram = next }

    let withVramBankByte bank address value memory =
        let next = Array.copy memory.Vram
        next[(bank &&& 0x01) * VramBankSize + address - 0x8000] <- value
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

    let private stat memory (lcd: Lcd.State) =
        let raw = memory.Io[0x41] &&& 0xF8uy
        let coincidence =
            if lcd.Line = memory.Io[0x45] then
                0x04uy
            else
                0uy

        raw ||| coincidence ||| Lcd.modeBits lcd.Mode

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

    let private isCgb memory =
        memory.Mode = Hardware.Cgb

    let private selectedWramBank memory =
        if isCgb memory then memory.WramBank else 1

    let private wramOffset address memory =
        match address with
        | value when value >= 0xC000 && value <= 0xCFFF -> value - 0xC000
        | value when value >= 0xD000 && value <= 0xDFFF ->
            selectedWramBank memory * WramBankSize + value - 0xD000
        | value when value >= 0xE000 && value <= 0xEFFF -> value - 0xE000
        | value -> selectedWramBank memory * WramBankSize + value - 0xF000

    let private cgbPaletteRead paletteRegister (paletteRam: byte[]) =
        let index = int (paletteRegister &&& 0x3Fuy)
        paletteRam[index]

    let private cgbPaletteWrite indexRegister value (paletteRam: byte[]) =
        let index = int (indexRegister &&& 0x3Fuy)
        let next = Array.copy paletteRam
        next[index] <- value
        next

    let private incrementPaletteIndex indexRegister =
        if indexRegister &&& 0x80uy = 0uy then
            indexRegister
        else
            (indexRegister &&& 0x80uy) ||| 0x40uy ||| ((indexRegister + 1uy) &&& 0x3Fuy)

    let private dmaReadByte address memory =
        let address = int address

        match address with
        | value when value <= 0x7FFF -> CartridgeMemory.readByte (uint16 value) memory.Cartridge
        | value when value >= 0x8000 && value <= 0x9FFF ->
            memory.Vram[memory.VramBank * VramBankSize + value - 0x8000]
        | value when value >= 0xA000 && value <= 0xBFFF -> CartridgeMemory.readByte (uint16 value) memory.Cartridge
        | value when value >= 0xC000 && value <= 0xDFFF -> memory.Wram[wramOffset value memory]
        | value when value >= 0xE000 && value <= 0xFDFF -> memory.Wram[wramOffset value memory]
        | _ -> 0xFFuy

    let private copyHdmaBlock source destination memory =
        for offset in 0 .. 0x0F do
            let value = dmaReadByte (source + uint16 offset) memory
            memory.Vram[memory.VramBank * VramBankSize + int destination - 0x8000 + offset] <- value

        memory

    let private runGeneralDma memory =
        let mutable current = memory
        let mutable source = memory.HdmaSource
        let mutable destination = memory.HdmaDestination

        for _ in 1 .. memory.HdmaRemaining do
            current <- copyHdmaBlock source destination current
            source <- source + 0x10us
            destination <- 0x8000us + ((destination + 0x10us - 0x8000us) &&& 0x1FF0us)

        current.Io[0x55] <- 0xFFuy
        { current with
            HdmaSource = source
            HdmaDestination = destination
            HdmaRemaining = 0
            HdmaActive = false }

    let readByte (address: uint16) memory =
        let address = int address

        match address with
        | value when value <= 0x7FFF ->
            CartridgeMemory.readByte (uint16 value) memory.Cartridge
        | value when value >= 0x8000 && value <= 0x9FFF ->
            if vramBlocked memory then
                unusableRead
            else
                memory.Vram[memory.VramBank * VramBankSize + value - 0x8000]
        | value when value >= 0xA000 && value <= 0xBFFF ->
            CartridgeMemory.readByte (uint16 value) memory.Cartridge
        | value when value >= 0xC000 && value <= 0xDFFF ->
            memory.Wram[wramOffset value memory]
        | value when value >= 0xE000 && value <= 0xFDFF ->
            memory.Wram[wramOffset value memory]
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
            stat memory memory.Lcd
        | 0xFF44 ->
            memory.Lcd.Line
        | 0xFF4D when isCgb memory ->
            0x7Euy
            ||| (if memory.DoubleSpeed then 0x80uy else 0uy)
            ||| (if memory.SpeedSwitchPrepared then 0x01uy else 0uy)
        | 0xFF4F when isCgb memory ->
            0xFEuy ||| byte memory.VramBank
        | 0xFF55 when isCgb memory ->
            if memory.HdmaActive then byte (memory.HdmaRemaining - 1) else 0xFFuy
        | 0xFF69 when isCgb memory ->
            cgbPaletteRead memory.Io[0x68] memory.BgPaletteRam
        | 0xFF6B when isCgb memory ->
            cgbPaletteRead memory.Io[0x6A] memory.ObjPaletteRam
        | 0xFF6C when isCgb memory ->
            0xFEuy ||| (memory.Io[0x6C] &&& 0x01uy)
        | 0xFF70 when isCgb memory ->
            0xF8uy ||| byte memory.WramBank
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
                memory.Vram[memory.VramBank * VramBankSize + addr - 0x8000] <- value
                memory
        | addr when addr >= 0xA000 && addr <= 0xBFFF ->
            { memory with Cartridge = CartridgeMemory.writeByte (uint16 addr) value memory.Cartridge }
        | addr when addr >= 0xC000 && addr <= 0xDFFF ->
            memory.Wram[wramOffset addr memory] <- value
            memory
        | addr when addr >= 0xE000 && addr <= 0xFDFF ->
            memory.Wram[wramOffset addr memory] <- value
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
            let apu = Apu.resetDiv memory.Timer.Divider memory.Io memory.Apu
            memory.Io[0x26] <- Apu.statusRegister memory.Io apu

            { memory with
                Timer = Timer.resetDiv memory.Timer
                Apu = apu }
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
        | 0xFF4D when isCgb memory ->
            memory.Io[0x4D] <- 0x7Euy ||| (value &&& 0x01uy)
            { memory with SpeedSwitchPrepared = value &&& 0x01uy <> 0uy }
        | 0xFF4F when isCgb memory ->
            memory.Io[0x4F] <- 0xFEuy ||| (value &&& 0x01uy)
            { memory with VramBank = int (value &&& 0x01uy) }
        | 0xFF51 when isCgb memory ->
            memory.Io[0x51] <- value
            { memory with HdmaSource = (uint16 value <<< 8) ||| (memory.HdmaSource &&& 0x00F0us) }
        | 0xFF52 when isCgb memory ->
            let source = (memory.HdmaSource &&& 0xFF00us) ||| (uint16 (value &&& 0xF0uy))
            memory.Io[0x52] <- value &&& 0xF0uy
            { memory with HdmaSource = source }
        | 0xFF53 when isCgb memory ->
            let destination = 0x8000us ||| ((uint16 (value &&& 0x1Fuy)) <<< 8) ||| (memory.HdmaDestination &&& 0x00F0us)
            memory.Io[0x53] <- value &&& 0x1Fuy
            { memory with HdmaDestination = destination }
        | 0xFF54 when isCgb memory ->
            let destination = 0x8000us ||| (memory.HdmaDestination &&& 0x1F00us) ||| uint16 (value &&& 0xF0uy)
            memory.Io[0x54] <- value &&& 0xF0uy
            { memory with HdmaDestination = destination }
        | 0xFF55 when isCgb memory ->
            if memory.HdmaActive && value &&& 0x80uy = 0uy then
                memory.Io[0x55] <- byte (memory.HdmaRemaining - 1) ||| 0x80uy
                { memory with HdmaActive = false }
            else
                let length = int (value &&& 0x7Fuy) + 1
                memory.Io[0x55] <- value &&& 0x7Fuy
                let next =
                    { memory with
                        HdmaRemaining = length
                        HdmaActive = value &&& 0x80uy <> 0uy }

                if next.HdmaActive then next else runGeneralDma next
        | 0xFF68 when isCgb memory ->
            memory.Io[0x68] <- value ||| 0x40uy
            memory
        | 0xFF69 when isCgb memory ->
            let paletteRam = cgbPaletteWrite memory.Io[0x68] value memory.BgPaletteRam
            memory.Io[0x68] <- incrementPaletteIndex memory.Io[0x68]
            { memory with BgPaletteRam = paletteRam }
        | 0xFF6A when isCgb memory ->
            memory.Io[0x6A] <- value ||| 0x40uy
            memory
        | 0xFF6B when isCgb memory ->
            let paletteRam = cgbPaletteWrite memory.Io[0x6A] value memory.ObjPaletteRam
            memory.Io[0x6A] <- incrementPaletteIndex memory.Io[0x6A]
            { memory with ObjPaletteRam = paletteRam }
        | 0xFF6C when isCgb memory ->
            memory.Io[0x6C] <- 0xFEuy ||| (value &&& 0x01uy)
            memory
        | 0xFF70 when isCgb memory ->
            let bank = int (value &&& 0x07uy)
            let bank = if bank = 0 then 1 else bank
            memory.Io[0x70] <- 0xF8uy ||| byte bank
            { memory with WramBank = bank }
        | addr when addr >= 0xFF10 && addr <= 0xFF26 ->
            let wasApuPowered = memory.Io[0x26] &&& 0x80uy <> 0uy
            let nextIo, apu = Apu.writeRegister (addr - 0xFF00) value memory.Io memory.Apu
            let isApuPowered = nextIo[0x26] &&& 0x80uy <> 0uy
            let dividerApuBitHigh = memory.Timer.Divider &&& 0x1000us <> 0us
            let apu =
                if addr = 0xFF26 && not wasApuPowered && isApuPowered && dividerApuBitHigh then
                    Apu.skipNextFrameSequencerClock apu
                else
                    apu

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
        let hardwareCycles = hardwareCyclesForCpuCycles cycles memory

        let registers: Timer.Registers =
            { Div = readByte 0xFF04us memory
              Tima = readByte 0xFF05us memory
              Tma = readByte 0xFF06us memory
              Tac = readByte 0xFF07us memory
              InterruptFlags = readByte 0xFF0Fus memory }

        let timerResult = Timer.tick cycles memory.Timer registers
        let lcd =
            if lcdEnabled memory then
                Lcd.tick hardwareCycles memory.Lcd
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
        memory.Io[0x41] <- stat memory lcd
        memory.Io[0x44] <- lcd.Line

        let apu = Apu.tick hardwareCycles memory.Io memory.Apu
        memory.Io[0x26] <- Apu.statusRegister memory.Io apu

        let next =
            { memory with
                Timer = timerResult.State
                Lcd = { lcd with StatSignal = statSignal }
                Apu = apu }

        if next.HdmaActive && lcd.Mode = Lcd.HBlank && memory.Lcd.Mode <> Lcd.HBlank then
            let copied = copyHdmaBlock next.HdmaSource next.HdmaDestination next
            let remaining = next.HdmaRemaining - 1
            let source = next.HdmaSource + 0x10us
            let destination = 0x8000us + ((next.HdmaDestination + 0x10us - 0x8000us) &&& 0x1FF0us)
            copied.Io[0x55] <- if remaining = 0 then 0xFFuy else byte (remaining - 1)

            { copied with
                HdmaSource = source
                HdmaDestination = destination
                HdmaRemaining = remaining
                HdmaActive = remaining > 0 }
        else
            next

    let stop memory =
        if isCgb memory && memory.SpeedSwitchPrepared then
            let nextDoubleSpeed = not memory.DoubleSpeed
            memory.Io[0x4D] <- 0x7Euy ||| (if nextDoubleSpeed then 0x80uy else 0uy)

            { memory with
                DoubleSpeed = nextDoubleSpeed
                SpeedSwitchPrepared = false }
        else
            memory

    let setButton button pressed memory =
        let wasPressed = Set.contains button memory.Joypad.Pressed
        let joypad = Joypad.setButton button pressed memory.Joypad
        let next = { memory with Joypad = joypad }

        if pressed && not wasPressed then
            let flags = readByte 0xFF0Fus next |> Interrupt.request Interrupt.JoypadBit
            writeByte 0xFF0Fus flags next
        else
            next
