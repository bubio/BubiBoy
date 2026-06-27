namespace BubiBoy.Core

open System
open System.Security.Cryptography

/// Maps CPU addresses to cartridge and hardware devices and advances shared hardware state.
module Bus =
    type private BootRomKind =
        | Dmg
        | Cgb

    type private BootRom =
        { Kind: BootRomKind
          Bytes: byte[]
          Sha256: string
          Enabled: bool }

    /// Represents the complete mutable-memory and device state visible through the CPU bus.
    type Memory =
        private
            { Cartridge: CartridgeMemory.CartridgeImage
              Mode: Hardware.GameBoyMode
              BootRom: BootRom option
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
              mutable HdmaSource: uint16
              mutable HdmaDestination: uint16
              mutable HdmaRemaining: int
              mutable HdmaActive: bool
              mutable Timer: Timer.State
              mutable Lcd: Lcd.State
              Joypad: Joypad.State
              mutable Apu: Apu.State
              mutable PendingApuCycles: int
              InterruptEnable: byte }

    /// Contains a serializable copy of bus-owned memory and device state.
    type Snapshot =
        { CartridgeSnapshot: CartridgeMemory.Snapshot
          ModeSnapshot: Hardware.GameBoyMode
          BootRomEnabledSnapshot: bool
          BootRomSha256Snapshot: string option
          VramSnapshot: byte[]
          WramSnapshot: byte[]
          OamSnapshot: byte[]
          IoSnapshot: byte[]
          HramSnapshot: byte[]
          VramBankSnapshot: int
          WramBankSnapshot: int
          BgPaletteRamSnapshot: byte[]
          ObjPaletteRamSnapshot: byte[]
          DoubleSpeedSnapshot: bool
          SpeedSwitchPreparedSnapshot: bool
          HdmaSourceSnapshot: uint16
          HdmaDestinationSnapshot: uint16
          HdmaRemainingSnapshot: int
          HdmaActiveSnapshot: bool
          TimerSnapshot: Timer.State
          LcdSnapshot: Lcd.State
          JoypadSnapshot: Joypad.State
          ApuSnapshot: Apu.StateSnapshot
          InterruptEnableSnapshot: byte }

    /// The size of one CGB video RAM bank in bytes.
    [<Literal>]
    let VramBankSize = 8 * 1024

    /// The total CGB video RAM size in bytes.
    [<Literal>]
    let VramSize = 2 * VramBankSize

    /// The size of one CGB work RAM bank in bytes.
    [<Literal>]
    let WramBankSize = 4 * 1024

    /// The total CGB work RAM size in bytes.
    [<Literal>]
    let WramSize = 8 * WramBankSize

    /// The object attribute memory size in bytes.
    [<Literal>]
    let OamSize = 160

    /// The memory-mapped I/O register area size in bytes.
    [<Literal>]
    let IoSize = 128

    /// The high RAM size in bytes.
    [<Literal>]
    let HramSize = 127

    [<Literal>]
    let private OamDmaProgressIndex = 0x7F

    [<Literal>]
    let private TimerReloadMarkerIndex = 0x7E

    [<Literal>]
    let private InternalStateSignatureIndex = 0x7D

    [<Literal>]
    let private InternalStateSignature = 0xB7uy

    let private postBootIo mode =
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
        io[InternalStateSignatureIndex] <- InternalStateSignature
        io[TimerReloadMarkerIndex] <- 0uy
        io[OamDmaProgressIndex] <- 0xFFuy

        match mode with
        | Hardware.Dmg -> ()
        | Hardware.CgbCompatibility
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

    let private powerOnIo () =
        let io = Array.zeroCreate<byte> IoSize
        io[InternalStateSignatureIndex] <- InternalStateSignature
        io[TimerReloadMarkerIndex] <- 0uy
        io[OamDmaProgressIndex] <- 0xFFuy
        io

    let private modeForCartridge cartridge =
        match (CartridgeMemory.header cartridge).CgbSupport with
        | Cartridge.DmgOnly -> Hardware.Dmg
        | Cartridge.CgbEnhanced
        | Cartridge.CgbOnly -> Hardware.Cgb

    let private createMemory cartridge mode bootRom io =
        { Cartridge = cartridge
          Mode = mode
          BootRom = bootRom
          Vram = Array.zeroCreate<byte> VramSize
          Wram = Array.zeroCreate<byte> WramSize
          Oam = Array.zeroCreate<byte> OamSize
          Io = io mode
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
          PendingApuCycles = 0
          InterruptEnable = 0uy }

    /// Creates reset bus state after the built-in boot sequence has completed.
    let create cartridge =
        createMemory cartridge (modeForCartridge cartridge) None postBootIo

    /// Creates DMG power-on bus state with a 256-byte boot ROM mapped.
    let createWithDmgBootRom (bootRom: byte[]) cartridge =
        if isNull bootRom then
            Error "DMG boot ROM data is null."
        elif bootRom.Length <> 256 then
            Error $"DMG boot ROM size mismatch: expected 256 bytes, got {bootRom.Length} bytes."
        elif modeForCartridge cartridge <> Hardware.Dmg then
            Error "DMG boot ROM can only be used with a DMG-only cartridge."
        else
            let bytes = Array.copy bootRom
            let sha256 = SHA256.HashData bytes |> Convert.ToHexString

            Ok(
                createMemory
                    cartridge
                    Hardware.Dmg
                    (Some
                        { Kind = Dmg
                          Bytes = bytes
                          Sha256 = sha256
                          Enabled = true })
                    (fun _ -> powerOnIo ())
            )

    /// Creates CGB power-on bus state with a 2304-byte boot ROM mapped.
    let createWithCgbBootRom (bootRom: byte[]) cartridge =
        if isNull bootRom then
            Error "CGB boot ROM data is null."
        elif bootRom.Length <> 2304 then
            Error $"CGB boot ROM size mismatch: expected 2304 bytes, got {bootRom.Length} bytes."
        else
            let bytes = Array.copy bootRom
            let sha256 = SHA256.HashData bytes |> Convert.ToHexString

            Ok(
                createMemory
                    cartridge
                    Hardware.Cgb
                    (Some
                        { Kind = Cgb
                          Bytes = bytes
                          Sha256 = sha256
                          Enabled = true })
                    (fun _ -> powerOnIo ())
            )

    let private synchronizeApu memory =
        if memory.PendingApuCycles > 0 then
            if memory.Io[0x26] &&& 0x80uy <> 0uy then
                memory.Apu <- Apu.tick memory.PendingApuCycles memory.Io memory.Apu
                memory.Io[0x26] <- Apu.statusRegister memory.Io memory.Apu

            memory.PendingApuCycles <- 0

        memory

    module private MemorySnapshot =
        let private validateArray name expected (bytes: byte[]) =
            if isNull bytes then
                Error $"{name} is null."
            elif bytes.Length <> expected then
                Error $"{name} size mismatch: expected {expected} bytes, got {bytes.Length} bytes."
            else
                Ok()

        let capture (memory: Memory) : Snapshot =
            synchronizeApu memory |> ignore

            { CartridgeSnapshot = CartridgeMemory.snapshot memory.Cartridge
              ModeSnapshot = memory.Mode
              BootRomEnabledSnapshot = memory.BootRom |> Option.exists (fun bootRom -> bootRom.Enabled)
              BootRomSha256Snapshot = memory.BootRom |> Option.map (fun bootRom -> bootRom.Sha256)
              VramSnapshot = Array.copy memory.Vram
              WramSnapshot = Array.copy memory.Wram
              OamSnapshot = Array.copy memory.Oam
              IoSnapshot = Array.copy memory.Io
              HramSnapshot = Array.copy memory.Hram
              VramBankSnapshot = memory.VramBank
              WramBankSnapshot = memory.WramBank
              BgPaletteRamSnapshot = Array.copy memory.BgPaletteRam
              ObjPaletteRamSnapshot = Array.copy memory.ObjPaletteRam
              DoubleSpeedSnapshot = memory.DoubleSpeed
              SpeedSwitchPreparedSnapshot = memory.SpeedSwitchPrepared
              HdmaSourceSnapshot = memory.HdmaSource
              HdmaDestinationSnapshot = memory.HdmaDestination
              HdmaRemainingSnapshot = memory.HdmaRemaining
              HdmaActiveSnapshot = memory.HdmaActive
              TimerSnapshot = memory.Timer
              LcdSnapshot = memory.Lcd
              JoypadSnapshot = memory.Joypad
              ApuSnapshot = Apu.snapshot memory.Apu
              InterruptEnableSnapshot = memory.InterruptEnable }

        let private restoreIoSnapshot (snapshot: Snapshot) =
            let io = Array.copy snapshot.IoSnapshot

            if io[InternalStateSignatureIndex] <> InternalStateSignature then
                io[OamDmaProgressIndex] <- 0xFFuy

            io[InternalStateSignatureIndex] <- InternalStateSignature
            io[TimerReloadMarkerIndex] <- 0uy
            io

        let restore (snapshot: Snapshot) (current: Memory) =
            validateArray "VRAM" VramSize snapshot.VramSnapshot
            |> Result.bind (fun () -> validateArray "WRAM" WramSize snapshot.WramSnapshot)
            |> Result.bind (fun () -> validateArray "OAM" OamSize snapshot.OamSnapshot)
            |> Result.bind (fun () -> validateArray "IO" IoSize snapshot.IoSnapshot)
            |> Result.bind (fun () -> validateArray "HRAM" HramSize snapshot.HramSnapshot)
            |> Result.bind (fun () -> validateArray "CGB background palette RAM" 64 snapshot.BgPaletteRamSnapshot)
            |> Result.bind (fun () -> validateArray "CGB object palette RAM" 64 snapshot.ObjPaletteRamSnapshot)
            |> Result.bind (fun () ->
                if snapshot.BootRomEnabledSnapshot then
                    match current.BootRom, snapshot.BootRomSha256Snapshot with
                    | Some bootRom, Some expected when bootRom.Sha256 = expected -> Ok()
                    | Some bootRom, Some expected ->
                        Error
                            $"Boot ROM identity mismatch: save state requires {expected}, current boot ROM is {bootRom.Sha256}."
                    | None, Some expected -> Error $"Boot ROM required by save state is unavailable: {expected}."
                    | _, None -> Error "Save state has an enabled boot ROM without an identity."
                else
                    Ok())
            |> Result.bind (fun () -> CartridgeMemory.restoreSnapshot snapshot.CartridgeSnapshot current.Cartridge)
            |> Result.map (fun cartridge ->
                let bootRom =
                    if snapshot.BootRomEnabledSnapshot then
                        current.BootRom
                    else
                        current.BootRom
                        |> Option.filter (fun value -> snapshot.BootRomSha256Snapshot = Some value.Sha256)
                        |> Option.map (fun value -> { value with Enabled = false })

                { Cartridge = cartridge
                  Mode = snapshot.ModeSnapshot
                  BootRom = bootRom
                  Vram = Array.copy snapshot.VramSnapshot
                  Wram = Array.copy snapshot.WramSnapshot
                  Oam = Array.copy snapshot.OamSnapshot
                  Io = restoreIoSnapshot snapshot
                  Hram = Array.copy snapshot.HramSnapshot
                  VramBank = snapshot.VramBankSnapshot &&& 0x01
                  WramBank =
                    if snapshot.WramBankSnapshot = 0 then
                        1
                    else
                        snapshot.WramBankSnapshot &&& 0x07
                  BgPaletteRam = Array.copy snapshot.BgPaletteRamSnapshot
                  ObjPaletteRam = Array.copy snapshot.ObjPaletteRamSnapshot
                  DoubleSpeed = snapshot.DoubleSpeedSnapshot
                  SpeedSwitchPrepared = snapshot.SpeedSwitchPreparedSnapshot
                  HdmaSource = snapshot.HdmaSourceSnapshot
                  HdmaDestination = snapshot.HdmaDestinationSnapshot
                  HdmaRemaining = max 0 snapshot.HdmaRemainingSnapshot
                  HdmaActive = snapshot.HdmaActiveSnapshot
                  Timer = snapshot.TimerSnapshot
                  Lcd = snapshot.LcdSnapshot
                  Joypad = snapshot.JoypadSnapshot
                  Apu = Apu.restore snapshot.ApuSnapshot
                  PendingApuCycles = 0
                  InterruptEnable = snapshot.InterruptEnableSnapshot })

    let internal snapshot memory = MemorySnapshot.capture memory

    let internal restoreSnapshot snapshot current = MemorySnapshot.restore snapshot current

    let private unusableRead = 0xFFuy

    let private lcdEnabled memory = memory.Io[0x40] &&& 0x80uy <> 0uy

    /// Returns the loaded cartridge state.
    let cartridge memory = memory.Cartridge

    /// Returns the current joypad state.
    let joypad memory = memory.Joypad

    /// Returns the active hardware compatibility mode.
    let mode memory = memory.Mode

    /// Returns whether CGB palette RAM supplies the displayed colors.
    let usesColorPalettes memory =
        memory.Mode = Hardware.Cgb || memory.Mode = Hardware.CgbCompatibility

    /// Returns whether the boot ROM is still mapped into the CPU address space.
    let isBootRomEnabled memory =
        memory.BootRom |> Option.exists (fun bootRom -> bootRom.Enabled)

    /// Returns the SHA-256 identity of the attached boot ROM, when present.
    let bootRomSha256 memory =
        memory.BootRom |> Option.map (fun bootRom -> bootRom.Sha256)

    let internal hardwareCyclesForCpuCycles cycles memory =
        if memory.DoubleSpeed then cycles / 2 else cycles

    /// Replaces cartridge state while preserving all other bus state.
    let withCartridge cartridge memory = { memory with Cartridge = cartridge }

    let internal lcdState memory = memory.Lcd

    let internal rawIoByte index memory = memory.Io[index]

    let internal rawVramByte address memory =
        memory.Vram[memory.VramBank * VramBankSize + address - 0x8000]

    let internal rawVramBankByte bank address memory =
        memory.Vram[(bank &&& 0x01) * VramBankSize + address - 0x8000]

    let internal rawOamByte index memory = memory.Oam[index]

    let internal rawBgPaletteByte index memory = memory.BgPaletteRam[index &&& 0x3F]

    let internal rawObjPaletteByte index memory = memory.ObjPaletteRam[index &&& 0x3F]

    let private readInspectionByte (address: uint32) memory =
        let readNativeAddress (nativeAddress: int) =
            match nativeAddress with
            | value when value <= 0x7FFF -> CartridgeMemory.readByte (uint16 value) memory.Cartridge
            | value when value >= 0x8000 && value <= 0x9FFF ->
                memory.Vram[memory.VramBank * VramBankSize + value - 0x8000]
            | value when value >= 0xA000 && value <= 0xBFFF ->
                CartridgeMemory.readPhysicalRamByte 0 (value - 0xA000) memory.Cartridge
            | value when value >= 0xC000 && value <= 0xCFFF -> memory.Wram[value - 0xC000]
            // The RetroAchievements Game Boy map exposes bank 1 here even when
            // a different CGB WRAM bank is selected by SVBK.
            | value when value >= 0xD000 && value <= 0xDFFF -> memory.Wram[WramBankSize + value - 0xD000]
            | value when value >= 0xE000 && value <= 0xFDFF ->
                let mirrored = value - 0x2000

                if mirrored <= 0xCFFF then
                    memory.Wram[mirrored - 0xC000]
                else
                    memory.Wram[WramBankSize + mirrored - 0xD000]
            | value when value >= 0xFE00 && value <= 0xFE9F -> memory.Oam[value - 0xFE00]
            | value when value >= 0xFEA0 && value <= 0xFEFF -> 0xFFuy
            | 0xFF00 -> Joypad.readP1 memory.Joypad
            | 0xFF04 -> Timer.div memory.Timer
            | 0xFF0F -> 0xE0uy ||| (memory.Io[0x0F] &&& 0x1Fuy)
            | 0xFF41 ->
                let raw = memory.Io[0x41] &&& 0xF8uy
                let coincidence = if memory.Lcd.Line = memory.Io[0x45] then 0x04uy else 0uy
                raw ||| coincidence ||| Lcd.modeBits memory.Lcd.Mode
            | 0xFF44 -> memory.Lcd.Line
            | 0xFF4D when memory.Mode = Hardware.Cgb ->
                0x7Euy
                ||| (if memory.DoubleSpeed then 0x80uy else 0uy)
                ||| (if memory.SpeedSwitchPrepared then 0x01uy else 0uy)
            | 0xFF4F when memory.Mode = Hardware.Cgb -> 0xFEuy ||| byte memory.VramBank
            | 0xFF55 when memory.Mode = Hardware.Cgb ->
                if memory.HdmaActive then
                    byte (memory.HdmaRemaining - 1)
                else
                    0xFFuy
            | 0xFF69 when memory.Mode = Hardware.Cgb -> memory.BgPaletteRam[int (memory.Io[0x68] &&& 0x3Fuy)]
            | 0xFF6B when memory.Mode = Hardware.Cgb -> memory.ObjPaletteRam[int (memory.Io[0x6A] &&& 0x3Fuy)]
            | 0xFF6C when memory.Mode = Hardware.Cgb -> 0xFEuy ||| (memory.Io[0x6C] &&& 0x01uy)
            | 0xFF70 when memory.Mode = Hardware.Cgb -> 0xF8uy ||| byte memory.WramBank
            | value when value >= 0xFF00 && value <= 0xFF7F -> memory.Io[value - 0xFF00]
            | value when value >= 0xFF80 && value <= 0xFFFE -> memory.Hram[value - 0xFF80]
            | 0xFFFF -> memory.InterruptEnable
            | _ -> 0xFFuy

        if address <= 0xFFFFu then
            readNativeAddress (int address)
        elif address >= 0x10000u && address <= 0x15FFFu then
            if memory.Mode = Hardware.Cgb then
                let offset = int (address - 0x10000u)
                let bank = 2 + offset / WramBankSize
                memory.Wram[bank * WramBankSize + offset % WramBankSize]
            else
                0xFFuy
        elif address >= 0x16000u && address <= 0x33FFFu then
            let offset = int (address - 0x16000u)
            let bank = 1 + offset / 0x2000
            CartridgeMemory.readPhysicalRamByte bank (offset % 0x2000) memory.Cartridge
        else
            0xFFuy

    /// Copies bytes from the side-effect-free debugger/achievement memory map.
    /// Returns the number of bytes copied before the buffer or mapped range ends.
    let readInspectionMemory address (buffer: byte[]) bufferOffset count memory =
        if
            isNull buffer
            || bufferOffset < 0
            || count < 0
            || bufferOffset > buffer.Length
            || count > buffer.Length - bufferOffset
            || address > 0x33FFFu
        then
            0
        else
            let available = int (0x34000u - address)
            let copied = min count available

            for index = 0 to copied - 1 do
                buffer[bufferOffset + index] <- readInspectionByte (address + uint32 index) memory

            copied

    /// Copies all audio samples currently waiting on the bus.
    let pendingAudioSamples memory =
        synchronizeApu memory |> ignore
        Apu.pendingSamples memory.Apu

    let internal drainAudioSamples memory =
        synchronizeApu memory |> ignore

        Apu.pendingSamples memory.Apu,
        { memory with
            Apu = Apu.clearPendingSamples memory.Apu
            PendingApuCycles = 0 }

    /// Returns bus state with one I/O byte replaced.
    let withIoByte index value memory =
        let next = Array.copy memory.Io
        next[index] <- value
        { memory with Io = next }

    /// Returns bus state with one byte in the selected VRAM bank replaced.
    let withVramByte address value memory =
        let next = Array.copy memory.Vram
        next[memory.VramBank * VramBankSize + address - 0x8000] <- value
        { memory with Vram = next }

    /// Returns bus state with one byte in an explicit VRAM bank replaced.
    let withVramBankByte bank address value memory =
        let next = Array.copy memory.Vram
        next[(bank &&& 0x01) * VramBankSize + address - 0x8000] <- value
        { memory with Vram = next }

    /// Returns bus state with one object attribute memory byte replaced.
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

    module private IoRegisters =
        let lcdStatus memory (lcd: Lcd.State) =
            let raw = memory.Io[0x41] &&& 0xF8uy
            let coincidence = if lcd.Line = memory.Io[0x45] then 0x04uy else 0uy

            raw ||| coincidence ||| Lcd.modeBits lcd.Mode

        let lcdStatusInterruptSignal (memory: Memory) (lcd: Lcd.State) =
            let statRegister = memory.Io[0x41] &&& 0xF8uy

            let coincidenceSelected =
                statRegister &&& 0x40uy <> 0uy && lcd.Line = memory.Io[0x45]

            let modeSelected =
                match lcd.Mode with
                | Lcd.HBlank -> statRegister &&& 0x08uy <> 0uy
                | Lcd.VBlank -> statRegister &&& 0x10uy <> 0uy
                | Lcd.OamSearch -> statRegister &&& 0x20uy <> 0uy
                | Lcd.Transfer -> false

            coincidenceSelected || modeSelected

        let writeApu address value memory =
            let wasApuPowered = memory.Io[0x26] &&& 0x80uy <> 0uy
            let nextIo, apu = Apu.writeRegister (address - 0xFF00) value memory.Io memory.Apu
            let isApuPowered = nextIo[0x26] &&& 0x80uy <> 0uy
            let dividerApuBitHigh = memory.Timer.Divider &&& 0x1000us <> 0us

            let apu =
                if address = 0xFF26 && not wasApuPowered && isApuPowered && dividerApuBitHigh then
                    Apu.skipNextFrameSequencerClock apu
                else
                    apu

            { memory with Io = nextIo; Apu = apu }

    module private CgbMemory =
        let isCgb memory = memory.Mode = Hardware.Cgb

        let selectedWramBank memory =
            if isCgb memory then memory.WramBank else 1

        let wramOffset address memory =
            match address with
            | value when value >= 0xC000 && value <= 0xCFFF -> value - 0xC000
            | value when value >= 0xD000 && value <= 0xDFFF -> selectedWramBank memory * WramBankSize + value - 0xD000
            | value when value >= 0xE000 && value <= 0xEFFF -> value - 0xE000
            | value -> selectedWramBank memory * WramBankSize + value - 0xF000

        let paletteRead paletteRegister (paletteRam: byte[]) =
            let index = int (paletteRegister &&& 0x3Fuy)
            paletteRam[index]

        let paletteWrite indexRegister value (paletteRam: byte[]) =
            let index = int (indexRegister &&& 0x3Fuy)
            let next = Array.copy paletteRam
            next[index] <- value
            next

        let incrementPaletteIndex indexRegister =
            if indexRegister &&& 0x80uy = 0uy then
                indexRegister
            else
                (indexRegister &&& 0x80uy) ||| 0x40uy ||| ((indexRegister + 1uy) &&& 0x3Fuy)

    module private Hdma =
        let private readByte address memory =
            let address = int address

            match address with
            | value when value <= 0x7FFF -> CartridgeMemory.readByte (uint16 value) memory.Cartridge
            | value when value >= 0x8000 && value <= 0x9FFF ->
                memory.Vram[memory.VramBank * VramBankSize + value - 0x8000]
            | value when value >= 0xA000 && value <= 0xBFFF -> CartridgeMemory.readByte (uint16 value) memory.Cartridge
            | value when value >= 0xC000 && value <= 0xDFFF -> memory.Wram[CgbMemory.wramOffset value memory]
            | value when value >= 0xE000 && value <= 0xFDFF -> memory.Wram[CgbMemory.wramOffset value memory]
            | _ -> 0xFFuy

        let copyBlock source destination memory =
            for offset in 0..0x0F do
                let value = readByte (source + uint16 offset) memory
                memory.Vram[memory.VramBank * VramBankSize + int destination - 0x8000 + offset] <- value

            memory

        let runGeneral memory =
            let mutable current = memory
            let mutable source = memory.HdmaSource
            let mutable destination = memory.HdmaDestination

            for _ in 1 .. memory.HdmaRemaining do
                current <- copyBlock source destination current
                source <- source + 0x10us
                destination <- 0x8000us + ((destination + 0x10us - 0x8000us) &&& 0x1FF0us)

            current.Io[0x55] <- 0xFFuy

            { current with
                HdmaSource = source
                HdmaDestination = destination
                HdmaRemaining = 0
                HdmaActive = false }

    let private tryReadBootRom address memory =
        memory.BootRom
        |> Option.bind (fun bootRom ->
            if not bootRom.Enabled then
                None
            else
                match bootRom.Kind with
                | Dmg when address <= 0x00FF -> Some bootRom.Bytes[address]
                | Cgb when address <= 0x00FF || (address >= 0x0200 && address <= 0x08FF) -> Some bootRom.Bytes[address]
                | _ -> None)

    /// Reads one byte through the CPU-visible memory map.
    let readByte (address: uint16) memory =
        if address >= 0xFF10us && address <= 0xFF3Fus then
            synchronizeApu memory |> ignore

        let address = int address

        match tryReadBootRom address memory with
        | Some value -> value
        | None ->
            match address with
            | value when value <= 0x7FFF -> CartridgeMemory.readByte (uint16 value) memory.Cartridge
            | value when value >= 0x8000 && value <= 0x9FFF ->
                if vramBlocked memory then
                    unusableRead
                else
                    memory.Vram[memory.VramBank * VramBankSize + value - 0x8000]
            | value when value >= 0xA000 && value <= 0xBFFF -> CartridgeMemory.readByte (uint16 value) memory.Cartridge
            | value when value >= 0xC000 && value <= 0xDFFF -> memory.Wram[CgbMemory.wramOffset value memory]
            | value when value >= 0xE000 && value <= 0xFDFF -> memory.Wram[CgbMemory.wramOffset value memory]
            | value when value >= 0xFE00 && value <= 0xFE9F ->
                if oamBlocked memory then
                    unusableRead
                else
                    memory.Oam[value - 0xFE00]
            | value when value >= 0xFEA0 && value <= 0xFEFF -> unusableRead
            | 0xFF00 -> Joypad.readP1 memory.Joypad
            | 0xFF04 -> Timer.div memory.Timer
            | 0xFF0F -> 0xE0uy ||| (memory.Io[0x0F] &&& 0x1Fuy)
            | 0xFF41 -> IoRegisters.lcdStatus memory memory.Lcd
            | 0xFF44 -> memory.Lcd.Line
            | 0xFF4D when CgbMemory.isCgb memory ->
                0x7Euy
                ||| (if memory.DoubleSpeed then 0x80uy else 0uy)
                ||| (if memory.SpeedSwitchPrepared then 0x01uy else 0uy)
            | 0xFF4F when CgbMemory.isCgb memory -> 0xFEuy ||| byte memory.VramBank
            | 0xFF55 when CgbMemory.isCgb memory ->
                if memory.HdmaActive then
                    byte (memory.HdmaRemaining - 1)
                else
                    0xFFuy
            | 0xFF69 when CgbMemory.isCgb memory -> CgbMemory.paletteRead memory.Io[0x68] memory.BgPaletteRam
            | 0xFF6B when CgbMemory.isCgb memory -> CgbMemory.paletteRead memory.Io[0x6A] memory.ObjPaletteRam
            | 0xFF6C when CgbMemory.isCgb memory -> 0xFEuy ||| (memory.Io[0x6C] &&& 0x01uy)
            | 0xFF70 when CgbMemory.isCgb memory -> 0xF8uy ||| byte memory.WramBank
            | 0xFF7D
            | 0xFF7E
            | 0xFF7F -> unusableRead
            | value when value >= 0xFF00 && value <= 0xFF7F -> memory.Io[value - 0xFF00]
            | value when value >= 0xFF80 && value <= 0xFFFE -> memory.Hram[value - 0xFF80]
            | 0xFFFF -> memory.InterruptEnable
            | _ -> unusableRead

    let private oamDmaActive memory =
        memory.Io[OamDmaProgressIndex] <> 0xFFuy

    /// Reads one byte through the CPU bus, including OAM DMA access restrictions.
    let internal cpuReadByte address memory =
        if oamDmaActive memory && address >= 0xFE00us && address <= 0xFE9Fus then
            unusableRead
        else
            readByte address memory

    /// Writes one byte through the CPU-visible memory map.
    let writeByte (address: uint16) (value: byte) memory =
        if address = 0xFF04us || (address >= 0xFF10us && address <= 0xFF3Fus) then
            synchronizeApu memory |> ignore

        let address = int address

        match address with
        | addr when addr <= 0x7FFF ->
            { memory with
                Cartridge = CartridgeMemory.writeByte (uint16 addr) value memory.Cartridge }
        | addr when addr >= 0x8000 && addr <= 0x9FFF ->
            if vramBlocked memory then
                memory
            else
                memory.Vram[memory.VramBank * VramBankSize + addr - 0x8000] <- value
                memory
        | addr when addr >= 0xA000 && addr <= 0xBFFF ->
            { memory with
                Cartridge = CartridgeMemory.writeByte (uint16 addr) value memory.Cartridge }
        | addr when addr >= 0xC000 && addr <= 0xDFFF ->
            memory.Wram[CgbMemory.wramOffset addr memory] <- value
            memory
        | addr when addr >= 0xE000 && addr <= 0xFDFF ->
            memory.Wram[CgbMemory.wramOffset addr memory] <- value
            memory
        | addr when addr >= 0xFE00 && addr <= 0xFE9F ->
            if oamBlocked memory then
                memory
            else
                memory.Oam[addr - 0xFE00] <- value
                memory
        | addr when addr >= 0xFEA0 && addr <= 0xFEFF -> memory
        | 0xFF00 ->
            { memory with
                Joypad = Joypad.writeP1 value memory.Joypad }
        | 0xFF04 ->
            let registers: Timer.Registers =
                { Div = Timer.div memory.Timer
                  Tima = memory.Io[0x05]
                  Tma = memory.Io[0x06]
                  Tac = memory.Io[0x07]
                  InterruptFlags = memory.Io[0x0F] }

            let timerResult = Timer.resetDiv memory.Timer registers
            let apu = Apu.resetDiv memory.Timer.Divider memory.Io memory.Apu
            memory.Io[0x04] <- timerResult.Registers.Div
            memory.Io[0x05] <- timerResult.Registers.Tima
            memory.Io[0x0F] <- timerResult.Registers.InterruptFlags
            memory.Io[0x26] <- Apu.statusRegister memory.Io apu

            { memory with
                Timer = timerResult.State
                Apu = apu }
        | 0xFF05 ->
            if memory.Io[TimerReloadMarkerIndex] <> 0uy then
                memory
            else
                let registers: Timer.Registers =
                    { Div = Timer.div memory.Timer
                      Tima = memory.Io[0x05]
                      Tma = memory.Io[0x06]
                      Tac = memory.Io[0x07]
                      InterruptFlags = memory.Io[0x0F] }

                let result = Timer.writeTima value memory.Timer registers
                memory.Io[0x05] <- result.Registers.Tima
                { memory with Timer = result.State }
        | 0xFF06 ->
            memory.Io[0x06] <- value

            if memory.Io[TimerReloadMarkerIndex] <> 0uy then
                memory.Io[0x05] <- value

            memory
        | 0xFF07 ->
            let registers: Timer.Registers =
                { Div = Timer.div memory.Timer
                  Tima = memory.Io[0x05]
                  Tma = memory.Io[0x06]
                  Tac = memory.Io[0x07]
                  InterruptFlags = memory.Io[0x0F] }

            let result = Timer.writeTac value memory.Timer registers
            memory.Io[0x05] <- result.Registers.Tima
            memory.Io[0x07] <- result.Registers.Tac
            { memory with Timer = result.State }
        | 0xFF0F ->
            memory.Io[0x0F] <- value &&& 0x1Fuy
            memory
        | 0xFF41 ->
            memory.Io[0x41] <- value &&& 0xF8uy
            memory
        | 0xFF44 ->
            memory.Io[0x44] <- 0uy

            { memory with
                Lcd = Lcd.resetLine memory.Lcd }
        | 0xFF4C when CgbMemory.isCgb memory && isBootRomEnabled memory ->
            memory.Io[0x4C] <- value
            memory
        | 0xFF50 ->
            if value = 0uy then
                memory
            else
                memory.Io[0x50] <- value

                { memory with
                    Mode =
                        if
                            CgbMemory.isCgb memory
                            && isBootRomEnabled memory
                            && memory.Io[0x4C] &&& 0x04uy <> 0uy
                            && (CartridgeMemory.header memory.Cartridge).CgbSupport = Cartridge.DmgOnly
                        then
                            Hardware.CgbCompatibility
                        else
                            memory.Mode
                    BootRom = memory.BootRom |> Option.map (fun bootRom -> { bootRom with Enabled = false }) }
        | 0xFF46 ->
            memory.Io[0x46] <- value
            memory.Io[OamDmaProgressIndex] <- 0xFDuy
            memory
        | 0xFF4D when CgbMemory.isCgb memory ->
            memory.Io[0x4D] <- 0x7Euy ||| (value &&& 0x01uy)

            { memory with
                SpeedSwitchPrepared = value &&& 0x01uy <> 0uy }
        | 0xFF4F when CgbMemory.isCgb memory ->
            memory.Io[0x4F] <- 0xFEuy ||| (value &&& 0x01uy)

            { memory with
                VramBank = int (value &&& 0x01uy) }
        | 0xFF51 when CgbMemory.isCgb memory ->
            memory.Io[0x51] <- value

            { memory with
                HdmaSource = (uint16 value <<< 8) ||| (memory.HdmaSource &&& 0x00F0us) }
        | 0xFF52 when CgbMemory.isCgb memory ->
            let source = (memory.HdmaSource &&& 0xFF00us) ||| (uint16 (value &&& 0xF0uy))
            memory.Io[0x52] <- value &&& 0xF0uy
            { memory with HdmaSource = source }
        | 0xFF53 when CgbMemory.isCgb memory ->
            let destination =
                0x8000us
                ||| ((uint16 (value &&& 0x1Fuy)) <<< 8)
                ||| (memory.HdmaDestination &&& 0x00F0us)

            memory.Io[0x53] <- value &&& 0x1Fuy

            { memory with
                HdmaDestination = destination }
        | 0xFF54 when CgbMemory.isCgb memory ->
            let destination =
                0x8000us ||| (memory.HdmaDestination &&& 0x1F00us) ||| uint16 (value &&& 0xF0uy)

            memory.Io[0x54] <- value &&& 0xF0uy

            { memory with
                HdmaDestination = destination }
        | 0xFF55 when CgbMemory.isCgb memory ->
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

                if next.HdmaActive then next else Hdma.runGeneral next
        | 0xFF68 when CgbMemory.isCgb memory ->
            memory.Io[0x68] <- value ||| 0x40uy
            memory
        | 0xFF69 when CgbMemory.isCgb memory ->
            let paletteRam = CgbMemory.paletteWrite memory.Io[0x68] value memory.BgPaletteRam
            memory.Io[0x68] <- CgbMemory.incrementPaletteIndex memory.Io[0x68]

            { memory with
                BgPaletteRam = paletteRam }
        | 0xFF6A when CgbMemory.isCgb memory ->
            memory.Io[0x6A] <- value ||| 0x40uy
            memory
        | 0xFF6B when CgbMemory.isCgb memory ->
            let paletteRam = CgbMemory.paletteWrite memory.Io[0x6A] value memory.ObjPaletteRam
            memory.Io[0x6A] <- CgbMemory.incrementPaletteIndex memory.Io[0x6A]

            { memory with
                ObjPaletteRam = paletteRam }
        | 0xFF6C when CgbMemory.isCgb memory ->
            memory.Io[0x6C] <- 0xFEuy ||| (value &&& 0x01uy)
            memory
        | 0xFF70 when CgbMemory.isCgb memory ->
            let bank = int (value &&& 0x07uy)
            let bank = if bank = 0 then 1 else bank
            memory.Io[0x70] <- 0xF8uy ||| byte bank
            { memory with WramBank = bank }
        | 0xFF7D
        | 0xFF7E
        | 0xFF7F -> memory
        | addr when addr >= 0xFF10 && addr <= 0xFF26 -> IoRegisters.writeApu addr value memory
        | addr when addr >= 0xFF00 && addr <= 0xFF7F ->
            memory.Io[addr - 0xFF00] <- value
            memory
        | addr when addr >= 0xFF80 && addr <= 0xFFFE ->
            memory.Hram[addr - 0xFF80] <- value
            memory
        | 0xFFFF -> { memory with InterruptEnable = value }
        | _ -> memory

    /// Writes one byte through the CPU bus, including OAM DMA access restrictions.
    let internal cpuWriteByte address value memory =
        if oamDmaActive memory && address >= 0xFE00us && address <= 0xFE9Fus then
            memory
        else
            writeByte address value memory

    let private advanceCpuClockedDevices cycles memory =
        let hardwareCycles = hardwareCyclesForCpuCycles cycles memory

        if oamDmaActive memory then
            let transfers = max 1 (hardwareCycles / 4)
            let sourceBase = uint16 memory.Io[0x46] <<< 8
            let mutable progress = memory.Io[OamDmaProgressIndex]

            for _ in 1..transfers do
                if progress = 0xFDuy then
                    progress <- 0xFEuy
                elif progress = 0xFEuy then
                    progress <- 0uy
                elif progress < byte OamSize then
                    memory.Oam[int progress] <- readByte (sourceBase + uint16 progress) memory
                    progress <- progress + 1uy

            memory.Io[OamDmaProgressIndex] <- if progress = byte OamSize then 0xFFuy else progress

        let registers: Timer.Registers =
            { Div = Timer.div memory.Timer
              Tima = memory.Io[0x05]
              Tma = memory.Io[0x06]
              Tac = memory.Io[0x07]
              InterruptFlags = 0xE0uy ||| memory.Io[0x0F] }

        let struct (timerResult, timerReloaded) =
            Timer.tickWithReload cycles memory.Timer registers

        memory.Io[TimerReloadMarkerIndex] <- if timerReloaded then 1uy else 0uy

        let isLcdEnabled = lcdEnabled memory

        let lcd =
            if isLcdEnabled then
                Lcd.tick hardwareCycles memory.Lcd
            else
                Lcd.disabled memory.Lcd

        let mutable interruptFlags =
            if isLcdEnabled && memory.Lcd.Line <> 144uy && lcd.Line = 144uy then
                Interrupt.request Interrupt.VBlankBit timerResult.Registers.InterruptFlags
            else
                timerResult.Registers.InterruptFlags

        let statSignal =
            if isLcdEnabled then
                IoRegisters.lcdStatusInterruptSignal memory lcd
            else
                false

        if statSignal && not memory.Lcd.StatSignal then
            interruptFlags <- Interrupt.request Interrupt.LcdStatBit interruptFlags

        memory.Io[0x04] <- timerResult.Registers.Div
        memory.Io[0x05] <- timerResult.Registers.Tima
        memory.Io[0x06] <- timerResult.Registers.Tma
        memory.Io[0x07] <- timerResult.Registers.Tac
        memory.Io[0x0F] <- interruptFlags
        memory.Io[0x41] <- IoRegisters.lcdStatus memory lcd
        memory.Io[0x44] <- lcd.Line

        let previousLcdMode = memory.Lcd.Mode
        memory.Timer <- timerResult.State
        memory.Lcd <- { lcd with StatSignal = statSignal }

        if memory.HdmaActive && lcd.Mode = Lcd.HBlank && previousLcdMode <> Lcd.HBlank then
            Hdma.copyBlock memory.HdmaSource memory.HdmaDestination memory |> ignore
            let remaining = memory.HdmaRemaining - 1
            let source = memory.HdmaSource + 0x10us

            let destination =
                0x8000us + ((memory.HdmaDestination + 0x10us - 0x8000us) &&& 0x1FF0us)

            memory.Io[0x55] <- if remaining = 0 then 0xFFuy else byte (remaining - 1)
            memory.HdmaSource <- source
            memory.HdmaDestination <- destination
            memory.HdmaRemaining <- remaining
            memory.HdmaActive <- remaining > 0

        memory

    let internal beginCpuStep memory = { memory with Timer = memory.Timer }

    let internal tickCpuMachineCycle memory =
        advanceCpuClockedDevices 4 memory |> ignore

        if memory.Io[0x26] &&& 0x80uy = 0uy then
            memory.PendingApuCycles <- 0
        else
            memory.PendingApuCycles <- memory.PendingApuCycles + hardwareCyclesForCpuCycles 4 memory

    /// Advances all bus-owned hardware by the specified CPU cycles.
    let tick cycles memory =
        let next = { memory with Timer = memory.Timer }
        synchronizeApu next |> ignore
        advanceCpuClockedDevices cycles next |> ignore

        next.PendingApuCycles <- hardwareCyclesForCpuCycles cycles next
        synchronizeApu next

    /// Executes the CGB speed-switch behavior associated with the STOP instruction.
    let stop memory =
        synchronizeApu memory |> ignore

        if CgbMemory.isCgb memory && memory.SpeedSwitchPrepared then
            let nextDoubleSpeed = not memory.DoubleSpeed
            memory.Io[0x4D] <- 0x7Euy ||| (if nextDoubleSpeed then 0x80uy else 0uy)

            { memory with
                DoubleSpeed = nextDoubleSpeed
                SpeedSwitchPrepared = false }
        else
            memory

    /// Updates one joypad button and requests an interrupt on a new press.
    let setButton button pressed memory =
        let wasPressed = Set.contains button memory.Joypad.Pressed
        let joypad = Joypad.setButton button pressed memory.Joypad
        let next = { memory with Joypad = joypad }

        if pressed && not wasPressed then
            let flags = readByte 0xFF0Fus next |> Interrupt.request Interrupt.JoypadBit
            writeByte 0xFF0Fus flags next
        else
            next
