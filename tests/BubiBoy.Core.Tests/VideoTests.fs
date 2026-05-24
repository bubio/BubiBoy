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
    Bus.withIoByte index value bus

let private withVram address value (bus: Bus.Memory) =
    Bus.withVramByte address value bus

let private withOam index value (bus: Bus.Memory) =
    Bus.withOamByte index value bus

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

[<Fact>]
let ``sprites with lower OAM index win when x coordinates match`` () =
    let framebuffer =
        makeBus ()
        |> withIo 0x40 0x93uy
        |> withIo 0x48 0xE4uy
        |> withVram 0x8010 0x80uy
        |> withVram 0x8011 0x00uy
        |> withVram 0x8020 0x00uy
        |> withVram 0x8021 0x80uy
        |> withOam 0 16uy
        |> withOam 1 8uy
        |> withOam 2 1uy
        |> withOam 3 0uy
        |> withOam 4 16uy
        |> withOam 5 8uy
        |> withOam 6 2uy
        |> withOam 7 0uy
        |> Video.renderFrame

    Assert.Equal(Video.DmgColors[1], pixel 0 0 framebuffer)

[<Fact>]
let ``sprites with smaller x coordinate win over later OAM entries`` () =
    let framebuffer =
        makeBus ()
        |> withIo 0x40 0x93uy
        |> withIo 0x48 0xE4uy
        |> withVram 0x8010 0x40uy
        |> withVram 0x8011 0x00uy
        |> withVram 0x8020 0x00uy
        |> withVram 0x8021 0x40uy
        |> withOam 0 16uy
        |> withOam 1 9uy
        |> withOam 2 1uy
        |> withOam 3 0uy
        |> withOam 4 16uy
        |> withOam 5 8uy
        |> withOam 6 2uy
        |> withOam 7 0uy
        |> Video.renderFrame

    Assert.Equal(Video.DmgColors[2], pixel 1 0 framebuffer)

[<Fact>]
let ``only first ten OAM sprites on a scanline are rendered`` () =
    let bus =
        makeBus ()
        |> withIo 0x40 0x93uy
        |> withIo 0x48 0xE4uy
        |> withVram 0x8010 0x80uy
        |> withVram 0x8011 0x00uy

    let busWithFirstTenSprites =
        [ 0 .. 9 ]
        |> List.fold
            (fun current spriteIndex ->
                let baseIndex = spriteIndex * 4

                current
                |> withOam baseIndex 16uy
                |> withOam (baseIndex + 1) 40uy
                |> withOam (baseIndex + 2) 1uy
                |> withOam (baseIndex + 3) 0uy)
            bus

    let framebuffer =
        busWithFirstTenSprites
        |> withOam 40 16uy
        |> withOam 41 8uy
        |> withOam 42 1uy
        |> withOam 43 0uy
        |> Video.renderFrame

    Assert.Equal(Video.DmgColors[0], pixel 0 0 framebuffer)

[<Fact>]
let ``renderScanline preserves lines rendered with earlier scroll values`` () =
    let framebuffer = Video.blankFrame ()

    let firstLine =
        makeBus ()
        |> withIo 0x40 0x91uy
        |> withIo 0x47 0xE4uy
        |> withIo 0x42 0x00uy
        |> withVram 0x9800 0x01uy
        |> withVram 0x8010 0x80uy
        |> withVram 0x8011 0x00uy

    Video.renderScanline 0 firstLine framebuffer

    let secondLine =
        firstLine
        |> withIo 0x42 0x07uy
        |> withVram (0x9800 + 32) 0x02uy
        |> withVram 0x8020 0x80uy
        |> withVram 0x8021 0x80uy

    Video.renderScanline 1 secondLine framebuffer

    Assert.Equal(Video.DmgColors[1], pixel 0 0 framebuffer)
    Assert.Equal(Video.DmgColors[3], pixel 0 1 framebuffer)
