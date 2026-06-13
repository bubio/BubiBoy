module BubiBoy.IO.Tests.AppSettingsTests

open System
open System.IO
open BubiBoy.IO
open Xunit

let private tempPath name =
    Path.Combine(Path.GetTempPath(), $"bubiboy-{Guid.NewGuid():N}", name)

let private fullPath path = Path.GetFullPath path

[<Fact>]
let ``saveToPath writes versioned settings and creates directories`` () =
    let path = tempPath "nested/settings.json"
    let recentRoms = [ tempPath "one.gb"; tempPath "two.gbc" ]

    let settings: AppSettings.Settings =
        { VolumePercent = 75
          RecentRoms = recentRoms
          Scale = 4
          ShowFullScreenInfo = false
          VideoFilter = AppSettings.Smooth
          BootRomSelection = AppSettings.Cgb
          KeyboardMapping = AppSettings.defaultKeyboardMapping |> Map.add "A" "C" |> Map.add "B" "V"
          ControllerMapping =
            AppSettings.defaultControllerMapping
            |> Map.add "A" "West"
            |> Map.add "B" "North" }

    match AppSettings.saveToPath path settings with
    | Error message -> Assert.Fail message
    | Ok() ->
        Assert.True(File.Exists path)
        let savedJson = File.ReadAllText path
        Assert.Contains("\"Version\": 7", savedJson)
        Assert.Contains("\"VideoFilter\": \"Smooth\"", savedJson)
        Assert.DoesNotContain("IsFloating", savedJson)

        match AppSettings.loadFromPath path with
        | Error message -> Assert.Fail message
        | Ok loaded ->
            Assert.Equal(75, loaded.VolumePercent)
            Assert.Equal<string list>(recentRoms |> List.map fullPath, loaded.RecentRoms)
            Assert.Equal(4, loaded.Scale)
            Assert.False(loaded.ShowFullScreenInfo)
            Assert.Equal(AppSettings.Smooth, loaded.VideoFilter)
            Assert.Equal(AppSettings.Cgb, loaded.BootRomSelection)
            Assert.Equal("C", loaded.KeyboardMapping["A"])
            Assert.Equal("V", loaded.KeyboardMapping["B"])
            Assert.Equal("West", loaded.ControllerMapping["A"])
            Assert.Equal("North", loaded.ControllerMapping["B"])

[<Fact>]
let ``loadFromPath returns defaults when settings file is missing`` () =
    match AppSettings.loadFromPath (tempPath "missing.json") with
    | Error message -> Assert.Fail message
    | Ok settings -> Assert.Equal(AppSettings.defaults, settings)

[<Fact>]
let ``normalize clamps volume and limits deduplicated recent ROMs`` () =
    let paths = [ for index in 0..12 -> tempPath $"game{index}.gb" ]

    let raw: AppSettings.Settings =
        { VolumePercent = 125
          RecentRoms = paths @ [ paths[1]; ""; "   " ]
          Scale = 99
          ShowFullScreenInfo = false
          VideoFilter = AppSettings.Lcd
          BootRomSelection = AppSettings.Automatic
          KeyboardMapping =
            AppSettings.defaultKeyboardMapping
            |> Map.add "A" "Q"
            |> Map.add "B" "Q"
            |> Map.add "Unknown" "W"
            |> Map.add "Start" ""
          ControllerMapping =
            AppSettings.defaultControllerMapping
            |> Map.add "A" "West"
            |> Map.add "B" "West"
            |> Map.add "Select" "NotAControl"
            |> Map.add "Unknown" "North" }

    let settings = AppSettings.normalize raw

    Assert.Equal(100, settings.VolumePercent)
    Assert.Equal(AppSettings.MaxRecentRoms, settings.RecentRoms.Length)
    Assert.Equal(fullPath paths[0], settings.RecentRoms.Head)
    Assert.Equal(2, settings.Scale)
    Assert.False(settings.ShowFullScreenInfo)
    Assert.Equal(AppSettings.Lcd, settings.VideoFilter)
    Assert.Equal(AppSettings.Automatic, settings.BootRomSelection)
    Assert.Equal("Q", settings.KeyboardMapping["A"])
    Assert.Equal("X", settings.KeyboardMapping["B"])
    Assert.Equal("Enter", settings.KeyboardMapping["Start"])
    Assert.False(settings.KeyboardMapping.ContainsKey "Unknown")
    Assert.Equal("West", settings.ControllerMapping["A"])
    Assert.Equal("East", settings.ControllerMapping["B"])
    Assert.Equal("Select", settings.ControllerMapping["Select"])
    Assert.False(settings.ControllerMapping.ContainsKey "Unknown")

[<Fact>]
let ``rememberRom moves existing ROM to front`` () =
    let one = tempPath "one.gb"
    let two = tempPath "two.gb"

    let raw: AppSettings.Settings =
        { VolumePercent = 50
          RecentRoms = [ one; two ]
          Scale = 2
          ShowFullScreenInfo = true
          VideoFilter = AppSettings.Off
          BootRomSelection = AppSettings.Disabled
          KeyboardMapping = AppSettings.defaultKeyboardMapping
          ControllerMapping = AppSettings.defaultControllerMapping }

    let settings = raw |> AppSettings.rememberRom two

    Assert.Equal<string list>([ fullPath two; fullPath one ], settings.RecentRoms)

[<Fact>]
let ``withBootRomSelection persists the selected policy`` () =
    let settings =
        AppSettings.defaults |> AppSettings.withBootRomSelection AppSettings.Dmg

    Assert.Equal(AppSettings.Dmg, settings.BootRomSelection)

[<Fact>]
let ``withScale accepts supported integer scales`` () =
    let settings = AppSettings.defaults |> AppSettings.withScale 8

    Assert.Equal(8, settings.Scale)

[<Fact>]
let ``withShowFullScreenInfo persists the selected visibility`` () =
    let settings = AppSettings.defaults |> AppSettings.withShowFullScreenInfo false

    Assert.False(settings.ShowFullScreenInfo)

[<Fact>]
let ``withVideoFilter persists the selected filter`` () =
    let settings = AppSettings.defaults |> AppSettings.withVideoFilter AppSettings.Lcd

    Assert.Equal(AppSettings.Lcd, settings.VideoFilter)

[<Fact>]
let ``withKeyboardMapping persists normalized keyboard mapping`` () =
    let settings =
        AppSettings.defaults
        |> AppSettings.withKeyboardMapping (AppSettings.defaultKeyboardMapping |> Map.add "A" "C")

    Assert.Equal("C", settings.KeyboardMapping["A"])

[<Fact>]
let ``withControllerMapping persists normalized controller mapping`` () =
    let settings =
        AppSettings.defaults
        |> AppSettings.withControllerMapping (AppSettings.defaultControllerMapping |> Map.add "A" "West")

    Assert.Equal("West", settings.ControllerMapping["A"])

[<Fact>]
let ``loadFromPath migrates version 1 settings with default scale and input mapping`` () =
    let path = tempPath "settings.json"
    let oldRom = tempPath "old.gb"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, $"""{{"Version":1,"VolumePercent":25,"RecentRoms":["{oldRom.Replace("\\", "\\\\")}"]}}""")

    match AppSettings.loadFromPath path with
    | Error message -> Assert.Fail message
    | Ok settings ->
        Assert.Equal(25, settings.VolumePercent)
        Assert.Equal<string list>([ fullPath oldRom ], settings.RecentRoms)
        Assert.Equal(2, settings.Scale)
        Assert.True(settings.ShowFullScreenInfo)
        Assert.Equal(AppSettings.Off, settings.VideoFilter)
        Assert.Equal(AppSettings.Disabled, settings.BootRomSelection)
        Assert.Equal<Map<string, string>>(AppSettings.defaultKeyboardMapping, settings.KeyboardMapping)
        Assert.Equal<Map<string, string>>(AppSettings.defaultControllerMapping, settings.ControllerMapping)

[<Fact>]
let ``loadFromPath migrates version 2 settings and ignores floating mode`` () =
    let path = tempPath "settings.json"
    let oldRom = tempPath "old.gb"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore

    File.WriteAllText(
        path,
        $"""{{"Version":2,"VolumePercent":25,"RecentRoms":["{oldRom.Replace("\\", "\\\\")}"],"Scale":4,"IsFloating":true}}"""
    )

    match AppSettings.loadFromPath path with
    | Error message -> Assert.Fail message
    | Ok settings ->
        Assert.Equal(25, settings.VolumePercent)
        Assert.Equal<string list>([ fullPath oldRom ], settings.RecentRoms)
        Assert.Equal(4, settings.Scale)
        Assert.True(settings.ShowFullScreenInfo)
        Assert.Equal(AppSettings.Off, settings.VideoFilter)
        Assert.Equal(AppSettings.Disabled, settings.BootRomSelection)
        Assert.Equal<Map<string, string>>(AppSettings.defaultKeyboardMapping, settings.KeyboardMapping)
        Assert.Equal<Map<string, string>>(AppSettings.defaultControllerMapping, settings.ControllerMapping)

[<Fact>]
let ``loadFromPath migrates version 3 settings and ignores floating mode`` () =
    let path = tempPath "settings.json"
    let oldRom = tempPath "old.gb"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore

    File.WriteAllText(
        path,
        $"""{{"Version":3,"VolumePercent":25,"RecentRoms":["{oldRom.Replace("\\", "\\\\")}"],"Scale":4,"IsFloating":true,"KeyboardMapping":{{"A":"C"}}}}"""
    )

    match AppSettings.loadFromPath path with
    | Error message -> Assert.Fail message
    | Ok settings ->
        Assert.Equal(25, settings.VolumePercent)
        Assert.Equal<string list>([ fullPath oldRom ], settings.RecentRoms)
        Assert.Equal(4, settings.Scale)
        Assert.True(settings.ShowFullScreenInfo)
        Assert.Equal(AppSettings.Off, settings.VideoFilter)
        Assert.Equal(AppSettings.Disabled, settings.BootRomSelection)
        Assert.Equal("C", settings.KeyboardMapping["A"])
        Assert.Equal<Map<string, string>>(AppSettings.defaultControllerMapping, settings.ControllerMapping)

[<Fact>]
let ``loadFromPath migrates version 4 settings with boot ROMs disabled`` () =
    let path = tempPath "settings.json"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore

    File.WriteAllText(
        path,
        """{"Version":4,"VolumePercent":50,"RecentRoms":[],"Scale":2,"KeyboardMapping":{},"ControllerMapping":{}}"""
    )

    match AppSettings.loadFromPath path with
    | Error message -> Assert.Fail message
    | Ok settings ->
        Assert.Equal(AppSettings.Disabled, settings.BootRomSelection)
        Assert.True(settings.ShowFullScreenInfo)
        Assert.Equal(AppSettings.Off, settings.VideoFilter)

[<Fact>]
let ``loadFromPath migrates version 5 settings with full-screen info enabled`` () =
    let path = tempPath "settings.json"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore

    File.WriteAllText(
        path,
        """{"Version":5,"VolumePercent":50,"RecentRoms":[],"Scale":2,"BootRomSelection":"Automatic","KeyboardMapping":{},"ControllerMapping":{}}"""
    )

    match AppSettings.loadFromPath path with
    | Error message -> Assert.Fail message
    | Ok settings ->
        Assert.Equal(AppSettings.Automatic, settings.BootRomSelection)
        Assert.True(settings.ShowFullScreenInfo)
        Assert.Equal(AppSettings.Off, settings.VideoFilter)

[<Fact>]
let ``loadFromPath migrates version 6 settings with video filter off`` () =
    let path = tempPath "settings.json"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore

    File.WriteAllText(
        path,
        """{"Version":6,"VolumePercent":50,"RecentRoms":[],"Scale":2,"ShowFullScreenInfo":false,"BootRomSelection":"Automatic","KeyboardMapping":{},"ControllerMapping":{}}"""
    )

    match AppSettings.loadFromPath path with
    | Error message -> Assert.Fail message
    | Ok settings ->
        Assert.False(settings.ShowFullScreenInfo)
        Assert.Equal(AppSettings.Off, settings.VideoFilter)

[<Fact>]
let ``loadFromPath normalizes unknown video filter to off`` () =
    let path = tempPath "settings.json"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore

    File.WriteAllText(
        path,
        """{"Version":7,"VolumePercent":50,"RecentRoms":[],"Scale":2,"ShowFullScreenInfo":true,"VideoFilter":"Unknown","BootRomSelection":"Disabled","KeyboardMapping":{},"ControllerMapping":{}}"""
    )

    match AppSettings.loadFromPath path with
    | Error message -> Assert.Fail message
    | Ok settings -> Assert.Equal(AppSettings.Off, settings.VideoFilter)

[<Fact>]
let ``loadFromPath reports unsupported settings version`` () =
    let path = tempPath "settings.json"
    Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
    File.WriteAllText(path, """{"Version":999,"VolumePercent":50,"RecentRoms":[]}""")

    match AppSettings.loadFromPath path with
    | Ok _ -> Assert.Fail "Expected unsupported settings version to fail."
    | Error message -> Assert.Contains("Unsupported settings version", message)
