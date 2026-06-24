namespace BubiBoy.App

open System
open System.Diagnostics
open System.Runtime.InteropServices
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Media
open Avalonia.Platform
open BubiBoy.Audio
open BubiBoy.Core
open BubiBoy.IO
open BubiBoy.RetroAchievements

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
        let mutable retroAchievementsResetHandler = fun () -> ()
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

        let viewport =
            GameViewport.create this settingsStore.Current.ShowFullScreenInfo settingsStore.Current.VideoFilter

        let runIndicator = AppChrome.createRunIndicator ()
        let volumeControl = VolumeControl.create settingsStore.Current.VolumePercent
        let statusBar = AppChrome.createStatusBar false runIndicator.Host volumeControl.Host
        let toast = AppChrome.createToast ()
        let isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)

        let layoutController =
            WindowLayoutController(this, isMacOS, settingsStore.Current.Scale, viewport, statusBar, toast)

        let notifications =
            AppNotificationCenter(toast, fun () -> layoutController.IsFloating)

        let retroAchievements =
            match RaClient.TryCreate(fun message -> Debug.WriteLine message) with
            | Ok client -> Some client
            | Error message ->
                if settingsStore.Current.RetroAchievementsEnabled then
                    notifications.Show $"RetroAchievements unavailable: {message}"

                None

        let mutable achievementsWindow: AchievementsWindow option = None

        retroAchievements
        |> Option.iter (fun client ->
            client.SetHardcoreEnabled settingsStore.Current.RetroAchievementsHardcore

            client.EventRaised.Add(fun event ->
                match RetroAchievementsPresentation.hostAction event with
                | RetroAchievementsPresentation.Notify message ->
                    Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                        notifications.Show(message, TimeSpan.FromSeconds 5.0))
                | RetroAchievementsPresentation.ResetRequested ->
                    Avalonia.Threading.Dispatcher.UIThread.Post retroAchievementsResetHandler
                | RetroAchievementsPresentation.Ignore -> ())

            if settingsStore.Current.RetroAchievementsEnabled then
                let username = settingsStore.Current.RetroAchievementsUsername

                if
                    not (client.LoginWithStoredToken username)
                    && not (String.IsNullOrWhiteSpace username)
                then
                    notifications.Show "Open Achievements to log in to RetroAchievements.")

        let retroAchievementsOverlay =
            retroAchievements
            |> Option.map (fun client ->
                new RetroAchievementsOverlayController(viewport.RetroAchievementsOverlayHost, client))

        let saveSettings () =
            match settingsStore.Save() with
            | Ok() -> ()
            | Error message -> notifications.Show $"Settings error: {message}"

        retroAchievements
        |> Option.iter (fun client ->
            client.Changed.Add(fun snapshot ->
                snapshot.User
                |> Option.iter (fun raUser ->
                    Avalonia.Threading.Dispatcher.UIThread.Post(fun () ->
                        if
                            not settingsStore.Current.RetroAchievementsEnabled
                            || settingsStore.Current.RetroAchievementsUsername <> raUser.Username
                        then
                            settingsStore.SetRetroAchievements(
                                true,
                                settingsStore.Current.RetroAchievementsHardcore,
                                raUser.Username
                            )
                            |> ignore

                            saveSettings ()))))

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

        let audioBufferCapacityFrames = audioFramesPerVideoFrame * 8
        let audioBufferTargetFrames = audioFramesPerVideoFrame * 4
        let audioBufferFallbackTargetFrames = audioFramesPerVideoFrame * 6

        let audioOutput, audioFallbackError =
            match Miniaudio.tryCreateDevice AudioHost.defaultFormat audioBufferCapacityFrames with
            | Ok device -> device :> AudioHost.AudioDevice, None
            | Error message ->
                AudioHost.createBufferedDevice audioBufferCapacityFrames :> AudioHost.AudioDevice, Some message

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
                audioBufferTargetFrames,
                audioBufferFallbackTargetFrames,
                TimeProvider.System,
                retroAchievements
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
                  RefreshMenus = fun () -> refreshMenus ()
                  RetroAchievements = retroAchievements }
            )

        viewModel.PropertyChanged.Add(fun args ->
            if args.PropertyName = "IsRunning" then
                runIndicator.SetRunning viewModel.IsRunning
                viewport.UpdateSessionInfo viewModel.RomDisplayName viewModel.IsRunning
            elif args.PropertyName = "RomDisplayName" then
                viewport.UpdateSessionInfo viewModel.RomDisplayName viewModel.IsRunning)

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
        retroAchievementsResetHandler <- sessionController.HandleRetroAchievementsReset
        clearRecentHandler <- clearRecentRoms

        let setScale scale =
            let normalizedScale = settingsStore.SetScale scale
            layoutController.SetScale normalizedScale
            viewModel.SelectedScale <- normalizedScale
            refreshMenus ()
            saveSettings ()

        let setVideoFilter filter =
            let normalizedFilter = settingsStore.SetVideoFilter filter
            viewport.SetVideoFilter normalizedFilter
            refreshMenus ()
            saveSettings ()

        let setFloating enabled =
            layoutController.SetFloating enabled
            viewModel.IsFloating <- enabled
            refreshMenus ()

        let toggleAlwaysOnTop () =
            layoutController.SetAlwaysOnTop(not layoutController.IsAlwaysOnTop)
            refreshMenus ()

        let toggleFullScreen () =
            if layoutController.IsFloating then
                setFloating false

            layoutController.ToggleFullScreen()
            refreshMenus ()

        let toggleFullScreenInfo () =
            let enabled =
                settingsStore.SetShowFullScreenInfo(not settingsStore.Current.ShowFullScreenInfo)

            viewport.SetSidePanelsEnabled enabled
            refreshMenus ()
            saveSettings ()

        let platformModifier = if isMacOS then KeyModifiers.Meta else KeyModifiers.Control

        let openSettings () =
            async {
                let! result = AppDialogs.showSettings this settingsStore.Current |> Async.AwaitTask

                match result with
                | Some selection ->
                    settingsStore.SetBootRomSelection selection.BootRomSelection |> ignore

                    settingsStore.SetRetroAchievements(
                        selection.RetroAchievementsEnabled,
                        selection.RetroAchievementsHardcore,
                        selection.RetroAchievementsUsername
                    )
                    |> ignore

                    saveSettings ()

                    match retroAchievements with
                    | Some client when not selection.RetroAchievementsEnabled ->
                        client.SetHardcoreEnabled false
                        client.Logout()
                    | Some client ->
                        client.SetHardcoreEnabled selection.RetroAchievementsHardcore

                        if client.Snapshot.Status = LoggedOut then
                            if not (client.LoginWithStoredToken selection.RetroAchievementsUsername) then
                                notifications.Show "RetroAchievements enabled. Open Achievements to log in."
                    | None -> ()

                    notifications.Show "Settings saved. Boot ROM changes apply on the next ROM load or reset."
                | None -> ()
            }
            |> Async.StartImmediate

        let openAchievements () =
            match retroAchievements with
            | Some client ->
                match achievementsWindow with
                | Some window ->
                    if window.WindowState = WindowState.Minimized then
                        window.WindowState <- WindowState.Normal

                    window.Activate()
                | None ->
                    let window = AchievementsWindow(client)
                    achievementsWindow <- Some window

                    window.Closed.Add(fun _ ->
                        if
                            achievementsWindow
                            |> Option.exists (fun current -> Object.ReferenceEquals(current, window))
                        then
                            achievementsWindow <- None)

                    window.Show(this)
            | None -> notifications.Show "RetroAchievements native support is unavailable."

        let menuElements =
            MainWindowMenus.create
                this
                isMacOS
                platformModifier
                viewModel
                { OpenSettings = openSettings
                  OpenAchievements = openAchievements
                  OpenInputMapping = inputHost.OpenMapping
                  SaveState = sessionController.SaveState
                  LoadState = sessionController.LoadState
                  SetScale = setScale
                  SetVideoFilter = setVideoFilter
                  ToggleFullScreen = toggleFullScreen
                  ToggleFullScreenInfo = toggleFullScreenInfo
                  ToggleFloating = fun () -> setFloating (not layoutController.IsFloating)
                  ToggleAlwaysOnTop = toggleAlwaysOnTop
                  LoadRecent = fun path -> sessionController.LoadRomPath(path, true)
                  Close = fun () -> this.Close()
                  ShowAbout = this.ShowAbout }

        let menuBar = menuElements.MenuBar

        refreshMenus <-
            fun () ->
                menuElements.Refresh
                    { RecentRoms = settingsStore.Current.RecentRoms
                      IsFloating = layoutController.IsFloating
                      IsAlwaysOnTop = layoutController.IsAlwaysOnTop
                      IsFullScreen = this.WindowState = WindowState.FullScreen
                      ShowFullScreenInfo = settingsStore.Current.ShowFullScreenInfo
                      CanLoadState =
                        retroAchievements
                        |> Option.forall (fun client ->
                            let snapshot = client.Snapshot
                            snapshot.Status <> Active || not snapshot.HardcoreEnabled)
                      VideoFilter = settingsStore.Current.VideoFilter }

        let frameDisplayTimer =
            FrameDisplayTimer(
                { IsRunning = fun () -> sessionController.IsRunning
                  DequeueFrame = emulationRunner.DequeueFrame
                  UpdateFrame = sessionController.UpdateFrame
                  UpdateDiagnostics = fun () -> viewModel.DebugDetails <- sessionController.FormatRuntimeDiagnostics()
                  AudioDiagnostics = audioOutput.Diagnostics
                  PumpServices =
                    fun () ->
                        retroAchievements
                        |> Option.iter (fun client ->
                            try
                                client.Pump(not sessionController.IsRunning)
                            with ex ->
                                Debug.WriteLine $"RetroAchievements pump failed: {ex}"
                                client.SetOffline "RetroAchievements service processing failed."
                                notifications.Show "RetroAchievements went offline; emulation will continue.") },
                performanceCounters,
                traceCounters,
                perfTrace,
                TopLevelAnimationFrameScheduler(this)
            )

        let executeCommand (command: System.Windows.Input.ICommand) =
            if command.CanExecute null then
                command.Execute null

        this.KeyDown.Add(fun args ->
            if args.Key = Key.P && args.KeyModifiers = platformModifier then
                executeCommand viewModel.RunPauseCommand
                args.Handled <- true
            elif not isMacOS && args.Key = Key.O && args.KeyModifiers = platformModifier then
                executeCommand viewModel.OpenRomCommand
                args.Handled <- true
            elif not isMacOS && args.Key = Key.R && args.KeyModifiers = platformModifier then
                executeCommand viewModel.ResetCommand
                args.Handled <- true
            elif not isMacOS && args.Key = Key.S && args.KeyModifiers = platformModifier then
                sessionController.SaveState()
                args.Handled <- true
            elif not isMacOS && args.Key = Key.L && args.KeyModifiers = platformModifier then
                sessionController.LoadState()
                args.Handled <- true
            elif not isMacOS && args.Key = Key.F && args.KeyModifiers = platformModifier then
                toggleFullScreen ()
                args.Handled <- true
            elif
                not isMacOS
                && args.Key = Key.F
                && args.KeyModifiers = (platformModifier ||| KeyModifiers.Shift)
            then
                setFloating (not layoutController.IsFloating)
                args.Handled <- true
            elif not isMacOS && args.Key = Key.D1 && args.KeyModifiers = platformModifier then
                setScale 1
                args.Handled <- true
            elif not isMacOS && args.Key = Key.D2 && args.KeyModifiers = platformModifier then
                setScale 2
                args.Handled <- true
            elif not isMacOS && args.Key = Key.D4 && args.KeyModifiers = platformModifier then
                setScale 4
                args.Handled <- true
            elif not isMacOS && args.Key = Key.D8 && args.KeyModifiers = platformModifier then
                setScale 8
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
            viewport.StopTimers()
            inputHost.Dispose()

            retroAchievementsOverlay
            |> Option.iter (fun overlay -> (overlay :> IDisposable).Dispose())

            retroAchievements
            |> Option.iter (fun client -> (client :> IDisposable).Dispose())

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
        this.Opened.Add(fun _ -> frameDisplayTimer.Start())

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
