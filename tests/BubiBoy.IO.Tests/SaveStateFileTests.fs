module BubiBoy.IO.Tests.SaveStateFileTests

open System
open System.IO
open BubiBoy.Core
open BubiBoy.IO
open Xunit

let private makeRom () =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy
    rom[0x014D] <- 0x24uy
    rom[0x0100] <- 0x3Cuy
    rom

let private createSession () =
    match makeRom () |> Emulator.createSession with
    | Ok session -> session
    | Error message -> failwith message

let private tempPath name =
    Path.Combine(Path.GetTempPath(), $"BubiBoy-{Guid.NewGuid():N}", name)

[<Fact>]
let ``defaultStatePath replaces ROM extension with state`` () =
    let romPath = tempPath "game.gb"
    let expected = Path.Combine(Path.GetDirectoryName romPath, "game.state")

    match SaveStateFile.defaultStatePath romPath with
    | Ok path -> Assert.Equal(expected, path)
    | Error message -> failwith message

[<Fact>]
let ``saveToPath writes state and loadFromPath restores it`` () =
    let path = tempPath "game.state"
    let session = createSession () |> Emulator.run 1 |> fun result -> result.Session

    match SaveStateFile.saveToPath path session with
    | Error message -> failwith message
    | Ok() ->
        Assert.True(File.Exists path)

        match SaveStateFile.loadFromPath path (createSession ()) with
        | Error message -> failwith message
        | Ok restored ->
            Assert.Equal(session.Cpu, restored.Cpu)
            Assert.Equal(session.TotalCycles, restored.TotalCycles)

[<Fact>]
let ``saveToPath backs up previous state before overwriting`` () =
    let path = tempPath "game.state"
    let first = createSession () |> Emulator.run 1 |> fun result -> result.Session
    let second = first |> Emulator.run 1 |> fun result -> result.Session

    match SaveStateFile.saveToPath path first with
    | Error message -> failwith message
    | Ok() ->
        match SaveStateFile.saveToPath path second with
        | Error message -> failwith message
        | Ok() ->
            Assert.True(File.Exists $"{path}.bak")

            match SaveStateFile.loadFromPath $"{path}.bak" (createSession ()) with
            | Error message -> failwith message
            | Ok restored ->
                Assert.Equal(first.Cpu, restored.Cpu)
                Assert.Equal(first.TotalCycles, restored.TotalCycles)

[<Fact>]
let ``loadFromPath reports corrupt save state`` () =
    let path = tempPath "corrupt.state"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllBytes(path, [| 0x01uy; 0x02uy |])

    match SaveStateFile.loadFromPath path (createSession ()) with
    | Ok _ -> failwith "Expected corrupt save state to fail."
    | Error message -> Assert.Contains("save state", message)
