module BubiBoy.Core.Tests.CartridgeHeaderTests

open BubiBoy.Core
open Xunit

let private blankRom () = Array.zeroCreate<byte> 0x0150

let private writeAscii offset (text: string) (rom: byte[]) =
    text.ToCharArray()
    |> Array.iteri (fun index ch -> rom[offset + index] <- byte ch)

[<Fact>]
let ``parseHeader reads title and cartridge metadata`` () =
    let rom = blankRom ()
    writeAscii 0x0134 "BUBIBOY" rom
    rom[0x0143] <- 0x80uy
    rom[0x0146] <- 0x03uy
    rom[0x0147] <- 0x01uy
    rom[0x0148] <- 0x02uy
    rom[0x0149] <- 0x03uy
    rom[0x014A] <- 0x01uy
    rom[0x014D] <- 0x42uy

    let actual = Cartridge.parseHeader rom

    match actual with
    | Ok header ->
        Assert.Equal("BUBIBOY", header.Title)
        Assert.Equal(Cartridge.CgbEnhanced, header.CgbSupport)
        Assert.Equal(Cartridge.SgbEnhanced, header.SgbSupport)
        Assert.Equal(0x01uy, header.CartridgeTypeCode)
        Assert.Equal(Cartridge.Mbc1, header.CartridgeKind)
        Assert.Equal(0x02uy, header.RomSizeCode)
        Assert.Equal(0x03uy, header.RamSizeCode)
        Assert.Equal(0x01uy, header.DestinationCode)
        Assert.Equal(0x42uy, header.HeaderChecksum)
    | Error message -> Assert.Fail message

[<Fact>]
let ``parseHeader rejects data smaller than header`` () =
    let actual = Cartridge.parseHeader (Array.zeroCreate<byte> 0x014F)

    match actual with
    | Ok _ -> Assert.Fail "Expected short ROM data to be rejected."
    | Error message -> Assert.Contains("too small", message)

[<Fact>]
let ``romSizeFromCode converts standard ROM size codes`` () =
    let actual = Cartridge.romSizeFromCode 0x04uy

    match actual with
    | Ok size ->
        Assert.Equal(512 * 1024, size.Bytes)
        Assert.Equal(32, size.Banks)
    | Error message -> Assert.Fail message

[<Fact>]
let ``ramSizeFromCode converts standard RAM size codes`` () =
    let actual = Cartridge.ramSizeFromCode 0x03uy

    match actual with
    | Ok size ->
        Assert.Equal(32 * 1024, size.Bytes)
        Assert.Equal(4, size.Banks)
    | Error message -> Assert.Fail message

[<Fact>]
let ``sgbSupportFromCode recognizes SGB enhanced cartridges`` () =
    Assert.Equal(Cartridge.SgbEnhanced, Cartridge.sgbSupportFromCode 0x03uy)
    Assert.Equal(Cartridge.NoSgb, Cartridge.sgbSupportFromCode 0x00uy)
