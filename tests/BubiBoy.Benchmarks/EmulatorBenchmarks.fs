namespace BubiBoy.Benchmarks

open BenchmarkDotNet.Attributes
open BubiBoy.Core

[<AutoOpen>]
module private BenchmarkConstants =
    [<Literal>]
    let StepsPerInvoke = 200_000

    [<Literal>]
    let MaxStepsPerFrame = 250_000

/// Benchmarks the real emulation hot path (CPU step -> Bus.tick -> scanline render)
/// against the synthetic ROM. [<MemoryDiagnoser>] reports bytes allocated per
/// operation, which is the primary signal for the struct-conversion stages.
[<MemoryDiagnoser>]
type EmulatorBenchmarks() =
    let mutable rom: byte[] = [||]
    let mutable freshSession: Emulator.Session = Unchecked.defaultof<_>
    let mutable frameSession: Emulator.Session = Unchecked.defaultof<_>

    let createSession () =
        match Emulator.createSession rom with
        | Ok session -> session
        | Error message -> failwith $"Failed to create benchmark session: {message}"

    [<GlobalSetup>]
    member _.Setup() =
        rom <- SyntheticRom.build ()
        freshSession <- createSession ()
        frameSession <- createSession ()

    /// Per-step allocation and throughput. A fresh session each iteration keeps the
    /// workload deterministic; OperationsPerInvoke normalises results to a single step.
    [<IterationSetup(Target = "Step")>]
    member _.ResetStep() = freshSession <- createSession ()

    [<Benchmark(OperationsPerInvoke = StepsPerInvoke, Description = "Emulator.step")>]
    member _.Step() =
        let mutable session = freshSession

        for _ in 1..StepsPerInvoke do
            session <- Emulator.step session

        session.TotalCycles

    /// Full-frame throughput including scanline rendering and audio drain, mirroring
    /// the App's per-frame loop. State drift across invocations is intentional and harmless.
    [<Benchmark(Description = "Emulator.runFrame")>]
    member _.RunFrame() =
        let result = Emulator.runFrame MaxStepsPerFrame frameSession
        frameSession <- result.Session
        result.Session.TotalCycles
