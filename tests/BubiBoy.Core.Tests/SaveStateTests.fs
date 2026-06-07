module BubiBoy.Core.Tests.SaveStateTests

open System
open System.Security.Cryptography
open BubiBoy.Core
open Xunit

let private makeRom (title: string) program =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    let titleBytes = System.Text.Encoding.ASCII.GetBytes title

    titleBytes
    |> Array.truncate 16
    |> Array.iteri (fun index value -> rom[0x0134 + index] <- value)

    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy
    rom[0x014D] <- 0x42uy

    program |> Array.iteri (fun index value -> rom[0x0100 + index] <- value)

    rom

let private createSession rom =
    match Emulator.createSession rom with
    | Ok session -> session
    | Error message -> failwith message

let private encodeSession session =
    session |> SaveState.capture |> SaveState.encode

[<Fact>]
let ``version 3 wire format remains stable`` () =
    let bytes = makeRom "S" Array.empty |> createSession |> encodeSession
    let hash = SHA256.HashData bytes |> Convert.ToHexString

    Assert.Equal(142_175, bytes.Length)
    Assert.Equal("52E3AB123133B3CAE36396C45D00FA05AF668FD21E176F165D60E52BCA1F7E17", hash)

[<Fact>]
let ``save state round trips session and can continue deterministically`` () =
    let rom = makeRom "STATE" [| 0x3Cuy; 0xEAuy; 0x00uy; 0xC0uy; 0x00uy |]
    let session = createSession rom |> Emulator.run 3 |> (fun result -> result.Session)
    let encoded = encodeSession session

    let restored = createSession rom |> SaveState.restoreBytes encoded

    match restored with
    | Error message -> failwith message
    | Ok restored ->
        Assert.Equal(session.Cpu, restored.Cpu)
        Assert.Equal(session.TotalCycles, restored.TotalCycles)
        Assert.Equal(session.Steps, restored.Steps)
        Assert.Equal(Bus.readByte 0xC000us session.Bus, Bus.readByte 0xC000us restored.Bus)
        Assert.True((session.Framebuffer, restored.Framebuffer) ||> Array.forall2 (=))

        let originalAfter = Emulator.run 20 session
        let restoredAfter = Emulator.run 20 restored
        Assert.Equal(originalAfter.Session.Cpu, restoredAfter.Session.Cpu)
        Assert.Equal(originalAfter.Session.TotalCycles, restoredAfter.Session.TotalCycles)
        Assert.Equal(Bus.readByte 0xC000us originalAfter.Session.Bus, Bus.readByte 0xC000us restoredAfter.Session.Bus)

[<Fact>]
let ``save state rejects different ROM identity`` () =
    let first = createSession (makeRom "STATE-A" [| 0x00uy |])
    let second = createSession (makeRom "STATE-B" [| 0x00uy |])
    let encoded = encodeSession first

    match SaveState.restoreBytes encoded second with
    | Ok _ -> failwith "Expected ROM identity mismatch."
    | Error message -> Assert.Contains("ROM identity", message)

[<Fact>]
let ``save state rejects magic mismatch`` () =
    let bytes = makeRom "MAGIC" Array.empty |> createSession |> encodeSession
    bytes[0] <- bytes[0] ^^^ 0xFFuy

    match SaveState.decode bytes with
    | Ok _ -> failwith "Expected save-state magic mismatch."
    | Error message -> Assert.Equal("File is not a BubiBoy save state.", message)

[<Fact>]
let ``save state rejects version mismatch`` () =
    let bytes = makeRom "VERSION" Array.empty |> createSession |> encodeSession
    BitConverter.GetBytes(SaveState.CurrentVersion + 1).CopyTo(bytes, 9)

    match SaveState.decode bytes with
    | Ok _ -> failwith "Expected save-state version mismatch."
    | Error message -> Assert.Equal($"Unsupported save state version: {SaveState.CurrentVersion + 1}.", message)

[<Fact>]
let ``save state restore rejects framebuffer size mismatch`` () =
    let session = makeRom "FRAME" Array.empty |> createSession

    let snapshot =
        { SaveState.capture session with
            Framebuffer = Array.empty }

    match SaveState.restore snapshot session with
    | Ok _ -> failwith "Expected framebuffer size mismatch."
    | Error message -> Assert.Contains("framebuffer size mismatch", message)

[<Fact>]
let ``save state restore rejects bus array size mismatch`` () =
    let session = makeRom "VRAM" Array.empty |> createSession
    let snapshot = SaveState.capture session

    let invalidBus =
        { snapshot.Bus with
            VramSnapshot = Array.empty }

    match SaveState.restore { snapshot with Bus = invalidBus } session with
    | Ok _ -> failwith "Expected VRAM size mismatch."
    | Error message -> Assert.Contains("VRAM size mismatch", message)
