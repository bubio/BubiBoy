# Phase 2 Audit

This audit reconciles the Phase 2 plan with the current implementation state.

## Completed

- CPU register state, flags, instruction dispatch, instruction execution, CB-prefixed operations, stack operations, jumps/calls/returns, interrupt entry, HALT/STOP handling, and unsupported-opcode diagnostics are implemented in `BubiBoy.Core`.
- The memory bus covers cartridge ROM/RAM access, VRAM, WRAM and echo RAM, OAM, IO registers, HRAM, interrupt enable, post-boot register defaults, timer/LCD ticking, OAM DMA, and LCD-mode VRAM/OAM access restrictions.
- Interrupts, timers, divider behavior, joypad input, and serial IO register stubs are implemented and covered by focused tests.
- ROM-only cartridge support exists, and later cartridge work extends this through MBC1, MBC2, MBC3, and MBC5.

## Remaining Phase 2 Gap

The remaining unchecked item is CPU validation with redistributable test ROMs.

Current coverage is useful but not enough to close this item:

- Focused unit tests exercise CPU instructions, flags, interrupts, bus behavior, timers, joypad, cartridge memory, and video behavior.
- The ROM smoke runner executes local ROMs and reports load errors, unsupported opcodes, bad stack pointers, and suspicious program counters.
- Local commercial ROMs are useful for smoke testing but cannot be committed and do not provide standardized pass/fail CPU validation.

To close Phase 2 fully, add a test-ROM harness that can run permissively licensed or explicitly redistributable CPU test ROMs and detect pass/fail through serial output or documented memory locations. Each imported ROM must be recorded in `reference-provenance.md` with license and redistribution notes before it is committed.

## Next Actions

1. Select CPU test ROMs with redistribution terms compatible with the repository license posture.
2. Add a `tests/BubiBoy.TestRoms` harness or equivalent test project that runs ROMs deterministically.
3. Capture pass/fail output through serial or memory conventions.
4. Document each test ROM in `reference-provenance.md`.
5. Mark the remaining Phase 2 validation item complete only after the harness runs in `dotnet test`.
