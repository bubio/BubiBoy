namespace BubiBoy.Core

module CartridgeMemory =
    type BankingMode =
        | RomBanking
        | RamBanking

    type Mbc1State =
        { RamEnabled: bool
          RomBankLow5: int
          BankHigh2: int
          BankingMode: BankingMode }

    type Mbc2State =
        { RamEnabled: bool
          RomBank: int }

    type Mbc3State =
        { RamEnabled: bool
          RomBank: int
          RamOrRtcSelect: int
          HasRtc: bool
          RtcRegisters: byte[]
          LatchedRtcRegisters: byte[] option
          RtcLatchPrepared: bool }

    type Mbc5State =
        { RamEnabled: bool
          RomBankLow8: int
          RomBankHigh1: int
          RamBank: int }

    type MbcState =
        | NoMbc
        | Mbc1 of Mbc1State
        | Mbc2 of Mbc2State
        | Mbc3 of Mbc3State
        | Mbc5 of Mbc5State

    type RtcSave =
        { Registers: byte[]
          LatchedRegisters: byte[] option
          LatchPrepared: bool }

    type CartridgeImage =
        private
            { Header: Cartridge.CartridgeHeader
              Rom: byte[]
              RomBanks: int
              Ram: byte[]
              RamBanks: int
              Mbc: MbcState }

    type Snapshot =
        { HeaderSnapshot: Cartridge.CartridgeHeader
          RomLengthSnapshot: int
          RomBanksSnapshot: int
          RamSnapshot: byte[]
          RamBanksSnapshot: int
          MbcSnapshot: MbcState }

    type BankDebug =
        | NoBanking
        | Mbc1Debug of romBankLow5: int * bankHigh2: int * bankingMode: BankingMode * rom0Bank: int * romXBank: int
        | Mbc2Debug of romBank: int * ramEnabled: bool
        | Mbc3Debug of romBank: int * ramOrRtcSelect: int * ramEnabled: bool
        | Mbc5Debug of romBank: int * ramBank: int * ramEnabled: bool

    let private bankSize = 16 * 1024
    let private ramBankSize = 8 * 1024

    let private supportsMbc1 kind =
        match kind with
        | Cartridge.Mbc1
        | Cartridge.Mbc1Ram
        | Cartridge.Mbc1RamBattery -> true
        | _ -> false

    let private supportsMbc2 kind =
        match kind with
        | Cartridge.Mbc2
        | Cartridge.Mbc2Battery -> true
        | _ -> false

    let private supportsMbc3 kind =
        match kind with
        | Cartridge.Mbc3TimerBattery
        | Cartridge.Mbc3TimerRamBattery
        | Cartridge.Mbc3
        | Cartridge.Mbc3Ram
        | Cartridge.Mbc3RamBattery -> true
        | _ -> false

    let private supportsMbc5 kind =
        match kind with
        | Cartridge.Mbc5
        | Cartridge.Mbc5Ram
        | Cartridge.Mbc5RamBattery -> true
        | _ -> false

    let private hasBattery kind =
        match kind with
        | Cartridge.Mbc1RamBattery
        | Cartridge.Mbc2Battery
        | Cartridge.Mbc3TimerBattery
        | Cartridge.Mbc3TimerRamBattery
        | Cartridge.Mbc3RamBattery
        | Cartridge.Mbc5RamBattery -> true
        | _ -> false

    let private supportsRam kind =
        match kind with
        | Cartridge.Mbc1Ram
        | Cartridge.Mbc1RamBattery
        | Cartridge.Mbc2
        | Cartridge.Mbc2Battery
        | Cartridge.Mbc3TimerRamBattery
        | Cartridge.Mbc3Ram
        | Cartridge.Mbc3RamBattery
        | Cartridge.Mbc5Ram
        | Cartridge.Mbc5RamBattery -> true
        | _ -> false

    let private defaultMbc1 =
        { RamEnabled = false
          RomBankLow5 = 1
          BankHigh2 = 0
          BankingMode = RomBanking }

    let private defaultMbc2 =
        { RamEnabled = false
          RomBank = 1 }

    let private defaultMbc3 hasRtc =
        { RamEnabled = false
          RomBank = 1
          RamOrRtcSelect = 0
          HasRtc = hasRtc
          RtcRegisters = Array.zeroCreate 5
          LatchedRtcRegisters = None
          RtcLatchPrepared = false }

    let private defaultMbc5 =
        { RamEnabled = false
          RomBankLow8 = 1
          RomBankHigh1 = 0
          RamBank = 0 }

    let private mbcState kind =
        if supportsMbc1 kind then Mbc1 defaultMbc1
        elif supportsMbc2 kind then Mbc2 defaultMbc2
        elif supportsMbc3 kind then
            let hasRtc =
                match kind with
                | Cartridge.Mbc3TimerBattery
                | Cartridge.Mbc3TimerRamBattery -> true
                | _ -> false

            Mbc3(defaultMbc3 hasRtc)
        elif supportsMbc5 kind then Mbc5 defaultMbc5
        else NoMbc

    let create (rom: byte[]) =
        match Cartridge.parseHeader rom with
        | Error message -> Error message
        | Ok header ->
            match Cartridge.romSizeFromCode header.RomSizeCode, Cartridge.ramSizeFromCode header.RamSizeCode with
            | Error message, _ -> Error message
            | _, Error message -> Error message
            | Ok romSize, Ok ramSize ->
                if rom.Length < romSize.Bytes then
                    Error $"ROM data is smaller than the size declared in the header: expected {romSize.Bytes} bytes, got {rom.Length} bytes."
                else
                    let ramBytes =
                        if supportsMbc2 header.CartridgeKind then
                            Array.zeroCreate<byte> 512
                        elif supportsRam header.CartridgeKind then
                            Array.zeroCreate<byte> ramSize.Bytes
                        else
                            Array.empty

                    Ok
                        { Header = header
                          Rom = Array.copy rom
                          RomBanks = romSize.Banks
                          Ram = ramBytes
                          RamBanks = ramSize.Banks
                          Mbc = mbcState header.CartridgeKind }

    let private normalizeRomBank bankCount bank =
        if bankCount <= 0 then
            0
        else
            bank % bankCount

    let private normalizeBankForDebug bankCount bank =
        if bankCount <= 0 then 0 else bank % bankCount

    let private mbc1LowerRomBank state =
        match state.BankingMode with
        | RomBanking -> 0
        | RamBanking -> state.BankHigh2 <<< 5

    let private mbc1UpperRomBank state =
        let rawBank = (state.BankHigh2 <<< 5) ||| state.RomBankLow5
        if rawBank &&& 0x1F = 0 then rawBank ||| 1 else rawBank

    let private readRomBank image bank offset =
        let normalizedBank = normalizeRomBank image.RomBanks bank
        image.Rom[normalizedBank * bankSize + offset]

    let private readRamBank image bank offset =
        if image.Ram.Length = 0 then
            0xFFuy
        else
            let normalizedBank = normalizeRomBank image.RamBanks bank
            let baseOffset = normalizedBank * ramBankSize
            let bankedOffset = baseOffset + (offset % ramBankSize)
            let effectiveOffset = bankedOffset % image.Ram.Length
            image.Ram[effectiveOffset]

    let private writeRamBank image bank offset value =
        if image.Ram.Length = 0 then
            image
        else
            let normalizedBank = normalizeRomBank image.RamBanks bank
            let nextRam = Array.copy image.Ram
            let baseOffset = normalizedBank * ramBankSize
            let bankedOffset = baseOffset + (offset % ramBankSize)
            let effectiveOffset = bankedOffset % nextRam.Length
            nextRam[effectiveOffset] <- value
            { image with Ram = nextRam }

    let private mbc3RtcRegisterIndex selector =
        if selector >= 0x08 && selector <= 0x0C then
            Some(selector - 0x08)
        else
            None

    let private rtcDay (registers: byte[]) =
        int registers[3] ||| ((int registers[4] &&& 0x01) <<< 8)

    let private rtcHalted (registers: byte[]) =
        registers[4] &&& 0x40uy <> 0uy

    let private rtcCarry (registers: byte[]) =
        registers[4] &&& 0x80uy <> 0uy

    let private normalizeRtcRegister index value =
        match index with
        | 0
        | 1 -> value % 60
        | 2 -> value % 24
        | 3 -> value &&& 0xFF
        | 4 -> value &&& 0xC1
        | _ -> value &&& 0xFF

    let private setRtcFromTotalSeconds carry halted totalSeconds (registers: byte[]) =
        let secondsPerMinute = 60
        let secondsPerHour = secondsPerMinute * 60
        let secondsPerDay = secondsPerHour * 24
        let boundedTotal = max 0 totalSeconds
        let day = min 511 (boundedTotal / secondsPerDay)
        let remainderAfterDays = boundedTotal % secondsPerDay
        let hour = remainderAfterDays / secondsPerHour
        let remainderAfterHours = remainderAfterDays % secondsPerHour
        let minute = remainderAfterHours / secondsPerMinute
        let second = remainderAfterHours % secondsPerMinute
        let high =
            (if day &&& 0x100 <> 0 then 0x01uy else 0uy)
            ||| (if halted then 0x40uy else 0uy)
            ||| (if carry then 0x80uy else 0uy)

        registers[0] <- byte second
        registers[1] <- byte minute
        registers[2] <- byte hour
        registers[3] <- byte (day &&& 0xFF)
        registers[4] <- high

    let private rtcTotalSeconds (registers: byte[]) =
        int registers[0]
        + int registers[1] * 60
        + int registers[2] * 60 * 60
        + rtcDay registers * 24 * 60 * 60

    let private advanceRtcRegisters seconds (registers: byte[]) =
        let next = Array.copy registers

        if seconds > 0 && not (rtcHalted next) then
            let secondsPerDay = 24 * 60 * 60
            let currentTotal = rtcTotalSeconds next
            let advancedTotal = currentTotal + seconds
            let carry = rtcCarry next || advancedTotal >= 512 * secondsPerDay
            let wrappedTotal = advancedTotal % (512 * secondsPerDay)
            setRtcFromTotalSeconds carry false wrappedTotal next

        next

    let private readMbc3Rtc state rtcIndex =
        if not state.HasRtc then
            0xFFuy
        else
            let registers =
                match state.LatchedRtcRegisters with
                | Some latched -> latched
                | None -> state.RtcRegisters

            registers[rtcIndex]

    let private writeMbc3Rtc state rtcIndex value =
        if not state.HasRtc then
            state
        else
            let nextRtc = Array.copy state.RtcRegisters
            nextRtc[rtcIndex] <- byte (normalizeRtcRegister rtcIndex (int value))
            { state with RtcRegisters = nextRtc }

    let private latchMbc3Rtc value state =
        if not state.HasRtc then
            state
        else if value = 0 then
            { state with RtcLatchPrepared = true }
        else if value = 1 && state.RtcLatchPrepared then
            { state with
                LatchedRtcRegisters = Some(Array.copy state.RtcRegisters)
                RtcLatchPrepared = false }
        else
            { state with RtcLatchPrepared = false }

    let hasBatteryBackedRam image =
        hasBattery image.Header.CartridgeKind && image.Ram.Length > 0

    let header image =
        image.Header

    let romLength image =
        image.Rom.Length

    let snapshot (image: CartridgeImage) : Snapshot =
        { HeaderSnapshot = image.Header
          RomLengthSnapshot = image.Rom.Length
          RomBanksSnapshot = image.RomBanks
          RamSnapshot = Array.copy image.Ram
          RamBanksSnapshot = image.RamBanks
          MbcSnapshot = image.Mbc }

    let restoreSnapshot (snapshot: Snapshot) (image: CartridgeImage) =
        if snapshot.RomLengthSnapshot <> image.Rom.Length then
            Error $"ROM size mismatch: expected {snapshot.RomLengthSnapshot} bytes, got {image.Rom.Length} bytes."
        elif snapshot.HeaderSnapshot.CartridgeTypeCode <> image.Header.CartridgeTypeCode
             || snapshot.HeaderSnapshot.RomSizeCode <> image.Header.RomSizeCode
             || snapshot.HeaderSnapshot.RamSizeCode <> image.Header.RamSizeCode
             || snapshot.HeaderSnapshot.HeaderChecksum <> image.Header.HeaderChecksum
             || snapshot.HeaderSnapshot.Title <> image.Header.Title then
            Error "Save state ROM identity does not match the loaded cartridge."
        elif isNull snapshot.RamSnapshot then
            Error "Save state cartridge RAM is null."
        elif snapshot.RamSnapshot.Length <> image.Ram.Length then
            Error $"Save state RAM size mismatch: expected {image.Ram.Length} bytes, got {snapshot.RamSnapshot.Length} bytes."
        else
            Ok
                { image with
                    Ram = Array.copy snapshot.RamSnapshot
                    RamBanks = snapshot.RamBanksSnapshot
                    RomBanks = snapshot.RomBanksSnapshot
                    Mbc = snapshot.MbcSnapshot }

    let bankDebug image =
        match image.Mbc with
        | NoMbc -> NoBanking
        | Mbc1 state ->
            let upperRaw = (state.BankHigh2 <<< 5) ||| state.RomBankLow5
            let upperBank = if upperRaw &&& 0x1F = 0 then upperRaw ||| 1 else upperRaw
            let lowerBank =
                match state.BankingMode with
                | RomBanking -> 0
                | RamBanking -> state.BankHigh2 <<< 5

            Mbc1Debug(
                state.RomBankLow5,
                state.BankHigh2,
                state.BankingMode,
                normalizeBankForDebug image.RomBanks lowerBank,
                normalizeBankForDebug image.RomBanks upperBank
            )
        | Mbc2 state -> Mbc2Debug(normalizeBankForDebug image.RomBanks state.RomBank, state.RamEnabled)
        | Mbc3 state -> Mbc3Debug(normalizeBankForDebug image.RomBanks state.RomBank, state.RamOrRtcSelect, state.RamEnabled)
        | Mbc5 state ->
            let romBank = (state.RomBankHigh1 <<< 8) ||| state.RomBankLow8
            Mbc5Debug(normalizeBankForDebug image.RomBanks romBank, state.RamBank, state.RamEnabled)

    let exportSaveRam image =
        if hasBatteryBackedRam image then
            Some(Array.copy image.Ram)
        else
            None

    let importSaveRam (saveRam: byte[]) image =
        if isNull saveRam then
            Error "Save RAM data is null."
        elif not (hasBatteryBackedRam image) then
            Error "Cartridge does not have battery-backed RAM."
        elif saveRam.Length <> image.Ram.Length then
            Error $"Save RAM size mismatch: expected {image.Ram.Length} bytes, got {saveRam.Length} bytes."
        else
            Ok { image with Ram = Array.copy saveRam }

    let hasRtc image =
        match image.Mbc with
        | Mbc3 state -> state.HasRtc
        | _ -> false

    let exportRtc image =
        match image.Mbc with
        | Mbc3 state when state.HasRtc ->
            Some
                { Registers = Array.copy state.RtcRegisters
                  LatchedRegisters = state.LatchedRtcRegisters |> Option.map Array.copy
                  LatchPrepared = state.RtcLatchPrepared }
        | _ -> None

    let importRtc rtc image =
        let validRegisters (registers: byte[]) =
            not (isNull registers) && registers.Length = 5

        match image.Mbc with
        | Mbc3 state when state.HasRtc ->
            if not (validRegisters rtc.Registers) then
                Error "RTC register data must contain exactly 5 bytes."
            else
                match rtc.LatchedRegisters with
                | Some latched when not (validRegisters latched) ->
                    Error "Latched RTC register data must contain exactly 5 bytes."
                | _ ->
                    Ok
                        { image with
                            Mbc =
                                Mbc3
                                    { state with
                                        RtcRegisters = Array.copy rtc.Registers
                                        LatchedRtcRegisters = rtc.LatchedRegisters |> Option.map Array.copy
                                        RtcLatchPrepared = rtc.LatchPrepared } }
        | _ -> Error "Cartridge does not have an MBC3 RTC."

    let advanceRtcSeconds seconds image =
        match image.Mbc with
        | Mbc3 state when state.HasRtc ->
            { image with Mbc = Mbc3 { state with RtcRegisters = advanceRtcRegisters seconds state.RtcRegisters } }
        | _ -> image

    let readByte (address: uint16) image =
        let address = int address

        match address with
        | value when value >= 0x0000 && value <= 0x3FFF ->
            match image.Mbc with
            | Mbc1 state -> readRomBank image (mbc1LowerRomBank state) value
            | _ -> readRomBank image 0 value
        | value when value >= 0x4000 && value <= 0x7FFF ->
            let offset = value - 0x4000

            match image.Mbc with
            | Mbc1 state -> readRomBank image (mbc1UpperRomBank state) offset
            | Mbc2 state -> readRomBank image state.RomBank offset
            | Mbc3 state -> readRomBank image state.RomBank offset
            | Mbc5 state -> readRomBank image ((state.RomBankHigh1 <<< 8) ||| state.RomBankLow8) offset
            | NoMbc -> readRomBank image 1 offset
        | value when value >= 0xA000 && value <= 0xBFFF ->
            let offset = value - 0xA000

            match image.Mbc with
            | Mbc1 state when state.RamEnabled && image.Ram.Length > 0 ->
                let ramBank =
                    match state.BankingMode with
                    | RomBanking -> 0
                    | RamBanking -> state.BankHigh2

                readRamBank image ramBank offset
            | Mbc2 state when state.RamEnabled && image.Ram.Length > 0 ->
                0xF0uy ||| (image.Ram[offset &&& 0x01FF] &&& 0x0Fuy)
            | Mbc3 state when state.RamEnabled ->
                match mbc3RtcRegisterIndex state.RamOrRtcSelect with
                | Some rtcIndex -> readMbc3Rtc state rtcIndex
                | None when state.RamOrRtcSelect >= 0 && state.RamOrRtcSelect <= 3 ->
                    readRamBank image state.RamOrRtcSelect offset
                | _ -> 0xFFuy
            | Mbc5 state when state.RamEnabled && image.Ram.Length > 0 ->
                readRamBank image state.RamBank offset
            | _ -> 0xFFuy
        | _ -> 0xFFuy

    let writeByte (address: uint16) (value: byte) image =
        let address = int address
        let numericValue = int value

        match image.Mbc with
        | NoMbc -> image
        | Mbc1 state ->
            match address with
            | addr when addr >= 0x0000 && addr <= 0x1FFF ->
                { image with Mbc = Mbc1 { state with RamEnabled = numericValue &&& 0x0F = 0x0A } }
            | addr when addr >= 0x2000 && addr <= 0x3FFF ->
                let low5 = numericValue &&& 0x1F
                { image with Mbc = Mbc1 { state with RomBankLow5 = if low5 = 0 then 1 else low5 } }
            | addr when addr >= 0x4000 && addr <= 0x5FFF ->
                { image with Mbc = Mbc1 { state with BankHigh2 = numericValue &&& 0x03 } }
            | addr when addr >= 0x6000 && addr <= 0x7FFF ->
                { image with
                    Mbc = Mbc1 { state with BankingMode = if numericValue &&& 0x01 = 0 then RomBanking else RamBanking } }
            | addr when addr >= 0xA000 && addr <= 0xBFFF && state.RamEnabled ->
                let ramBank =
                    match state.BankingMode with
                    | RomBanking -> 0
                    | RamBanking -> state.BankHigh2

                writeRamBank image ramBank (addr - 0xA000) value
            | _ -> image
        | Mbc2 state ->
            match address with
            | addr when addr >= 0x0000 && addr <= 0x3FFF ->
                if addr &&& 0x0100 = 0 then
                    { image with Mbc = Mbc2 { state with RamEnabled = numericValue &&& 0x0F = 0x0A } }
                else
                    let bank = numericValue &&& 0x0F
                    { image with Mbc = Mbc2 { state with RomBank = if bank = 0 then 1 else bank } }
            | addr when addr >= 0xA000 && addr <= 0xBFFF && state.RamEnabled ->
                let nextRam = Array.copy image.Ram
                nextRam[(addr - 0xA000) &&& 0x01FF] <- value &&& 0x0Fuy
                { image with Ram = nextRam }
            | _ -> image
        | Mbc3 state ->
            match address with
            | addr when addr >= 0x0000 && addr <= 0x1FFF ->
                { image with Mbc = Mbc3 { state with RamEnabled = numericValue &&& 0x0F = 0x0A } }
            | addr when addr >= 0x2000 && addr <= 0x3FFF ->
                let bank = numericValue &&& 0x7F
                { image with Mbc = Mbc3 { state with RomBank = if bank = 0 then 1 else bank } }
            | addr when addr >= 0x4000 && addr <= 0x5FFF ->
                { image with Mbc = Mbc3 { state with RamOrRtcSelect = numericValue } }
            | addr when addr >= 0x6000 && addr <= 0x7FFF ->
                { image with Mbc = Mbc3(latchMbc3Rtc (numericValue &&& 0x01) state) }
            | addr when addr >= 0xA000 && addr <= 0xBFFF && state.RamEnabled ->
                match mbc3RtcRegisterIndex state.RamOrRtcSelect with
                | Some rtcIndex ->
                    { image with Mbc = Mbc3(writeMbc3Rtc state rtcIndex value) }
                | None when state.RamOrRtcSelect >= 0 && state.RamOrRtcSelect <= 3 ->
                    writeRamBank image state.RamOrRtcSelect (addr - 0xA000) value
                | _ -> image
            | _ -> image
        | Mbc5 state ->
            match address with
            | addr when addr >= 0x0000 && addr <= 0x1FFF ->
                { image with Mbc = Mbc5 { state with RamEnabled = numericValue &&& 0x0F = 0x0A } }
            | addr when addr >= 0x2000 && addr <= 0x2FFF ->
                { image with Mbc = Mbc5 { state with RomBankLow8 = numericValue &&& 0xFF } }
            | addr when addr >= 0x3000 && addr <= 0x3FFF ->
                { image with Mbc = Mbc5 { state with RomBankHigh1 = numericValue &&& 0x01 } }
            | addr when addr >= 0x4000 && addr <= 0x5FFF ->
                { image with Mbc = Mbc5 { state with RamBank = numericValue &&& 0x0F } }
            | addr when addr >= 0xA000 && addr <= 0xBFFF && state.RamEnabled ->
                writeRamBank image state.RamBank (addr - 0xA000) value
            | _ -> image
