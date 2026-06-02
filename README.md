# BubiBoy

BubiBoy is a Game Boy and Game Boy Color emulator written in F# on .NET 10.

This project is strongly experimental and is not recommended as a daily-use emulator. There are many excellent Game Boy Color emulators available today, and those are a better choice if you want reliable compatibility and a polished play experience.

It provides an Avalonia desktop app for macOS, Linux, and Windows. The emulator core is kept separate from UI, audio devices, and platform-specific APIs.

## Current Status

- Opens `.gb` and `.gbc` ROM files.
- Runs DMG and CGB sessions with video, audio, keyboard input, pause/reset, scaling, fullscreen, and floating window mode.
- Supports ROM-only, MBC1, MBC2, MBC3, and MBC5 cartridges.
- Loads and saves battery-backed `.sav` data automatically.
- Supports save states and recent ROMs.
- Stores app settings such as volume, scale, floating mode, and keyboard mapping.

Compatibility is still in progress, so some games and cartridge variants may not work correctly yet.

## Run

```sh
dotnet run --project src/BubiBoy.App/BubiBoy.App.fsproj
```

## Build

```sh
dotnet build
```

## Requirements

- .NET 10 SDK

## License

BubiBoy is licensed under the MIT License.
