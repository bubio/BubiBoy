namespace BubiBoy.RomSmoke

open System
open System.IO
open BubiBoy.Core
open BubiBoy.IO

type Options =
    { RomRoot: string
      Steps: int
      MaxFiles: int option
      IncludeBios: bool
      FailOnLoadError: bool }

type SmokeStatus =
    | LoadError of string
    | Ran of Emulator.RunResult

module Program =
    let private usage =
        """Usage:
  dotnet run --project tools/BubiBoy.RomSmoke/BubiBoy.RomSmoke.fsproj -- <rom-root> [--steps N] [--max N] [--include-bios] [--fail-on-load-error]

Examples:
  dotnet run --project tools/BubiBoy.RomSmoke/BubiBoy.RomSmoke.fsproj -- /Volumes/CrucialX6/roms/GB --steps 2000
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
        |> Seq.sort
        |> fun paths ->
            match options.MaxFiles with
            | Some maxFiles -> paths |> Seq.truncate maxFiles
            | None -> paths
        |> Seq.toArray

    let private describeStopReason reason =
        match reason with
        | Emulator.StepLimitReached -> "STEP_LIMIT"
        | Emulator.Halted -> "HALTED"
        | Emulator.UnsupportedOpcode(opcode, pc) -> $"UNSUPPORTED_OPCODE opcode=0x{opcode:X2} pc=0x{pc:X4}"

    let private runRom steps path =
        match RomFile.load path with
        | Error message -> LoadError message
        | Ok loaded ->
            match Emulator.createSession loaded.Bytes with
            | Error message -> LoadError message
            | Ok session -> Ran(Emulator.run steps session)

    let private printResult root path status =
        let relativePath = Path.GetRelativePath(root, path)

        match status with
        | LoadError message ->
            printfn $"LOAD_ERROR\t{relativePath}\t{message}"
        | Ran result ->
            let registers = result.Session.Cpu.Registers
            printfn
                $"%s{describeStopReason result.StopReason}\t%s{relativePath}\tsteps=%d{result.Session.Steps}\tcycles=%d{result.Session.TotalCycles}\tpc=0x%04X{registers.PC}\tsp=0x%04X{registers.SP}\ta=0x%02X{registers.A}\tf=0x%02X{registers.F}"

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
                    let status = runRom options.Steps path
                    printResult options.RomRoot path status
                    status)

            let loadErrors =
                results
                |> Array.sumBy (function
                    | LoadError _ -> 1
                    | Ran _ -> 0)

            let unsupported =
                results
                |> Array.sumBy (function
                    | Ran { StopReason = Emulator.UnsupportedOpcode _ } -> 1
                    | _ -> 0)

            let stepLimit =
                results
                |> Array.sumBy (function
                    | Ran { StopReason = Emulator.StepLimitReached } -> 1
                    | _ -> 0)

            printfn $"SUMMARY\tfiles={roms.Length}\tloadErrors={loadErrors}\tunsupported={unsupported}\tstepLimit={stepLimit}"

            if options.FailOnLoadError && loadErrors > 0 then 1 else 0
