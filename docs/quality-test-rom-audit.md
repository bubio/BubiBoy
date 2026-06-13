# Test ROM Quality Audit

## Baseline

The quality pass follows the recommendations in Keichi Takahashi's article
"ゲームボーイのエミュレータを自作した話": use hardware-verified test ROMs, treat
unofficial documentation as fallible, and reduce ROM failures to deterministic unit
tests.

The baseline on 2026-06-13 was 532 passing tests with two Mooneye acceptance ROMs.
The pinned Mooneye distribution is `mts-20240926-1737-443f6e1`.

## Completed Acceptance Coverage

The normal test suite now runs 30 MIT-licensed Mooneye ROMs covering:

- DAA and F-register behavior;
- CALL, JP, RET, PUSH, POP, RST, and SP-relative instruction bus timing;
- interrupt entry and IF/IE register timing;
- EI delay, DI/EI sequences, HALT wake-up, and interrupt entry;
- all four timer input frequencies, DIV-write edge behavior, delayed TIMA reload,
  and TIMA/TMA writes colliding with reload.

Exact files and hashes are recorded in
`tests/BubiBoy.TestRoms/roms/mooneye/README.md`.
The existing macOS, Linux, and Windows workflows already run the complete solution
test suite, so the pinned ROM set is enforced on every supported CI platform.

## Changes Driven By ROM Failures

- CPU IME enable is now delayed until the instruction after `EI`.
- A halted CPU wakes for a pending enabled interrupt even when IME is clear, without
  servicing the interrupt.
- The timer now increments TIMA from falling edges of the selected DIV bit.
- DIV and TAC writes apply the hardware falling-edge increment behavior.
- TIMA overflow remains zero for four CPU cycles before reloading TMA and requesting
  the timer interrupt.
- TIMA writes before reload cancel the pending reload.
- `Cpu.step` now advances the bus in four-clock machine cycles. Opcode fetches,
  operand reads, writes, stack operations, and internal waits occur at their actual
  instruction positions instead of ticking all peripherals after the instruction.
- Interrupt entry is modeled as five machine cycles, including the two stack writes
  in high-byte then low-byte order. Interrupt request observation does not add a
  memory-read cycle.
- CPU bus writes at the end of the TIMA reload cycle implement the hardware
  collision behavior: writes to TIMA are ignored, while writes to TMA update both
  TMA and the reloaded TIMA value.
- OAM DMA now has its two-machine-cycle start delay and advances one byte per
  machine cycle. IF reads expose unused high bits as set.
- Save-state version 7 preserves pending EI and timer reload state.

Each behavior has a focused unit regression test.

The public stepping contract is unchanged: `StepResult.Cycles`,
`Session.TotalCycles`, double-speed hardware-clock conversion, scanline rendering,
`Cpu.State`, `Emulator.Session`, and save-state version 7 retain their existing
external shapes.

After this pass, the local solution test count is 579 passing tests, including 37
tests in `BubiBoy.TestRoms`.

## Resolved Failure Groups

- instruction internal timing: `call_timing`, `call_cc_timing`, `jp_timing`,
  `jp_cc_timing`, `ret_timing`, `ret_cc_timing`, `push_timing`, `pop_timing`,
  `rst_timing`, `add_sp_e_timing`, and `ld_hl_sp_e_timing`;
- interrupt register bus timing: `if_ie_registers`;
- timer write collision timing: `tima_write_reloading` and
  `tma_write_reloading`.

All 14 ROMs now run in the normal macOS, Linux, and Windows CI path. No instruction
cycle constants or ROM-specific timing exceptions were used to force a pass.

## Deferred Coverage

PPU and CGB suites remain outside the enabled set. Their timing must be evaluated
as a separate audit; PPU tests that depend on a variable Mode 3 duration must not be
forced to pass using fixed timing constants.

Blargg ROMs remain external-only until redistribution rights for each selected
artifact are confirmed.
