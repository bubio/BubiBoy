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

Two benchmarks are exposed, both with `[<MemoryDiagnoser>]` so allocated bytes per operation are
reported (the primary signal when chasing GC pressure):

- `Emulator.step` — one CPU step (decode/execute -> `Bus.tick` -> optional scanline render).
- `Emulator.runFrame` — a full frame including scanline rendering and audio drain.

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

## Measured and rejected

These were tried and reverted because the benchmark did not support them. Record before re-trying.

- **`[<Struct>]` on `Cpu.StepResult`: rejected.** It regressed `Emulator.step` from ~117 ns to
  ~216 ns (~+60%) despite lowering allocation, because `StepResult` is constructed and returned by
  value in every one of the ~256 opcode `match` arms, and the by-value copies cost more than the GC
  savings. It is kept as a reference record. The lesson: `[<Struct>]` is not a free win for types that
  flow by value through large/deep call sites — convert the leaf types, not the wide result wrapper.
- **`inline` on tiny accessors** (`combineBytes`, `getHL`, `split16`, `preserveCarry`, …): no
  measurable effect — the JIT already inlines them. Not adopted; it only adds source noise.
- **`Video.RenderScratch` in `Emulator.Session`: rejected.** It removed the same scanline scratch
  arrays, but adding another reference field to `Session` increased every `Emulator.step` result
  allocation. The thread-local scratch keeps the public session shape stable and measured better in
  the short benchmark (`Emulator.runFrame` ~4.95 MB allocated, `Emulator.step` ~436 B allocated).

## Notes for future work

- **`Bus.Memory` is deliberately a reference record**, not a struct. With 27 fields (including several
  arrays) a struct would be copied by value on every `{ with }` and every pass-through, which would be
  far more expensive than the single reference-record copy per tick it costs today. Its internal byte
  arrays (`Vram`/`Wram`/`Oam`/`Io`/`Hram`) are already mutated in place by `writeByte`.
- **.NET 10 JIT escape analysis** already stack-allocates short-lived, non-escaping records. Removing
  the `{ memory with Lcd = lcd }` temporary did not change measured allocation for that reason. Only
  objects that *escape* — returned from a step and threaded through `Session` — cost heap. Focus
  allocation work on escaping values.
- **Deferred high-risk lever** if more throughput is needed: making `Bus.Memory`'s scalar fields
  mutable to drop the per-step record copy.
