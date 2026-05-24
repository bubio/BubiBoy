module BubiBoy.Core.Tests.BusTests

open BubiBoy.Core
open Xunit

let private makeRom () =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    rom[0x0000] <- 0x31uy
    rom[0x4000] <- 0xC3uy
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy
    rom

let private makeBus () =
    match makeRom () |> CartridgeMemory.create with
    | Ok cartridge -> Bus.create cartridge
    | Error message -> failwith message

[<Fact>]
let ``readByte delegates ROM area to cartridge`` () =
    let bus = makeBus ()

    Assert.Equal(0x31uy, Bus.readByte 0x0000us bus)
    Assert.Equal(0xC3uy, Bus.readByte 0x4000us bus)

[<Fact>]
let ``bus starts with common DMG post boot IO defaults`` () =
    let bus = makeBus ()

    Assert.Equal(0xCFuy, Bus.readByte 0xFF00us bus)
    Assert.Equal(0x91uy, Bus.readByte 0xFF40us bus)
    Assert.Equal(0xFCuy, Bus.readByte 0xFF47us bus)
    Assert.Equal(0xE1uy, Bus.readByte 0xFF0Fus bus)

[<Fact>]
let ``writeByte stores WRAM and echo RAM consistently`` () =
    let bus = makeBus ()

    let withWram = Bus.writeByte 0xC123us 0x42uy bus
    let withEcho = Bus.writeByte 0xE123us 0x24uy withWram

    Assert.Equal(0x42uy, Bus.readByte 0xC123us withWram)
    Assert.Equal(0x42uy, Bus.readByte 0xE123us withWram)
    Assert.Equal(0x24uy, Bus.readByte 0xC123us withEcho)
    Assert.Equal(0x24uy, Bus.readByte 0xE123us withEcho)

[<Fact>]
let ``writeByte stores HRAM and interrupt enable`` () =
    let bus = makeBus ()

    let updated =
        bus
        |> Bus.writeByte 0xFF80us 0x12uy
        |> Bus.writeByte 0xFFFFus 0x1Fuy

    Assert.Equal(0x12uy, Bus.readByte 0xFF80us updated)
    Assert.Equal(0x1Fuy, Bus.readByte 0xFFFFus updated)

[<Fact>]
let ``unusable memory reads as FF and ignores writes`` () =
    let bus = makeBus ()
    let updated = Bus.writeByte 0xFEA0us 0x00uy bus

    Assert.Equal(0xFFuy, Bus.readByte 0xFEA0us updated)

[<Fact>]
let ``timer registers are mapped through bus and tick requests interrupt`` () =
    let bus =
        makeBus ()
        |> Bus.writeByte 0xFF05us 0xFFuy
        |> Bus.writeByte 0xFF06us 0x44uy
        |> Bus.writeByte 0xFF07us 0x05uy

    let updated = Bus.tick 16 bus

    Assert.Equal(0x44uy, Bus.readByte 0xFF05us updated)
    Assert.Equal(Interrupt.TimerBit, Bus.readByte 0xFF0Fus updated &&& Interrupt.TimerBit)

[<Fact>]
let ``writing DIV through bus resets divider`` () =
    let advanced = makeBus () |> Bus.tick 512
    let reset = Bus.writeByte 0xFF04us 0xFFuy advanced

    Assert.Equal(0x02uy, Bus.readByte 0xFF04us advanced)
    Assert.Equal(0x00uy, Bus.readByte 0xFF04us reset)

[<Fact>]
let ``joypad state is mapped through P1 and requests interrupt on press`` () =
    let bus = makeBus () |> Bus.writeByte 0xFF00us 0x10uy
    let updated = Bus.setButton Joypad.A true bus

    Assert.Equal(0xDEuy, Bus.readByte 0xFF00us updated)
    Assert.Equal(Interrupt.JoypadBit, Bus.readByte 0xFF0Fus updated &&& Interrupt.JoypadBit)

[<Fact>]
let ``serial registers are retained as IO stubs`` () =
    let bus =
        makeBus ()
        |> Bus.writeByte 0xFF01us 0x42uy
        |> Bus.writeByte 0xFF02us 0x81uy

    Assert.Equal(0x42uy, Bus.readByte 0xFF01us bus)
    Assert.Equal(0x81uy, Bus.readByte 0xFF02us bus)

[<Fact>]
let ``LCD LY advances with bus cycles and wraps after one frame`` () =
    let bus = makeBus ()
    let line1 = Bus.tick Lcd.CyclesPerLine bus
    let line153 = Bus.tick (Lcd.CyclesPerLine * 152) line1
    let wrapped = Bus.tick Lcd.CyclesPerLine line153

    Assert.Equal(0x01uy, Bus.readByte 0xFF44us line1)
    Assert.Equal(153uy, Bus.readByte 0xFF44us line153)
    Assert.Equal(0uy, Bus.readByte 0xFF44us wrapped)

[<Fact>]
let ``LCD entering VBlank requests VBlank interrupt`` () =
    let updated = makeBus () |> Bus.tick (Lcd.CyclesPerLine * 144)

    Assert.Equal(144uy, Bus.readByte 0xFF44us updated)
    Assert.Equal(Interrupt.VBlankBit, Bus.readByte 0xFF0Fus updated &&& Interrupt.VBlankBit)

[<Fact>]
let ``writing LY resets LCD line counter`` () =
    let advanced = makeBus () |> Bus.tick (Lcd.CyclesPerLine * 20)
    let reset = Bus.writeByte 0xFF44us 0xFFuy advanced

    Assert.Equal(20uy, Bus.readByte 0xFF44us advanced)
    Assert.Equal(0uy, Bus.readByte 0xFF44us reset)

[<Fact>]
let ``LCD STAT exposes current mode and coincidence flag`` () =
    let bus = makeBus () |> Bus.writeByte 0xFF45us 0x00uy
    let transfer = Bus.tick 80 bus
    let hblank = Bus.tick 252 bus
    let vblank = Bus.tick (Lcd.CyclesPerLine * 144) bus

    Assert.Equal(0x06uy, Bus.readByte 0xFF41us bus &&& 0x07uy)
    Assert.Equal(0x07uy, Bus.readByte 0xFF41us transfer &&& 0x07uy)
    Assert.Equal(0x00uy, Bus.readByte 0xFF41us hblank &&& 0x03uy)
    Assert.Equal(0x01uy, Bus.readByte 0xFF41us vblank &&& 0x03uy)

[<Fact>]
let ``LCD STAT selected mode requests LCD interrupt`` () =
    let bus = makeBus () |> Bus.writeByte 0xFF41us 0x08uy
    let hblank = Bus.tick 252 bus

    Assert.Equal(Interrupt.LcdStatBit, Bus.readByte 0xFF0Fus hblank &&& Interrupt.LcdStatBit)

[<Fact>]
let ``LCD STAT interrupt is requested only on signal rising edge`` () =
    let bus =
        makeBus ()
        |> Bus.writeByte 0xFF0Fus 0x00uy
        |> Bus.writeByte 0xFF41us 0x08uy

    let hblank = Bus.tick 252 bus
    let acknowledged = Bus.writeByte 0xFF0Fus 0x00uy hblank
    let stillHblank = Bus.tick 4 acknowledged

    Assert.Equal(Interrupt.LcdStatBit, Bus.readByte 0xFF0Fus hblank &&& Interrupt.LcdStatBit)
    Assert.Equal(0uy, Bus.readByte 0xFF0Fus stillHblank &&& Interrupt.LcdStatBit)

[<Fact>]
let ``OAM DMA copies one hundred sixty bytes from source page`` () =
    let source =
        [ 0 .. Bus.OamSize - 1 ]
        |> List.fold (fun bus offset -> Bus.writeByte (0xC000us + uint16 offset) (byte offset) bus) (makeBus ())

    let copied = Bus.writeByte 0xFF46us 0xC0uy source
    let readable = Bus.tick 252 copied

    Assert.Equal(0xC0uy, Bus.readByte 0xFF46us copied)
    Assert.Equal(0x00uy, Bus.readByte 0xFE00us readable)
    Assert.Equal(0x7Fuy, Bus.readByte 0xFE7Fus readable)
    Assert.Equal(0x9Fuy, Bus.readByte 0xFE9Fus readable)

[<Fact>]
let ``VRAM is inaccessible during LCD transfer mode`` () =
    let transfer = makeBus () |> Bus.tick 80
    let blocked = Bus.writeByte 0x8000us 0x42uy transfer
    let hblank = makeBus () |> Bus.tick 252
    let written = Bus.writeByte 0x8000us 0x42uy hblank

    Assert.Equal(0xFFuy, Bus.readByte 0x8000us blocked)
    Assert.Equal(0x42uy, Bus.readByte 0x8000us written)

[<Fact>]
let ``OAM is accessible only during HBlank and VBlank`` () =
    let oamSearch = makeBus ()
    let blocked = Bus.writeByte 0xFE00us 0x42uy oamSearch
    let hblank = makeBus () |> Bus.tick 252
    let written = Bus.writeByte 0xFE00us 0x42uy hblank

    Assert.Equal(0xFFuy, Bus.readByte 0xFE00us blocked)
    Assert.Equal(0x42uy, Bus.readByte 0xFE00us written)
