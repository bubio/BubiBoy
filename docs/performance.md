# Performance

This document records BubiBoy's emulation-core performance work: how to measure it, what
optimizations were applied, and which approaches were measured and rejected. It exists so that
future performance changes start from data rather than intuition.

The guiding constraint is the one in `AGENTS.md`: prefer small immutable domain types, and use
tightly scoped mutation on hot paths only when it is measured or plainly necessary.

## Benchmarking

The core has a BenchmarkDotNet harness at `tests/BubiBoy.Benchmarks`. It runs against a fully
synthetic, license-safe ROM built in code (`SyntheticRom.fs`) — no commercial ROM is bundled.

```sh
# Full, stable run (reliable Mean and Allocated):
dotnet run -c Release --project tests/BubiBoy.Benchmarks -- --filter '*'

# Fast dev loop (3 iterations). Allocated is deterministic and trustworthy;
# the Mean/Error are noisy at this job length — do not compare timings across short-job runs.
dotnet run -c Release --project tests/BubiBoy.Benchmarks -- --filter '*' --job short

# A single benchmark:
dotnet run -c Release --project tests/BubiBoy.Benchmarks -- --filter '*step*'
```

Eight benchmarks are exposed, all with `[<MemoryDiagnoser>]` so allocated bytes per operation are
reported:

- `Emulator.step` — one CPU step (decode/execute with machine-cycle bus advancement -> optional
  scanline render).
- `Emulator.runFrame` — a full frame including scanline rendering and audio drain.
- focused CPU mixes for NOP, WRAM writes, CALL/RET, and CB operations on `(HL)`;
- the mixed workload with the APU powered on and off.

Always re-validate correctness alongside performance:

```sh
dotnet test BubiBoy.slnx
```

## Applied optimizations

Measured cumulative effect on an Apple M4, .NET 10, Release:

| Benchmark | Before | After | Delta |
| --- | --- | --- | --- |
| `Emulator.step` | 142.9 ns / 740 B | 111.1 ns / 441 B | -22% time / -40% alloc |
| `Emulator.runFrame` | 1.55 ms / 8.40 MB | 1.24 ms / 5.00 MB | -20% time / -40% alloc |

- **`[<Struct>]` on small leaf records.** `Timer.State`/`Registers`/`TickResult`, `Lcd.State`,
  `Cpu.Registers`, and `Cpu.State` were made struct records. The `{ x with ... }` syntax, structural
  equality, and field access are unchanged; only the heap allocation disappears (they now live inline
  in their parent / on the stack).
- **`Bus.tick` redundant allocation.** `stat` now takes the `Lcd.State` explicitly instead of building
  a throwaway `{ memory with Lcd = lcd }` copy, mirroring `statInterruptSignal`.
- **Video sprite/pixel rework** (`Video.fs`):
  - `Sprite` and `BackgroundPixel` are struct records.
  - The per-scanline `seq { } |> Seq.truncate |> Seq.sortWith |> Seq.toArray` sprite pipeline was
    replaced with an in-place fill of a fixed `Sprite[]` buffer plus an insertion sort
    (`collectLineSprites` / `compareSprites`), reproducing the previous draw-priority ordering.
  - `renderWindowPixel`'s `Option<BackgroundPixel>` return was folded into a struct-returning
    `renderBackgroundOrWindowPixel`, removing ~737 KB/frame of `Some` allocations.
- **App display path** (`src/BubiBoy.App/Program.fs`, outside the core): a single `WriteableBitmap`
  and BGRA scratch buffer are created once and written in place each frame via `writeInto` +
  `InvalidateVisual()`, instead of allocating a new bitmap and ~100 KB byte array every frame.
  `applyVolume` scales the freshly drained sample buffer in place instead of `Array.map`.
- **Video scanline scratch reuse** (`Video.fs`): the emulator step path uses a thread-local
  `RenderScratch` for sprite collection and background shade/priority arrays, avoiding three small
  array allocations on each rendered scanline without adding fields to `Emulator.Session`.
- **`[<Struct>]` on `Emulator.Session`.** `Session` is the value threaded through every step and frame.
  Making it a struct removed the per-step heap allocation for the session wrapper while keeping its
  reference fields (`Bus`, `Framebuffer`) shared. In the short dev benchmark after the scanline scratch
  reuse, this moved `Emulator.step` from ~436 B to ~372 B allocated and `Emulator.runFrame` from
  ~4.95 MB to ~4.03 MB allocated.
- **Machine-cycle CPU/bus timing.** The 2026-06-13 refactor advances Timer, LCD, and DMA at each
  four-clock CPU machine cycle. One private bus working copy is created per instruction; the
  machine-cycle loop mutates only that copy and does not allocate another `Bus.Memory`.

  Stable Apple M4 / .NET 10.0.8 results before and after the timing refactor:

  | Benchmark | Before | Machine-cycle model | Delta |
  | --- | --- | --- | --- |
  | `Emulator.step` | 111.1 ns / 441 B | 219.2 ns / 1.05 KB | +97% time / +144% alloc |
  | `Emulator.runFrame` | 1.24 ms / 5.00 MB | 2.46 ms / 11.66 MB | +98% time / +133% alloc |

  A direct version that replaced `Bus.Memory` after every machine cycle measured 226.5 ns / 1.28 KB
  per step and 2.59 ms / 14.24 MB per frame. It was not kept. The retained implementation removes
  machine-cycle-proportional bus/APU allocation, but the fixed per-instruction execution context and
  working copy remain measurable costs.
- **Deferred APU synchronization.** CPU machine cycles now accumulate hardware clocks without calling
  `Apu.tick`. The pending clocks are synchronized only before APU register access, DIV reset, speed
  switching, audio observation/drain, save-state capture, or explicit external `Bus.tick`. When NR52
  has the APU powered off, pending clocks are discarded. The pending count is private transient state,
  so save-state version 7 is unchanged.
- **Timer machine-cycle allocation removal.** Internal timer transitions now return struct tuples.
  This removed reference-tuple allocation on every CPU clock while preserving the public timer result
  types and reload-collision behavior.
- **Machine-cycle bus fast path.** Timer registers are assembled directly from the owned bus state and
  the LCD enable bit is evaluated once per machine cycle instead of repeatedly passing through the
  general memory map.
- **Single CPU completion path.** Opcode branches now write their CPU result and expected cycle count
  into the existing private execution context. Removing the private `CoreStepResult` record eliminated
  one fixed allocation per instruction while keeping public `Cpu.StepResult` as a reference record.

  Stable Apple M4 / .NET 10.0.8 results after these changes:

  | Benchmark | Machine-cycle baseline | Optimized | Delta |
  | --- | --- | --- | --- |
  | `Emulator.step` | 219.2 ns / 1.05 KiB | 138.80 ns / 421 B | -37% time / -61% alloc |
  | `Emulator.runFrame` | 2.46 ms / 11.66 MiB | 1.622 ms / 4.92 MiB | -34% time / -58% alloc |

  The optimized allocation is below the pre-machine-cycle baseline (441 B per step and 5.00 MiB per
  frame). Time remains 25% to 31% above that baseline because Timer, LCD, DMA, and interrupt-visible
  bus state are now advanced at each real machine-cycle boundary.

  Focused final results:

  | Workload | Mean | Allocated |
  | --- | ---: | ---: |
  | NOP-heavy | 98.54 ns | 366 B |
  | WRAM-write-heavy | 154.12 ns | 461 B |
  | CALL/RET-heavy | 315.64 ns | 731 B |
  | CB `(HL)`-heavy | 285.04 ns | 686 B |
  | APU enabled | 139.66 ns | 421 B |
  | APU disabled | 138.57 ns | 421 B |

## Measured and rejected

These were tried and reverted because the benchmark did not support them. Record before re-trying.

- **`[<Struct>]` on `Cpu.StepResult`: rejected.** It regressed `Emulator.step` from ~117 ns to
  ~216 ns (~+60%) despite lowering allocation, because `StepResult` is constructed and returned by
  value in every one of the ~256 opcode `match` arms, and the by-value copies cost more than the GC
  savings. It is kept as a reference record. The lesson: `[<Struct>]` is not a free win for types that
  flow by value through large/deep call sites — convert the leaf types, not the wide result wrapper.
- **`[<Struct>]` on private `CoreStepResult`: rejected.** After deferred APU synchronization and timer
  tuple cleanup, the mixed step regressed from 160.14 ns / 453 B to 275.20 ns / 405 B. Returning the
  wide struct through every opcode branch cost substantially more than the 48 B allocation saving.
  The retained single-completion-path implementation removes the record instead.
- **`inline` on tiny accessors** (`combineBytes`, `getHL`, `split16`, `preserveCarry`, …): no
  measurable effect — the JIT already inlines them. Not adopted; it only adds source noise.
- **`Video.RenderScratch` in `Emulator.Session`: rejected.** It removed the same scanline scratch
  arrays, but adding another reference field to `Session` increased every `Emulator.step` result
  allocation. The thread-local scratch keeps the public session shape stable and measured better in
  the short benchmark (`Emulator.runFrame` ~4.95 MB allocated, `Emulator.step` ~436 B allocated).
- **`[<Struct>]` on broad APU state/channel records: rejected.** Converting `Apu.State`,
  `PendingSamples`, and channel/envelope records reduced little allocation and made copies much wider.
  The short benchmark regressed to ~216 ns/426 B for `Emulator.step` and ~2.68 ms/4.83 MB for
  `Emulator.runFrame`, so the APU records remain reference records for now.

## Notes for future work

- **`Bus.Memory` is deliberately a reference record**, not a struct. With 27 fields (including several
  arrays) a struct would be copied by value on every `{ with }` and every pass-through. Its internal
  byte arrays (`Vram`/`Wram`/`Oam`/`Io`/`Hram`) are mutated in place. Timer, LCD, APU, and HDMA scalar
  fields are mutable only so a private per-instruction working copy can advance without allocating a
  replacement record on every machine cycle; public bus transitions still return the resulting
  `Memory` value.
- **.NET 10 JIT escape analysis** already stack-allocates short-lived, non-escaping records. Removing
  the `{ memory with Lcd = lcd }` temporary did not change measured allocation for that reason. Only
  objects that *escape* — returned from a step and threaded through `Session` — cost heap. Focus
  allocation work on escaping values.
- **Deferred high-risk lever** if more throughput is needed: replace the per-instruction
  `Bus.Memory` working copy and `Cpu.Execution` reference record with a measured ownership-safe
  struct/byref design. Current allocation is already below the pre-machine-cycle baseline, so this
  broad signature rewrite was not justified in this pass. Mutating the caller's bus value would break
  state-transition expectations and is not an acceptable shortcut.
