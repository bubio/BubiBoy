module BubiBoy.Core.Tests.SaveStateTests

open System
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
let ``current wire format is deterministic`` () =
    let session = makeRom "S" Array.empty |> createSession
    let first = encodeSession session
    let second = encodeSession session

    Assert.Equal(SaveState.CurrentVersion, BitConverter.ToInt32(first, 9))
    Assert.Equal<byte>(first, second)

[<Fact>]
let ``save state preserves pending EI and timer reload state`` () =
    let session = makeRom "PENDING" Array.empty |> createSession

    let snapshot =
        { SaveState.capture session with
            Cpu =
                { session.Cpu with
                    EnableInterruptsAfterInstruction = true }
            Bus =
                { (SaveState.capture session).Bus with
                    TimerSnapshot =
                        { Divider = 0x1234us
                          ReloadDelay = Some 3 } } }

    match SaveState.decode (SaveState.encode snapshot) with
    | Error message -> Assert.Fail message
    | Ok decoded ->
        Assert.True(decoded.Cpu.EnableInterruptsAfterInstruction)
        Assert.Equal(0x1234us, decoded.Bus.TimerSnapshot.Divider)
        Assert.Equal(Some 3, decoded.Bus.TimerSnapshot.ReloadDelay)

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

[<Fact>]
let ``save state restores CGB compatibility mode`` () =
    let rom = makeRom "CGB-DMG" [| 0x00uy |]
    let bootRom = Array.init 2304 (fun index -> byte index)

    let createBootSession () =
        match Emulator.createSessionWithCgbBootRom bootRom rom with
        | Ok session -> session
        | Error message -> failwith message

    let session =
        let bootSession = createBootSession ()

        { bootSession with
            Bus =
                bootSession.Bus
                |> Bus.writeByte 0xFF4Cus 0x04uy
                |> Bus.writeByte 0xFF50us 0x01uy }

    match SaveState.restoreBytes (encodeSession session) (createSession rom) with
    | Error message -> Assert.Fail message
    | Ok restored ->
        Assert.Equal(Hardware.CgbCompatibility, Bus.mode restored.Bus)
        Assert.False(Bus.isBootRomEnabled restored.Bus)
