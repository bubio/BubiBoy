module BubiBoy.Core.Tests.VideoTests

open BubiBoy.Core
open Xunit

let private makeRom () =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy
    rom

let private makeBus () =
    match makeRom () |> CartridgeMemory.create with
    | Ok cartridge -> Bus.create cartridge
    | Error message -> failwith message

let private withIo index value (bus: Bus.Memory) =
    let io = Array.copy bus.Io
    io[index] <- value
    { bus with Io = io }

let private withVram address value (bus: Bus.Memory) =
    let vram = Array.copy bus.Vram
    vram[address - 0x8000] <- value
    { bus with Vram = vram }

let private withOam index value (bus: Bus.Memory) =
    let oam = Array.copy bus.Oam
    oam[index] <- value
    { bus with Oam = oam }

let private pixel x y (framebuffer: uint32[]) =
    framebuffer[y * Hardware.ScreenWidth + x]

[<Fact>]
let ``disabled LCD renders blank DMG shade zero`` () =
    let framebuffer =
        makeBus ()
        |> withIo 0x40 0x00uy
        |> Video.renderFrame

    Assert.All(framebuffer, fun color -> Assert.Equal(Video.DmgColors[0], color))

[<Fact>]
let ``background renders unsigned tile data through BGP`` () =
    let framebuffer =
        makeBus ()
        |> withIo 0x40 0x91uy
        |> withIo 0x47 0xE4uy
        |> withVram 0x9800 0x01uy
        |> withVram 0x8010 0x80uy
        |> withVram 0x8011 0x00uy
        |> Video.renderFrame

    Assert.Equal(Video.DmgColors[1], pixel 0 0 framebuffer)
    Assert.Equal(Video.DmgColors[0], pixel 1 0 framebuffer)

[<Fact>]
let ``background supports signed tile data area`` () =
    let framebuffer =
        makeBus ()
        |> withIo 0x40 0x81uy
        |> withIo 0x47 0xE4uy
        |> withVram 0x9800 0xFFuy
        |> withVram 0x8FF0 0x80uy
        |> withVram 0x8FF1 0x80uy
        |> Video.renderFrame

    Assert.Equal(Video.DmgColors[3], pixel 0 0 framebuffer)
    Assert.Equal(Video.DmgColors[0], pixel 1 0 framebuffer)

[<Fact>]
let ``background scroll wraps across tile map`` () =
    let framebuffer =
        makeBus ()
        |> withIo 0x40 0x91uy
        |> withIo 0x47 0xE4uy
        |> withIo 0x43 0xFFuy
        |> withVram (0x9800 + 31) 0x01uy
        |> withVram 0x8010 0x01uy
        |> withVram 0x8011 0x01uy
        |> Video.renderFrame

    Assert.Equal(Video.DmgColors[3], pixel 0 0 framebuffer)

[<Fact>]
let ``window overrides background when enabled`` () =
    let framebuffer =
        makeBus ()
        |> withIo 0x40 0xF1uy
        |> withIo 0x47 0xE4uy
        |> withIo 0x4A 0x00uy
        |> withIo 0x4B 0x07uy
        |> withVram 0x9800 0x01uy
        |> withVram 0x8010 0x80uy
        |> withVram 0x8011 0x00uy
        |> withVram 0x9C00 0x02uy
        |> withVram 0x8020 0x80uy
        |> withVram 0x8021 0x80uy
        |> Video.renderFrame

    Assert.Equal(Video.DmgColors[3], pixel 0 0 framebuffer)

[<Fact>]
let ``sprites render nonzero pixels over background`` () =
    let framebuffer =
        makeBus ()
        |> withIo 0x40 0x93uy
        |> withIo 0x47 0xE4uy
        |> withIo 0x48 0xE4uy
        |> withVram 0x8000 0x00uy
        |> withVram 0x8001 0x00uy
        |> withVram 0x8010 0x80uy
        |> withVram 0x8011 0x00uy
        |> withOam 0 16uy
        |> withOam 1 8uy
        |> withOam 2 1uy
        |> withOam 3 0uy
        |> Video.renderFrame

    Assert.Equal(Video.DmgColors[1], pixel 0 0 framebuffer)
    Assert.Equal(Video.DmgColors[0], pixel 1 0 framebuffer)

[<Fact>]
let ``sprites behind background keep nonzero background pixels`` () =
    let framebuffer =
        makeBus ()
        |> withIo 0x40 0x93uy
        |> withIo 0x47 0xE4uy
        |> withIo 0x48 0xE4uy
        |> withVram 0x9800 0x01uy
        |> withVram 0x8010 0x80uy
        |> withVram 0x8011 0x00uy
        |> withVram 0x8020 0x80uy
        |> withVram 0x8021 0x80uy
        |> withOam 0 16uy
        |> withOam 1 8uy
        |> withOam 2 2uy
        |> withOam 3 0x80uy
        |> Video.renderFrame

    Assert.Equal(Video.DmgColors[1], pixel 0 0 framebuffer)

[<Fact>]
let ``sprite x and y flip select mirrored tile pixels`` () =
    let framebuffer =
        makeBus ()
        |> withIo 0x40 0x93uy
        |> withIo 0x48 0xE4uy
        |> withVram 0x801E 0x01uy
        |> withVram 0x801F 0x00uy
        |> withOam 0 16uy
        |> withOam 1 8uy
        |> withOam 2 1uy
        |> withOam 3 0x60uy
        |> Video.renderFrame

    Assert.Equal(Video.DmgColors[1], pixel 0 0 framebuffer)
