module BubiBoy.IO.Tests.AppSettingsTests

open System
open System.IO
open BubiBoy.IO
open Xunit

let private tempPath name =
    Path.Combine(Path.GetTempPath(), $"bubiboy-{Guid.NewGuid():N}", name)

[<Fact>]
let ``saveToPath writes versioned settings and creates directories`` () =
    let path = tempPath "nested/settings.json"

    let settings: AppSettings.Settings =
        { VolumePercent = 75
          RecentRoms = [ "/tmp/one.gb"; "/tmp/two.gbc" ]
          Scale = 4
          IsFloating = true }

    match AppSettings.saveToPath path settings with
    | Error message -> Assert.Fail message
    | Ok () ->
        Assert.True(File.Exists path)

        match AppSettings.loadFromPath path with
        | Error message -> Assert.Fail message
        | Ok loaded ->
            Assert.Equal(75, loaded.VolumePercent)
            Assert.Equal<string list>([ "/tmp/one.gb"; "/tmp/two.gbc" ], loaded.RecentRoms)
            Assert.Equal(4, loaded.Scale)
            Assert.True(loaded.IsFloating)

[<Fact>]
let ``loadFromPath returns defaults when settings file is missing`` () =
    match AppSettings.loadFromPath (tempPath "missing.json") with
    | Error message -> Assert.Fail message
    | Ok settings -> Assert.Equal(AppSettings.defaults, settings)

[<Fact>]
let ``normalize clamps volume and limits deduplicated recent ROMs`` () =
    let paths = [ for index in 0 .. 12 -> $"/tmp/game{index}.gb" ]

    let raw: AppSettings.Settings =
        { VolumePercent = 125
          RecentRoms = paths @ [ "/tmp/game1.gb"; ""; "   " ]
          Scale = 99
          IsFloating = true }

    let settings = AppSettings.normalize raw

    Assert.Equal(100, settings.VolumePercent)
    Assert.Equal(AppSettings.MaxRecentRoms, settings.RecentRoms.Length)
    Assert.Equal("/tmp/game0.gb", settings.RecentRoms.Head)
    Assert.Equal(2, settings.Scale)
    Assert.True(settings.IsFloating)

[<Fact>]
let ``rememberRom moves existing ROM to front`` () =
    let raw: AppSettings.Settings =
        { VolumePercent = 50
          RecentRoms = [ "/tmp/one.gb"; "/tmp/two.gb" ]
          Scale = 2
          IsFloating = false }

    let settings = raw |> AppSettings.rememberRom "/tmp/two.gb"

    Assert.Equal<string list>([ "/tmp/two.gb"; "/tmp/one.gb" ], settings.RecentRoms)

[<Fact>]
let ``withScale accepts supported integer scales`` () =
    let settings = AppSettings.defaults |> AppSettings.withScale 8

    Assert.Equal(8, settings.Scale)

[<Fact>]
let ``withFloating persists floating mode preference`` () =
    let settings = AppSettings.defaults |> AppSettings.withFloating true

    Assert.True(settings.IsFloating)

[<Fact>]
let ``loadFromPath migrates version 1 settings with default scale and floating mode`` () =
    let path = tempPath "settings.json"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, """{"Version":1,"VolumePercent":25,"RecentRoms":["/tmp/old.gb"]}""")

    match AppSettings.loadFromPath path with
    | Error message -> Assert.Fail message
    | Ok settings ->
        Assert.Equal(25, settings.VolumePercent)
        Assert.Equal<string list>([ "/tmp/old.gb" ], settings.RecentRoms)
        Assert.Equal(2, settings.Scale)
        Assert.False(settings.IsFloating)

[<Fact>]
let ``loadFromPath reports unsupported settings version`` () =
    let path = tempPath "settings.json"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, """{"Version":999,"VolumePercent":50,"RecentRoms":[]}""")

    match AppSettings.loadFromPath path with
    | Ok _ -> Assert.Fail "Expected unsupported settings version to fail."
    | Error message -> Assert.Contains("Unsupported settings version", message)
