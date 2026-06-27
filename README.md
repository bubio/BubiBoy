# BubiBoy

<p align="center">
  <img src="assets/AppIcon.png" alt="BubiBoy" width="128" height="128">
</p>

BubiBoy is a Game Boy and Game Boy Color emulator written in F# on .NET 10.

<p align="center">
  <a href="https://github.com/bubio/BubiBoy/releases/latest">
    <img src="https://img.shields.io/github/v/release/bubio/BubiBoy" alt="Latest Release">
  </a>
  <a href="https://github.com/bubio/BubiBoy/blob/main/LICENSE">
    <img src="https://img.shields.io/github/license/bubio/BubiBoy" alt="License">
  </a>
  <a href="https://github.com/bubio/BubiBoy/releases/latest">
    <img src="https://img.shields.io/github/downloads/bubio/BubiBoy/total.svg" alt="Downloads">
  </a>
</p>

This project is strongly experimental and is not recommended as a daily-use emulator. There are many excellent Game Boy Color emulators available today, and those are a better choice if you want reliable compatibility and a polished play experience.

BubiBoy is a lightweight, cross-platform Game Boy and Game Boy Color emulator with a native desktop
experience on macOS, Linux, and Windows. It combines accurate hardware timing with practical features for
playing and testing classic handheld games.

![Wizardry on BubiBoy running on macOS Tahoe](/docs/Screenshot1.png)
![Dragon Quest III on BubiBoy running on Windows 11](/docs/Screenshot2.png)
![Prince of Persia on BubiBoy running on Ubuntu 24.04](/docs/Screenshot3.png)

## Features

- Opens `.gb` and `.gbc` ROM files.
- Runs DMG and CGB sessions with video, audio, keyboard/controller input, pause/reset, scaling, fullscreen, and floating window mode.
- Supports ROM-only, MBC1, MBC2, MBC3, and MBC5 cartridges.
- Loads and saves battery-backed `.sav` data automatically.
- Supports save states and recent ROMs.
- Supports opt-in RetroAchievements Softcore/Hardcore login, game identification, achievement and leaderboard lists, in-game trackers and scoreboards, badges, unlock notifications, Rich Presence, and Softcore RA-aware save states on macOS.
- Supports configurable keyboard/controller mappings and native controller input on macOS, Linux, and Windows.
- Provides nearest-neighbor, smooth, and LCD-style image filters.
- Can use external DMG and CGB boot ROMs when supplied by the user.
- Stores app settings such as volume, scale, image filter, recent ROMs, boot ROM selection, and input mappings.

## Emulation Accuracy

BubiBoy models CPU and memory activity at machine-cycle granularity, including instruction bus access,
interrupts, DMA, and timer edge behavior. This timing-focused design improves compatibility with games
that rely on subtle hardware behavior.

The emulator passes 30 hardware-verified
[Mooneye Test Suite](https://github.com/Gekkio/mooneye-test-suite) acceptance ROMs covering CPU flags,
instruction timing, interrupts, HALT/IME behavior, DIV, and timer edge cases.
[Blargg's test ROMs](http://gbdev.gg8.se/files/roms/blargg-gb-tests/) are also used for compatibility
testing.

## Run From Source

```sh
dotnet run --project src/BubiBoy.App/BubiBoy.App.fsproj
```

## Build

```sh
dotnet build BubiBoy.slnx
dotnet test BubiBoy.slnx
```

## Publish

The managed build automatically builds the native audio wrapper for the current host RID when CMake and a
C compiler are available. Cross-RID publishing still needs a prebuilt native wrapper for the target RID;
the CI workflow does this for its artifacts.

CI publishes self-contained distribution artifacts:

- `BubiBoy-win-x64` and `BubiBoy-win-arm64`: a single-file `BubiBoy.exe`.
- `BubiBoy-<osx-rid>`: a `BubiBoy.app` bundle.
- `BubiBoy-linux-x64` and `BubiBoy-linux-arm64`: an AppImage.

### macOS

```sh
tools/build-ci-macos.sh osx-arm64
tools/build-ci-macos.sh osx-x64
```

Output: `src/BubiBoy.App/bin/Release/<rid>/BubiBoy.app`

The script mirrors the macOS CI sequence: build both native libraries, restore,
build, test, self-contained publish, copy RID-specific native libraries, and
ad-hoc sign the completed bundle.

### Linux

```sh
dotnet publish src/BubiBoy.App/BubiBoy.App.fsproj -c Release -r linux-x64 --self-contained true
dotnet publish src/BubiBoy.App/BubiBoy.App.fsproj -c Release -r linux-arm64 --self-contained true
```

Output: `src/BubiBoy.App/bin/Release/net10.0/<rid>/publish`

The CI workflow wraps this publish output as an AppImage.

### Windows

```sh
dotnet publish src/BubiBoy.App/BubiBoy.App.fsproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish src/BubiBoy.App/BubiBoy.App.fsproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `src/BubiBoy.App/bin/Release/net10.0/<rid>/publish/BubiBoy.exe`

For packaged audio output, the native wrapper is copied under `runtimes/<rid>/native`.

## Requirements

- .NET 10 SDK
- CMake and a C compiler for native audio builds

## Third-party components and licenses

This project includes or bundles third-party components used for UI, audio, testing, and tooling. See the listed files and upstream projects for full license text where noted.

- Avalonia (UI): Avalonia.Desktop, Avalonia.Themes.Fluent - MIT License. See https://github.com/AvaloniaUI/Avalonia
- miniaudio (native audio wrapper): included under native/miniaudio.h and native/miniaudio-LICENSE. miniaudio is dual-licensed (Public Domain or MIT No Attribution). Full text: native/miniaudio-LICENSE. See https://github.com/mackron/miniaudio
- rcheevos 12.3.0 (RetroAchievements client): vendored under `ThirdParty/rcheevos` with the MIT license in `ThirdParty/rcheevos-LICENSE`. See https://github.com/RetroAchievements/rcheevos
- BenchmarkDotNet (benchmarks) - MIT License.
- Test-related packages: xUnit.net, coverlet.collector, Microsoft.NET.Test.Sdk - used for tests and coverage. Check each package/NuGet entry for precise license details.

## License

BubiBoy is licensed under the MIT License.
