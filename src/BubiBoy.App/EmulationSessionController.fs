namespace BubiBoy.App

open System
open System.IO
open Avalonia.Controls
open Avalonia.Threading
open BubiBoy.Audio
open BubiBoy.Core
open BubiBoy.IO
open BubiBoy.RetroAchievements

/// Host dependencies used by the emulator session workflow.
type EmulationSessionDependencies =
    { Owner: Window
      ViewModel: MainWindowViewModel
      Runner: EmulationRunner
      AudioOutput: AudioHost.AudioDevice
      PerformanceCounters: RuntimePerformanceCounters
      PresentFrame: uint32[] -> unit
      SettingsStore: AppSettingsStore
      SaveSettings: unit -> unit
      Notifications: AppNotificationCenter
      RefreshMenus: unit -> unit
      RetroAchievements: RaClient option }

/// Owns the loaded ROM, live emulator session, and run-state workflows.
type EmulationSessionController(dependencies: EmulationSessionDependencies) =
    let sessionGate = obj ()
    let mutable loadedRom: RomFile.LoadedRom option = None
    let mutable currentSession: Emulator.Session option = None
    let mutable isRunning = false
    let mutable pendingRaRom: RomFile.LoadedRom option = None
    let mutable pendingRaStart = false

    let getCurrentSession () =
        lock sessionGate (fun () -> currentSession)

    let setCurrentSession session =
        lock sessionGate (fun () -> currentSession <- session)

    let updateSessionState () =
        let hasSession = getCurrentSession().IsSome
        dependencies.ViewModel.UpdateSessionState(hasSession, loadedRom.IsSome)

    let stopRunning () =
        isRunning <- false
        dependencies.ViewModel.IsRunning <- false
        dependencies.Runner.StopLoop()
        dependencies.AudioOutput.Stop()

    let startRunning () =
        isRunning <- true
        dependencies.ViewModel.IsRunning <- true
        dependencies.PerformanceCounters.Reset()
        dependencies.AudioOutput.Start()
        dependencies.Runner.Start(getCurrentSession, Some >> setCurrentSession, stopRunning)

    let raConsoleId (rom: RomFile.LoadedRom) =
        match rom.Header.CgbSupport with
        | Cartridge.DmgOnly -> 4u
        | Cartridge.CgbEnhanced
        | Cartridge.CgbOnly -> 6u

    let beginRaLoad (client: RaClient) rom =
        match client.Snapshot.Status, getCurrentSession () with
        | Ready, Some session ->
            pendingRaRom <- Some rom
            pendingRaStart <- true
            client.LoadGame(raConsoleId rom, rom.Bytes, session)
        | _ -> ()

    let startLoadedRomWithRa rom =
        if not dependencies.SettingsStore.Current.RetroAchievementsEnabled then
            startRunning ()
        else
            match dependencies.RetroAchievements with
            | None ->
                dependencies.Notifications.Show "RetroAchievements is unavailable; starting an offline session."
                startRunning ()
            | Some client ->
                match client.Snapshot.Status with
                | Active
                | OfflineSession _ ->
                    client.UnloadGame()

                    if client.Snapshot.Status = Ready then
                        beginRaLoad client rom
                    else
                        client.SetOffline "RetroAchievements login is unavailable."
                        startRunning ()
                | Ready -> beginRaLoad client rom
                | Authenticating ->
                    pendingRaRom <- Some rom
                    pendingRaStart <- true
                    dependencies.Notifications.Show "Waiting for RetroAchievements login."
                | LoggedOut
                | Disabled ->
                    client.SetOffline "Not logged in to RetroAchievements."
                    dependencies.Notifications.Show "RetroAchievements login is required; starting an offline session."
                    startRunning ()
                | LoadingGame ->
                    pendingRaRom <- Some rom
                    pendingRaStart <- true

    let saveCurrentRam () =
        let outcome = RomWorkflow.saveRam loadedRom (getCurrentSession ())
        dependencies.Notifications.SetLastStatus outcome.LastSaveStatus
        outcome.ToastMessage |> Option.iter dependencies.Notifications.Show

    let resumeAfterStateOperation wasRunning =
        if wasRunning then
            startRunning ()
        else
            dependencies.ViewModel.IsRunning <- false

        dependencies.RefreshMenus()
        dependencies.Owner.Focus() |> ignore

    let authorize operation =
        match RetroAchievementsOperations.evaluate dependencies.RetroAchievements operation with
        | OperationAllowed -> true
        | OperationDenied message ->
            dependencies.Notifications.Show message
            false

    do
        dependencies.RetroAchievements
        |> Option.iter (fun client ->
            client.Changed.Add(fun snapshot ->
                Dispatcher.UIThread.Post(fun () ->
                    if pendingRaStart && snapshot.Generation = client.Snapshot.Generation then
                        match snapshot.Status, pendingRaRom with
                        | Ready, Some rom -> beginRaLoad client rom
                        | Active, _
                        | OfflineSession _, _ ->
                            pendingRaStart <- false
                            pendingRaRom <- None

                            if getCurrentSession().IsSome && not isRunning then
                                startRunning ()
                        | LoggedOut, _
                        | Disabled, _ ->
                            pendingRaStart <- false
                            pendingRaRom <- None
                            client.SetOffline "RetroAchievements login failed."

                            if getCurrentSession().IsSome && not isRunning then
                                startRunning ()
                        | _ -> ())))

    /// Gets whether emulation is currently running.
    member _.IsRunning = isRunning

    /// Stops the active emulation loop and audio output.
    member _.StopRunning() = stopRunning ()

    /// Saves cartridge RAM for the current session when supported.
    member _.SaveCurrentRam() = saveCurrentRam ()

    /// Formats current display and audio performance diagnostics.
    member _.FormatRuntimeDiagnostics() =
        dependencies.PerformanceCounters.FormatDiagnostics(dependencies.AudioOutput.Diagnostics())

    /// Presents an emulated frame and handles non-frame-complete stop reasons.
    member this.UpdateFrame(result: Emulator.FrameResult) =
        dependencies.PresentFrame result.Framebuffer

        dependencies.ViewModel.DebugDetails <-
            if isRunning then
                this.FormatRuntimeDiagnostics()
            else
                $"{DebugDisplay.formatFrameResult result}\n{this.FormatRuntimeDiagnostics()}"

        match result.StopReason with
        | Emulator.FrameCompleted -> ()
        | _ -> stopRunning ()

    /// Loads a ROM path and optionally records it in recent files.
    member _.LoadRomPath(path: string, rememberRecent: bool) =
        if String.IsNullOrWhiteSpace path then
            dependencies.Notifications.Show "Could not open the selected ROM path."
        elif not (authorize ChangeGame) then
            ()
        else
            saveCurrentRam ()
            stopRunning ()
            pendingRaRom <- None
            pendingRaStart <- false

            dependencies.RetroAchievements
            |> Option.iter (fun client ->
                match client.Snapshot.Status with
                | Active
                | LoadingGame
                | OfflineSession _ -> client.UnloadGame()
                | Disabled
                | LoggedOut
                | Authenticating
                | Ready -> ())

            match
                RomWorkflow.load
                    dependencies.SettingsStore.Current.BootRomSelection
                    path
                    dependencies.Notifications.LastStatus
            with
            | RomWorkflow.EmptyPath -> dependencies.Notifications.Show "Could not open the selected ROM path."
            | RomWorkflow.Loaded outcome ->
                loadedRom <- Some outcome.Rom
                let headerTitle = outcome.Rom.Header.Title

                dependencies.ViewModel.RomDisplayName <-
                    if String.IsNullOrWhiteSpace headerTitle then
                        Some(Path.GetFileNameWithoutExtension outcome.Rom.Path)
                    else
                        Some headerTitle

                setCurrentSession outcome.Session
                dependencies.Runner.ClearFrames()
                updateSessionState ()
                dependencies.PresentFrame(Video.blankFrame ())

                if rememberRecent then
                    dependencies.SettingsStore.RememberRom outcome.Rom.Path |> ignore
                    dependencies.SaveSettings()

                dependencies.Notifications.Show outcome.ToastMessage
                dependencies.ViewModel.RomDetails <- outcome.RomDetails
                dependencies.ViewModel.DebugDetails <- outcome.DebugDetails

                if outcome.Session.IsSome then
                    startLoadedRomWithRa outcome.Rom

                dependencies.RefreshMenus()
                dependencies.Owner.Focus() |> ignore
            | RomWorkflow.LoadFailed(toastMessage, romDetails, debugDetails) ->
                loadedRom <- None
                dependencies.ViewModel.RomDisplayName <- None
                setCurrentSession None
                dependencies.Runner.ClearFrames()
                updateSessionState ()
                dependencies.Notifications.Show toastMessage
                dependencies.ViewModel.RomDetails <- romDetails
                dependencies.ViewModel.DebugDetails <- debugDetails
                dependencies.RefreshMenus()

    /// Resets the currently loaded ROM.
    member private _.ResetCurrentRomCore(notifyRuntime: bool) =
        if notifyRuntime && not (authorize Reset) then
            ()
        else
            match loadedRom with
            | None -> dependencies.Notifications.Show "Load a ROM before resetting."
            | Some rom ->
                let wasRunning = isRunning
                saveCurrentRam ()
                stopRunning ()

                let outcome =
                    RomWorkflow.reset dependencies.SettingsStore.Current.BootRomSelection rom

                setCurrentSession outcome.Session
                dependencies.Runner.ClearFrames()
                updateSessionState ()
                dependencies.PerformanceCounters.Reset()
                dependencies.PresentFrame(Video.blankFrame ())
                dependencies.Notifications.Show outcome.ToastMessage
                dependencies.ViewModel.DebugDetails <- outcome.DebugDetails

                if notifyRuntime && outcome.Session.IsSome then
                    dependencies.RetroAchievements
                    |> Option.iter (fun client ->
                        if client.Snapshot.Status = Active then
                            client.Reset())

                if wasRunning && outcome.Session.IsSome then
                    startRunning ()
                else
                    dependencies.ViewModel.IsRunning <- false

                dependencies.RefreshMenus()
                dependencies.Owner.Focus() |> ignore

    /// Resets the currently loaded ROM after checking the active RA policy.
    member this.ResetCurrentRom() = this.ResetCurrentRomCore(true)

    /// Applies a reset requested by RetroAchievements without notifying the runtime again.
    member this.HandleRetroAchievementsReset() = this.ResetCurrentRomCore(false)

    /// Saves state for the current ROM.
    member _.SaveState() =
        if not (authorize SaveState) then
            ()
        else
            let wasRunning = isRunning
            stopRunning ()

            let outcome: RomWorkflow.SaveStateOutcome =
                match dependencies.RetroAchievements, getCurrentSession () with
                | Some client, Some session when client.Snapshot.Status = Active ->
                    match RaStateWorkflow.save dependencies.SettingsStore.Path client session with
                    | Ok() ->
                        { ToastMessage = "RetroAchievements state written."
                          DebugDetails = "RetroAchievements state written." }
                    | Error message ->
                        { ToastMessage = $"Save state error: {message}"
                          DebugDetails = message }
                | _ -> RomWorkflow.saveState loadedRom (getCurrentSession ())

            dependencies.Notifications.Show outcome.ToastMessage

            if not (String.IsNullOrWhiteSpace outcome.DebugDetails) then
                dependencies.ViewModel.DebugDetails <- outcome.DebugDetails

            resumeAfterStateOperation wasRunning

    /// Loads state for the current ROM.
    member _.LoadState() =
        if not (authorize LoadState) then
            ()
        else
            let wasRunning = isRunning
            stopRunning ()

            let outcome: RomWorkflow.LoadStateOutcome =
                match dependencies.RetroAchievements, getCurrentSession () with
                | Some client, Some session when client.Snapshot.Status = Active ->
                    match RaStateWorkflow.load dependencies.SettingsStore.Path client session with
                    | Ok loaded ->
                        if not loaded.ProgressRestored then
                            client.Reset()

                        { RestoredSession = Some loaded.Session
                          ToastMessage =
                            if loaded.ProgressRestored then
                                "RetroAchievements state loaded."
                            else
                                "State loaded; RetroAchievements progress was reset."
                          DebugDetails = "RetroAchievements state loaded." }
                    | Error message ->
                        { RestoredSession = None
                          ToastMessage = $"Save state error: {message}"
                          DebugDetails = message }
                | _ -> RomWorkflow.loadState loadedRom (getCurrentSession ())

            match outcome.RestoredSession with
            | Some restored ->
                setCurrentSession (Some restored)
                dependencies.Runner.ClearFrames()
                dependencies.PerformanceCounters.Reset()
                dependencies.PresentFrame restored.Framebuffer
                updateSessionState ()
            | None -> ()

            dependencies.Notifications.Show outcome.ToastMessage

            if not (String.IsNullOrWhiteSpace outcome.DebugDetails) then
                dependencies.ViewModel.DebugDetails <- outcome.DebugDetails

            resumeAfterStateOperation wasRunning

    /// Toggles emulation between running and paused.
    member _.ToggleRunPause() =
        match getCurrentSession () with
        | None -> dependencies.Notifications.Show "Load a ROM before running."
        | Some _ ->
            if isRunning then
                if authorize Pause then
                    saveCurrentRam ()
                    stopRunning ()
            else
                startRunning ()

            dependencies.RefreshMenus()
            dependencies.Owner.Focus() |> ignore
