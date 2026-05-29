# BubiBoy Agent Guide

This repository is for a Game Boy and Game Boy Color emulator written in F# on .NET 10.
The target platforms are macOS, Linux, and Windows. The planned UI stack is Avalonia UI, and
audio output is expected to use miniaudio through a thin interop layer.

## Project Priorities

- Keep the emulator core portable, deterministic, and independent from UI and host APIs.
- Prefer idiomatic F#: small immutable domain types, discriminated unions where they clarify state,
  explicit data flow, and tightly scoped mutation for hot emulation paths.
- Maintain a permissive licensing posture. Do not copy code from GPL, LGPL, AGPL, or unclear-license
  projects into this repository.
- Existing emulators may be used as behavioral references, but implementation should be original.
  When referencing another project, record the project name, license, and what was learned.
- Keep platform-specific behavior behind narrow interfaces.

## Expected Architecture

Use a layered structure unless the repository evolves toward a better local convention:

- `src/BubiBoy.Core`: CPU, memory bus, cartridge, PPU, APU model, timers, joypad, serial, save RAM,
  and deterministic frame stepping.
- `src/BubiBoy.IO`: ROM loading, save data persistence, configuration, and platform-neutral file helpers.
- `src/BubiBoy.Audio`: miniaudio binding and audio device integration.
- `src/BubiBoy.App`: Avalonia UI shell, input mapping, windowing, debugger views, and user settings.
- `tests/BubiBoy.Core.Tests`: focused emulator core tests.
- `tests/BubiBoy.TestRoms`: test-ROM harnesses and expected-result checks where licensing permits.
- `docs`: design notes, plans, compatibility notes, and reference provenance.

The core must not depend on Avalonia or miniaudio.

## F# Style

- Prefer modules for stateless behavior and records/unions for domain data.
- Use classes sparingly, mainly when implementing framework interfaces or encapsulating mutable runtime
  objects that have clear ownership.
- Keep mutation explicit and local. For hot paths such as CPU stepping, PPU rendering, and APU mixing,
  mutation is acceptable when it is measured or plainly necessary. See `docs/performance.md` for the
  benchmark harness and which optimizations (e.g. `[<Struct>]` conversions) were measured and kept or
  rejected before changing hot-path code.
- Avoid clever computation expressions or custom operators unless they materially simplify emulator logic.
- Model hardware registers and flags with clear names and small helper functions.
- Keep public APIs narrow. Expose frame stepping, state reset, input updates, and audio/video buffers
  through deliberate interfaces.

## Testing And Verification

- Add unit tests for CPU instruction behavior, flags, memory mapping, timers, interrupts, cartridge MBCs,
  and PPU mode timing.
- Prefer public-domain or permissively licensed test ROMs. Track license and source in documentation.
- For behavior derived from proprietary manuals or reverse-engineered notes, cite the reference in docs
  rather than copying large text into source.
- Add regression tests for every emulator bug that can be reduced to a deterministic case.
- Keep tests runnable on macOS, Linux, and Windows through `dotnet test`.

## Licensing Rules

- The repository should remain compatible with a permissive license such as MIT, BSD-2-Clause,
  BSD-3-Clause, ISC, or Apache-2.0.
- Do not paste emulator code from incompatible projects.
- Do not import assets, ROMs, BIOS files, fonts, or test files without a clear redistribution license.
- Keep third-party dependency licenses documented before adding them.
- If a reference implementation is consulted, write new code from the hardware behavior description,
  not from line-by-line translation.

## Development Workflow

- Inspect the existing code before changing structure.
- Keep changes small and cohesive.
- Use `dotnet format` when available after code changes.
- Run focused tests first, then broader `dotnet test` before finishing substantial work.
- Do not mix unrelated refactors with emulator behavior changes.
- Preserve cross-platform paths and avoid macOS-only assumptions outside explicitly platform-specific code.

## Documentation Expectations

- Put development plans and design notes in `docs/`.
- Record important emulator references and license notes in documentation as they are introduced.
- Keep docs practical: decisions, constraints, risks, and implementation milestones matter more than broad
  emulator background.
