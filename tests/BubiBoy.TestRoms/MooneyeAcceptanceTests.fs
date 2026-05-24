module BubiBoy.TestRoms.MooneyeAcceptanceTests

open System
open System.IO
open BubiBoy.Core
open Xunit

type MooneyeResult =
    | Passed of steps: int
    | Failed of steps: int
    | StepLimitReached of steps: int
    | UnsupportedOpcode of opcode: byte * pc: uint16 * steps: int

let private romPath relativePath =
    Path.Combine(AppContext.BaseDirectory, "roms", relativePath)

let private isMooneyeBreakpoint (registers: Cpu.Registers) =
    registers.B = 0x03uy
    && registers.C = 0x05uy
    && registers.D = 0x08uy
    && registers.E = 0x0Duy
    && registers.H = 0x15uy
    && registers.L = 0x22uy

let private isMooneyeFailure (registers: Cpu.Registers) =
    registers.B = 0x42uy
    && registers.C = 0x42uy
    && registers.D = 0x42uy
    && registers.E = 0x42uy
    && registers.H = 0x42uy
    && registers.L = 0x42uy

let private runMooneye maxSteps relativePath =
    let bytes = File.ReadAllBytes(romPath relativePath)

    match Emulator.createSession bytes with
    | Error message -> failwith message
    | Ok initial ->
        let mutable session = initial
        let mutable result = None

        while result.IsNone && session.Steps < maxSteps do
            let registers = session.Cpu.Registers
            let opcode = Bus.readByte registers.PC session.Bus

            if opcode = 0x40uy && isMooneyeBreakpoint registers then
                result <- Some(Passed session.Steps)
            elif opcode = 0x40uy && isMooneyeFailure registers then
                result <- Some(Failed session.Steps)
            else
                try
                    session <- Emulator.step session
                with
                | Cpu.UnsupportedOpcode(opcode, pc) ->
                    result <- Some(UnsupportedOpcode(opcode, pc, session.Steps))

        defaultArg result (StepLimitReached session.Steps)

[<Theory>]
[<InlineData("mooneye/acceptance/instr/daa.gb")>]
[<InlineData("mooneye/acceptance/bits/reg_f.gb")>]
let ``Mooneye acceptance ROM reports pass`` relativePath =
    match runMooneye 5_000_000 relativePath with
    | Passed _ -> ()
    | Failed steps -> Assert.Fail($"{relativePath} reported failure after {steps} steps.")
    | StepLimitReached steps -> Assert.Fail($"{relativePath} did not report pass/fail within {steps} steps.")
    | UnsupportedOpcode(opcode, pc, steps) ->
        Assert.Fail($"{relativePath} hit unsupported opcode 0x{opcode:X2} at PC 0x{pc:X4} after {steps} steps.")
