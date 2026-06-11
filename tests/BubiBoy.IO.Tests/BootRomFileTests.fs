module BubiBoy.IO.Tests.BootRomFileTests

open System
open System.IO
open BubiBoy.IO
open Xunit

let private tempPath name =
    Path.Combine(Path.GetTempPath(), $"bubiboy-bootrom-{Guid.NewGuid():N}", name)

[<Fact>]
let ``loadDmgFromPath loads a 256 byte boot ROM`` () =
    let path = tempPath BootRomFile.DmgFileName
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllBytes(path, Array.init BootRomFile.DmgSize byte)

    match BootRomFile.loadDmgFromPath path with
    | Error message -> Assert.Fail message
    | Ok bootRom ->
        Assert.Equal(path, bootRom.Path)
        Assert.Equal(BootRomFile.DmgSize, bootRom.Bytes.Length)
        Assert.Equal(64, bootRom.Sha256.Length)

[<Fact>]
let ``loadDmgFromPath reports a missing boot ROM`` () =
    let path = tempPath BootRomFile.DmgFileName

    match BootRomFile.loadDmgFromPath path with
    | Ok _ -> Assert.Fail "Expected missing boot ROM error."
    | Error message -> Assert.Contains(path, message)

[<Fact>]
let ``loadDmgFromPath rejects an invalid size`` () =
    let path = tempPath BootRomFile.DmgFileName
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllBytes(path, Array.zeroCreate<byte> 255)

    match BootRomFile.loadDmgFromPath path with
    | Ok _ -> Assert.Fail "Expected boot ROM size error."
    | Error message -> Assert.Contains("expected 256 bytes, got 255 bytes", message)
