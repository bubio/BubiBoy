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
- The pinned Mooneye subset contains 16 acceptance ROMs for flags, DAA, DIV, IME/EI/DI, HALT,
  interrupt timing, timer frequencies, DIV-write edges, and delayed TIMA reload. Exact files and hashes
  are recorded in `tests/BubiBoy.TestRoms/roms/mooneye/README.md`.
- The shared harness detects the documented `LD B,B` register protocol and serial pass/fail output, and
  reports a bounded register/cycle trace on failure.

Each imported ROM is recorded in `reference-provenance.md` with license and redistribution notes.

## Follow-up Coverage

Phase 2 is closed, but machine-cycle bus timing and PPU/CGB acceptance coverage remain follow-up work.
Current investigated failures and the reason they are not enabled in CI are recorded in
`quality-test-rom-audit.md`.

## State Modeling Follow-up

`Bus.Memory` and `CartridgeMemory.CartridgeImage` now hide their record representations and expose narrow accessor/transition functions instead of public `byte[]` fields. This keeps existing hot-path arrays inside the core while preventing outside modules from mutating bus or cartridge memory behind the state transition API.

New code should continue this direction: prefer narrow transition functions, defensive copies at IO boundaries, and small domain types over raw byte arrays when the data is not on a hot path.
