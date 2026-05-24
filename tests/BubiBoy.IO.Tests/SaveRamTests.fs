module BubiBoy.IO.Tests.SaveRamTests

open System
open System.IO
open BubiBoy.Core
open BubiBoy.IO
open Xunit

let private writeAscii offset (text: string) (rom: byte[]) =
    text.ToCharArray()
    |> Array.iteri (fun index ch -> rom[offset + index] <- byte ch)

let private makeRom cartridgeType romSizeCode ramSizeCode bankCount =
    let rom = Array.zeroCreate<byte> (bankCount * 16 * 1024)
    writeAscii 0x0134 "SAVETEST" rom
    rom[0x0147] <- cartridgeType
    rom[0x0148] <- romSizeCode
    rom[0x0149] <- ramSizeCode
    rom

let private makeCartridge () =
    let rom = makeRom 0x03uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Ok image -> image
    | Error message -> failwith message

let private makeRtcCartridge () =
    let rom = makeRom 0x10uy 0x04uy 0x03uy 32

    match CartridgeMemory.create rom with
    | Ok image -> image
    | Error message -> failwith message

let private tempPath name =
    Path.Combine(Path.GetTempPath(), $"bubiboy-{Guid.NewGuid():N}", name)

[<Fact>]
let ``defaultSavePath replaces ROM extension with sav`` () =
    match SaveRam.defaultSavePath "/tmp/game.gb" with
    | Error message -> Assert.Fail message
    | Ok path -> Assert.Equal("/tmp/game.sav", path)

[<Fact>]
let ``saveToPath writes battery backed RAM and creates directories`` () =
    let savePath = tempPath "nested/game.sav"

    let cartridge =
        makeCartridge ()
        |> CartridgeMemory.writeByte 0x0000us 0x0Auy
        |> CartridgeMemory.writeByte 0xA000us 0x42uy

    match SaveRam.saveToPath savePath cartridge with
    | Error message -> Assert.Fail message
    | Ok wrote ->
        Assert.True wrote
        Assert.True(File.Exists savePath)
        Assert.Equal(32 * 1024, File.ReadAllBytes(savePath).Length)
        Assert.Equal(0x42uy, File.ReadAllBytes(savePath)[0])

[<Fact>]
let ``saveToPath skips cartridges without battery backed RAM`` () =
    let rom = makeRom 0x02uy 0x04uy 0x03uy 32
    let savePath = tempPath "game.sav"

    match CartridgeMemory.create rom with
    | Error message -> Assert.Fail message
    | Ok cartridge ->
        match SaveRam.saveToPath savePath cartridge with
        | Error message -> Assert.Fail message
        | Ok wrote ->
            Assert.False wrote
            Assert.False(File.Exists savePath)

[<Fact>]
let ``loadFromPath imports existing save data`` () =
    let savePath = tempPath "game.sav"
    Directory.CreateDirectory(Path.GetDirectoryName savePath) |> ignore

    let saveRam = Array.zeroCreate<byte> (32 * 1024)
    saveRam[0] <- 0x24uy
    File.WriteAllBytes(savePath, saveRam)

    match SaveRam.loadFromPath savePath (makeCartridge ()) with
    | Error message -> Assert.Fail message
    | Ok cartridge ->
        let readable = cartridge |> CartridgeMemory.writeByte 0x0000us 0x0Auy
        Assert.Equal(0x24uy, CartridgeMemory.readByte 0xA000us readable)

[<Fact>]
let ``loadFromPath leaves cartridge unchanged when save file is missing`` () =
    let savePath = tempPath "missing.sav"

    match SaveRam.loadFromPath savePath (makeCartridge ()) with
    | Error message -> Assert.Fail message
    | Ok cartridge -> Assert.True(CartridgeMemory.hasBatteryBackedRam cartridge)

[<Fact>]
let ``saveForRom writes RTC data next to save RAM`` () =
    let romPath = tempPath "game.gb"

    let cartridge =
        makeRtcCartridge ()
        |> CartridgeMemory.advanceRtcSeconds 42

    match SaveRam.saveForRom romPath cartridge with
    | Error message -> Assert.Fail message
    | Ok wrote ->
        Assert.True wrote
        Assert.True(File.Exists(Path.ChangeExtension(romPath, ".rtc")))

[<Fact>]
let ``loadForRom imports RTC data`` () =
    let romPath = tempPath "game.gb"

    let cartridge =
        makeRtcCartridge ()
        |> CartridgeMemory.advanceRtcSeconds 42

    match SaveRam.saveForRom romPath cartridge with
    | Error message -> Assert.Fail message
    | Ok _ ->
        match SaveRam.loadForRom romPath (makeRtcCartridge ()) with
        | Error message -> Assert.Fail message
        | Ok loaded ->
            let readable =
                loaded
                |> CartridgeMemory.writeByte 0x0000us 0x0Auy
                |> CartridgeMemory.writeByte 0x4000us 0x08uy

            Assert.Equal(42uy, CartridgeMemory.readByte 0xA000us readable)

[<Fact>]
let ``loadRtcFromPath reports corrupt RTC files as errors`` () =
    let rtcPath = tempPath "game.rtc"
    Directory.CreateDirectory(Path.GetDirectoryName rtcPath) |> ignore
    File.WriteAllBytes(rtcPath, [| 0uy; 1uy; 2uy |])

    match SaveRam.loadRtcFromPath rtcPath (makeRtcCartridge ()) with
    | Ok _ -> Assert.Fail "Expected corrupt RTC file to fail."
    | Error message -> Assert.Contains("RTC data", message)
