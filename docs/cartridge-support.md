# Cartridge Support

This document records the current cartridge hardware support and known gaps.

## Implemented

- ROM-only fixed-bank cartridges.
- MBC1 ROM banking, RAM banking mode, RAM enable, and external RAM reads/writes.
- MBC2 ROM banking, RAM enable, and 512 x 4-bit internal RAM reads/writes.
- MBC3 ROM banking, RAM bank selection, RAM enable, external RAM reads/writes, deterministic RTC advancement, RTC latch snapshots, halt behavior, day carry, and RTC register storage.
- MBC5 9-bit ROM banking, 4-bit RAM bank selection, RAM enable, and external RAM reads/writes.
- Battery-backed cartridge RAM export/import in the core.
- `.sav` file load/save helpers in `BubiBoy.IO`.
- Automatic `.sav` loading when opening a ROM in the Avalonia app.
- Automatic `.sav` saving when stopping, opening another ROM, or closing the Avalonia app.

## Known Gaps

- MBC3 RTC does not automatically sync against host wall-clock time. The core only advances RTC through explicit deterministic calls.
- MBC3 RTC state is not persisted to disk yet; `.sav` persistence currently covers cartridge RAM.
- Rumble cartridge variants are not classified or implemented.
- HuC1, HuC3, MMM01, Pocket Camera, Bandai TAMA5, and other uncommon cartridge hardware are not implemented.
- The emulator does not currently expose compatibility warnings in the UI for unsupported cartridge hardware.

## Verification

Focused unit tests cover ROM bank switching, RAM enable behavior, RAM bank selection, MBC2 nibble RAM, MBC3 RTC register storage, deterministic RTC advancement, RTC latching, halt/carry behavior, MBC5 high ROM bank selection, defensive save RAM import/export, and `.sav` file IO.
