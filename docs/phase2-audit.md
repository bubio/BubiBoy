# Phase 2 Audit

This audit reconciles the Phase 2 plan with the current implementation state.

## Completed

- CPU register state, flags, instruction dispatch, instruction execution, CB-prefixed operations, stack operations, jumps/calls/returns, interrupt entry, HALT/STOP handling, and unsupported-opcode diagnostics are implemented in `BubiBoy.Core`.
- The memory bus covers cartridge ROM/RAM access, VRAM, WRAM and echo RAM, OAM, IO registers, HRAM, interrupt enable, post-boot register defaults, timer/LCD ticking, OAM DMA, and LCD-mode VRAM/OAM access restrictions.
- Interrupts, timers, divider behavior, joypad input, and serial IO register stubs are implemented and covered by focused tests.
- ROM-only cartridge support exists, and later cartridge work extends this through MBC1, MBC2, MBC3, and MBC5.

## Test ROM Validation

Phase 2 CPU validation is covered by a redistributable Mooneye Test Suite subset.

Current coverage:

- Focused unit tests exercise CPU instructions, flags, interrupts, bus behavior, timers, joypad, cartridge memory, and video behavior.
- The ROM smoke runner executes local ROMs and reports load errors, unsupported opcodes, bad stack pointers, and suspicious program counters.
- Local commercial ROMs are useful for smoke testing but cannot be committed and do not provide standardized pass/fail CPU validation.
- `tests/BubiBoy.TestRoms` runs MIT-licensed Mooneye acceptance ROMs under `dotnet test`.
- The current vendored Mooneye subset is `acceptance/instr/daa.gb` and `acceptance/bits/reg_f.gb`.
- The harness detects Mooneye pass/fail through the documented `LD B,B` breakpoint register protocol.

Each imported ROM is recorded in `reference-provenance.md` with license and redistribution notes.

## Follow-up Coverage

Phase 2 is closed, but the Mooneye harness should grow as CPU, interrupt, timer, and PPU timing accuracy improves. Add new ROMs one at a time so failures remain actionable.

## State Modeling Follow-up

The core still exposes several mutable arrays on hardware state records for pragmatic bring-up speed and rendering performance. New code should prefer narrow transition functions, defensive copies at IO boundaries, and small domain types over raw byte arrays when the data is not on a hot path.
