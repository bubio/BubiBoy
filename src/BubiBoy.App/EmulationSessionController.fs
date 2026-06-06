namespace BubiBoy.App

open System
open Avalonia.Controls
open BubiBoy.Audio
open BubiBoy.Core
open BubiBoy.IO

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
      RefreshMenus: unit -> unit }

/// Owns the loaded ROM, live emulator session, and run-state workflows.
type EmulationSessionController(dependencies: EmulationSessionDependencies) =
    let sessionGate = obj ()
    let mutable loadedRom: RomFile.LoadedRom option = None
    let mutable currentSession: Emulator.Session option = None
    let mutable isRunning = false

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
        dependencies.Runner.PrimeAudioBuffer(getCurrentSession, Some >> setCurrentSession)
        dependencies.Runner.Start(getCurrentSession, Some >> setCurrentSession, stopRunning)

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

    /// Gets whether emulation is currently running.
    member _.IsRunning = isRunning

    /// Stops the active emulation loop and audio output.
    member _.StopRunning() =
        stopRunning ()

    /// Saves cartridge RAM for the current session when supported.
    member _.SaveCurrentRam() =
        saveCurrentRam ()

    /// Formats current display and audio performance diagnostics.
    member _.FormatRuntimeDiagnostics() =
        dependencies.PerformanceCounters.FormatDiagnostics(
            dependencies.AudioOutput.Diagnostics()
        )

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
        else
            saveCurrentRam ()

            match RomWorkflow.load path dependencies.Notifications.LastStatus with
            | RomWorkflow.EmptyPath ->
                dependencies.Notifications.Show "Could not open the selected ROM path."
            | RomWorkflow.Loaded outcome ->
                loadedRom <- Some outcome.Rom
                setCurrentSession outcome.Session
                dependencies.Runner.ClearFrames()
                updateSessionState ()
                stopRunning ()
                dependencies.PresentFrame(Video.blankFrame ())

                if rememberRecent then
                    dependencies.SettingsStore.RememberRom outcome.Rom.Path |> ignore
                    dependencies.RefreshMenus()
                    dependencies.SaveSettings()

                dependencies.Notifications.Show outcome.ToastMessage
                dependencies.ViewModel.RomDetails <- outcome.RomDetails
                dependencies.ViewModel.DebugDetails <- outcome.DebugDetails
            | RomWorkflow.LoadFailed(toastMessage, romDetails, debugDetails) ->
                loadedRom <- None
                setCurrentSession None
                dependencies.Runner.ClearFrames()
                updateSessionState ()
                stopRunning ()
                dependencies.Notifications.Show toastMessage
                dependencies.ViewModel.RomDetails <- romDetails
                dependencies.ViewModel.DebugDetails <- debugDetails

    /// Resets the currently loaded ROM.
    member _.ResetCurrentRom() =
        match loadedRom with
        | None ->
            dependencies.Notifications.Show "Load a ROM before resetting."
        | Some rom ->
            let wasRunning = isRunning
            saveCurrentRam ()
            stopRunning ()

            let outcome = RomWorkflow.reset rom

            setCurrentSession outcome.Session
            dependencies.Runner.ClearFrames()
            updateSessionState ()
            dependencies.PerformanceCounters.Reset()
            dependencies.PresentFrame(Video.blankFrame ())
            dependencies.Notifications.Show outcome.ToastMessage
            dependencies.ViewModel.DebugDetails <- outcome.DebugDetails

            if wasRunning && outcome.Session.IsSome then
                startRunning ()
            else
                dependencies.ViewModel.IsRunning <- false

            dependencies.RefreshMenus()
            dependencies.Owner.Focus() |> ignore

    /// Saves state for the current ROM.
    member _.SaveState() =
        let wasRunning = isRunning
        stopRunning ()

        let outcome = RomWorkflow.saveState loadedRom (getCurrentSession ())
        dependencies.Notifications.Show outcome.ToastMessage

        if not (String.IsNullOrWhiteSpace outcome.DebugDetails) then
            dependencies.ViewModel.DebugDetails <- outcome.DebugDetails

        resumeAfterStateOperation wasRunning

    /// Loads state for the current ROM.
    member _.LoadState() =
        let wasRunning = isRunning
        stopRunning ()

        let outcome = RomWorkflow.loadState loadedRom (getCurrentSession ())

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
        | None ->
            dependencies.Notifications.Show "Load a ROM before running."
        | Some _ ->
            if isRunning then
                saveCurrentRam ()
                stopRunning ()
            else
                startRunning ()

            dependencies.RefreshMenus()
            dependencies.Owner.Focus() |> ignore
