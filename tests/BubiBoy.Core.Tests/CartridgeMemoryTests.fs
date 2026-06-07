module BubiBoy.Core.Tests.CartridgeMemoryTests

open BubiBoy.Core
open Xunit

let private writeAscii offset (text: string) (rom: byte[]) =
    text.ToCharArray()
    |> Array.iteri (fun index ch -> rom[offset + index] <- byte ch)

let private makeRom cartridgeType romSizeCode ramSizeCode bankCount =
    let rom = Array.zeroCreate<byte> (bankCount * 16 * 1024)

    for bank in 0 .. bankCount - 1 do
        rom[bank * 16 * 1024] <- byte (bank >>> 8)
        rom[bank * 16 * 1024 + 0x0123] <- byte ((bank + 0x40) &&& 0xFF)

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

        Assert.Equal(0x41uy, CartridgeMemory.readByte 0x4123us image)
        Assert.Equal(0x42uy, CartridgeMemory.readByte 0x4123us bank2)
        Assert.Equal(0x5Fuy, CartridgeMemory.readByte 0x4123us bank31)

[<Fact>]
let ``MBC1 maps bank zero writes to bank one`` () =
    let rom = makeRom 0x01uy 0x04uy 0x00uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let bank1 = image |> CartridgeMemory.writeByte 0x2000us 0x00uy

        Assert.Equal(0x41uy, CartridgeMemory.readByte 0x4123us bank1)

[<Fact>]
let ``MBC1 RAM writes require enable and honor RAM banking mode`` () =
    let rom = makeRom 0x03uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let ignored = image |> CartridgeMemory.writeByte 0xA000us 0x11uy

        let bank2 =
            image
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.writeByte 0x6000us 0x01uy
            |> CartridgeMemory.writeByte 0x4000us 0x02uy
            |> CartridgeMemory.writeByte 0xA000us 0x22uy

        let bank0 =
            bank2
            |> CartridgeMemory.writeByte 0x4000us 0x00uy
            |> CartridgeMemory.writeByte 0xA000us 0x33uy

        Assert.Equal(0xFFuy, CartridgeMemory.readByte 0xA000us ignored)
        Assert.Equal(0x33uy, CartridgeMemory.readByte 0xA000us bank0)
        Assert.Equal(0x22uy, CartridgeMemory.readByte 0xA000us (bank0 |> CartridgeMemory.writeByte 0x4000us 0x02uy))

[<Fact>]
let ``two KiB external RAM mirrors across cartridge RAM address range`` () =
    let rom = makeRom 0x03uy 0x04uy 0x01uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let written =
            image
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.writeByte 0xA000us 0x12uy
            |> CartridgeMemory.writeByte 0xA800us 0x34uy

        Assert.Equal(0x34uy, CartridgeMemory.readByte 0xA000us written)
        Assert.Equal(0x34uy, CartridgeMemory.readByte 0xA800us written)

[<Fact>]
let ``create copies ROM bytes defensively`` () =
    let rom = makeRom 0x00uy 0x00uy 0x00uy 2

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        rom[0x0123] <- 0x99uy
        Assert.Equal(0x40uy, CartridgeMemory.readByte 0x0123us image)

[<Fact>]
let ``MBC2 switches ROM banks and stores internal nibble RAM`` () =
    let rom = makeRom 0x05uy 0x03uy 0x00uy 16

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let bank3 =
            image
            |> CartridgeMemory.writeByte 0x2100us 0x03uy
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.writeByte 0xA123us 0xBEuy

        Assert.Equal(0x43uy, CartridgeMemory.readByte 0x4123us bank3)
        Assert.Equal(0xFEuy, CartridgeMemory.readByte 0xA123us bank3)

[<Fact>]
let ``MBC3 switches ROM banks RAM banks and deterministic RTC registers`` () =
    let rom = makeRom 0x10uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let withRam =
            image
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.writeByte 0x2000us 0x05uy
            |> CartridgeMemory.writeByte 0x4000us 0x01uy
            |> CartridgeMemory.writeByte 0xA000us 0x66uy

        let withRtc =
            withRam
            |> CartridgeMemory.writeByte 0x4000us 0x08uy
            |> CartridgeMemory.writeByte 0xA000us 0x12uy

        Assert.Equal(0x45uy, CartridgeMemory.readByte 0x4123us withRam)
        Assert.Equal(0x66uy, CartridgeMemory.readByte 0xA000us (withRtc |> CartridgeMemory.writeByte 0x4000us 0x01uy))
        Assert.Equal(0x12uy, CartridgeMemory.readByte 0xA000us (withRtc |> CartridgeMemory.writeByte 0x4000us 0x08uy))

[<Fact>]
let ``MBC3 RTC advances deterministically across seconds minutes hours and days`` () =
    let rom = makeRom 0x10uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let advanced =
            image
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.advanceRtcSeconds (1 + 2 * 60 + 3 * 60 * 60 + 4 * 24 * 60 * 60)

        Assert.Equal(0x01uy, CartridgeMemory.readByte 0xA000us (advanced |> CartridgeMemory.writeByte 0x4000us 0x08uy))
        Assert.Equal(0x02uy, CartridgeMemory.readByte 0xA000us (advanced |> CartridgeMemory.writeByte 0x4000us 0x09uy))
        Assert.Equal(0x03uy, CartridgeMemory.readByte 0xA000us (advanced |> CartridgeMemory.writeByte 0x4000us 0x0Auy))
        Assert.Equal(0x04uy, CartridgeMemory.readByte 0xA000us (advanced |> CartridgeMemory.writeByte 0x4000us 0x0Buy))
        Assert.Equal(0x00uy, CartridgeMemory.readByte 0xA000us (advanced |> CartridgeMemory.writeByte 0x4000us 0x0Cuy))

[<Fact>]
let ``MBC3 RTC latch keeps a stable snapshot until latched again`` () =
    let rom = makeRom 0x10uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let latched =
            image
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.advanceRtcSeconds 10
            |> CartridgeMemory.writeByte 0x6000us 0x00uy
            |> CartridgeMemory.writeByte 0x6000us 0x01uy

        let advanced = latched |> CartridgeMemory.advanceRtcSeconds 5

        Assert.Equal(0x0Auy, CartridgeMemory.readByte 0xA000us (advanced |> CartridgeMemory.writeByte 0x4000us 0x08uy))

        let relatched =
            advanced
            |> CartridgeMemory.writeByte 0x6000us 0x00uy
            |> CartridgeMemory.writeByte 0x6000us 0x01uy

        Assert.Equal(0x0Fuy, CartridgeMemory.readByte 0xA000us (relatched |> CartridgeMemory.writeByte 0x4000us 0x08uy))

[<Fact>]
let ``MBC3 RTC can be exported and imported defensively`` () =
    let rom = makeRom 0x10uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let withRtc =
            image
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.advanceRtcSeconds 42
            |> CartridgeMemory.writeByte 0x6000us 0x00uy
            |> CartridgeMemory.writeByte 0x6000us 0x01uy

        match CartridgeMemory.exportRtc withRtc with
        | None -> Assert.Fail "Expected RTC export."
        | Some rtc ->
            let registers = Array.copy rtc.Registers
            registers[0] <- 0x01uy

            let editedRtc =
                { rtc with
                    Registers = registers
                    LatchedRegisters = None }

            match CartridgeMemory.importRtc editedRtc image with
            | Error message -> Assert.Fail message
            | Ok imported ->
                registers[0] <- 0x7Fuy

                let readable =
                    imported
                    |> CartridgeMemory.writeByte 0x0000us 0x0Auy
                    |> CartridgeMemory.writeByte 0x4000us 0x08uy

                Assert.Equal(0x01uy, CartridgeMemory.readByte 0xA000us readable)

[<Fact>]
let ``MBC3 RTC halt bit stops deterministic advancement`` () =
    let rom = makeRom 0x10uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let halted =
            image
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.writeByte 0x4000us 0x0Cuy
            |> CartridgeMemory.writeByte 0xA000us 0x40uy
            |> CartridgeMemory.advanceRtcSeconds 120

        Assert.Equal(0x00uy, CartridgeMemory.readByte 0xA000us (halted |> CartridgeMemory.writeByte 0x4000us 0x08uy))
        Assert.Equal(0x40uy, CartridgeMemory.readByte 0xA000us (halted |> CartridgeMemory.writeByte 0x4000us 0x0Cuy))

[<Fact>]
let ``MBC3 RTC sets carry and wraps after five hundred twelve days`` () =
    let rom = makeRom 0x10uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let advanced =
            image
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.advanceRtcSeconds (512 * 24 * 60 * 60)

        Assert.Equal(0x00uy, CartridgeMemory.readByte 0xA000us (advanced |> CartridgeMemory.writeByte 0x4000us 0x0Buy))
        Assert.Equal(0x80uy, CartridgeMemory.readByte 0xA000us (advanced |> CartridgeMemory.writeByte 0x4000us 0x0Cuy))

[<Fact>]
let ``MBC3 cartridges without timer do not expose RTC registers`` () =
    let rom = makeRom 0x13uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let attempted =
            image
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.writeByte 0x4000us 0x08uy
            |> CartridgeMemory.writeByte 0xA000us 0x12uy
            |> CartridgeMemory.advanceRtcSeconds 30

        Assert.Equal(0xFFuy, CartridgeMemory.readByte 0xA000us attempted)

[<Fact>]
let ``MBC5 uses nine bit ROM banks and four bit RAM banks`` () =
    let rom = makeRom 0x1Buy 0x08uy 0x04uy 512

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let switched =
            image
            |> CartridgeMemory.writeByte 0x2000us 0x01uy
            |> CartridgeMemory.writeByte 0x3000us 0x01uy
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.writeByte 0x4000us 0x07uy
            |> CartridgeMemory.writeByte 0xA000us 0x77uy

        Assert.Equal(0x01uy, CartridgeMemory.readByte 0x4000us switched)
        Assert.Equal(0x77uy, CartridgeMemory.readByte 0xA000us switched)

[<Fact>]
let ``battery-backed RAM can be exported and imported defensively`` () =
    let rom = makeRom 0x03uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        let withSave =
            image
            |> CartridgeMemory.writeByte 0x0000us 0x0Auy
            |> CartridgeMemory.writeByte 0xA000us 0x5Auy

        match CartridgeMemory.exportSaveRam withSave with
        | None -> Assert.Fail "Expected battery-backed RAM to export."
        | Some saveRam ->
            saveRam[0] <- 0x00uy

            match CartridgeMemory.importSaveRam saveRam image with
            | Error message -> Assert.Fail message
            | Ok imported ->
                Assert.Equal(
                    0x00uy,
                    CartridgeMemory.readByte 0xA000us (imported |> CartridgeMemory.writeByte 0x0000us 0x0Auy)
                )

                saveRam[0] <- 0x7Fuy

                Assert.Equal(
                    0x00uy,
                    CartridgeMemory.readByte 0xA000us (imported |> CartridgeMemory.writeByte 0x0000us 0x0Auy)
                )

[<Fact>]
let ``non battery cartridges do not export save RAM`` () =
    let rom = makeRom 0x02uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        Assert.False(CartridgeMemory.hasBatteryBackedRam image)
        Assert.Equal(None, CartridgeMemory.exportSaveRam image)

[<Fact>]
let ``importSaveRam rejects wrong size`` () =
    let rom = makeRom 0x03uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok image ->
        match CartridgeMemory.importSaveRam [| 0uy |] image with
        | Ok _ -> Assert.Fail "Expected save RAM size mismatch."
        | Error message -> Assert.Contains("size mismatch", message)
