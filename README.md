# BubiBoy

BubiBoy is an early-stage Game Boy and Game Boy Color emulator written in F# on .NET 10.

The planned desktop UI is Avalonia UI. Audio integration is expected to use miniaudio through a narrow host layer. The emulator core is kept separate from UI, audio devices, and platform-specific APIs.

## Current Status

- F#/.NET 10 solution and project layout.
- Avalonia shell that can open `.gb` and `.gbc` files.
- Cartridge header parsing and display.
- ROM-only and basic MBC1 ROM bank reads.
- Initial memory bus.
- Early CPU stepping for a small set of opcodes.

This is not yet a playable emulator.

## Build

```sh
dotnet build BubiBoy.slnx
```

## Test

```sh
dotnet test BubiBoy.slnx
```

## ROM Smoke Testing

Local ROM collections can be used for private verification without committing ROMs to the repository:

```sh
dotnet run --project tools/BubiBoy.RomSmoke/BubiBoy.RomSmoke.fsproj -- /Volumes/CrucialX6/roms/GB --steps 2000
```

See [docs/rom-smoke.md](docs/rom-smoke.md).

## Run

```sh
dotnet run --project src/BubiBoy.App/BubiBoy.App.fsproj
```

For performance testing, use Release builds. Debug builds are not representative for emulator speed:

```sh
dotnet run -c Release --project src/BubiBoy.App/BubiBoy.App.fsproj
```

In constrained local environments, set `DOTNET_CLI_HOME` to a writable directory inside the repository:

```sh
DOTNET_CLI_HOME="$PWD/.dotnet-cli-home" dotnet run -c Release --project src/BubiBoy.App/BubiBoy.App.fsproj
```

## License

BubiBoy is licensed under the MIT License. Do not add code, ROMs, assets, or test files with incompatible or unclear redistribution terms.
