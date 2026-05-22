namespace BubiBoy.Core

open System
open System.Text

module Cartridge =
    type CgbSupport =
        | DmgOnly
        | CgbEnhanced
        | CgbOnly

    type SgbSupport =
        | NoSgb
        | SgbEnhanced

    type CartridgeKind =
        | RomOnly
        | Mbc1
        | Mbc1Ram
        | Mbc1RamBattery
        | Mbc2
        | Mbc2Battery
        | Mbc3TimerBattery
        | Mbc3TimerRamBattery
        | Mbc3
        | Mbc3Ram
        | Mbc3RamBattery
        | Mbc5
        | Mbc5Ram
        | Mbc5RamBattery
        | Unknown of byte

    type CartridgeHeader =
        { Title: string
          CgbSupport: CgbSupport
          SgbSupport: SgbSupport
          CartridgeTypeCode: byte
          CartridgeKind: CartridgeKind
          RomSizeCode: byte
          RamSizeCode: byte
          DestinationCode: byte
          HeaderChecksum: byte }

    type RomSize =
        { Code: byte
          Bytes: int
          Banks: int }

    type RamSize =
        { Code: byte
          Bytes: int
          Banks: int }

    let private classifyCartridgeType code =
        match code with
        | 0x00uy -> RomOnly
        | 0x01uy -> Mbc1
        | 0x02uy -> Mbc1Ram
        | 0x03uy -> Mbc1RamBattery
        | 0x05uy -> Mbc2
        | 0x06uy -> Mbc2Battery
        | 0x0Fuy -> Mbc3TimerBattery
        | 0x10uy -> Mbc3TimerRamBattery
        | 0x11uy -> Mbc3
        | 0x12uy -> Mbc3Ram
        | 0x13uy -> Mbc3RamBattery
        | 0x19uy -> Mbc5
        | 0x1Auy -> Mbc5Ram
        | 0x1Buy -> Mbc5RamBattery
        | other -> Unknown other

    let private classifyCgbSupport code =
        match code with
        | 0x80uy -> CgbEnhanced
        | 0xC0uy -> CgbOnly
        | _ -> DmgOnly

    let sgbSupportFromCode code =
        match code with
        | 0x03uy -> SgbEnhanced
        | _ -> NoSgb

    let romSizeFromCode code =
        match code with
        | value when value <= 0x08uy ->
            let banks = 2 <<< int value

            Ok
                { Code = code
                  Bytes = banks * 16 * 1024
                  Banks = banks }
        | _ -> Error $"Unsupported ROM size code: 0x{code:X2}"

    let ramSizeFromCode code =
        match code with
        | 0x00uy ->
            Ok
                { Code = code
                  Bytes = 0
                  Banks = 0 }
        | 0x01uy ->
            Ok
                { Code = code
                  Bytes = 2 * 1024
                  Banks = 1 }
        | 0x02uy ->
            Ok
                { Code = code
                  Bytes = 8 * 1024
                  Banks = 1 }
        | 0x03uy ->
            Ok
                { Code = code
                  Bytes = 32 * 1024
                  Banks = 4 }
        | 0x04uy ->
            Ok
                { Code = code
                  Bytes = 128 * 1024
                  Banks = 16 }
        | 0x05uy ->
            Ok
                { Code = code
                  Bytes = 64 * 1024
                  Banks = 8 }
        | _ -> Error $"Unsupported RAM size code: 0x{code:X2}"

    let private readTitle (rom: byte[]) =
        let titleStart = 0x0134
        let titleLength = 16

        rom[titleStart .. titleStart + titleLength - 1]
        |> Array.takeWhile (fun value -> value <> 0uy)
        |> Encoding.ASCII.GetString

    let parseHeader (rom: byte[]) =
        if isNull rom then
            Error "ROM data is null."
        elif rom.Length < 0x0150 then
            Error "ROM data is too small to contain a Game Boy cartridge header."
        else
            let cartridgeTypeCode = rom[0x0147]

            Ok
                { Title = readTitle rom
                  CgbSupport = classifyCgbSupport rom[0x0143]
                  SgbSupport = sgbSupportFromCode rom[0x0146]
                  CartridgeTypeCode = cartridgeTypeCode
                  CartridgeKind = classifyCartridgeType cartridgeTypeCode
                  RomSizeCode = rom[0x0148]
                  RamSizeCode = rom[0x0149]
                  DestinationCode = rom[0x014A]
                  HeaderChecksum = rom[0x014D] }
