module BubiBoy.TestRoms.MooneyeAcceptanceTests

open System.IO
open Xunit

let private romPath relativePath =
    Path.Combine(System.AppContext.BaseDirectory, "roms", relativePath)

[<Theory>]
[<InlineData("mooneye/acceptance/instr/daa.gb")>]
[<InlineData("mooneye/acceptance/bits/reg_f.gb")>]
[<InlineData("mooneye/acceptance/ei_sequence.gb")>]
[<InlineData("mooneye/acceptance/ei_timing.gb")>]
[<InlineData("mooneye/acceptance/rapid_di_ei.gb")>]
[<InlineData("mooneye/acceptance/halt_ime0_ei.gb")>]
[<InlineData("mooneye/acceptance/halt_ime0_nointr_timing.gb")>]
[<InlineData("mooneye/acceptance/halt_ime1_timing.gb")>]
[<InlineData("mooneye/acceptance/intr_timing.gb")>]
[<InlineData("mooneye/acceptance/div_timing.gb")>]
[<InlineData("mooneye/acceptance/timer/tim00.gb")>]
[<InlineData("mooneye/acceptance/timer/tim01.gb")>]
[<InlineData("mooneye/acceptance/timer/tim10.gb")>]
[<InlineData("mooneye/acceptance/timer/tim11.gb")>]
[<InlineData("mooneye/acceptance/timer/div_write.gb")>]
[<InlineData("mooneye/acceptance/timer/tima_reload.gb")>]
let ``Mooneye acceptance ROM reports pass`` relativePath =
    let result =
        File.ReadAllBytes(romPath relativePath)
        |> TestRomRunner.runBytes TestRomRunner.defaultOptions

    match result with
    | TestRomRunner.Passed _ -> ()
    | _ -> Assert.Fail(TestRomRunner.describe relativePath result)
