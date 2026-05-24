# Cartridge Support

This document records the current cartridge hardware support and known gaps.

## Implemented

- ROM-only fixed-bank cartridges.
- MBC1 ROM banking, RAM banking mode, RAM enable, and external RAM reads/writes.
- MBC2 ROM banking, RAM enable, and 512 x 4-bit internal RAM reads/writes.
- MBC3 ROM banking, RAM bank selection, RAM enable, external RAM reads/writes, and deterministic RTC register storage.
- MBC5 9-bit ROM banking, 4-bit RAM bank selection, RAM enable, and external RAM reads/writes.

## Known Gaps

- Battery-backed save RAM is not persisted to disk yet.
- MBC3 RTC does not advance time, latch time snapshots, or model day carry/halt behavior yet.
- Rumble cartridge variants are not classified or implemented.
- HuC1, HuC3, MMM01, Pocket Camera, Bandai TAMA5, and other uncommon cartridge hardware are not implemented.
- The emulator does not currently expose compatibility warnings in the UI for unsupported cartridge hardware.

## Verification

Focused unit tests cover ROM bank switching, RAM enable behavior, RAM bank selection, MBC2 nibble RAM, MBC3 RTC register storage, and MBC5 high ROM bank selection.
