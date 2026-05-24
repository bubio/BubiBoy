module BubiBoy.Core.Tests.EmulatorTests

open BubiBoy.Core
open Xunit

let private makeRomWithProgram (program: byte[]) =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy

    program
    |> Array.iteri (fun index value -> rom[0x0100 + index] <- value)

    rom

let private createSession program =
    match makeRomWithProgram program |> Emulator.createSession with
    | Ok session -> session
    | Error message -> failwith message

let private withIo index value (bus: Bus.Memory) =
    let io = Array.copy bus.Io
    io[index] <- value
    { bus with Io = io }

let private withVram address value (bus: Bus.Memory) =
    let vram = Array.copy bus.Vram
    vram[address - 0x8000] <- value
    { bus with Vram = vram }

[<Fact>]
let ``runFrame advances until one hardware frame elapses`` () =
    let result = createSession [| 0x00uy |] |> Emulator.runFrame 20_000

    Assert.Equal(Emulator.FrameCompleted, result.StopReason)
    Assert.True(result.Session.TotalCycles >= int64 Hardware.CyclesPerFrame)
    Assert.Equal(17_556, result.Session.Steps)
    Assert.Equal(Video.FramebufferPixels, result.Framebuffer.Length)

[<Fact>]
let ``runFrame returns scanline framebuffer captured during the frame`` () =
    let session = createSession [| 0x00uy |]
    let bus =
        session.Bus
        |> withIo 0x40 0x91uy
        |> withIo 0x47 0xE4uy
        |> withVram 0x9800 0x01uy
        |> withVram 0x8010 0x80uy
        |> withVram 0x8011 0x00uy

    let result = Emulator.runFrame 20_000 { session with Bus = bus }

    Assert.Equal(Emulator.FrameCompleted, result.StopReason)
    Assert.Equal(Video.DmgColors[1], result.Framebuffer[0])

[<Fact>]
let ``runFrame stops at step limit before frame completion`` () =
    let result = createSession [| 0x00uy |] |> Emulator.runFrame 10

    Assert.Equal(Emulator.StepLimitReached, result.StopReason)
    Assert.Equal(10, result.Session.Steps)
    Assert.Equal(40L, result.Session.TotalCycles)
    Assert.Equal(Video.FramebufferPixels, result.Framebuffer.Length)

[<Fact>]
let ``runFrame reports unsupported opcode with current framebuffer`` () =
    let result = createSession [| 0xD3uy |] |> Emulator.runFrame 20_000

    Assert.Equal(Emulator.UnsupportedOpcode(0xD3uy, 0x0100us), result.StopReason)
    Assert.Equal(0, result.Session.Steps)
    Assert.Equal(0L, result.Session.TotalCycles)
    Assert.Equal(Video.FramebufferPixels, result.Framebuffer.Length)
