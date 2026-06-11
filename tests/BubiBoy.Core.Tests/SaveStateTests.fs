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
let ``version 4 wire format remains stable`` () =
    let bytes = makeRom "S" Array.empty |> createSession |> encodeSession
    let hash = SHA256.HashData bytes |> Convert.ToHexString

    Assert.Equal(142_177, bytes.Length)
    Assert.Equal("1366D44EE4F3D8C74850B8F09C9C4BBC27EC3FDCE096A6401F7EAD4439587662", hash)

[<Fact>]
let ``version 3 post boot wire format remains readable`` () =
    let session = makeRom "S" Array.empty |> createSession
    let version4 = encodeSession session
    let bootRomMetadataOffset = 57

    let version3 =
        Array.concat
            [ version4[.. bootRomMetadataOffset - 1]
              version4[bootRomMetadataOffset + 2 ..] ]

    BitConverter.GetBytes(3).CopyTo(version3, 9)

    Assert.Equal(142_175, version3.Length)

    Assert.Equal(
        "52E3AB123133B3CAE36396C45D00FA05AF668FD21E176F165D60E52BCA1F7E17",
        SHA256.HashData version3 |> Convert.ToHexString
    )

    match SaveState.restoreBytes version3 session with
    | Error message -> Assert.Fail message
    | Ok restored -> Assert.Equal(session.Cpu, restored.Cpu)

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

[<Fact>]
let ``save state restores an enabled boot ROM with matching identity`` () =
    let rom = makeRom "BOOT" [| 0x00uy |]
    let bootRom = Array.init 256 byte

    let createBootSession bytes =
        match Emulator.createSessionWithDmgBootRom bytes rom with
        | Ok session -> session
        | Error message -> failwith message

    let session =
        createBootSession bootRom |> Emulator.run 1 |> (fun result -> result.Session)

    let encoded = encodeSession session

    match SaveState.restoreBytes encoded (createBootSession bootRom) with
    | Error message -> Assert.Fail message
    | Ok restored ->
        Assert.True(Bus.isBootRomEnabled restored.Bus)
        Assert.Equal(session.Cpu, restored.Cpu)

[<Fact>]
let ``save state rejects an enabled boot ROM with a different identity`` () =
    let rom = makeRom "BOOT-ID" [| 0x00uy |]

    let createBootSession bytes =
        match Emulator.createSessionWithDmgBootRom bytes rom with
        | Ok session -> session
        | Error message -> failwith message

    let encoded = createBootSession (Array.create 256 0x00uy) |> encodeSession

    match SaveState.restoreBytes encoded (createBootSession (Array.create 256 0x01uy)) with
    | Ok _ -> Assert.Fail "Expected boot ROM identity mismatch."
    | Error message -> Assert.Contains("Boot ROM identity mismatch", message)

[<Fact>]
let ``save state rejects an enabled boot ROM when no BIOS is available`` () =
    let rom = makeRom "BOOT-NONE" [| 0x00uy |]

    let bootSession =
        match Emulator.createSessionWithDmgBootRom (Array.zeroCreate<byte> 256) rom with
        | Ok session -> session
        | Error message -> failwith message

    match SaveState.restoreBytes (encodeSession bootSession) (createSession rom) with
    | Ok _ -> Assert.Fail "Expected unavailable boot ROM error."
    | Error message -> Assert.Contains("Boot ROM required by save state is unavailable", message)

[<Fact>]
let ``save state restores a disabled boot ROM without the BIOS file`` () =
    let rom = makeRom "BOOT-OFF" [| 0x00uy |]
    let bootRom = Array.zeroCreate<byte> 256

    let bootSession =
        match Emulator.createSessionWithDmgBootRom bootRom rom with
        | Ok session -> session
        | Error message -> failwith message

    let disabled =
        { bootSession with
            Bus = Bus.writeByte 0xFF50us 1uy bootSession.Bus }

    let encoded = encodeSession disabled

    match SaveState.restoreBytes encoded (createSession rom) with
    | Error message -> Assert.Fail message
    | Ok restored -> Assert.False(Bus.isBootRomEnabled restored.Bus)

[<Fact>]
let ``save state restores an enabled CGB boot ROM with matching identity`` () =
    let rom = makeRom "CGB-BOOT" [| 0x00uy |]
    rom[0x0143] <- 0xC0uy
    let bootRom = Array.init 2304 (fun index -> byte index)

    let createBootSession bytes =
        match Emulator.createSessionWithCgbBootRom bytes rom with
        | Ok session -> session
        | Error message -> failwith message

    let session =
        createBootSession bootRom |> Emulator.run 1 |> (fun result -> result.Session)

    match SaveState.restoreBytes (encodeSession session) (createBootSession bootRom) with
    | Error message -> Assert.Fail message
    | Ok restored ->
        Assert.Equal(Hardware.Cgb, Bus.mode restored.Bus)
        Assert.True(Bus.isBootRomEnabled restored.Bus)
        Assert.Equal(session.Cpu, restored.Cpu)
