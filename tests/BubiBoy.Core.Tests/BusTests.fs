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

let private makeCgbBus () =
    let rom = makeRom ()
    rom[0x0143] <- 0xC0uy

    match rom |> CartridgeMemory.create with
    | Ok cartridge -> Bus.create cartridge
    | Error message -> failwith message

let private makeBootBus bootRom =
    match makeRom () |> CartridgeMemory.create with
    | Ok cartridge ->
        match Bus.createWithDmgBootRom bootRom cartridge with
        | Ok bus -> bus
        | Error message -> failwith message
    | Error message -> failwith message

let private makeCgbBootBus bootRom =
    let rom = makeRom ()
    rom[0x0100] <- 0x42uy
    rom[0x0200] <- 0x24uy
    rom[0x08FF] <- 0x81uy
    rom[0x0900] <- 0x18uy
    rom[0x0143] <- 0xC0uy

    match rom |> CartridgeMemory.create with
    | Ok cartridge ->
        match Bus.createWithCgbBootRom bootRom cartridge with
        | Ok bus -> bus
        | Error message -> failwith message
    | Error message -> failwith message

let private makeDmgCgbBootBus bootRom =
    match makeRom () |> CartridgeMemory.create with
    | Ok cartridge ->
        match Bus.createWithCgbBootRom bootRom cartridge with
        | Ok bus -> bus
        | Error message -> failwith message
    | Error message -> failwith message

[<Fact>]
let ``readByte delegates ROM area to cartridge`` () =
    let bus = makeBus ()

    Assert.Equal(0x31uy, Bus.readByte 0x0000us bus)
    Assert.Equal(0xC3uy, Bus.readByte 0x4000us bus)

[<Fact>]
let ``DMG boot ROM overlays only the first 256 cartridge bytes`` () =
    let bootRom = Array.init 256 (fun index -> byte (index ^^^ 0x5A))
    let bus = makeBootBus bootRom

    Assert.True(Bus.isBootRomEnabled bus)
    Assert.Equal(bootRom[0], Bus.readByte 0x0000us bus)
    Assert.Equal(bootRom[0xFF], Bus.readByte 0x00FFus bus)
    Assert.Equal(0uy, Bus.readByte 0x0100us bus)
    Assert.Equal(0xC3uy, Bus.readByte 0x4000us bus)

[<Fact>]
let ``CGB boot ROM overlays its two mapped ranges`` () =
    let bootRom = Array.init 2304 (fun index -> byte (index ^^^ 0x5A))
    let bus = makeCgbBootBus bootRom

    Assert.True(Bus.isBootRomEnabled bus)
    Assert.Equal(bootRom[0], Bus.readByte 0x0000us bus)
    Assert.Equal(bootRom[0xFF], Bus.readByte 0x00FFus bus)
    Assert.Equal(0x42uy, Bus.readByte 0x0100us bus)
    Assert.Equal(bootRom[0x200], Bus.readByte 0x0200us bus)
    Assert.Equal(bootRom[0x8FF], Bus.readByte 0x08FFus bus)
    Assert.Equal(0x18uy, Bus.readByte 0x0900us bus)

[<Fact>]
let ``FF50 disables all CGB boot ROM ranges`` () =
    let bootRom = Array.create 2304 0x99uy
    let disabled = makeCgbBootBus bootRom |> Bus.writeByte 0xFF50us 1uy

    Assert.False(Bus.isBootRomEnabled disabled)
    Assert.Equal(0x31uy, Bus.readByte 0x0000us disabled)
    Assert.Equal(0x24uy, Bus.readByte 0x0200us disabled)

[<Fact>]
let ``CGB boot ROM starts a DMG-only cartridge in full CGB mode`` () =
    let bus = makeDmgCgbBootBus (Array.zeroCreate<byte> 2304)

    Assert.Equal(Hardware.Cgb, Bus.mode bus)
    Assert.True(Bus.isBootRomEnabled bus)

[<Fact>]
let ``KEY0 selects CGB compatibility mode for a DMG-only cartridge`` () =
    let bus =
        makeDmgCgbBootBus (Array.zeroCreate<byte> 2304) |> Bus.writeByte 0xFF4Cus 0x04uy

    Assert.Equal(Hardware.CgbCompatibility, Bus.mode bus)
    Assert.True(Bus.isBootRomEnabled bus)

[<Fact>]
let ``KEY0 cannot change mode after the boot ROM is disabled`` () =
    let bus =
        makeDmgCgbBootBus (Array.zeroCreate<byte> 2304)
        |> Bus.writeByte 0xFF50us 0x01uy
        |> Bus.writeByte 0xFF4Cus 0x04uy

    Assert.Equal(Hardware.Cgb, Bus.mode bus)
    Assert.False(Bus.isBootRomEnabled bus)

[<Fact>]
let ``FF50 permanently disables the boot ROM after a nonzero write`` () =
    let bootRom = Array.create 256 0x99uy
    let bus = makeBootBus bootRom
    let stillEnabled = Bus.writeByte 0xFF50us 0uy bus
    let disabled = Bus.writeByte 0xFF50us 1uy stillEnabled
    let cannotReenable = Bus.writeByte 0xFF50us 0uy disabled

    Assert.True(Bus.isBootRomEnabled stillEnabled)
    Assert.False(Bus.isBootRomEnabled disabled)
    Assert.False(Bus.isBootRomEnabled cannotReenable)
    Assert.Equal(0x31uy, Bus.readByte 0x0000us cannotReenable)

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

    Assert.Equal(0x42uy, Bus.readByte 0xC123us withWram)
    Assert.Equal(0x42uy, Bus.readByte 0xE123us withWram)

    let withEcho = Bus.writeByte 0xE123us 0x24uy withWram

    Assert.Equal(0x24uy, Bus.readByte 0xC123us withEcho)
    Assert.Equal(0x24uy, Bus.readByte 0xE123us withEcho)

[<Fact>]
let ``writeByte stores HRAM and interrupt enable`` () =
    let bus = makeBus ()

    let updated = bus |> Bus.writeByte 0xFF80us 0x12uy |> Bus.writeByte 0xFFFFus 0x1Fuy

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
        makeBus () |> Bus.writeByte 0xFF01us 0x42uy |> Bus.writeByte 0xFF02us 0x81uy

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
        makeBus () |> Bus.writeByte 0xFF0Fus 0x00uy |> Bus.writeByte 0xFF41us 0x08uy

    let hblank = Bus.tick 252 bus
    let requested = Bus.readByte 0xFF0Fus hblank &&& Interrupt.LcdStatBit
    let acknowledged = Bus.writeByte 0xFF0Fus 0x00uy hblank
    let stillHblank = Bus.tick 4 acknowledged

    Assert.Equal(Interrupt.LcdStatBit, requested)
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
let ``VRAM is accessible during transfer mode when LCD is disabled`` () =
    let disabledTransfer = makeBus () |> Bus.tick 80 |> Bus.writeByte 0xFF40us 0x00uy

    let written = Bus.writeByte 0x8000us 0x42uy disabledTransfer

    Assert.Equal(0x42uy, Bus.readByte 0x8000us written)

[<Fact>]
let ``CGB VRAM bank register selects independent VRAM banks`` () =
    let bus =
        makeCgbBus ()
        |> Bus.writeByte 0xFF40us 0x00uy
        |> Bus.writeByte 0x8000us 0x11uy
        |> Bus.writeByte 0xFF4Fus 0x01uy
        |> Bus.writeByte 0x8000us 0x22uy

    Assert.Equal(0x22uy, Bus.readByte 0x8000us bus)
    let bank0 = Bus.writeByte 0xFF4Fus 0x00uy bus
    Assert.Equal(0x11uy, Bus.readByte 0x8000us bank0)

[<Fact>]
let ``CGB WRAM bank register selects switchable bank at D000`` () =
    let bus =
        makeCgbBus ()
        |> Bus.writeByte 0xD000us 0x11uy
        |> Bus.writeByte 0xFF70us 0x02uy
        |> Bus.writeByte 0xD000us 0x22uy

    Assert.Equal(0x22uy, Bus.readByte 0xD000us bus)
    let bank1 = Bus.writeByte 0xFF70us 0x01uy bus
    Assert.Equal(0x11uy, Bus.readByte 0xD000us bank1)

[<Fact>]
let ``CGB palette data ports auto increment index`` () =
    let bus =
        makeCgbBus ()
        |> Bus.writeByte 0xFF68us 0x80uy
        |> Bus.writeByte 0xFF69us 0x1Fuy
        |> Bus.writeByte 0xFF69us 0x00uy

    Assert.Equal(0xC2uy, Bus.readByte 0xFF68us bus)
    let readBack = Bus.writeByte 0xFF68us 0x00uy bus
    Assert.Equal(0x1Fuy, Bus.readByte 0xFF69us readBack)

[<Fact>]
let ``CGB object priority mode register stores low bit`` () =
    let bus = makeCgbBus () |> Bus.writeByte 0xFF6Cus 0x01uy

    Assert.Equal(0xFFuy, Bus.readByte 0xFF6Cus bus)

    let cgbPriority = Bus.writeByte 0xFF6Cus 0x00uy bus

    Assert.Equal(0xFEuy, Bus.readByte 0xFF6Cus cgbPriority)

[<Fact>]
let ``CGB double speed keeps timer running at CPU speed while LCD uses hardware speed`` () =
    let doubleSpeed =
        makeCgbBus ()
        |> Bus.writeByte 0xFF4Dus 0x01uy
        |> Bus.stop
        |> Bus.writeByte 0xFF05us 0x10uy
        |> Bus.writeByte 0xFF07us 0x05uy
        |> Bus.tick 16

    Assert.Equal(0x11uy, Bus.readByte 0xFF05us doubleSpeed)
    Assert.Equal(0uy, Bus.readByte 0xFF44us doubleSpeed)

[<Fact>]
let ``CGB general DMA copies selected source block into selected VRAM bank`` () =
    let source =
        makeCgbBus ()
        |> Bus.writeByte 0xFF40us 0x00uy
        |> Bus.writeByte 0xC000us 0x42uy
        |> Bus.writeByte 0xFF4Fus 0x01uy
        |> Bus.writeByte 0xFF51us 0xC0uy
        |> Bus.writeByte 0xFF52us 0x00uy
        |> Bus.writeByte 0xFF53us 0x00uy
        |> Bus.writeByte 0xFF54us 0x00uy

    let copied = Bus.writeByte 0xFF55us 0x00uy source

    Assert.Equal(0xFFuy, Bus.readByte 0xFF55us copied)
    Assert.Equal(0x42uy, Bus.readByte 0x8000us copied)

[<Fact>]
let ``CGB HBlank DMA copies one block on each HBlank entry`` () =
    let active =
        makeCgbBus ()
        |> Bus.writeByte 0xC000us 0x11uy
        |> Bus.writeByte 0xC010us 0x22uy
        |> Bus.writeByte 0xFF51us 0xC0uy
        |> Bus.writeByte 0xFF52us 0x00uy
        |> Bus.writeByte 0xFF53us 0x00uy
        |> Bus.writeByte 0xFF54us 0x00uy
        |> Bus.writeByte 0xFF55us 0x81uy

    Assert.Equal(0x01uy, Bus.readByte 0xFF55us active)

    let firstHBlank = Bus.tick 252 active

    Assert.Equal(0x00uy, Bus.readByte 0xFF55us firstHBlank)
    Assert.Equal(0x11uy, Bus.readByte 0x8000us firstHBlank)

    let nextLineOam = Bus.tick 204 firstHBlank
    let secondHBlank = Bus.tick 252 nextLineOam

    Assert.Equal(0xFFuy, Bus.readByte 0xFF55us secondHBlank)
    Assert.Equal(0x22uy, Bus.readByte 0x8010us secondHBlank)

[<Fact>]
let ``OAM is accessible only during HBlank and VBlank`` () =
    let oamSearch = makeBus ()
    let blocked = Bus.writeByte 0xFE00us 0x42uy oamSearch
    let hblank = makeBus () |> Bus.tick 252
    let written = Bus.writeByte 0xFE00us 0x42uy hblank

    Assert.Equal(0xFFuy, Bus.readByte 0xFE00us blocked)
    Assert.Equal(0x42uy, Bus.readByte 0xFE00us written)

[<Fact>]
let ``OAM is accessible during OAM search when LCD is disabled`` () =
    let disabledOamSearch = makeBus () |> Bus.writeByte 0xFF40us 0x00uy

    let written = Bus.writeByte 0xFE00us 0x42uy disabledOamSearch

    Assert.Equal(0x42uy, Bus.readByte 0xFE00us written)

[<Fact>]
let ``disabled LCD holds LY at zero and reports HBlank mode`` () =
    let disabled =
        makeBus () |> Bus.writeByte 0xFF40us 0x00uy |> Bus.tick (Lcd.CyclesPerLine * 20)

    Assert.Equal(0uy, Bus.readByte 0xFF44us disabled)
    Assert.Equal(0x04uy, Bus.readByte 0xFF41us disabled &&& 0x07uy)
