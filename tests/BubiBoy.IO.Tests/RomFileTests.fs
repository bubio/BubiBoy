module BubiBoy.IO.Tests.RomFileTests

open System
open System.IO
open BubiBoy.IO
open Xunit

let private tempPath name =
    Path.Combine(Path.GetTempPath(), $"bubiboy-{Guid.NewGuid():N}", name)

[<Fact>]
let ``isCandidatePath rejects macOS AppleDouble metadata files`` () =
    Assert.False(RomFile.isCandidatePath "/roms/._Pocket Monsters.gb")
    Assert.True(RomFile.isCandidatePath "/roms/Pocket Monsters.gb")

[<Fact>]
let ``load rejects AppleDouble metadata before reading bytes`` () =
    let path = tempPath "._game.gb"

    match RomFile.load path with
    | Error message -> Assert.Contains("AppleDouble", message)
    | Ok _ -> Assert.Fail "Expected AppleDouble metadata to be rejected."

[<Theory>]
[<InlineData("game.gb", true)>]
[<InlineData("game.gbc", true)>]
[<InlineData("game.txt", false)>]
let ``hasSupportedExtension recognizes Game Boy ROM extensions`` fileName expected =
    Assert.Equal(expected, RomFile.hasSupportedExtension fileName)
