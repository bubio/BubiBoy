namespace BubiBoy.App

open System
open System.Runtime.InteropServices
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Media
open Avalonia.Platform
open BubiBoy.Audio
open BubiBoy.Core

type MainWindow(?startupRomPath: string) as this =
    inherit Window()

    do
        this.Title <- "BubiBoy"
        this.Icon <- WindowIcon(AssetLoader.Open(Uri("avares://BubiBoy/Assets/AppIcon.png")))
        this.WindowStartupLocation <- WindowStartupLocation.CenterScreen
        this.Width <- float Hardware.ScreenWidth * 2.0
        this.Height <- float Hardware.ScreenHeight * 2.0 + 32.0
        this.MinWidth <- float Hardware.ScreenWidth
        this.MinHeight <- float Hardware.ScreenHeight
        this.CanResize <- false
        AppTheme.bindBrush this Window.BackgroundProperty AppTheme.WindowBackground
        this.FontFamily <- AppFonts.ui
        this.Focusable <- true

        let loadedSettings = AppSettingsStore.loadDefault ()
        let settingsStore = loadedSettings.Store
        let mutable openRomHandler = fun () -> ()
        let mutable toggleRunPauseHandler = fun () -> ()
        let mutable resetHandler = fun () -> ()
        let mutable clearRecentHandler = fun () -> ()

        let viewModel =
            MainWindowViewModel(
                settingsStore.Current.Scale,
                false,
                settingsStore.Current.VolumePercent,
                (fun () -> openRomHandler ()),
                (fun () -> toggleRunPauseHandler ()),
                (fun () -> resetHandler ()),
                (fun () -> clearRecentHandler ())
            )

        this.DataContext <- viewModel

        let viewport = GameViewport.create this
        let runIndicator = AppChrome.createRunIndicator ()
        let volumeControl = VolumeControl.create settingsStore.Current.VolumePercent
        let statusBar = AppChrome.createStatusBar false runIndicator.Host volumeControl.Host
        let toast = AppChrome.createToast ()
        let isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)

        let layoutController =
            WindowLayoutController(this, isMacOS, settingsStore.Current.Scale, viewport, statusBar, toast)

        let notifications =
            AppNotificationCenter(toast, fun () -> layoutController.IsFloating)

        let saveSettings () =
            match settingsStore.Save() with
            | Ok() -> ()
            | Error message -> notifications.Show $"Settings error: {message}"

        loadedSettings.LoadError
        |> Option.iter (fun message -> notifications.Show $"Settings error: {message}")

        let inputHost = AppInputHost(this, settingsStore, saveSettings, notifications.Show)

        let outputVolume = OutputVolumeController(settingsStore.Current.VolumePercent)

        let performanceCounters = RuntimePerformanceCounters()
        let traceCounters = RuntimeTraceCounters()
        let perfTrace = PerfTrace.createFromEnvironment ()

        let audioFramesPerVideoFrame =
            int (
                Math.Round(
                    float AudioHost.defaultFormat.SampleRate * float Hardware.CyclesPerFrame
                    / float Hardware.DmgClockHz
                )
            )

        let audioBufferTargetFrames = audioFramesPerVideoFrame * 16

        let audioOutput, audioFallbackError =
            match Miniaudio.tryCreateDevice AudioHost.defaultFormat AudioHost.defaultFormat.SampleRate with
            | Ok device -> device :> AudioHost.AudioDevice, None
            | Error message ->
                AudioHost.createBufferedDevice AudioHost.defaultFormat.SampleRate :> AudioHost.AudioDevice, Some message

        audioFallbackError
        |> Option.iter (fun message ->
            notifications.Show $"Audio device unavailable; continuing without sound. ({message})")

        let emulationRunner =
            EmulationRunner(
                audioOutput,
                outputVolume.Apply,
                inputHost.ApplyInput,
                performanceCounters,
                traceCounters,
                perfTrace,
                250_000,
                audioBufferTargetFrames
            )

        let mutable refreshMenus = fun () -> ()

        let sessionController =
            EmulationSessionController(
                { Owner = this
                  ViewModel = viewModel
                  Runner = emulationRunner
                  AudioOutput = audioOutput
                  PerformanceCounters = performanceCounters
                  PresentFrame = viewport.PresentFrame
                  SettingsStore = settingsStore
                  SaveSettings = saveSettings
                  Notifications = notifications
                  RefreshMenus = fun () -> refreshMenus () }
            )

        viewModel.PropertyChanged.Add(fun args ->
            if args.PropertyName = "IsRunning" then
                runIndicator.SetRunning viewModel.IsRunning)

        let openRomPicker () =
            async {
                try
                    let! selectedPath = AppDialogs.pickRomPath this

                    match selectedPath with
                    | Some path -> sessionController.LoadRomPath(path, true)
                    | None -> ()
                with ex ->
                    notifications.Show $"ROM picker error: {ex.Message}"
            }
            |> Async.StartImmediate

        let clearRecentRoms () =
            settingsStore.ClearRecentRoms() |> ignore
            refreshMenus ()
            saveSettings ()

        openRomHandler <- openRomPicker
        toggleRunPauseHandler <- sessionController.ToggleRunPause
        resetHandler <- sessionController.ResetCurrentRom
        clearRecentHandler <- clearRecentRoms

        let setScale scale =
            let normalizedScale = settingsStore.SetScale scale
            layoutController.SetScale normalizedScale
            viewModel.SelectedScale <- normalizedScale
            refreshMenus ()
            saveSettings ()

        let setFloating enabled =
            layoutController.SetFloating enabled
            viewModel.IsFloating <- enabled
            refreshMenus ()

        let toggleFullScreen () =
            if layoutController.IsFloating then
                setFloating false

            layoutController.ToggleFullScreen()
            refreshMenus ()

        let platformModifier = if isMacOS then KeyModifiers.Meta else KeyModifiers.Control

        let menuElements =
            MainWindowMenus.create
                this
                isMacOS
                platformModifier
                viewModel
                { OpenInputMapping = inputHost.OpenMapping
                  SaveState = sessionController.SaveState
                  LoadState = sessionController.LoadState
                  SetScale = setScale
                  ToggleFullScreen = toggleFullScreen
                  ToggleFloating = fun () -> setFloating (not layoutController.IsFloating)
                  LoadRecent = fun path -> sessionController.LoadRomPath(path, true)
                  Close = fun () -> this.Close()
                  ShowAbout = this.ShowAbout }

        let menuBar = menuElements.MenuBar

        refreshMenus <-
            fun () ->
                menuElements.Refresh
                    { RecentRoms = settingsStore.Current.RecentRoms
                      IsFloating = layoutController.IsFloating
                      IsFullScreen = this.WindowState = WindowState.FullScreen }

        let frameDisplayTimer =
            FrameDisplayTimer(
                { IsRunning = fun () -> sessionController.IsRunning
                  DequeueFrame = emulationRunner.DequeueFrame
                  UpdateFrame = sessionController.UpdateFrame
                  UpdateDiagnostics = fun () -> viewModel.DebugDetails <- sessionController.FormatRuntimeDiagnostics()
                  AudioDiagnostics = audioOutput.Diagnostics },
                performanceCounters,
                traceCounters,
                perfTrace
            )

        let executeCommand (command: System.Windows.Input.ICommand) =
            if command.CanExecute null then
                command.Execute null

        this.KeyDown.Add(fun args ->
            if args.Key = Key.P && args.KeyModifiers = platformModifier then
                executeCommand viewModel.RunPauseCommand
                args.Handled <- true
            elif inputHost.UpdateKeyboardKey(args.Key, true) then
                args.Handled <- true)

        this.KeyUp.Add(fun args ->
            if inputHost.UpdateKeyboardKey(args.Key, false) then
                args.Handled <- true)

        let setVolumePercent percent =
            let clamped = settingsStore.SetVolumePercent percent
            outputVolume.SetPercent clamped
            viewModel.VolumePercent <- clamped
            volumeControl.SetVisual clamped
            saveSettings ()

        VolumeControl.bind volumeControl (fun () -> viewModel.VolumePercent) setVolumePercent

        this.Closing.Add(fun _ ->
            sessionController.SaveCurrentRam()
            saveSettings ())

        this.Closed.Add(fun _ ->
            sessionController.StopRunning()
            frameDisplayTimer.Stop()
            inputHost.Dispose()
            PerfTrace.close perfTrace)

        let contentGrid = Grid(RowDefinitions = RowDefinitions("Auto,*,Auto"))

        Grid.SetRow(menuBar, 0)
        Grid.SetRow(viewport.Host, 1)
        Grid.SetRow(statusBar, 2)
        contentGrid.Children.Add menuBar |> ignore
        contentGrid.Children.Add viewport.Host |> ignore
        contentGrid.Children.Add statusBar |> ignore

        let overlay = Grid()
        overlay.Children.Add contentGrid |> ignore
        overlay.Children.Add toast.Host |> ignore

        this.Content <- overlay
        layoutController.Attach(menuBar, contentGrid)

        this
            .GetObservable(Window.WindowStateProperty)
            .Subscribe(fun _ ->
                layoutController.HandleWindowStateChanged()
                refreshMenus ())
        |> ignore

        refreshMenus ()
        layoutController.ApplyInitialLayout()
        inputHost.Start()
        frameDisplayTimer.Start()

        startupRomPath
        |> Option.filter (String.IsNullOrWhiteSpace >> not)
        |> Option.iter (fun path -> sessionController.LoadRomPath(path, true))

    member this.ShowAbout() =
        let version =
            this.GetType().Assembly.GetName().Version
            |> Option.ofObj
            |> Option.map string
            |> Option.defaultValue "development"

        let dialog = AboutWindow(version)
        dialog.ShowDialog(this) |> ignore
