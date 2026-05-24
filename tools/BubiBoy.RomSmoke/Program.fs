namespace BubiBoy.RomSmoke

open System
open System.Collections.Generic
open System.IO
open BubiBoy.Core
open BubiBoy.IO

type Options =
    { RomRoot: string
      Steps: int
      MaxFiles: int option
      TraceTail: int option
      NameContains: string option
      StopOnBadSp: bool
      StopOnBadPc: bool
      IncludeBios: bool
      FailOnLoadError: bool }

type SmokeStatus =
    | LoadError of string
    | Ran of Emulator.RunResult * string list
    | BadStackPointer of Emulator.Session * string list
    | BadProgramCounter of Emulator.Session * string list

type TraceRun =
    | Completed of Emulator.RunResult
    | BadStack of Emulator.Session
    | BadPc of Emulator.Session

module Program =
    let private usage =
        """Usage:
  dotnet run --project tools/BubiBoy.RomSmoke/BubiBoy.RomSmoke.fsproj -- <rom-root> [--steps N] [--max N] [--name TEXT] [--trace-tail N] [--stop-on-bad-sp] [--stop-on-bad-pc] [--include-bios] [--fail-on-load-error]

Examples:
  dotnet run --project tools/BubiBoy.RomSmoke/BubiBoy.RomSmoke.fsproj -- /Volumes/CrucialX6/roms/GB --steps 2000
  dotnet run --project tools/BubiBoy.RomSmoke/BubiBoy.RomSmoke.fsproj -- /Volumes/CrucialX6/roms/GB --steps 50000000 --name "Street Fighter" --trace-tail 32
"""

    let private tryParseInt (value: string) =
        match Int32.TryParse value with
        | true, parsed when parsed >= 0 -> Some parsed
        | _ -> None

    let private parseArgs (args: string[]) =
        let rec loop index options =
            if index >= args.Length then
                Ok options
            else
                match args[index] with
                | "--steps" when index + 1 < args.Length ->
                    match tryParseInt args[index + 1] with
                    | Some steps -> loop (index + 2) { options with Steps = steps }
                    | None -> Error $"Invalid --steps value: {args[index + 1]}"
                | "--max" when index + 1 < args.Length ->
                    match tryParseInt args[index + 1] with
                    | Some maxFiles -> loop (index + 2) { options with MaxFiles = Some maxFiles }
                    | None -> Error $"Invalid --max value: {args[index + 1]}"
                | "--trace-tail" when index + 1 < args.Length ->
                    match tryParseInt args[index + 1] with
                    | Some traceTail -> loop (index + 2) { options with TraceTail = Some traceTail }
                    | None -> Error $"Invalid --trace-tail value: {args[index + 1]}"
                | "--name" when index + 1 < args.Length ->
                    loop (index + 2) { options with NameContains = Some args[index + 1] }
                | "--stop-on-bad-sp" ->
                    loop (index + 1) { options with StopOnBadSp = true }
                | "--stop-on-bad-pc" ->
                    loop (index + 1) { options with StopOnBadPc = true }
                | "--include-bios" ->
                    loop (index + 1) { options with IncludeBios = true }
                | "--fail-on-load-error" ->
                    loop (index + 1) { options with FailOnLoadError = true }
                | flag when flag.StartsWith("--", StringComparison.Ordinal) ->
                    Error $"Unknown option: {flag}"
                | path when String.IsNullOrWhiteSpace options.RomRoot ->
                    loop (index + 1) { options with RomRoot = path }
                | extra ->
                    Error $"Unexpected argument: {extra}"

        loop
            0
            { RomRoot = ""
              Steps = 2000
              MaxFiles = None
              TraceTail = None
              NameContains = None
              StopOnBadSp = false
              StopOnBadPc = false
              IncludeBios = false
              FailOnLoadError = false }

    let private isRomPath (path: string) =
        let extension = Path.GetExtension(path)
        extension.Equals(".gb", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".gbc", StringComparison.OrdinalIgnoreCase)

    let private isBiosPath (path: string) =
        Path.GetFileName(path).Contains("[BIOS]", StringComparison.OrdinalIgnoreCase)

    let private findRoms options =
        Directory.EnumerateFiles(options.RomRoot, "*", SearchOption.AllDirectories)
        |> Seq.filter isRomPath
        |> Seq.filter (fun path -> options.IncludeBios || not (isBiosPath path))
        |> Seq.filter (fun path ->
            match options.NameContains with
            | None -> true
            | Some text -> Path.GetFileName(path).Contains(text, StringComparison.OrdinalIgnoreCase))
        |> Seq.sort
        |> fun paths ->
            match options.MaxFiles with
            | Some maxFiles -> paths |> Seq.truncate maxFiles
            | None -> paths
        |> Seq.toArray

    let private describeStopReason reason =
        match reason with
        | Emulator.StepLimitReached -> "STEP_LIMIT"
        | Emulator.FrameCompleted -> "FRAME_COMPLETED"
        | Emulator.Halted -> "HALTED"
        | Emulator.UnsupportedOpcode(opcode, pc) -> $"UNSUPPORTED_OPCODE opcode=0x{opcode:X2} pc=0x{pc:X4}"

    let private describeCartridgeBank (cartridge: CartridgeMemory.CartridgeImage) =
        match cartridge.Mbc1 with
        | None -> ""
        | Some state ->
            let upperRaw = (state.BankHigh2 <<< 5) ||| state.RomBankLow5
            let upperBank = if upperRaw &&& 0x1F = 0 then upperRaw ||| 1 else upperRaw
            let lowerBank =
                match state.BankingMode with
                | CartridgeMemory.RomBanking -> 0
                | CartridgeMemory.RamBanking -> state.BankHigh2 <<< 5

            $"\tmbc1Low=%d{state.RomBankLow5}\tmbc1High=%d{state.BankHigh2}\tmbc1Mode=%A{state.BankingMode}\trom0Bank=%d{lowerBank % cartridge.RomBanks}\tromXBank=%d{upperBank % cartridge.RomBanks}"

    let private formatTraceEntry step cycles (session: Emulator.Session) =
        let registers = session.Cpu.Registers
        let opcode = Bus.readByte registers.PC session.Bus
        let cartridgeBank = describeCartridgeBank session.Bus.Cartridge

        $"TRACE\tstep=%d{step}\tcycles=%d{cycles}\tpc=0x%04X{registers.PC}\topcode=0x%02X{opcode}\tsp=0x%04X{registers.SP}\ta=0x%02X{registers.A}\tf=0x%02X{registers.F}\tb=0x%02X{registers.B}\tc=0x%02X{registers.C}\td=0x%02X{registers.D}\te=0x%02X{registers.E}\th=0x%02X{registers.H}\tl=0x%02X{registers.L}%s{cartridgeBank}"

    let private isSuspiciousProgramCounter pc =
        (pc >= 0x8000us && pc <= 0x9FFFus)
        || (pc >= 0xFE00us && pc <= 0xFEFFus)
        || pc = 0xFFFFus

    let private runWithTrace (traceTail: int) (stopOnBadSp: bool) (stopOnBadPc: bool) (maxSteps: int) (session: Emulator.Session) : TraceRun * string list =
        let trace = Queue<Emulator.Session>()
        let mutable remaining = maxSteps
        let mutable current = session
        let mutable stopReason: Emulator.StopReason option = None
        let mutable badStack = None
        let mutable badProgramCounter = None

        while stopReason.IsNone && badStack.IsNone && badProgramCounter.IsNone do
            if remaining <= 0 then
                stopReason <- Some Emulator.StepLimitReached
            else
                if trace.Count = traceTail then
                    trace.Dequeue() |> ignore

                trace.Enqueue(current)

                try
                    current <- Emulator.step current
                    remaining <- remaining - 1

                    if stopOnBadSp && current.Cpu.Registers.SP < 0xC000us then
                        badStack <- Some current

                    if stopOnBadPc && isSuspiciousProgramCounter current.Cpu.Registers.PC then
                        badProgramCounter <- Some current
                with
                | Cpu.UnsupportedOpcode(opcode, pc) ->
                    stopReason <- Some(Emulator.UnsupportedOpcode(opcode, pc))

        let traceLines =
            trace
            |> Seq.map (fun entry -> formatTraceEntry entry.Steps entry.TotalCycles entry)
            |> Seq.toList

        match badStack with
        | Some session -> BadStack session, traceLines
        | None ->
            match badProgramCounter with
            | Some session -> BadPc session, traceLines
            | None ->
                Completed
                    { Session = current
                      StopReason = stopReason.Value },
                traceLines

    let private runRom steps traceTail stopOnBadSp stopOnBadPc path =
        match RomFile.load path with
        | Error message -> LoadError message
        | Ok loaded ->
            match Emulator.createSession loaded.Bytes with
            | Error message -> LoadError message
            | Ok session ->
                match traceTail with
                | Some tail when tail > 0 ->
                    match runWithTrace tail stopOnBadSp stopOnBadPc steps session with
                    | Completed result, trace -> Ran(result, trace)
                    | BadStack session, trace -> BadStackPointer(session, trace)
                    | BadPc session, trace -> BadProgramCounter(session, trace)
                | _ -> Ran(Emulator.run steps session, [])

    let private printResult root path status =
        let relativePath = Path.GetRelativePath(root, path)

        match status with
        | LoadError message ->
            printfn $"LOAD_ERROR\t{relativePath}\t{message}"
        | Ran(result, trace) ->
            let registers = result.Session.Cpu.Registers
            let cartridgeBank = describeCartridgeBank result.Session.Bus.Cartridge
            printfn
                $"%s{describeStopReason result.StopReason}\t%s{relativePath}\tsteps=%d{result.Session.Steps}\tcycles=%d{result.Session.TotalCycles}\tpc=0x%04X{registers.PC}\tsp=0x%04X{registers.SP}\ta=0x%02X{registers.A}\tf=0x%02X{registers.F}%s{cartridgeBank}"

            match result.StopReason, trace with
            | Emulator.UnsupportedOpcode _, _ :: _ ->
                trace |> List.iter (printfn "%s")
            | _ -> ()
        | BadStackPointer(session, trace) ->
            let registers = session.Cpu.Registers
            let cartridgeBank = describeCartridgeBank session.Bus.Cartridge
            printfn
                $"BAD_STACK_POINTER\t%s{relativePath}\tsteps=%d{session.Steps}\tcycles=%d{session.TotalCycles}\tpc=0x%04X{registers.PC}\tsp=0x%04X{registers.SP}\ta=0x%02X{registers.A}\tf=0x%02X{registers.F}%s{cartridgeBank}"

            trace |> List.iter (printfn "%s")
        | BadProgramCounter(session, trace) ->
            let registers = session.Cpu.Registers
            let cartridgeBank = describeCartridgeBank session.Bus.Cartridge
            printfn
                $"BAD_PROGRAM_COUNTER\t%s{relativePath}\tsteps=%d{session.Steps}\tcycles=%d{session.TotalCycles}\tpc=0x%04X{registers.PC}\tsp=0x%04X{registers.SP}\ta=0x%02X{registers.A}\tf=0x%02X{registers.F}%s{cartridgeBank}"

            trace |> List.iter (printfn "%s")

    [<EntryPoint>]
    let main args =
        match parseArgs args with
        | Error message ->
            eprintfn "%s" message
            eprintf "%s" usage
            2
        | Ok options when String.IsNullOrWhiteSpace options.RomRoot ->
            eprintf "%s" usage
            2
        | Ok options when not (Directory.Exists options.RomRoot) ->
            eprintfn $"ROM root does not exist: {options.RomRoot}"
            2
        | Ok options ->
            let roms = findRoms options
            printfn $"BubiBoy ROM smoke: root={options.RomRoot} steps={options.Steps} files={roms.Length}"

            let results =
                roms
                |> Array.map (fun path ->
                    let status = runRom options.Steps options.TraceTail options.StopOnBadSp options.StopOnBadPc path
                    printResult options.RomRoot path status
                    status)

            let loadErrors =
                results
                |> Array.sumBy (function
                    | LoadError _ -> 1
                    | Ran _ -> 0
                    | BadStackPointer _ -> 0
                    | BadProgramCounter _ -> 0)

            let unsupported =
                results
                |> Array.sumBy (function
                    | Ran({ StopReason = Emulator.UnsupportedOpcode _ }, _) -> 1
                    | _ -> 0)

            let stepLimit =
                results
                |> Array.sumBy (function
                    | Ran({ StopReason = Emulator.StepLimitReached }, _) -> 1
                    | _ -> 0)

            let badStack =
                results
                |> Array.sumBy (function
                    | BadStackPointer _ -> 1
                    | _ -> 0)

            let badProgramCounter =
                results
                |> Array.sumBy (function
                    | BadProgramCounter _ -> 1
                    | _ -> 0)

            printfn $"SUMMARY\tfiles={roms.Length}\tloadErrors={loadErrors}\tunsupported={unsupported}\tstepLimit={stepLimit}\tbadStack={badStack}\tbadPc={badProgramCounter}"

            if options.FailOnLoadError && loadErrors > 0 then 1 else 0
