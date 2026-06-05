# BubiBoy Development Plan

## Goal

BubiBoy is a Game Boy and Game Boy Color emulator implemented in idiomatic F# on .NET 10. The emulator
should run on macOS, Linux, and Windows, with an Avalonia UI frontend and miniaudio-backed audio output.
The core should stay portable, deterministic, testable, and independent from UI concerns.

## Non-Goals For The First Milestone

- Cycle-perfect behavior for every commercial title.
- Link cable networking.
- Advanced debugger UX.
- Shader pipelines, filters, rewind, netplay, or TAS tooling.
- Built-in copyrighted ROMs, BIOS files, or proprietary assets.

## Guiding Constraints

- Keep a permissive license posture.
- Use existing emulators only as behavioral references, not as source-code donors.
- Favor F# domain modeling where it improves clarity.
- Allow localized mutation in performance-sensitive hardware loops.
- Make the emulator core usable from tests and alternate frontends without Avalonia.

## Proposed Repository Layout

```text
src/
  BubiBoy.Core/
  BubiBoy.IO/
  BubiBoy.Audio/
  BubiBoy.App/
tests/
  BubiBoy.Core.Tests/
  BubiBoy.TestRoms/
docs/
```

`BubiBoy.Core` should contain all hardware emulation. `BubiBoy.App` should only orchestrate UI, input,
video presentation, settings, and user workflows. Audio device code should remain outside the core.

## Phase 1: Foundation

- [x] Create the .NET solution and F# projects.
- [x] Add CI for macOS, Linux, and Windows.
- [x] Choose and document the repository license.
- [x] Establish formatting, warnings, and test conventions.
- [x] Add a minimal ROM loading path that parses cartridge headers.
- [x] Add a small reference-provenance document for manuals, test ROMs, and emulator references.

Deliverable: a buildable solution with an empty but shaped emulator core and tests.

## Phase 2: DMG Core Bring-Up

- [x] Implement CPU register state, flags, instruction decoding, and instruction execution.
- [x] Implement the memory bus, boot state assumptions, cartridge ROM access, WRAM, HRAM, and IO registers.
- [x] Implement interrupts, timers, divider behavior, joypad input, and serial stubs.
- [x] Add ROM-only cartridge support.
- [x] Validate CPU behavior using permissively licensed test ROMs and focused unit tests.

Deliverable: basic DMG test ROMs execute far enough to report pass/fail through memory or serial output.

## Phase 3: Video Path

- [x] Implement LCD control/status registers and PPU mode timing.
- [x] Render background, window, and sprites for DMG mode.
- [x] Expose a stable framebuffer from the core.
- [x] Build a simple Avalonia viewport that displays frames.
- [x] Add frame stepping and throttling in the app layer.

Deliverable: simple DMG games and visual test ROMs render recognizable output.

## Phase 4: Cartridge Support

- [x] Implement MBC1, MBC2, MBC3, MBC5, RAM enable, and ROM/RAM banking.
- [x] Add battery-backed save handling through `BubiBoy.IO`.
- [x] Add RTC support for MBC3 with deterministic test hooks.
- [x] Document unsupported cartridge hardware as explicit compatibility gaps.

Deliverable: common DMG cartridges load, run, and persist saves.

## Phase 5: Audio

- [x] Implement APU channel state, frame sequencer, envelopes, sweep, length counters, and mixer behavior.
- [x] Keep sample generation deterministic in the core.
- [x] Add miniaudio output through a thin host layer.
- [x] Handle underrun, latency configuration, pause/resume, and device changes.

Deliverable: audible DMG playback with acceptable latency and no UI dependency in the core.

Current progress:

- [x] Added a deterministic core APU state machine with pulse channel triggering, frame sequencer,
  length counters, envelopes, sweep, stereo mixing, and reusable-buffer sample accumulation/draining.
- [x] Routed audio register writes through the bus so trigger bits behave as write-only events instead of
  retriggering every tick.
- [x] Exposed per-frame audio samples from `Emulator.runFrame` without adding UI or native-audio
  dependencies to `BubiBoy.Core`.
- [x] Added initial wave channel, noise channel, and NR52 channel status behavior.
- [x] Added a bounded audio host buffer with underrun padding, latency-bounding overflow behavior, and
  app-side sample submission.
- [x] Added PCM16 stereo conversion and WAV writing helpers for deterministic audio diagnostics.
- [x] Added a thin miniaudio native wrapper boundary and managed P/Invoke device with buffered fallback.
- [x] Added RID-aware miniaudio native library probing and build/publish item wiring for runtime-native
  artifact layouts.
- [x] Tightened NR52 power-off behavior so audio registers clear and powered-off channel writes are ignored.
- [x] Added CI native audio builds for macOS, Linux, and Windows, with loader availability checked by tests.
- [x] Bundled CI-built miniaudio runtime files into the published app artifacts.
- [x] Added a Phase 5 audio audit documenting completed work, licensing constraints, and close criteria.
- [x] Added an external APU ROM harness that captures serial pass/fail output without vendoring ROMs.
- [x] Fixed DIV writes so a reset while DIV bit 12 is high clocks the APU frame sequencer.
- [x] Closed Phase 5 with the remaining cycle-exact APU gap recorded in [phase5-audio-audit.md](phase5-audio-audit.md).

Known follow-up:

- SameSuite `same-suite/apu/div_write_trigger_10.gb` still fails. This is a cycle-exact APU/DIV startup
  edge case and should be handled as a focused compatibility task rather than blocking the Phase 5
  audible-playback milestone.

## Phase 6: Game Boy Color

- [x] Add CGB mode detection and hardware state.
- [x] Implement initial VRAM banks, WRAM banks, CGB palettes, HDMA/GDMA, speed switching, and CGB-specific registers.
- [x] Extend PPU rendering for CGB background/window/sprite attributes and palettes.
- [x] Add CGB compatibility tests and known-title smoke tests where legally available.

Progress note:

- Dragon Quest III - Soshite Densetsu e... (Japan).gbc reaches 2,000,000 smoke-test steps without
  unsupported opcodes, bad program counters, or load errors.
- Added focused HBlank DMA regression coverage and an external CGB smoke-test harness driven by
  `BUBIBOY_CGB_SMOKE_ROMS`, so local legally available `.gbc` titles can be checked without committing
  copyrighted ROMs.
- The external CGB smoke harness currently validates local Dragon Quest III and Wizardry I `.gbc`
  cartridges for 2,000,000 steps without early execution failures.
- Closed Phase 6 with remaining pixel-perfect visual validation and broader CGB compatibility suites
  recorded in [phase6-cgb-audit.md](phase6-cgb-audit.md).

Deliverable: representative CGB titles boot and render with correct palette behavior.

## Phase 7: Product Quality

- [x] Add persistent settings for volume, scale, floating mode, and recent ROMs.
- [x] Add pause, reset, frame step, ROM recent list, fullscreen, scaling, and basic diagnostics.
- [x] Improve the macOS app bundle for routine local use: app identity, self-contained `osx-arm64`
  publish output, ad-hoc signing, fixed arbitrary window resizing, and floating-mode layout.
- [x] Add keyboard input mapping UI and persistent mappings.
- [x] Add controller input mapping UI and persistent mappings.
- [x] Add save-state support with versioned serialization.
- [ ] Improve error messages for unsupported ROMs or invalid files.
- [ ] Improve save-data confidence for routine play, such as clearer save-state/save-RAM status and failure
  handling.

Progress note:

- The desktop shell now supports routine ROM loading and emulator control workflows, including
  open/recent ROMs, run/pause, reset, frame step, fullscreen, fixed scale selection, floating mode,
  volume control, save-RAM persistence notifications, and basic cartridge/debug details.
- Settings are stored through `BubiBoy.IO.AppSettings` with migration and normalization tests.
- Keyboard input mappings are configurable from the Avalonia app through a compact list dialog, persisted
  in versioned settings, normalized on load, and default to the original `Z`/`X`/arrow-key layout.
- Controller input mappings are configurable through the same input mapping dialog and persisted in
  versioned settings.
- Save states are available from the app menu, written next to the loaded ROM as `.state` files, and
  serialized through a versioned core format with ROM identity checks.
- The Release `osx-arm64` `.app` publish path now updates `Contents/MacOS` directly, publishes
  self-contained output, and ad-hoc signs the bundle so it can be launched with `open`.
- Remaining Phase 7 work is focused on wrap-up polish for routine play: clearer user-facing load errors and
  better confidence around save-data operations.

Deliverable: a usable desktop emulator for routine testing and play.

## Testing Strategy

- Unit-test small hardware behaviors directly.
- Use test ROM harnesses for CPU, PPU, timers, interrupts, and APU where licenses permit redistribution.
- Keep test ROM provenance documented.
- Add deterministic frame tests for rendering only when the expected output is stable enough to maintain.
- Run `dotnet test` in CI across all target operating systems.

## Reference And License Policy

Permissive and documentation-oriented references are preferred. Before adding code, tests, or data from
another project, record:

- project or document name;
- source URL;
- license;
- whether redistribution is allowed;
- how it is used by BubiBoy.

Never translate incompatible emulator source directly into F#. Hardware behavior can be reimplemented from
public documentation, test results, and independently written notes.

## Early Risks

- CPU timing and interrupt edge cases can create hidden bugs if implemented too coarsely.
- PPU timing is likely to require iterative correction against test ROMs.
- Audio correctness can be difficult to validate without a good test strategy.
- CGB support will significantly expand memory and PPU complexity.
- Native audio packaging must be checked on all target platforms early, not at the end.

## Initial Next Steps

1. Create the solution and project skeleton.
2. Select the repository license.
3. Add the core domain types for CPU registers, cartridge headers, and memory map constants.
4. Add cartridge header parsing tests.
5. Add a reference-provenance document before importing any test ROMs or external assets.
