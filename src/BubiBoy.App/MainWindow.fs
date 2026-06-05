namespace BubiBoy.App

open System
open System.Collections.Generic
open System.Diagnostics
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Data
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Platform
open Avalonia.Platform.Storage
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

        let normalVideoHostBackground = SolidColorBrush(Color.Parse("#F4F5F7")) :> IBrush
        let fullscreenVideoHostBackground = Brushes.Black :> IBrush
        let mutable applyVideoHostBackground = fun () -> ()

        let framebuffer =
            Border(
                Width = float Hardware.ScreenWidth * 2.0,
                Height = float Hardware.ScreenHeight * 2.0,
                Background = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            )

        // A single display bitmap and BGRA scratch buffer are reused for every frame;
        // writeInto blits the emulator framebuffer into them in place (no per-frame
        // WriteableBitmap or 100 KiB byte[] allocation).
        let displayBitmap = FramebufferBitmap.createBitmap ()
        let displayBytes = Array.zeroCreate<byte> (Hardware.ScreenWidth * Hardware.ScreenHeight * 4)
        FramebufferBitmap.writeInto (Video.blankFrame ()) displayBitmap displayBytes

        let framebufferImage =
            Image(
                Width = float Hardware.ScreenWidth * 2.0,
                Height = float Hardware.ScreenHeight * 2.0,
                Stretch = Stretch.Uniform,
                Source = displayBitmap
            )

        framebuffer.Child <- framebufferImage

        // Writes pixels into the persistent bitmap and asks Avalonia to repaint it.
        let presentFrame (pixels: uint32[]) =
            FramebufferBitmap.writeInto pixels displayBitmap displayBytes
            framebufferImage.InvalidateVisual()

        let mutable loadedRom: RomFile.LoadedRom option = None
        let mutable currentSession: Emulator.Session option = None
        let pendingFrames = Queue<Emulator.FrameResult>()
        let mutable emulationLoop: CancellationTokenSource option = None
        let mutable isRunning = false
        let mutable lastSaveStatus: string option = None
        let mutable displayedFrames = 0
        let mutable emulatedFrames = 0
        let mutable measuredDisplayFps = 0.0
        let mutable measuredEmulationFps = 0.0
        let mutable lastFrameMilliseconds = 0.0
        let mutable lastFpsSample = DateTime.UtcNow
        let mutable generatedFrameCounter = 0
        let mutable displayTickCounter = 0
        let mutable displayedFrameCounter = 0
        let mutable lastDisplayTickMs = 0.0
        let settingsPath = AppSettings.defaultPath ()
        let loadedSettings, settingsLoadError =
            match AppSettings.loadFromPath settingsPath with
            | Ok settings -> settings, None
            | Error message -> AppSettings.defaults, Some message

        let mutable appSettings = loadedSettings
        let mutable openRomHandler = fun () -> ()
        let mutable toggleRunPauseHandler = fun () -> ()
        let mutable resetHandler = fun () -> ()
        let mutable clearRecentHandler = fun () -> ()

        let viewModel =
            MainWindowViewModel(
                appSettings.Scale,
                false,
                appSettings.VolumePercent,
                (fun () -> openRomHandler ()),
                (fun () -> toggleRunPauseHandler ()),
                (fun () -> resetHandler ()),
                (fun () -> clearRecentHandler ())
            )

        this.DataContext <- viewModel
        let mutable selectedScale = appSettings.Scale
        let mutable isFloating = false
        let mutable outputVolume = VolumeControl.gainFromPercent appSettings.VolumePercent
        let sessionGate = obj ()
        let perfGate = obj ()
        let volumeGate = obj ()
        let inputGate = obj ()
        // The authoritative set of currently-held buttons is tracked per input source.
        // The emulation thread reconciles the union into the live session at frame
        // boundaries, so one source releasing a button cannot clear another source's hold.
        let mutable desiredKeyboardButtons: Set<Joypad.Button> = Set.empty
        let mutable desiredControllerButtons: Set<Joypad.Button> = Set.empty
        let mutable activeControllerId: ControllerInput.GamepadId option = None
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

        let applyVolume (samples: Apu.Sample[]) =
            let volume = lock volumeGate (fun () -> outputVolume)

            if volume <> 1.0f then
                // samples is a freshly drained buffer owned by this frame result, so we
                // scale it in place instead of allocating a new array each frame.
                for index in 0 .. samples.Length - 1 do
                    let sample = samples[index]
                    samples[index] <- { Left = sample.Left * volume; Right = sample.Right * volume }

            samples

        let runIndicator = AppChrome.createRunIndicator ()

        viewModel.PropertyChanged.Add(fun args ->
            if args.PropertyName = "IsRunning" then
                runIndicator.SetRunning viewModel.IsRunning)

        let volumeControl = VolumeControl.create appSettings.VolumePercent
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

        let romFileType =
            FilePickerFileType(
                "Game Boy ROM",
                Patterns = [| "*.gb"; "*.gbc" |],
                MimeTypes = [| "application/octet-stream" |]
            )

        let mutable notify = fun (message: string) -> lastSaveStatus <- Some message

        let saveSettings () =
            match AppSettings.saveToPath settingsPath appSettings with
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
                    InputMappingWindow.Show(
                        this,
                        appSettings.KeyboardMapping,
                        appSettings.ControllerMapping,
                        controllerHost
                    )

                match result with
                | Some inputMapping ->
                    appSettings <-
                        appSettings
                        |> AppSettings.withKeyboardMapping inputMapping.KeyboardMapping
                        |> AppSettings.withControllerMapping inputMapping.ControllerMapping

                    lock inputGate (fun () -> desiredKeyboardButtons <- Set.empty)
                    saveSettings ()
                    showToast "Input mapping saved."
                | None -> ()
            }
            |> ignore

        settingsLoadError
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

            if isFullScreen then
                framebuffer.Width <- Double.NaN
                framebuffer.Height <- Double.NaN
                framebuffer.HorizontalAlignment <- HorizontalAlignment.Stretch
                framebuffer.VerticalAlignment <- VerticalAlignment.Stretch
                framebufferImage.Width <- Double.NaN
                framebufferImage.Height <- Double.NaN
                framebufferImage.HorizontalAlignment <- HorizontalAlignment.Stretch
                framebufferImage.VerticalAlignment <- VerticalAlignment.Stretch
            else
                framebuffer.Width <- videoWidth
                framebuffer.Height <- videoHeight
                framebuffer.HorizontalAlignment <- HorizontalAlignment.Center
                framebuffer.VerticalAlignment <- VerticalAlignment.Center
                framebufferImage.Width <- videoWidth
                framebufferImage.Height <- videoHeight
                framebufferImage.HorizontalAlignment <- HorizontalAlignment.Center
                framebufferImage.VerticalAlignment <- VerticalAlignment.Center

            applyVideoHostBackground ()

            if resizeWindow && not isFullScreen then
                let menuHeight =
                    if isMacOS || isFloating then 0.0 else 28.0

                let statusHeight =
                    if isFloating then 0.0 else AppChrome.StatusBarHeight

                this.Width <- videoWidth
                this.Height <- videoHeight + menuHeight + statusHeight

        let setScale scale =
            selectedScale <- (AppSettings.normalize { appSettings with Scale = scale }).Scale
            viewModel.SelectedScale <- selectedScale
            appSettings <- AppSettings.withScale selectedScale appSettings
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
            match emulationLoop with
            | Some cts ->
                try
                    cts.Cancel()
                with
                | :? ObjectDisposedException -> ()

                emulationLoop <- None
            | None -> ()

            audioOutput.Stop()

        let saveCurrentRam () =
            let session = lock sessionGate (fun () -> currentSession)

            match loadedRom, session with
            | Some rom, Some session ->
                match SaveRam.saveForRom rom.Path (Bus.cartridge session.Bus) with
                | Ok true ->
                    lastSaveStatus <- Some "Save RAM written."
                    showToast "Save RAM written."
                | Ok false -> lastSaveStatus <- None
                | Error message ->
                    lastSaveStatus <- Some $"Save RAM error: {message}"
                    showToast $"Save RAM error: {message}"
            | _ -> ()

        let recordEmulatedFrame () =
            lock perfGate (fun () -> emulatedFrames <- emulatedFrames + 1)

        let resetPerformance () =
            lock perfGate (fun () ->
                displayedFrames <- 0
                emulatedFrames <- 0
                measuredDisplayFps <- 0.0
                measuredEmulationFps <- 0.0
                lastFrameMilliseconds <- 0.0
                lastFpsSample <- DateTime.UtcNow)

        let recordDisplayedFrame () =
            lock perfGate (fun () ->
                displayedFrames <- displayedFrames + 1

                let now = DateTime.UtcNow
                let elapsed = now - lastFpsSample

                if elapsed.TotalSeconds >= 1.0 then
                    measuredDisplayFps <- float displayedFrames / elapsed.TotalSeconds
                    measuredEmulationFps <- float emulatedFrames / elapsed.TotalSeconds
                    displayedFrames <- 0
                    emulatedFrames <- 0
                    lastFpsSample <- now)

        let recordFrameTime elapsedMilliseconds =
            lock perfGate (fun () -> lastFrameMilliseconds <- elapsedMilliseconds)

        let performanceSnapshot () =
            lock perfGate (fun () -> measuredDisplayFps, measuredEmulationFps, lastFrameMilliseconds)

        let formatRuntimeDiagnostics () =
            let displayFps, emulationFps, frameMilliseconds = performanceSnapshot ()
            $"{DebugDisplay.formatPerformance displayFps emulationFps frameMilliseconds}\n{DebugDisplay.formatAudioDiagnostics (audioOutput.Diagnostics())}"

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

        let enqueueFrameAudio (session: Emulator.Session) =
            let diagnosticsBefore = audioOutput.Diagnostics()
            let beforeSteps = session.Steps
            let beforeCycles = session.TotalCycles
            let stopwatch = Stopwatch.StartNew()
            let result = Emulator.runFrame maxStepsPerFrame session
            stopwatch.Stop()
            recordEmulatedFrame ()
            let writeResult = audioOutput.Enqueue(applyVolume result.AudioSamples)
            let diagnosticsAfter = audioOutput.Diagnostics()
            let frame = Interlocked.Increment(&generatedFrameCounter)

            PerfTrace.writeFrame
                perfTrace
                frame
                stopwatch.Elapsed.TotalMilliseconds
                (result.Session.Steps - beforeSteps)
                (result.Session.TotalCycles - beforeCycles)
                result.Session.Cpu.Registers.PC
                result.StopReason
                writeResult.AcceptedFrames
                writeResult.DroppedFrames
                diagnosticsBefore.BufferedFrames
                diagnosticsAfter.BufferedFrames
                diagnosticsAfter.UnderrunFrames
                diagnosticsAfter.DroppedFrames

            lock sessionGate (fun () ->
                pendingFrames.Enqueue result

                while pendingFrames.Count > 30 do
                    pendingFrames.Dequeue() |> ignore)

            result

        let fillAudioLead (token: CancellationToken) (session: Emulator.Session) (initialDiagnostics: AudioHost.AudioDiagnostics) =
            let stopwatch = Stopwatch.StartNew()
            let mutable current = session
            let mutable latest = None
            let mutable diagnostics = initialDiagnostics
            let mutable framesGenerated = 0
            let mutable keepGoing = diagnostics.IsRunning

            while
                keepGoing
                && not token.IsCancellationRequested
                && diagnostics.BufferedFrames < audioBufferTargetFrames do
                let result = enqueueFrameAudio current
                current <- result.Session
                latest <- Some result
                framesGenerated <- framesGenerated + 1
                keepGoing <- result.StopReason = Emulator.FrameCompleted
                diagnostics <- audioOutput.Diagnostics()

            stopwatch.Stop()

            if framesGenerated > 0 then
                recordFrameTime(stopwatch.Elapsed.TotalMilliseconds / float framesGenerated)

            current, latest, keepGoing

        // Reconciles the session's joypad with the latest user input. Runs on whichever
        // thread is about to advance the session, so the session stays single-writer.
        // Bus.setButton only raises the joypad interrupt on a fresh press, so re-applying
        // an unchanged set is a no-op and held buttons never re-trigger.
        let pollControllerInput () =
            let controllers = controllerHost.Poll() |> Seq.toList

            let hasPressedInput (controller: ControllerInput.GamepadSnapshot) =
                controller.Pressed.Count > 0

            let chooseController activeId =
                let current =
                    activeId
                    |> Option.bind (fun id -> controllers |> List.tryFind (fun controller -> controller.Id = id))

                match current with
                | Some controller when hasPressedInput controller -> Some controller
                | Some controller ->
                    controllers
                    |> List.tryFind (fun candidate -> candidate.Id <> controller.Id && hasPressedInput candidate)
                    |> Option.orElse (Some controller)
                | None ->
                    controllers
                    |> List.tryFind hasPressedInput
                    |> Option.orElseWith (fun () -> controllers |> List.tryHead)

            let activeController = lock inputGate (fun () -> chooseController activeControllerId)
            let controllerButtons =
                activeController
                |> Option.map (ControllerInputAdapter.joypadButtonsForSnapshot appSettings.ControllerMapping)
                |> Option.defaultValue Set.empty

            let statusMessage =
                lock inputGate (fun () ->
                    desiredControllerButtons <- controllerButtons

                    match activeControllerId, activeController with
                    | None, Some controller ->
                        activeControllerId <- Some controller.Id
                        Some $"Controller connected: {controller.Name}"
                    | Some _, None ->
                        activeControllerId <- None
                        Some "Controller disconnected."
                    | Some previous, Some controller when previous <> controller.Id ->
                        activeControllerId <- Some controller.Id
                        Some $"Controller connected: {controller.Name}"
                    | _ -> None)

            statusMessage |> Option.iter showToast

        controllerPollTimer.Tick.Add(fun _ ->
            try
                pollControllerInput ()
            with ex ->
                controllerPollTimer.Stop()
                lock inputGate (fun () ->
                    desiredControllerButtons <- Set.empty
                    activeControllerId <- None)
                showToast $"Controller input disabled: {ex.Message}")
        controllerPollTimer.Start()

        let applyInput (session: Emulator.Session) =
            let desired =
                lock inputGate (fun () -> Set.union desiredKeyboardButtons desiredControllerButtons)

            if desired = (Bus.joypad session.Bus).Pressed then
                session
            else
                let bus =
                    InputMapping.allJoypadButtons
                    |> List.fold
                        (fun bus button ->
                            let want = Set.contains button desired
                            let have = Set.contains button (Bus.joypad bus).Pressed
                            if want = have then bus else Bus.setButton button want bus)
                        session.Bus

                { session with Bus = bus }

        let startEmulationLoop () =
            let cts = new CancellationTokenSource()
            let token = cts.Token
            emulationLoop <- Some cts

            let task =
                Task.Run(
                    (fun () ->
                        while not token.IsCancellationRequested do
                            let session = lock sessionGate (fun () -> currentSession)

                            match session with
                            | None -> Thread.Sleep 1
                            | Some session ->
                                let diagnostics = audioOutput.Diagnostics()

                                if diagnostics.IsRunning && not token.IsCancellationRequested then
                                    if diagnostics.BufferedFrames < audioBufferTargetFrames then
                                        let result = enqueueFrameAudio (applyInput session)

                                        lock sessionGate (fun () -> currentSession <- Some result.Session)

                                        if result.StopReason <> Emulator.FrameCompleted then
                                            token.ThrowIfCancellationRequested()
                                            cts.Cancel()
                                            Dispatcher.UIThread.Post(fun () -> stopRunning ())
                                    else
                                        Thread.Sleep 1
                                elif not token.IsCancellationRequested then
                                    Thread.Sleep 1

                                if token.IsCancellationRequested then
                                    token.ThrowIfCancellationRequested()
                            ),
                    token
                )

            task.ContinueWith(fun (_: Task) -> cts.Dispose()) |> ignore

        let primeAudioBuffer () =
            let session = lock sessionGate (fun () -> currentSession)

            match session with
            | None -> ()
            | Some session ->
                let current, _, _ = fillAudioLead CancellationToken.None (applyInput session) (audioOutput.Diagnostics())
                lock sessionGate (fun () -> currentSession <- Some current)

        let resetCurrentRom () =
            match loadedRom with
            | None ->
                showToast "Load a ROM before resetting."
            | Some rom ->
                let wasRunning = isRunning
                saveCurrentRam ()
                stopRunning ()

                let sessionResult = RomSession.createForRom rom
                let session = sessionResult |> Result.toOption

                lock sessionGate (fun () ->
                    currentSession <- session
                    pendingFrames.Clear())
                updateSessionState ()

                resetPerformance ()
                presentFrame (Video.blankFrame ())
                let resetMessage =
                    match sessionResult with
                    | Ok _ -> $"Reset {IO.Path.GetFileName rom.Path}"
                    | Error message -> $"Could not reset ROM: {UserMessage.formatRomStartError message}"

                showToast resetMessage

                viewModel.DebugDetails <-
                    match sessionResult with
                    | Ok _ -> "Reset complete."
                    | Error message -> UserMessage.formatRomStartError message

                if wasRunning && session.IsSome then
                    isRunning <- true
                    viewModel.IsRunning <- true
                    resetPerformance ()
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

                match RomFile.load path with
                | Ok loaded ->
                    let header = loaded.Header
                    let sessionResult = RomSession.createForRom loaded
                    let session = sessionResult |> Result.toOption
                    loadedRom <- Some loaded
                    lock sessionGate (fun () ->
                        currentSession <- session
                        pendingFrames.Clear())
                    updateSessionState ()

                    stopRunning ()
                    presentFrame (Video.blankFrame ())

                    if rememberRecent then
                        appSettings <- AppSettings.rememberRom loaded.Path appSettings
                        refreshMenus ()
                        saveSettings ()

                    let loadMessage =
                        match sessionResult, lastSaveStatus with
                        | Ok _, Some saveMessage -> $"Loaded {IO.Path.GetFileName loaded.Path}  {saveMessage}"
                        | Ok _, None -> $"Loaded {IO.Path.GetFileName loaded.Path}"
                        | Error message, _ -> $"Could not start ROM: {UserMessage.formatRomStartError message}"

                    showToast loadMessage

                    viewModel.RomDetails <-
                        $"Title: {header.Title}\nCGB: {header.CgbSupport}\nSGB: {header.SgbSupport}\nCartridge: {header.CartridgeKind} (0x{header.CartridgeTypeCode:X2})\nROM: {HeaderDisplay.formatRomSize header.RomSizeCode} (0x{header.RomSizeCode:X2})\nRAM: {HeaderDisplay.formatRamSize header.RamSizeCode} (0x{header.RamSizeCode:X2})"

                    viewModel.DebugDetails <-
                        match sessionResult with
                        | Ok _ -> "Ready to run frames."
                        | Error message -> UserMessage.formatRomStartError message
                | Error message ->
                    loadedRom <- None
                    lock sessionGate (fun () ->
                        currentSession <- None
                        pendingFrames.Clear())
                    updateSessionState ()

                    stopRunning ()
                    let displayMessage = UserMessage.formatRomLoadError message
                    showToast $"Could not load ROM: {displayMessage}"
                    viewModel.RomDetails <- displayMessage
                    viewModel.DebugDetails <- "Frame stepping is available after loading a ROM."

        let resumeAfterStateOperation wasRunning =
            if wasRunning then
                isRunning <- true
                viewModel.IsRunning <- true
                resetPerformance ()
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

            match loadedRom, session with
            | Some rom, Some session ->
                match SaveStateFile.saveForRom rom.Path session with
                | Ok() ->
                    showToast "Save state written."
                    viewModel.DebugDetails <- "Save state written."
                | Error message ->
                    let displayMessage = UserMessage.formatSaveStateError message
                    showToast $"Save state error: {displayMessage}"
                    viewModel.DebugDetails <- displayMessage
            | _ ->
                showToast "Load a ROM before saving state."

            resumeAfterStateOperation wasRunning

        let loadStateForCurrentRom () =
            let wasRunning = isRunning
            stopRunning ()

            let session = lock sessionGate (fun () -> currentSession)

            match loadedRom, session with
            | Some rom, Some session ->
                match SaveStateFile.loadForRom rom.Path session with
                | Ok restored ->
                    lock sessionGate (fun () ->
                        currentSession <- Some restored
                        pendingFrames.Clear())
                    resetPerformance ()
                    presentFrame restored.Framebuffer
                    showToast "Save state loaded."
                    viewModel.DebugDetails <- "Save state loaded."
                    updateSessionState ()
                | Error message ->
                    let displayMessage = UserMessage.formatSaveStateError message
                    showToast $"Save state error: {displayMessage}"
                    viewModel.DebugDetails <- displayMessage
            | _ ->
                showToast "Load a ROM before loading state."

            resumeAfterStateOperation wasRunning

        let toggleRunPause () =
            if currentSession.IsNone then
                showToast "Load a ROM before running."
            else
                isRunning <- not isRunning
                viewModel.IsRunning <- isRunning

                if isRunning then
                    resetPerformance ()
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
                let options =
                    FilePickerOpenOptions(
                        Title = "Open Game Boy ROM",
                        AllowMultiple = false,
                        FileTypeFilter = [| romFileType; FilePickerFileTypes.All |]
                    )

                let! files =
                    this.StorageProvider.OpenFilePickerAsync(options)
                    |> Async.AwaitTask

                if files.Count > 0 then
                    let path = files[0].TryGetLocalPath()
                    loadRomPath path true
            }
            |> Async.StartImmediate

        let clearRecentRoms () =
            appSettings <- { appSettings with RecentRoms = [] } |> AppSettings.normalize
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
                let tick = displayTickCounter + 1
                displayTickCounter <- tick
                let tickNow =
                    match perfTrace with
                    | None -> 0.0
                    | Some trace -> trace.Stopwatch.Elapsed.TotalMilliseconds

                let tickDelta =
                    if lastDisplayTickMs = 0.0 then
                        0.0
                    else
                        tickNow - lastDisplayTickMs

                lastDisplayTickMs <- tickNow
                let stopwatch = Stopwatch.StartNew()
                let mutable queueBefore = 0
                let mutable queueAfter = 0
                let frame =
                    lock sessionGate (fun () ->
                        queueBefore <- pendingFrames.Count
                        if pendingFrames.Count > 0 then
                            let frame = pendingFrames.Dequeue()
                            queueAfter <- pendingFrames.Count
                            Some frame
                        else
                            queueAfter <- 0
                            None)

                match frame with
                | Some result ->
                    displayedFrameCounter <- displayedFrameCounter + 1
                    recordDisplayedFrame ()
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
                    displayedFrameCounter
                    queueBefore
                    queueAfter
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
                    { RecentRoms = appSettings.RecentRoms
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
            match InputMapping.mapKey appSettings.KeyboardMapping key with
            | Some button ->
                // Only record intent here; the emulation thread reconciles it into the
                // session via applyInput. Recording the latest state (rather than queuing
                // edits) means a press immediately followed by a release can never be lost.
                lock inputGate (fun () ->
                    desiredKeyboardButtons <-
                        if pressed then
                            desiredKeyboardButtons.Add button
                        else
                            desiredKeyboardButtons.Remove button)

                true
            | _ -> false

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
            let clamped = Math.Clamp(percent, 0, 100)
            lock volumeGate (fun () -> outputVolume <- VolumeControl.gainFromPercent clamped)
            viewModel.VolumePercent <- clamped
            appSettings <- AppSettings.withVolumePercent clamped appSettings
            volumeControl.SetVisual clamped
            saveSettings ()

        let setVolumeFromPointer (args: PointerEventArgs) =
            setVolumePercent (volumeControl.PercentFromPointer args)

        let mutable isDraggingVolume = false

        volumeControl.Slider.PointerPressed.Add(fun args ->
            isDraggingVolume <- true
            volumeControl.Slider.Focus() |> ignore
            args.Pointer.Capture(volumeControl.Slider) |> ignore
            setVolumeFromPointer args
            args.Handled <- true)

        volumeControl.Slider.PointerMoved.Add(fun args ->
            if isDraggingVolume then
                setVolumeFromPointer args
                args.Handled <- true)

        volumeControl.Slider.PointerReleased.Add(fun args ->
            if isDraggingVolume then
                isDraggingVolume <- false
                args.Pointer.Capture(null) |> ignore
                setVolumeFromPointer args
                args.Handled <- true)

        volumeControl.Slider.KeyDown.Add(fun args ->
            let delta =
                match args.Key with
                | Key.Left | Key.Down -> Some -5
                | Key.Right | Key.Up -> Some 5
                | Key.Home -> Some -100
                | Key.End -> Some 100
                | _ -> None

            match delta with
            | Some change ->
                let next =
                    match args.Key with
                    | Key.Home -> 0
                    | Key.End -> 100
                    | _ -> viewModel.VolumePercent + change

                setVolumePercent next
                args.Handled <- true
            | None -> ())

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

        let videoHost =
            Grid(Background = normalVideoHostBackground)

        videoHost.PointerPressed.Add(fun args ->
            let pointer = args.GetCurrentPoint(videoHost)

            if
                not args.Handled
                && pointer.Properties.IsLeftButtonPressed
                && this.WindowState <> WindowState.FullScreen
            then
                this.BeginMoveDrag(args)
                args.Handled <- true)

        applyVideoHostBackground <-
            fun () ->
                videoHost.Background <-
                    if this.WindowState = WindowState.FullScreen then
                        fullscreenVideoHostBackground
                    else
                        normalVideoHostBackground

        videoHost.Children.Add framebuffer |> ignore
        Grid.SetRow(menuBar, 0)
        Grid.SetRow(videoHost, 1)
        Grid.SetRow(statusBar, 2)
        contentGrid.Children.Add menuBar |> ignore
        contentGrid.Children.Add videoHost |> ignore
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
