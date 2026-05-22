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
