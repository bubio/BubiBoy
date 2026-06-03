# Phase 5 Audio Audit

This audit reconciles the Phase 5 audio plan with the current implementation state.

## Completed

- `BubiBoy.Core` owns deterministic APU state and sample generation without depending on UI or native audio APIs.
- Pulse, wave, and noise channels have register-driven state, triggering, length counters, envelopes, sweep, channel status, and stereo routing coverage in unit tests.
- `Emulator.runFrame` returns drained audio samples for the app layer.
- `BubiBoy.Audio` provides bounded buffering, underrun padding, PCM16 conversion, WAV diagnostics, and a miniaudio-backed device behind a narrow interface.
- The app submits generated samples to the audio host and can fall back to the managed buffer when miniaudio is unavailable.
- CI builds `bubi_miniaudio` on macOS, Linux, and Windows, copies it into `native/build/runtimes/<rid>/native`, and verifies loader availability with `BUBIBOY_EXPECT_NATIVE_AUDIO=1`.
- CI bundles the built runtime-native miniaudio files into the published app artifacts.
- `tests/BubiBoy.TestRoms` can run external APU ROMs configured with `BUBIBOY_APU_TEST_ROMS` and checks
  serial output for pass/fail without vendoring unclear-license ROMs.

## Closure Decision

Phase 5 is closed as of 2026-05-27.

The milestone deliverable is met: BubiBoy has deterministic core sample generation, app-side audio
submission, a miniaudio-backed host layer with managed fallback, latency/underrun handling, CI native
audio builds for macOS/Linux/Windows, and an external-ROM validation path that does not vendor
unclear-license ROMs.

One dedicated APU timing ROM still fails. That remaining issue is a cycle-exact DIV/APU startup edge case,
not a blocker for the Phase 5 audible-playback milestone. Track it as a focused compatibility follow-up.

## Known Follow-Up

Dedicated APU test ROM validation is not complete. Blargg's `dmg_sound` ROMs are useful behavioral
references, but their redistribution status is mixed or unclear, so they must not be committed to this
repository. Use them only from a local external path until a clearly redistributable audio test ROM is
chosen.

Run external APU ROM validation with a path list separated by the platform path separator:

```sh
BUBIBOY_APU_TEST_ROMS="/path/to/01-registers.gb:/path/to/02-len ctr.gb" dotnet test tests/BubiBoy.TestRoms/BubiBoy.TestRoms.fsproj --filter ExternalApuTests
```

The harness watches the ROM's serial transfer register and treats serial text containing `Passed` as a
pass and `Failed` as a failure. It also accepts the common binary pass/fail protocol where
`03 05 08 0D 15 22` means pass and `42 42 42 42 42 42` means failure, and it recognizes the
SameSuite/Mooneye-style `LD B,B` breakpoint with pass/fail values in registers `B`, `C`, `D`, `E`, `H`,
and `L`.

Latest external validation:

| Date | ROM | Source | Result | Notes |
| --- | --- | --- | --- | --- |
| 2026-05-26 | `same-suite/apu/div_write_trigger.gb` from `c-sp/game-boy-test-roms` v7.0 | Downloaded to `/tmp`, not vendored | Passed | Passing after wiring DIV writes to the APU frame sequencer. |
| 2026-05-26 | `same-suite/apu/div_write_trigger_10.gb` from `c-sp/game-boy-test-roms` v7.0 | Downloaded to `/tmp`, not vendored | Failed | Serial output was `42 42 42 42 42 42` after 427,462 steps. SameSuite source describes this as the edge where starting APU while DIV bit 4 is set skips the first DIV-APU event. |

If Blargg `dmg_sound` is used locally, record it as an external reference only. Do not vendor the ROMs.
