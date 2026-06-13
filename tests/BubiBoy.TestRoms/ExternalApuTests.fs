module BubiBoy.TestRoms.ExternalApuTests

open System
open System.IO
open Xunit

let private configuredRoms () =
    match Environment.GetEnvironmentVariable("BUBIBOY_APU_TEST_ROMS") with
    | null
    | "" -> [||]
    | value ->
        value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> Array.distinct

[<Fact>]
let ``external APU test ROMs report pass over serial when configured`` () =
    let roms = configuredRoms ()

    for path in roms do
        if not (File.Exists path) then
            Assert.Fail($"Configured APU test ROM does not exist: {path}")

        let options =
            { TestRomRunner.defaultOptions with
                MaxSteps = 20_000_000 }

        let result = File.ReadAllBytes path |> TestRomRunner.runBytes options

        match result with
        | TestRomRunner.Passed _ -> ()
        | _ -> Assert.Fail(TestRomRunner.describe (Path.GetFileName path) result)
