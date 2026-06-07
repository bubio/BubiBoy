module BubiBoy.TestRoms.ExternalCgbSmokeTests

open System
open System.IO
open BubiBoy.Core
open Xunit

type CgbSmokeResult =
    | Completed of Emulator.Session
    | LoadError of string
    | DmgOnly of title: string
    | UnsupportedOpcode of opcode: byte * pc: uint16 * steps: int
    | SuspiciousProgramCounter of pc: uint16 * steps: int

let private configuredRoms () =
    match Environment.GetEnvironmentVariable("BUBIBOY_CGB_SMOKE_ROMS") with
    | null
    | "" -> [||]
    | value ->
        value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
        |> Array.distinct

let private configuredSteps () =
    match Environment.GetEnvironmentVariable("BUBIBOY_CGB_SMOKE_STEPS") with
    | null
    | "" -> 2_000_000
    | value ->
        match Int32.TryParse value with
        | true, steps when steps > 0 -> steps
        | _ -> failwith $"Invalid BUBIBOY_CGB_SMOKE_STEPS value: {value}"

let private titleFromHeader (bytes: byte[]) =
    let endIndex =
        [ 0x0134..0x0143 ]
        |> List.tryFind (fun index -> bytes[index] = 0uy)
        |> Option.defaultValue 0x0144

    Text.Encoding.ASCII.GetString(bytes, 0x0134, endIndex - 0x0134).TrimEnd(char 0)

let private isSuspiciousProgramCounter pc =
    (pc >= 0x8000us && pc <= 0x9FFFus)
    || (pc >= 0xFE00us && pc <= 0xFEFFus)
    || pc = 0xFFFFus

let private runSmoke maxSteps path =
    let bytes = File.ReadAllBytes path
    let title = titleFromHeader bytes

    match Emulator.createSession bytes with
    | Error message -> LoadError message
    | Ok initial when Bus.mode initial.Bus <> Hardware.Cgb -> DmgOnly title
    | Ok initial ->
        let mutable session = initial
        let mutable result = None

        while result.IsNone && session.Steps < maxSteps do
            try
                session <- Emulator.step session

                if isSuspiciousProgramCounter session.Cpu.Registers.PC then
                    result <- Some(SuspiciousProgramCounter(session.Cpu.Registers.PC, session.Steps))
            with Cpu.UnsupportedOpcode(opcode, pc) ->
                result <- Some(UnsupportedOpcode(opcode, pc, session.Steps))

        defaultArg result (Completed session)

[<Fact>]
let ``external CGB smoke ROMs run without early execution failures when configured`` () =
    let roms = configuredRoms ()
    let steps = configuredSteps ()

    for path in roms do
        if not (File.Exists path) then
            Assert.Fail($"Configured CGB smoke ROM does not exist: {path}")

        match runSmoke steps path with
        | Completed session ->
            Assert.Equal(steps, session.Steps)
            Assert.Equal(Hardware.Cgb, Bus.mode session.Bus)
        | LoadError message -> Assert.Fail($"{Path.GetFileName path} failed to load: {message}")
        | DmgOnly title ->
            Assert.Fail($"{Path.GetFileName path} is not a CGB ROM according to its header. Title: {title}")
        | UnsupportedOpcode(opcode, pc, steps) ->
            Assert.Fail(
                $"{Path.GetFileName path} hit unsupported opcode 0x{opcode:X2} at PC 0x{pc:X4} after {steps} steps."
            )
        | SuspiciousProgramCounter(pc, steps) ->
            Assert.Fail($"{Path.GetFileName path} reached suspicious PC 0x{pc:X4} after {steps} steps.")
