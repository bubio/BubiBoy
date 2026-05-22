# Testing Conventions

## Commands

Run the full test suite with:

```sh
dotnet test BubiBoy.slnx
```

Use focused filters while developing:

```sh
dotnet test BubiBoy.slnx --filter CpuTests
```

## Scope

- Unit tests should cover small hardware behaviors directly.
- Cartridge, bus, CPU, timer, interrupt, PPU, and APU behavior should each have focused tests.
- Bugs found through ROM execution should become deterministic tests when practical.
- Tests should not require copyrighted ROMs or assets.

## Test ROMs

Only commit test ROMs when redistribution is explicitly permitted. Record source, license, and usage in [reference-provenance.md](reference-provenance.md).

For private local ROM collections, use the smoke runner documented in [rom-smoke.md](rom-smoke.md). Local commercial ROMs must not be committed.

## Style

- Prefer readable test names that describe hardware behavior.
- Keep synthetic ROMs minimal and generated inside tests when possible.
- Assert PC, cycles, flags, memory side effects, and stop reasons explicitly.
