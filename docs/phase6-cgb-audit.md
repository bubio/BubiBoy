# Phase 6 CGB Audit

This audit reconciles the Phase 6 Game Boy Color plan with the current implementation state.

## Completed

- `BubiBoy.Core` detects CGB-capable cartridge headers and starts those sessions in CGB mode.
- CGB boot-state differences are covered by focused emulator tests, including the CGB CPU register
  post-boot state.
- The bus implements CGB VRAM banking, WRAM banking, CGB palette index/data ports, object priority mode,
  KEY1 speed switching, CGB-specific IO defaults, GDMA, and HBlank DMA.
- CGB timing behavior has focused regression coverage for double-speed timer/LCD interaction and HBlank
  DMA block transfers on HBlank entry.
- The PPU renders CGB background/window tile attributes, tile VRAM bank selection, palette selection,
  horizontal/vertical flips, background priority, object palettes, object tile VRAM bank selection, and
  CGB object priority ordering.
- `tests/BubiBoy.TestRoms` includes an external CGB smoke harness driven by `BUBIBOY_CGB_SMOKE_ROMS`.
  It validates local legally available `.gbc` files without committing copyrighted ROMs.
- The Phase 6 test surface includes unit coverage for CGB registers, memory banking, DMA, speed switching,
  palette behavior, and sprite/background priority behavior.

## Closure Decision

Phase 6 is closed as of 2026-05-31.

The milestone deliverable is met: representative CGB titles boot and execute without early CPU failures,
and the core has deterministic tests for the main CGB hardware surfaces needed for palette-aware rendering:
CGB mode detection, banking, palettes, DMA, speed switching, and CGB PPU attributes.

Visual accuracy is not declared complete. Pixel-perfect validation against known-good hardware captures and
larger CGB compatibility suites remain compatibility work, not blockers for closing the initial CGB support
milestone.

## Known Follow-Up

- Add clearly redistributable CGB test ROMs when available. Do not vendor commercial ROMs or test ROMs with
  unclear redistribution terms.
- Add screenshot-based regression tests only after expected frames are stable enough to maintain and the
  source assets/captures have clear redistribution permission.
- Expand local external CGB smoke coverage as more legally available titles or test ROMs are available.
- Investigate any title-specific CGB boot, DMA, priority, or palette issues as focused compatibility bugs.

Run local CGB smoke validation with a path list separated by the platform path separator:

```sh
BUBIBOY_CGB_SMOKE_ROMS="/path/to/title.gbc:/path/to/another.gbc" BUBIBOY_CGB_SMOKE_STEPS=2000000 dotnet test tests/BubiBoy.TestRoms/BubiBoy.TestRoms.fsproj --filter ExternalCgbSmokeTests
```

Latest external validation:

| Date | ROM | Source | Result | Notes |
| --- | --- | --- | --- | --- |
| 2026-05-31 | `Dragon Quest III - Soshite Densetsu e... (Japan).gbc` | Local ROM collection, not vendored | Passed | Reached 2,000,000 smoke-test steps without unsupported opcodes, suspicious program counters, or load errors. |
| 2026-05-31 | `Wizardry I - Proving Grounds of the Mad Overlord (Japan).gbc` | Local ROM collection, not vendored | Passed | Reached 2,000,000 smoke-test steps without unsupported opcodes, suspicious program counters, or load errors. |
