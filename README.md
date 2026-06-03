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

## Publish

Build the native audio wrapper first, then publish the Avalonia app for the target runtime.

```sh
cmake -S native -B native/build/cmake -DCMAKE_BUILD_TYPE=Release
cmake --build native/build/cmake --config Release
```

The CI workflow publishes self-contained distribution artifacts:

- `BubiBoy-win-x64` and `BubiBoy-win-arm64`: a single-file `BubiBoy.exe`.
- `BubiBoy-<osx-rid>`: a `BubiBoy.app` bundle.
- `BubiBoy-linux-x64` and `BubiBoy-linux-arm64`: an AppImage.

For local publishing:

```sh
dotnet publish src/BubiBoy.App/BubiBoy.App.fsproj -c Release -r linux-x64 --self-contained true
dotnet publish src/BubiBoy.App/BubiBoy.App.fsproj -c Release -r linux-arm64 --self-contained true
dotnet publish src/BubiBoy.App/BubiBoy.App.fsproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish src/BubiBoy.App/BubiBoy.App.fsproj -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

macOS publishes to `src/BubiBoy.App/bin/Release/<rid>/BubiBoy.app`. Linux and Windows publish to
`src/BubiBoy.App/bin/Release/net10.0/<rid>/publish`.

For packaged audio output, place the native wrapper in the published app under
`runtimes/<rid>/native`. The CI workflow does this automatically for its artifacts.

## Requirements

- .NET 10 SDK
- CMake and a C compiler for native audio builds

## License

BubiBoy is licensed under the MIT License.
