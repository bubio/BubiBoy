# Testing Conventions

## Commands

Run the full test suite with:

```sh
dotnet test BubiBoy.slnx
```

Use focused filters while developing:

```sh
dotnet test BubiBoy.slnx --filter CpuTests
```

Use Release when measuring emulator speed or audio stability:

```sh
dotnet run -c Release --project src/BubiBoy.App/BubiBoy.App.fsproj
```

Debug FPS is useful for development only and should not be used as a performance baseline.

## Scope

- Unit tests should cover small hardware behaviors directly.
- Cartridge, bus, CPU, timer, interrupt, PPU, and APU behavior should each have focused tests.
- Bugs found through ROM execution should become deterministic tests when practical.
- Tests should not require copyrighted ROMs or assets.

## Test ROMs

Only commit test ROMs when redistribution is explicitly permitted. Record source, license, and usage in [reference-provenance.md](reference-provenance.md).

For private local ROM collections, use the smoke runner documented in [rom-smoke.md](rom-smoke.md). Local commercial ROMs must not be committed.

Phase 2 CPU validation is tracked in [phase2-audit.md](phase2-audit.md). Redistributable test ROMs live under `tests/BubiBoy.TestRoms/roms/` with upstream license files.

Phase 5 audio status is tracked in [phase5-audio-audit.md](phase5-audio-audit.md). Dedicated APU test ROMs
with unclear redistribution terms should stay outside the repository and be recorded as external validation
only.

Phase 6 CGB status is tracked in [phase6-cgb-audit.md](phase6-cgb-audit.md). Commercial `.gbc` smoke
validation is external-only and must not add ROM files to the repository.

External APU ROMs that report over serial can be checked without committing the ROMs:

```sh
BUBIBOY_APU_TEST_ROMS="/path/to/01-registers.gb:/path/to/02-len ctr.gb" dotnet test tests/BubiBoy.TestRoms/BubiBoy.TestRoms.fsproj --filter ExternalApuTests
```

External CGB smoke ROMs can be checked the same way. These ROMs must come from a local, legally
available collection and must not be committed:

```sh
BUBIBOY_CGB_SMOKE_ROMS="/path/to/title.gbc:/path/to/another.gbc" BUBIBOY_CGB_SMOKE_STEPS=2000000 dotnet test tests/BubiBoy.TestRoms/BubiBoy.TestRoms.fsproj --filter ExternalCgbSmokeTests
```

## Style

- Prefer readable test names that describe hardware behavior.
- Keep synthetic ROMs minimal and generated inside tests when possible.
- Assert PC, cycles, flags, memory side effects, and stop reasons explicitly.
