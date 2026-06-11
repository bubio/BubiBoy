namespace BubiBoy.App

open System
open System.IO
open BubiBoy.Core
open BubiBoy.IO

module RomWorkflow =
    type SaveRamOutcome =
        { LastSaveStatus: string option
          ToastMessage: string option }

    type ResetOutcome =
        { SessionResult: Result<Emulator.Session, string>
          Session: Emulator.Session option
          ToastMessage: string
          DebugDetails: string }

    type LoadedRomOutcome =
        { Rom: RomFile.LoadedRom
          SessionResult: Result<Emulator.Session, string>
          Session: Emulator.Session option
          ToastMessage: string
          RomDetails: string
          DebugDetails: string }

    type LoadOutcome =
        | EmptyPath
        | Loaded of LoadedRomOutcome
        | LoadFailed of toastMessage: string * romDetails: string * debugDetails: string

    type SaveStateOutcome =
        { ToastMessage: string
          DebugDetails: string }

    type LoadStateOutcome =
        { RestoredSession: Emulator.Session option
          ToastMessage: string
          DebugDetails: string }

    let private romFileName (rom: RomFile.LoadedRom) = Path.GetFileName rom.Path

    let private formatHeaderDetails (header: Cartridge.CartridgeHeader) =
        $"Title: {header.Title}\nCGB: {header.CgbSupport}\nSGB: {header.SgbSupport}\nCartridge: {header.CartridgeKind} (0x{header.CartridgeTypeCode:X2})\nROM: {HeaderDisplay.formatRomSize header.RomSizeCode} (0x{header.RomSizeCode:X2})\nRAM: {HeaderDisplay.formatRamSize header.RamSizeCode} (0x{header.RamSizeCode:X2})"

    let saveRam (loadedRom: RomFile.LoadedRom option) (session: Emulator.Session option) =
        match loadedRom, session with
        | Some rom, Some session ->
            match SaveRam.saveForRom rom.Path (Bus.cartridge session.Bus) with
            | Ok true ->
                { LastSaveStatus = Some "Save RAM written."
                  ToastMessage = Some "Save RAM written." }
            | Ok false ->
                { LastSaveStatus = None
                  ToastMessage = None }
            | Error message ->
                let displayMessage = $"Save RAM error: {message}"

                { LastSaveStatus = Some displayMessage
                  ToastMessage = Some displayMessage }
        | _ ->
            { LastSaveStatus = None
              ToastMessage = None }

    let reset bootRomSelection (rom: RomFile.LoadedRom) =
        let creationResult = RomSession.createForRom bootRomSelection rom
        let sessionResult = creationResult |> Result.map (fun creation -> creation.Session)
        let session = sessionResult |> Result.toOption

        { SessionResult = sessionResult
          Session = session
          ToastMessage =
            match creationResult with
            | Ok creation ->
                match creation.BootRomWarning with
                | Some warning -> $"Reset {romFileName rom}. {warning}"
                | None -> $"Reset {romFileName rom}"
            | Error message -> $"Could not reset ROM: {UserMessage.formatRomStartError message}"
          DebugDetails =
            match creationResult with
            | Ok creation -> $"Reset complete.\n{creation.BootRomStatus}"
            | Error message -> UserMessage.formatRomStartError message }

    let load bootRomSelection (path: string) (lastSaveStatus: string option) =
        if String.IsNullOrWhiteSpace path then
            EmptyPath
        else
            match RomFile.load path with
            | Ok loaded ->
                let header = loaded.Header
                let creationResult = RomSession.createForRom bootRomSelection loaded
                let sessionResult = creationResult |> Result.map (fun creation -> creation.Session)
                let session = sessionResult |> Result.toOption

                Loaded
                    { Rom = loaded
                      SessionResult = sessionResult
                      Session = session
                      ToastMessage =
                        match creationResult, lastSaveStatus with
                        | Ok creation, saveStatus ->
                            let messages =
                                [ yield $"Loaded {romFileName loaded}"
                                  yield! creation.BootRomWarning |> Option.toList
                                  yield! saveStatus |> Option.toList ]

                            String.concat "  " messages
                        | Error message, _ -> $"Could not start ROM: {UserMessage.formatRomStartError message}"
                      RomDetails = formatHeaderDetails header
                      DebugDetails =
                        match creationResult with
                        | Ok creation -> $"Ready to run frames.\n{creation.BootRomStatus}"
                        | Error message -> UserMessage.formatRomStartError message }
            | Error message ->
                let displayMessage = UserMessage.formatRomLoadError message

                LoadFailed(
                    $"Could not load ROM: {displayMessage}",
                    displayMessage,
                    "Frame stepping is available after loading a ROM."
                )

    let saveState (loadedRom: RomFile.LoadedRom option) (session: Emulator.Session option) =
        match loadedRom, session with
        | Some rom, Some session ->
            match SaveStateFile.saveForRom rom.Path session with
            | Ok() ->
                { ToastMessage = "Save state written."
                  DebugDetails = "Save state written." }
            | Error message ->
                let displayMessage = UserMessage.formatSaveStateError message

                { ToastMessage = $"Save state error: {displayMessage}"
                  DebugDetails = displayMessage }
        | _ ->
            { ToastMessage = "Load a ROM before saving state."
              DebugDetails = "" }

    let loadState (loadedRom: RomFile.LoadedRom option) (session: Emulator.Session option) =
        match loadedRom, session with
        | Some rom, Some session ->
            match SaveStateFile.loadForRom rom.Path session with
            | Ok restored ->
                { RestoredSession = Some restored
                  ToastMessage = "Save state loaded."
                  DebugDetails = "Save state loaded." }
            | Error message ->
                let displayMessage = UserMessage.formatSaveStateError message

                { RestoredSession = None
                  ToastMessage = $"Save state error: {displayMessage}"
                  DebugDetails = displayMessage }
        | _ ->
            { RestoredSession = None
              ToastMessage = "Load a ROM before loading state."
              DebugDetails = "" }
