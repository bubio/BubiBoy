module BubiBoy.TestRoms.TestRomRunnerTests

open BubiBoy.Core
open Xunit

let private makeRom (program: byte[]) =
    let rom: byte[] = Array.zeroCreate 0x8000
    Array.blit program 0 rom 0x0100 program.Length
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy
    rom

let private registerResultProgram (value: byte[]) =
    [| 0x06uy
       value[0]
       0x0Euy
       value[1]
       0x16uy
       value[2]
       0x1Euy
       value[3]
       0x26uy
       value[4]
       0x2Euy
       value[5]
       0x40uy |]

let private serialResultProgram (values: byte[]) =
    let writes =
        values
        |> Array.collect (fun value -> [| 0x3Euy; value; 0xE0uy; 0x01uy; 0x3Euy; 0x81uy; 0xE0uy; 0x02uy |])

    Array.append writes [| 0x18uy; 0xFEuy |]

let private options maxSteps =
    { TestRomRunner.defaultOptions with
        MaxSteps = maxSteps
        TraceLength = 4 }

[<Fact>]
let ``runner detects register pass protocol`` () =
    let program =
        registerResultProgram [| 0x03uy; 0x05uy; 0x08uy; 0x0Duy; 0x15uy; 0x22uy |]

    match TestRomRunner.runBytes (options 100) (makeRom program) with
    | TestRomRunner.Passed _ -> ()
    | result -> Assert.Fail(TestRomRunner.describe "register pass" result)

[<Fact>]
let ``runner detects register failure protocol`` () =
    let program = registerResultProgram (Array.create 6 0x42uy)

    match TestRomRunner.runBytes (options 100) (makeRom program) with
    | TestRomRunner.Failed _ -> ()
    | result -> Assert.Fail(TestRomRunner.describe "register failure" result)

[<Fact>]
let ``runner detects binary serial pass protocol`` () =
    let program =
        serialResultProgram [| 0x03uy; 0x05uy; 0x08uy; 0x0Duy; 0x15uy; 0x22uy |]

    match TestRomRunner.runBytes (options 100) (makeRom program) with
    | TestRomRunner.Passed(output, _) ->
        Assert.Contains(char 0x03 |> string, output)
        Assert.Contains(char 0x22 |> string, output)
    | result -> Assert.Fail(TestRomRunner.describe "serial pass" result)

[<Fact>]
let ``runner reports step limit with recent trace`` () =
    let program = [| 0x00uy; 0x18uy; 0xFDuy |]

    match TestRomRunner.runBytes (options 8) (makeRom program) with
    | TestRomRunner.StepLimitReached(output, session) ->
        Assert.Equal(8, session.Steps)
        Assert.Contains("Trace:", output)
        Assert.Contains("pc=0x0100", output)
    | result -> Assert.Fail(TestRomRunner.describe "step limit" result)

[<Fact>]
let ``runner reports unsupported opcode with registers and trace`` () =
    match TestRomRunner.runBytes (options 8) (makeRom [| 0xD3uy |]) with
    | TestRomRunner.UnsupportedOpcode(output, opcode, pc, _) ->
        Assert.Equal(0xD3uy, opcode)
        Assert.Equal(0x0100us, pc)
        Assert.Contains("opcode=0xD3", output)
        Assert.Contains("sp=0xFFFE", output)
    | result -> Assert.Fail(TestRomRunner.describe "unsupported opcode" result)
