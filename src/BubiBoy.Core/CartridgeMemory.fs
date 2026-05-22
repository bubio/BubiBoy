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

    type CartridgeImage =
        { Header: Cartridge.CartridgeHeader
          Rom: byte[]
          RomBanks: int
          Ram: byte[]
          RamBanks: int
          Mbc1: Mbc1State option }

    let private bankSize = 16 * 1024
    let private ramBankSize = 8 * 1024

    let private supportsMbc1 kind =
        match kind with
        | Cartridge.Mbc1
        | Cartridge.Mbc1Ram
        | Cartridge.Mbc1RamBattery -> true
        | _ -> false

    let private supportsRam kind =
        match kind with
        | Cartridge.Mbc1Ram
        | Cartridge.Mbc1RamBattery
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
                        if supportsRam header.CartridgeKind then
                            Array.zeroCreate<byte> ramSize.Bytes
                        else
                            Array.empty

                    Ok
                        { Header = header
                          Rom = rom
                          RomBanks = romSize.Banks
                          Ram = ramBytes
                          RamBanks = ramSize.Banks
                          Mbc1 = if supportsMbc1 header.CartridgeKind then Some defaultMbc1 else None }

    let private normalizeRomBank bankCount bank =
        if bankCount <= 0 then
            0
        else
            bank % bankCount

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

    let readByte (address: uint16) image =
        let address = int address

        match address with
        | value when value >= 0x0000 && value <= 0x3FFF ->
            match image.Mbc1 with
            | Some state -> readRomBank image (mbc1LowerRomBank state) value
            | None -> readRomBank image 0 value
        | value when value >= 0x4000 && value <= 0x7FFF ->
            let offset = value - 0x4000

            match image.Mbc1 with
            | Some state -> readRomBank image (mbc1UpperRomBank state) offset
            | None -> readRomBank image 1 offset
        | value when value >= 0xA000 && value <= 0xBFFF ->
            match image.Mbc1 with
            | Some state when state.RamEnabled && image.Ram.Length > 0 ->
                let ramBank =
                    match state.BankingMode with
                    | RomBanking -> 0
                    | RamBanking -> state.BankHigh2

                image.Ram[ramBank * ramBankSize + value - 0xA000]
            | _ -> 0xFFuy
        | _ -> 0xFFuy

    let writeByte (address: uint16) (value: byte) image =
        let address = int address
        let value = int value

        match image.Mbc1 with
        | None -> image
        | Some state ->
            let nextState =
                match address with
                | addr when addr >= 0x0000 && addr <= 0x1FFF ->
                    { state with RamEnabled = value &&& 0x0F = 0x0A }
                | addr when addr >= 0x2000 && addr <= 0x3FFF ->
                    let low5 = value &&& 0x1F
                    { state with RomBankLow5 = if low5 = 0 then 1 else low5 }
                | addr when addr >= 0x4000 && addr <= 0x5FFF ->
                    { state with BankHigh2 = value &&& 0x03 }
                | addr when addr >= 0x6000 && addr <= 0x7FFF ->
                    { state with BankingMode = if value &&& 0x01 = 0 then RomBanking else RamBanking }
                | _ -> state

            { image with Mbc1 = Some nextState }
