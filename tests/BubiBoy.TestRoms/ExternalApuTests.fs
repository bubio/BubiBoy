module BubiBoy.TestRoms.ExternalApuTests

open System
open System.IO
open System.Text
open BubiBoy.Core
open Xunit

type ExternalApuResult =
    | Passed of output: string * steps: int
    | Failed of output: string * steps: int
    | StepLimitReached of output: string * steps: int
    | UnsupportedOpcode of output: string * opcode: byte * pc: uint16 * steps: int

let private configuredRoms () =
    match Environment.GetEnvironmentVariable("BUBIBOY_APU_TEST_ROMS") with
    | null
    | "" -> [||]
    | value ->
        value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> Array.distinct

let private captureSerialOutput (session: Emulator.Session) (output: StringBuilder) =
    let control = Bus.readByte 0xFF02us session.Bus

    if control &&& 0x80uy <> 0uy then
        let value = Bus.readByte 0xFF01us session.Bus
        output.Append(char value) |> ignore

        { session with
            Bus = Bus.writeByte 0xFF02us (control &&& 0x7Fuy) session.Bus }
    else
        session

let private isBinaryPass (output: string) =
    output.Length >= 6
    && output[output.Length - 6] = char 0x03
    && output[output.Length - 5] = char 0x05
    && output[output.Length - 4] = char 0x08
    && output[output.Length - 3] = char 0x0D
    && output[output.Length - 2] = char 0x15
    && output[output.Length - 1] = char 0x22

let private isBinaryFailure (output: string) =
    output.Length >= 6 && output.Substring(output.Length - 6) = "BBBBBB"

let private isRegisterPass (registers: Cpu.Registers) =
    registers.B = 0x03uy
    && registers.C = 0x05uy
    && registers.D = 0x08uy
    && registers.E = 0x0Duy
    && registers.H = 0x15uy
    && registers.L = 0x22uy

let private isRegisterFailure (registers: Cpu.Registers) =
    registers.B = 0x42uy
    && registers.C = 0x42uy
    && registers.D = 0x42uy
    && registers.E = 0x42uy
    && registers.H = 0x42uy
    && registers.L = 0x42uy

let private runApuRom maxSteps path =
    let bytes = File.ReadAllBytes path

    match Emulator.createSession bytes with
    | Error message -> failwith message
    | Ok initial ->
        let output = StringBuilder()
        let mutable session = initial
        let mutable result = None

        while result.IsNone && session.Steps < maxSteps do
            session <- captureSerialOutput session output
            let text = output.ToString()
            let registers = session.Cpu.Registers
            let opcode = Bus.readByte registers.PC session.Bus

            if opcode = 0x40uy && isRegisterPass registers then
                result <- Some(Passed(text, session.Steps))
            elif opcode = 0x40uy && isRegisterFailure registers then
                result <- Some(Failed(text, session.Steps))
            elif text.Contains("Passed", StringComparison.OrdinalIgnoreCase) || isBinaryPass text then
                result <- Some(Passed(text, session.Steps))
            elif
                text.Contains("Failed", StringComparison.OrdinalIgnoreCase)
                || isBinaryFailure text
            then
                result <- Some(Failed(text, session.Steps))
            else
                try
                    session <- Emulator.step session
                with Cpu.UnsupportedOpcode(opcode, pc) ->
                    result <- Some(UnsupportedOpcode(text, opcode, pc, session.Steps))

        let finalOutput = output.ToString()
        defaultArg result (StepLimitReached(finalOutput, session.Steps))

[<Fact>]
let ``external APU test ROMs report pass over serial when configured`` () =
    let roms = configuredRoms ()

    for path in roms do
        if not (File.Exists path) then
            Assert.Fail($"Configured APU test ROM does not exist: {path}")

        match runApuRom 20_000_000 path with
        | Passed _ -> ()
        | Failed(output, steps) ->
            Assert.Fail($"{Path.GetFileName path} reported failure after {steps} steps: {output}")
        | StepLimitReached(output, steps) ->
            Assert.Fail(
                $"{Path.GetFileName path} did not report pass/fail within {steps} steps. Serial output: {output}"
            )
        | UnsupportedOpcode(output, opcode, pc, steps) ->
            Assert.Fail(
                $"{Path.GetFileName path} hit unsupported opcode 0x{opcode:X2} at PC 0x{pc:X4} after {steps} steps. Serial output: {output}"
            )
