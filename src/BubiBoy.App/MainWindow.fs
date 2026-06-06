namespace BubiBoy.App

open System
open System.Diagnostics
open System.Runtime.InteropServices
open Avalonia
open Avalonia.Controls
open Avalonia.Data
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform
open Avalonia.Threading
open BubiBoy.Audio
open BubiBoy.Core
open BubiBoy.IO

type MainWindow() as this =
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
        this.Background <- SolidColorBrush(Color.Parse("#F4F5F7"))
        this.FontFamily <- AppFonts.ui
        this.Focusable <- true

        let title =
            TextBlock(
                Text = "BubiBoy",
                FontSize = 28.0,
                FontWeight = FontWeight.SemiBold,
                Foreground = SolidColorBrush(Color.Parse("#17202B"))
            )

        let subtitle =
            TextBlock(
                Text = "Game Boy / Game Boy Color emulator",
                FontSize = 15.0,
                Foreground = SolidColorBrush(Color.Parse("#4F5F72"))
            )

        let viewport = GameViewport.create this
        let presentFrame = viewport.PresentFrame

        let mutable loadedRom: RomFile.LoadedRom option = None
        let mutable currentSession: Emulator.Session option = None
        let mutable isRunning = false
        let mutable lastSaveStatus: string option = None
        let performanceCounters = RuntimePerformanceCounters()
        let traceCounters = RuntimeTraceCounters()
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
        let mutable selectedScale = settingsStore.Current.Scale
        let mutable isFloating = false
        let mutable outputVolume = VolumeControl.gainFromPercent settingsStore.Current.VolumePercent
        let sessionGate = obj ()
        let volumeGate = obj ()
        let inputState = InputStateController()
        let perfTrace = PerfTrace.createFromEnvironment ()
        let audioFramesPerVideoFrame =
            int (Math.Round(float AudioHost.defaultFormat.SampleRate * float Hardware.CyclesPerFrame / float Hardware.DmgClockHz))

        let maxStepsPerFrame = 250_000
        let audioBufferTargetFrames = audioFramesPerVideoFrame * 16
        let audioDevice =
            match Miniaudio.tryCreateDevice AudioHost.defaultFormat AudioHost.defaultFormat.SampleRate with
            | Ok device -> device :> AudioHost.AudioDevice
            | Error _ -> AudioHost.createBufferedDevice AudioHost.defaultFormat.SampleRate :> AudioHost.AudioDevice

        let audioOutput = audioDevice
        let controllerHost = ControllerInput.GamepadHosts.createDefault ()

        let getCurrentSession () =
            lock sessionGate (fun () -> currentSession)

        let setCurrentSession session =
            lock sessionGate (fun () -> currentSession <- Some session)

        let applyInput (session: Emulator.Session) =
            inputState.ApplyInput session

        let applyVolume (samples: Apu.Sample[]) =
            let volume = lock volumeGate (fun () -> outputVolume)

            if volume <> 1.0f then
                // samples is a freshly drained buffer owned by this frame result, so we
                // scale it in place instead of allocating a new array each frame.
                for index in 0 .. samples.Length - 1 do
                    let sample = samples[index]
                    samples[index] <- { Left = sample.Left * volume; Right = sample.Right * volume }

            samples

        let emulationRunner =
            EmulationRunner(
                audioOutput,
                applyVolume,
                applyInput,
                performanceCounters,
                traceCounters,
                perfTrace,
                maxStepsPerFrame,
                audioBufferTargetFrames
            )

        let runIndicator = AppChrome.createRunIndicator ()

        viewModel.PropertyChanged.Add(fun args ->
            if args.PropertyName = "IsRunning" then
                runIndicator.SetRunning viewModel.IsRunning)

        let volumeControl = VolumeControl.create settingsStore.Current.VolumePercent
        let statusBar = AppChrome.createStatusBar isFloating runIndicator.Host volumeControl.Host
        let toast = AppChrome.createToast ()
        let controllerPollTimer = DispatcherTimer(Interval = TimeSpan.FromMilliseconds(16.0))

        let romDetails =
            TextBlock(
                FontSize = 13.0,
                Foreground = SolidColorBrush(Color.Parse("#425166")),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 560.0,
                Height = 50.0
            )
        romDetails.Bind(TextBlock.TextProperty, Binding("RomDetails")) |> ignore

        let debugDetails =
            TextBlock(
                FontFamily = AppFonts.monospace,
                FontSize = 12.0,
                Foreground = SolidColorBrush(Color.Parse("#263448")),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 560.0,
                Height = 72.0
            )
        debugDetails.Bind(TextBlock.TextProperty, Binding("DebugDetails")) |> ignore

        let mutable notify = fun (message: string) -> lastSaveStatus <- Some message

        let saveSettings () =
            match settingsStore.Save() with
            | Ok () -> ()
            | Error message -> notify $"Settings error: {message}"

        let showToast message =
            if not isFloating then
                toast.Text.Text <- message
                toast.Host.IsVisible <- true
                toast.Timer.Stop()
                toast.Timer.Start()
            else
                lastSaveStatus <- Some message

        notify <- showToast

        toast.Timer.Tick.Add(fun _ ->
            toast.Timer.Stop()
            toast.Host.IsVisible <- false)

        let openInputMapping () =
            task {
                let! result =
                    AppDialogs.showInputMapping
                        this
                        settingsStore.Current.KeyboardMapping
                        settingsStore.Current.ControllerMapping
                        controllerHost

                match result with
                | Some inputMapping ->
                    settingsStore.SetInputMappings(
                        inputMapping.KeyboardMapping,
                        inputMapping.ControllerMapping
                    )
                    |> ignore

                    inputState.ResetKeyboard()
                    saveSettings ()
                    showToast "Input mapping saved."
                | None -> ()
            }
            |> ignore

        loadedSettings.LoadError
        |> Option.iter (fun message -> showToast $"Settings error: {message}")

        let isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        let platformModifier =
            if isMacOS then KeyModifiers.Meta else KeyModifiers.Control

        let mutable refreshMenus = fun () -> ()
        let mutable updateContentRows = fun () -> ()

        let mutable menuBar = Menu()
        menuBar.IsVisible <- not isMacOS && not isFloating

        let applyWindowChrome () =
            if isFloating then
                if this.WindowState = WindowState.FullScreen then
                    this.WindowState <- WindowState.Normal

                this.WindowDecorations <- WindowDecorations.BorderOnly
                this.ExtendClientAreaToDecorationsHint <- true
                this.ExtendClientAreaTitleBarHeightHint <- 0.0
                this.CanResize <- false
                statusBar.IsVisible <- false
                statusBar.MinHeight <- 0.0
                statusBar.Height <- 0.0
                menuBar.IsVisible <- false
                toast.Host.IsVisible <- false
            else
                this.ExtendClientAreaToDecorationsHint <- false
                this.ExtendClientAreaTitleBarHeightHint <- -1.0
                this.WindowDecorations <- WindowDecorations.Full
                this.CanResize <- false
                statusBar.IsVisible <- true
                statusBar.MinHeight <- AppChrome.StatusBarHeight
                statusBar.Height <- AppChrome.StatusBarHeight
                menuBar.IsVisible <- not isMacOS

            updateContentRows ()

        let applySelectedScale resizeWindow =
            let videoWidth = float Hardware.ScreenWidth * float selectedScale
            let videoHeight = float Hardware.ScreenHeight * float selectedScale
            let isFullScreen = this.WindowState = WindowState.FullScreen
            viewport.ApplyScale selectedScale this.WindowState

            if resizeWindow && not isFullScreen then
                let menuHeight =
                    if isMacOS || isFloating then 0.0 else 28.0

                let statusHeight =
                    if isFloating then 0.0 else AppChrome.StatusBarHeight

                this.Width <- videoWidth
                this.Height <- videoHeight + menuHeight + statusHeight

        let setScale scale =
            selectedScale <- settingsStore.SetScale scale
            viewModel.SelectedScale <- selectedScale
            applySelectedScale true
            refreshMenus ()
            saveSettings ()

        let setFloating enabled =
            isFloating <- enabled
            viewModel.IsFloating <- enabled
            applyWindowChrome ()
            applySelectedScale true
            refreshMenus ()

        let updateSessionState () =
            let hasSession = lock sessionGate (fun () -> currentSession.IsSome)
            viewModel.UpdateSessionState(hasSession, loadedRom.IsSome)

        let stopRunning () =
            isRunning <- false
            viewModel.IsRunning <- false
            emulationRunner.StopLoop()
            audioOutput.Stop()

        let saveCurrentRam () =
            let session = lock sessionGate (fun () -> currentSession)
            let outcome = RomWorkflow.saveRam loadedRom session
            lastSaveStatus <- outcome.LastSaveStatus
            outcome.ToastMessage |> Option.iter showToast

        let formatRuntimeDiagnostics () =
            performanceCounters.FormatDiagnostics(audioOutput.Diagnostics())

        let updateFrame (result: Emulator.FrameResult) =
            presentFrame result.Framebuffer
            viewModel.DebugDetails <-
                if isRunning then
                    formatRuntimeDiagnostics ()
                else
                    $"{DebugDisplay.formatFrameResult result}\n{formatRuntimeDiagnostics ()}"

            match result.StopReason with
            | Emulator.FrameCompleted -> ()
            | _ -> stopRunning ()

        let pollControllerInput () =
            inputState.PollController(controllerHost, settingsStore.Current.ControllerMapping)
            |> Option.iter showToast

        controllerPollTimer.Tick.Add(fun _ ->
            try
                pollControllerInput ()
            with ex ->
                controllerPollTimer.Stop()
                inputState.DisableController()
                showToast $"Controller input disabled: {ex.Message}")
        controllerPollTimer.Start()

        let startEmulationLoop () =
            emulationRunner.Start(getCurrentSession, setCurrentSession, stopRunning)

        let primeAudioBuffer () =
            emulationRunner.PrimeAudioBuffer(getCurrentSession, setCurrentSession)

        let resetCurrentRom () =
            match loadedRom with
            | None ->
                showToast "Load a ROM before resetting."
            | Some rom ->
                let wasRunning = isRunning
                saveCurrentRam ()
                stopRunning ()

                let outcome = RomWorkflow.reset rom

                lock sessionGate (fun () ->
                    currentSession <- outcome.Session)
                emulationRunner.ClearFrames()
                updateSessionState ()

                performanceCounters.Reset()
                presentFrame (Video.blankFrame ())
                showToast outcome.ToastMessage
                viewModel.DebugDetails <- outcome.DebugDetails

                if wasRunning && outcome.Session.IsSome then
                    isRunning <- true
                    viewModel.IsRunning <- true
                    performanceCounters.Reset()
                    audioOutput.Start()
                    primeAudioBuffer ()
                    startEmulationLoop ()
                else
                    viewModel.IsRunning <- false

                refreshMenus ()
                this.Focus() |> ignore

        let loadRomPath path rememberRecent =
            if String.IsNullOrWhiteSpace path then
                showToast "Could not open the selected ROM path."
            else
                saveCurrentRam ()

                match RomWorkflow.load path lastSaveStatus with
                | RomWorkflow.EmptyPath ->
                    showToast "Could not open the selected ROM path."
                | RomWorkflow.Loaded outcome ->
                    loadedRom <- Some outcome.Rom

                    lock sessionGate (fun () ->
                        currentSession <- outcome.Session)
                    emulationRunner.ClearFrames()
                    updateSessionState ()

                    stopRunning ()
                    presentFrame (Video.blankFrame ())

                    if rememberRecent then
                        settingsStore.RememberRom outcome.Rom.Path |> ignore
                        refreshMenus ()
                        saveSettings ()

                    showToast outcome.ToastMessage
                    viewModel.RomDetails <- outcome.RomDetails
                    viewModel.DebugDetails <- outcome.DebugDetails
                | RomWorkflow.LoadFailed(toastMessage, romDetails, debugDetails) ->
                    loadedRom <- None

                    lock sessionGate (fun () ->
                        currentSession <- None)
                    emulationRunner.ClearFrames()
                    updateSessionState ()

                    stopRunning ()
                    showToast toastMessage
                    viewModel.RomDetails <- romDetails
                    viewModel.DebugDetails <- debugDetails

        let resumeAfterStateOperation wasRunning =
            if wasRunning then
                isRunning <- true
                viewModel.IsRunning <- true
                performanceCounters.Reset()
                audioOutput.Start()
                primeAudioBuffer ()
                startEmulationLoop ()
            else
                viewModel.IsRunning <- false

            refreshMenus ()
            this.Focus() |> ignore

        let saveStateForCurrentRom () =
            let wasRunning = isRunning
            stopRunning ()

            let session = lock sessionGate (fun () -> currentSession)
            let outcome = RomWorkflow.saveState loadedRom session
            showToast outcome.ToastMessage

            if not (String.IsNullOrWhiteSpace outcome.DebugDetails) then
                viewModel.DebugDetails <- outcome.DebugDetails

            resumeAfterStateOperation wasRunning

        let loadStateForCurrentRom () =
            let wasRunning = isRunning
            stopRunning ()

            let session = lock sessionGate (fun () -> currentSession)
            let outcome = RomWorkflow.loadState loadedRom session

            match outcome.RestoredSession with
            | Some restored ->
                lock sessionGate (fun () ->
                    currentSession <- Some restored)
                emulationRunner.ClearFrames()
                performanceCounters.Reset()
                presentFrame restored.Framebuffer
                updateSessionState ()
            | None -> ()

            showToast outcome.ToastMessage

            if not (String.IsNullOrWhiteSpace outcome.DebugDetails) then
                viewModel.DebugDetails <- outcome.DebugDetails

            resumeAfterStateOperation wasRunning

        let toggleRunPause () =
            if currentSession.IsNone then
                showToast "Load a ROM before running."
            else
                isRunning <- not isRunning
                viewModel.IsRunning <- isRunning

                if isRunning then
                    performanceCounters.Reset()
                    audioOutput.Start()
                    primeAudioBuffer ()
                    startEmulationLoop ()
                else
                    saveCurrentRam ()
                    stopRunning ()

                refreshMenus ()
                this.Focus() |> ignore

        let openRomPicker () =
            async {
                try
                    let! selectedPath = AppDialogs.pickRomPath this

                    match selectedPath with
                    | Some path -> loadRomPath path true
                    | None -> ()
                with ex ->
                    showToast $"ROM picker error: {ex.Message}"
            }
            |> Async.StartImmediate

        let clearRecentRoms () =
            settingsStore.ClearRecentRoms() |> ignore
            refreshMenus ()
            saveSettings ()

        openRomHandler <- openRomPicker
        toggleRunPauseHandler <- toggleRunPause
        resetHandler <- resetCurrentRom
        clearRecentHandler <- clearRecentRoms

        let frameTimer = DispatcherTimer()
        frameTimer.Interval <- TimeSpan.FromMilliseconds(1000.0 * float Hardware.CyclesPerFrame / float Hardware.DmgClockHz)
        frameTimer.Tick.Add(fun _ ->
            if isRunning then
                let tick, tickDelta = traceCounters.NextDisplayTick(perfTrace)
                let stopwatch = Stopwatch.StartNew()
                let dequeued = emulationRunner.DequeueFrame()

                match dequeued.Frame with
                | Some result ->
                    traceCounters.RecordDisplayedFrame() |> ignore
                    performanceCounters.RecordDisplayedFrame()
                    updateFrame result
                | None ->
                    viewModel.DebugDetails <- formatRuntimeDiagnostics ()

                stopwatch.Stop()
                let diagnostics = audioOutput.Diagnostics()

                PerfTrace.writeDisplay
                    perfTrace
                    tick
                    stopwatch.Elapsed.TotalMilliseconds
                    tickDelta
                    traceCounters.DisplayedFrameCount
                    dequeued.QueueBefore
                    dequeued.QueueAfter
                    diagnostics.BufferedFrames
                    diagnostics.UnderrunFrames
                    diagnostics.DroppedFrames)
        frameTimer.Start()

        let toggleFullScreen () =
            if isFloating then
                setFloating false

            this.WindowState <-
                if this.WindowState = WindowState.FullScreen then
                    WindowState.Normal
                else
                    WindowState.FullScreen

            refreshMenus ()

        let menuElements =
            MainWindowMenus.create
                this
                isMacOS
                platformModifier
                viewModel
                { OpenInputMapping = openInputMapping
                  SaveState = saveStateForCurrentRom
                  LoadState = loadStateForCurrentRom
                  SetScale = setScale
                  ToggleFullScreen = toggleFullScreen
                  ToggleFloating = fun () -> setFloating (not isFloating)
                  LoadRecent = fun path -> loadRomPath path true
                  Close = fun () -> this.Close()
                  ShowAbout = this.ShowAbout }

        menuBar <- menuElements.MenuBar

        refreshMenus <-
            fun () ->
                menuElements.Refresh
                    { RecentRoms = settingsStore.Current.RecentRoms
                      IsFloating = isFloating
                      IsFullScreen = this.WindowState = WindowState.FullScreen }

        this.GetObservable(Window.WindowStateProperty).Subscribe(fun _ ->
            applySelectedScale false
            refreshMenus ())
        |> ignore

        refreshMenus ()
        applyWindowChrome ()
        applySelectedScale true

        let executeCommand (command: System.Windows.Input.ICommand) =
            if command.CanExecute null then
                command.Execute null

        let updateButtonState key pressed =
            inputState.UpdateKeyboardKey(settingsStore.Current.KeyboardMapping, key, pressed)

        this.KeyDown.Add(fun args ->
            if args.Key = Key.P && args.KeyModifiers = platformModifier then
                executeCommand viewModel.RunPauseCommand
                args.Handled <- true
            elif updateButtonState args.Key true then
                args.Handled <- true)

        this.KeyUp.Add(fun args ->
            if updateButtonState args.Key false then
                args.Handled <- true)

        let setVolumePercent percent =
            let clamped = settingsStore.SetVolumePercent percent
            lock volumeGate (fun () -> outputVolume <- VolumeControl.gainFromPercent clamped)
            viewModel.VolumePercent <- clamped
            volumeControl.SetVisual clamped
            saveSettings ()

        VolumeControl.bind volumeControl (fun () -> viewModel.VolumePercent) setVolumePercent

        this.Closing.Add(fun _ ->
            saveCurrentRam ()
            saveSettings ())
        this.Closed.Add(fun _ ->
            controllerPollTimer.Stop()
            controllerHost.Dispose()
            PerfTrace.close perfTrace)

        let contentGrid =
            Grid(RowDefinitions = RowDefinitions("Auto,*,Auto"))

        updateContentRows <-
            fun () ->
                contentGrid.RowDefinitions <-
                    if isFloating then
                        RowDefinitions("0,*,0")
                    else
                        RowDefinitions("Auto,*,Auto")

        updateContentRows ()

        Grid.SetRow(menuBar, 0)
        Grid.SetRow(viewport.Host, 1)
        Grid.SetRow(statusBar, 2)
        contentGrid.Children.Add menuBar |> ignore
        contentGrid.Children.Add viewport.Host |> ignore
        contentGrid.Children.Add statusBar |> ignore

        let overlay =
            Grid()

        overlay.Children.Add contentGrid |> ignore
        overlay.Children.Add toast.Host |> ignore

        this.Content <- overlay

    member this.ShowAbout() =
        let version =
            this.GetType().Assembly.GetName().Version
            |> Option.ofObj
            |> Option.map string
            |> Option.defaultValue "development"

        let dialog = AboutWindow(version)
        dialog.ShowDialog(this) |> ignore
