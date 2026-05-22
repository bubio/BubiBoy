module BubiBoy.Core.Tests.CartridgeMemoryTests

open BubiBoy.Core
open Xunit

let private writeAscii offset (text: string) (rom: byte[]) =
    text.ToCharArray()
    |> Array.iteri (fun index ch -> rom[offset + index] <- byte ch)

let private makeRom cartridgeType romSizeCode ramSizeCode bankCount =
    let rom = Array.zeroCreate<byte> (bankCount * 16 * 1024)

    for bank in 0 .. bankCount - 1 do
        rom[bank * 16 * 1024] <- byte bank
        rom[bank * 16 * 1024 + 0x0123] <- byte (bank + 0x40)

    writeAscii 0x0134 "BANKTEST" rom
    rom[0x0147] <- cartridgeType
    rom[0x0148] <- romSizeCode
    rom[0x0149] <- ramSizeCode
    rom

[<Fact>]
let ``ROM-only cartridges read fixed banks`` () =
    let rom = makeRom 0x00uy 0x00uy 0x00uy 2

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        Assert.Equal(0x40uy, CartridgeMemory.readByte 0x0123us image)
        Assert.Equal(0x41uy, CartridgeMemory.readByte 0x4123us image)

[<Fact>]
let ``MBC1 cartridges switch the upper ROM bank`` () =
    let rom = makeRom 0x01uy 0x04uy 0x00uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let bank2 = image |> CartridgeMemory.writeByte 0x2000us 0x02uy
        let bank31 = image |> CartridgeMemory.writeByte 0x2000us 0x1Fuy

        Assert.Equal(0x01uy, CartridgeMemory.readByte 0x4000us image)
        Assert.Equal(0x02uy, CartridgeMemory.readByte 0x4000us bank2)
        Assert.Equal(0x1Fuy, CartridgeMemory.readByte 0x4000us bank31)

[<Fact>]
let ``MBC1 maps bank zero writes to bank one`` () =
    let rom = makeRom 0x01uy 0x04uy 0x00uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let bank1 = image |> CartridgeMemory.writeByte 0x2000us 0x00uy

        Assert.Equal(0x01uy, CartridgeMemory.readByte 0x4000us bank1)
