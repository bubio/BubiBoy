module BubiBoy.TestRoms.TestRomRunner

open System
open System.Collections.Generic
open System.Text
open BubiBoy.Core

type RunOptions =
    { MaxSteps: int
      TraceLength: int
      CaptureSerial: bool }

type RunResult =
    | Passed of output: string * session: Emulator.Session
    | Failed of output: string * session: Emulator.Session
    | StepLimitReached of output: string * session: Emulator.Session
    | UnsupportedOpcode of output: string * opcode: byte * pc: uint16 * session: Emulator.Session
    | LoadError of message: string

let defaultOptions =
    { MaxSteps = 5_000_000
      TraceLength = 32
      CaptureSerial = true }

let private isRegisterResult (value: byte[]) (registers: Cpu.Registers) =
    registers.B = value[0]
    && registers.C = value[1]
    && registers.D = value[2]
    && registers.E = value[3]
    && registers.H = value[4]
    && registers.L = value[5]

let private passRegisters = [| 0x03uy; 0x05uy; 0x08uy; 0x0Duy; 0x15uy; 0x22uy |]
let private failRegisters = Array.create 6 0x42uy

let private isBinarySuffix (suffix: byte[]) (output: StringBuilder) =
    if output.Length < suffix.Length then
        false
    else
        let start = output.Length - suffix.Length

        suffix
        |> Array.mapi (fun index value -> byte output[start + index] = value)
        |> Array.forall id

let private containsText (text: string) (output: StringBuilder) =
    output.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)

let private captureSerialOutput (session: Emulator.Session) (output: StringBuilder) =
    let control = Bus.readByte 0xFF02us session.Bus

    if control &&& 0x80uy <> 0uy then
        output.Append(char (Bus.readByte 0xFF01us session.Bus)) |> ignore

        { session with
            Bus = Bus.writeByte 0xFF02us (control &&& 0x7Fuy) session.Bus }
    else
        session

let private formatTraceEntry (session: Emulator.Session) =
    let registers = session.Cpu.Registers
    let opcode = Bus.readByte registers.PC session.Bus

    $"step={session.Steps} cycles={session.TotalCycles} pc=0x{registers.PC:X4} opcode=0x{opcode:X2} sp=0x{registers.SP:X4} a=0x{registers.A:X2} f=0x{registers.F:X2} b=0x{registers.B:X2} c=0x{registers.C:X2} d=0x{registers.D:X2} e=0x{registers.E:X2} h=0x{registers.H:X2} l=0x{registers.L:X2}"

let private appendTrace (trace: Queue<string>) maxLength session =
    if maxLength > 0 then
        if trace.Count = maxLength then
            trace.Dequeue() |> ignore

        trace.Enqueue(formatTraceEntry session)

let private outputWithTrace (output: StringBuilder) (trace: Queue<string>) =
    let text = output.ToString()

    if trace.Count = 0 then
        text
    else
        let traceText = String.Join(Environment.NewLine, trace)

        if String.IsNullOrEmpty text then
            $"Trace:{Environment.NewLine}{traceText}"
        else
            $"{text}{Environment.NewLine}Trace:{Environment.NewLine}{traceText}"

let runBytes options (rom: byte[]) =
    match Emulator.createSession rom with
    | Error message -> LoadError message
    | Ok initial ->
        let output = StringBuilder()
        let trace = Queue<string>()
        let mutable session = initial
        let mutable result = None

        while result.IsNone && session.Steps < options.MaxSteps do
            if options.CaptureSerial then
                session <- captureSerialOutput session output

            appendTrace trace options.TraceLength session

            let registers = session.Cpu.Registers
            let opcode = Bus.readByte registers.PC session.Bus

            if opcode = 0x40uy && isRegisterResult passRegisters registers then
                result <- Some(Passed(outputWithTrace output trace, session))
            elif opcode = 0x40uy && isRegisterResult failRegisters registers then
                result <- Some(Failed(outputWithTrace output trace, session))
            elif containsText "Passed" output || isBinarySuffix passRegisters output then
                result <- Some(Passed(outputWithTrace output trace, session))
            elif containsText "Failed" output || isBinarySuffix failRegisters output then
                result <- Some(Failed(outputWithTrace output trace, session))
            else
                try
                    session <- Emulator.step session
                with Cpu.UnsupportedOpcode(opcode, pc) ->
                    result <- Some(UnsupportedOpcode(outputWithTrace output trace, opcode, pc, session))

        defaultArg result (StepLimitReached(outputWithTrace output trace, session))

let describe name result =
    match result with
    | Passed(output, session) -> $"{name} passed after {session.Steps} steps. Output: {output}"
    | Failed(output, session) -> $"{name} reported failure after {session.Steps} steps. Output: {output}"
    | StepLimitReached(output, session) ->
        $"{name} did not report pass/fail within {session.Steps} steps. Output: {output}"
    | UnsupportedOpcode(output, opcode, pc, session) ->
        $"{name} hit unsupported opcode 0x{opcode:X2} at PC 0x{pc:X4} after {session.Steps} steps. Output: {output}"
    | LoadError message -> $"{name} failed to load: {message}"
