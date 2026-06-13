namespace BubiBoy.Benchmarks

open BenchmarkDotNet.Attributes
open BubiBoy.Core

[<AutoOpen>]
module private BenchmarkConstants =
    [<Literal>]
    let StepsPerInvoke = 1_000_000

    [<Literal>]
    let MaxStepsPerFrame = 250_000

/// Benchmarks the real emulation hot path (CPU machine cycles -> bus devices -> scanline render)
/// against the synthetic ROM. [<MemoryDiagnoser>] reports bytes allocated per
/// operation, which is the primary signal for the struct-conversion stages.
[<MemoryDiagnoser>]
type EmulatorBenchmarks() =
    let mutable rom: byte[] = [||]
    let mutable freshSession: Emulator.Session = Unchecked.defaultof<_>
    let mutable nopSession: Emulator.Session = Unchecked.defaultof<_>
    let mutable memoryWriteSession: Emulator.Session = Unchecked.defaultof<_>
    let mutable callSession: Emulator.Session = Unchecked.defaultof<_>
    let mutable cbMemorySession: Emulator.Session = Unchecked.defaultof<_>
    let mutable apuEnabledSession: Emulator.Session = Unchecked.defaultof<_>
    let mutable apuDisabledSession: Emulator.Session = Unchecked.defaultof<_>
    let mutable frameSession: Emulator.Session = Unchecked.defaultof<_>

    let createSession rom =
        match Emulator.createSession rom with
        | Ok session -> session
        | Error message -> failwith $"Failed to create benchmark session: {message}"

    let runSteps session =
        let mutable current = session

        for _ in 1..StepsPerInvoke do
            current <- Emulator.step current

        current.TotalCycles

    [<GlobalSetup>]
    member _.Setup() =
        rom <- SyntheticRom.build ()
        freshSession <- createSession rom
        frameSession <- createSession rom

    /// Per-step allocation and throughput. A fresh session each iteration keeps the
    /// workload deterministic; OperationsPerInvoke normalises results to a single step.
    [<IterationSetup(Target = "Step")>]
    member _.ResetStep() = freshSession <- createSession rom

    [<Benchmark(OperationsPerInvoke = StepsPerInvoke, Description = "Emulator.step")>]
    member _.Step() = runSteps freshSession

    [<IterationSetup(Target = "NopStep")>]
    member _.ResetNopStep() =
        nopSession <- SyntheticRom.buildNop () |> createSession

    [<Benchmark(OperationsPerInvoke = StepsPerInvoke, Description = "CPU NOP-heavy")>]
    member _.NopStep() = runSteps nopSession

    [<IterationSetup(Target = "MemoryWriteStep")>]
    member _.ResetMemoryWriteStep() =
        memoryWriteSession <- SyntheticRom.buildMemoryWrite () |> createSession

    [<Benchmark(OperationsPerInvoke = StepsPerInvoke, Description = "CPU WRAM-write-heavy")>]
    member _.MemoryWriteStep() = runSteps memoryWriteSession

    [<IterationSetup(Target = "CallStep")>]
    member _.ResetCallStep() =
        callSession <- SyntheticRom.buildCall () |> createSession

    [<Benchmark(OperationsPerInvoke = StepsPerInvoke, Description = "CPU CALL/RET-heavy")>]
    member _.CallStep() = runSteps callSession

    [<IterationSetup(Target = "CbMemoryStep")>]
    member _.ResetCbMemoryStep() =
        cbMemorySession <- SyntheticRom.buildCbMemory () |> createSession

    [<Benchmark(OperationsPerInvoke = StepsPerInvoke, Description = "CPU CB-(HL)-heavy")>]
    member _.CbMemoryStep() = runSteps cbMemorySession

    [<IterationSetup(Target = "ApuEnabledStep")>]
    member _.ResetApuEnabledStep() = apuEnabledSession <- createSession rom

    [<Benchmark(OperationsPerInvoke = StepsPerInvoke, Description = "APU enabled")>]
    member _.ApuEnabledStep() = runSteps apuEnabledSession

    [<IterationSetup(Target = "ApuDisabledStep")>]
    member _.ResetApuDisabledStep() =
        let session = createSession rom

        apuDisabledSession <-
            { session with
                Bus = Bus.writeByte 0xFF26us 0x00uy session.Bus }

    [<Benchmark(OperationsPerInvoke = StepsPerInvoke, Description = "APU disabled")>]
    member _.ApuDisabledStep() = runSteps apuDisabledSession

    /// Full-frame throughput including scanline rendering and audio drain, mirroring
    /// the App's per-frame loop. State drift across invocations is intentional and harmless.
    [<Benchmark(Description = "Emulator.runFrame")>]
    member _.RunFrame() =
        let result = Emulator.runFrame MaxStepsPerFrame frameSession
        frameSession <- result.Session
        result.Session.TotalCycles
