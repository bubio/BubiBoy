# Test ROM Quality Audit

## Baseline

The quality pass follows the recommendations in Keichi Takahashi's article
"ゲームボーイのエミュレータを自作した話": use hardware-verified test ROMs, treat
unofficial documentation as fallible, and reduce ROM failures to deterministic unit
tests.

The baseline on 2026-06-13 was 532 passing tests with two Mooneye acceptance ROMs.
The pinned Mooneye distribution is `mts-20240926-1737-443f6e1`.

## Enabled Acceptance Coverage

The normal test suite now runs 16 MIT-licensed Mooneye ROMs covering:

- DAA and F-register behavior;
- DIV and instruction/interrupt timing used by the current execution model;
- EI delay, DI/EI sequences, HALT wake-up, and interrupt entry;
- all four timer input frequencies, DIV-write edge behavior, and delayed TIMA reload.

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
- Save-state version 7 preserves pending EI and timer reload state.

Each behavior has a focused unit regression test.

After this pass, the local solution test count is 555 passing tests, including 23
tests in `BubiBoy.TestRoms`.

## Known Failing Groups

The following Mooneye tests were evaluated but are not enabled in CI:

- instruction internal timing: `call_timing`, `call_cc_timing`, `jp_timing`,
  `jp_cc_timing`, `ret_timing`, `ret_cc_timing`, `push_timing`, `pop_timing`,
  `rst_timing`, `add_sp_e_timing`, and `ld_hl_sp_e_timing`;
- interrupt register bus timing: `if_ie_registers`;
- timer write collision timing: `tima_write_reloading` and
  `tma_write_reloading`.

These failures require bus reads and writes to occur at the correct machine cycle
inside an instruction. `Cpu.step` currently applies device ticking after the complete
instruction, so changing only the reported instruction cycle count would hide rather
than fix the problem. Address this as a dedicated machine-cycle execution refactor.

PPU and CGB suites remain outside the enabled set. Their timing must be evaluated
after the CPU/bus machine-cycle boundary is available; PPU tests that depend on a
variable Mode 3 duration must not be forced to pass using fixed timing constants.

Blargg ROMs remain external-only until redistribution rights for each selected
artifact are confirmed.
