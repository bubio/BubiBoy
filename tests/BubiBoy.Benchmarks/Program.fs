namespace BubiBoy.Benchmarks

open BenchmarkDotNet.Running

module Program =
    [<EntryPoint>]
    let main argv =
        BenchmarkSwitcher.FromAssembly(typeof<EmulatorBenchmarks>.Assembly).Run(argv)
        |> ignore

        0
