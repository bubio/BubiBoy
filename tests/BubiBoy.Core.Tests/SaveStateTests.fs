module BubiBoy.Core.Tests.SaveStateTests

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

    program
    |> Array.iteri (fun index value -> rom[0x0100 + index] <- value)

    rom

let private createSession rom =
    match Emulator.createSession rom with
    | Ok session -> session
    | Error message -> failwith message

[<Fact>]
let ``save state round trips session and can continue deterministically`` () =
    let rom = makeRom "STATE" [| 0x3Cuy; 0xEAuy; 0x00uy; 0xC0uy; 0x00uy |]
    let session = createSession rom |> Emulator.run 3 |> fun result -> result.Session
    let encoded = session |> SaveState.capture |> SaveState.encode

    let restored =
        createSession rom
        |> SaveState.restoreBytes encoded

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
    let encoded = first |> SaveState.capture |> SaveState.encode

    match SaveState.restoreBytes encoded second with
    | Ok _ -> failwith "Expected ROM identity mismatch."
    | Error message -> Assert.Contains("ROM identity", message)

[<Fact>]
let ``save state rejects corrupt magic`` () =
    let bytes = [| 0x00uy; 0x01uy; 0x02uy |]

    match SaveState.decode bytes with
    | Ok _ -> failwith "Expected corrupt save state to fail."
    | Error message -> Assert.Contains("save state", message)
