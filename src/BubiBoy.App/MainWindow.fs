namespace BubiBoy.App

open System
open System.Collections.Generic
open System.Diagnostics
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
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
        this.WindowStartupLocation <- WindowStartupLocation.CenterScreen
        this.Width <- float Hardware.ScreenWidth * 2.0
        this.Height <- float Hardware.ScreenHeight * 2.0 + 32.0
        this.MinWidth <- float Hardware.ScreenWidth
        this.MinHeight <- float Hardware.ScreenHeight
        this.CanResize <- false
        this.Background <- SolidColorBrush(Color.Parse("#F4F5F7"))
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
        let mutable stepFrameHandler = fun () -> ()
        let mutable resetHandler = fun () -> ()
        let mutable clearRecentHandler = fun () -> ()

        let viewModel =
            MainWindowViewModel(
                appSettings.Scale,
                appSettings.IsFloating,
                appSettings.VolumePercent,
                (fun () -> openRomHandler ()),
                (fun () -> toggleRunPauseHandler ()),
                (fun () -> stepFrameHandler ()),
                (fun () -> resetHandler ()),
                (fun () -> clearRecentHandler ())
            )

        this.DataContext <- viewModel
        let mutable selectedScale = appSettings.Scale
        let mutable isFloating = appSettings.IsFloating
        let volumeGainFromPercent percent =
            let normalized = single (Math.Clamp(percent, 0, 100)) / 100.0f
            normalized * normalized

        let mutable outputVolume = volumeGainFromPercent appSettings.VolumePercent
        let sessionGate = obj ()
        let perfGate = obj ()
        let volumeGate = obj ()
        let inputGate = obj ()
        // The authoritative set of currently-held buttons. Key events only update this
        // (cheaply, under inputGate); the emulation thread reconciles it into the live
        // session at the start of each frame. This keeps the emulation thread the sole
        // mutator of the session, so a frame's write-back can no longer clobber a key
        // press/release that arrived while that frame was running.
        let mutable desiredButtons: Set<Joypad.Button> = Set.empty
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

        let applyVolume (samples: Apu.Sample[]) =
            let volume = lock volumeGate (fun () -> outputVolume)

            if volume <> 1.0f then
                // samples is a freshly drained buffer owned by this frame result, so we
                // scale it in place instead of allocating a new array each frame.
                for index in 0 .. samples.Length - 1 do
                    let sample = samples[index]
                    samples[index] <- { Left = sample.Left * volume; Right = sample.Right * volume }

            samples

        let runIndicator =
            Ellipse(
                Width = 9.0,
                Height = 9.0,
                Fill = SolidColorBrush(Color.Parse("#8692A3")),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            )

        let runIndicatorHost =
            Border(
                Width = 28.0,
                Height = 28.0,
                Child = runIndicator,
                VerticalAlignment = VerticalAlignment.Center
            )

        let setRunIndicator running =
            runIndicator.Fill <-
                if running then
                    SolidColorBrush(Color.Parse("#18A058"))
                else
                    SolidColorBrush(Color.Parse("#8692A3"))

            ToolTip.SetTip(runIndicatorHost, if running then "Running" else "Paused")

        setRunIndicator false

        viewModel.PropertyChanged.Add(fun args ->
            if args.PropertyName = "IsRunning" then
                setRunIndicator viewModel.IsRunning)

        let volumeIcon =
            Path(
                Width = 14.4,
                Height = 14.4,
                Data =
                    Geometry.Parse(
                        "M2,8 L6,8 L11,3 L11,21 L6,16 L2,16 Z M14,8 C15.4,9.3 16.2,10.7 16.2,12 C16.2,13.3 15.4,14.7 14,16 L15.5,17.6 C17.4,15.8 18.5,14 18.5,12 C18.5,10 17.4,8.2 15.5,6.4 Z"
                    ),
                Fill = SolidColorBrush(Color.Parse("#5F6B7A")),
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            )

        ToolTip.SetTip(volumeIcon, "Volume")

        let volumeIconHost =
            Border(
                Width = 14.4,
                Height = 24.0,
                Child = volumeIcon,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            )

        let volumeSliderWidth = 88.0
        let volumeSliderHeight = 24.0
        let volumeThumbSize = 12.0
        let volumeTrackHeight = 4.0
        let volumeTrackLeft = volumeThumbSize / 2.0
        let volumeTrackWidth = volumeSliderWidth - volumeThumbSize

        let volumeSlider =
            Canvas(
                Width = volumeSliderWidth,
                Height = volumeSliderHeight,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Focusable = true,
                ClipToBounds = false,
                VerticalAlignment = VerticalAlignment.Center
            )

        let volumeTrack =
            Border(
                Width = volumeTrackWidth,
                Height = volumeTrackHeight,
                Background = SolidColorBrush(Color.Parse("#CBD2DC")),
                CornerRadius = CornerRadius(volumeTrackHeight / 2.0)
            )

        let volumeFill =
            Border(
                Height = volumeTrackHeight,
                Background = SolidColorBrush(Color.Parse("#178BFF")),
                CornerRadius = CornerRadius(volumeTrackHeight / 2.0)
            )

        let volumeThumb =
            Ellipse(
                Width = volumeThumbSize,
                Height = volumeThumbSize,
                Fill = SolidColorBrush(Color.Parse("#178BFF"))
            )

        let volumeTrackTop = (volumeSliderHeight - volumeTrackHeight) / 2.0
        let volumeThumbTop = (volumeSliderHeight - volumeThumbSize) / 2.0

        Canvas.SetLeft(volumeTrack, volumeTrackLeft)
        Canvas.SetTop(volumeTrack, volumeTrackTop)
        Canvas.SetLeft(volumeFill, volumeTrackLeft)
        Canvas.SetTop(volumeFill, volumeTrackTop)
        Canvas.SetTop(volumeThumb, volumeThumbTop)
        volumeSlider.Children.Add volumeTrack |> ignore
        volumeSlider.Children.Add volumeFill |> ignore
        volumeSlider.Children.Add volumeThumb |> ignore

        let updateVolumeSliderVisual percent =
            let clamped = Math.Clamp(percent, 0, 100)
            let fraction = float clamped / 100.0
            let centerX = volumeTrackLeft + volumeTrackWidth * fraction
            volumeFill.Width <- volumeTrackWidth * fraction
            Canvas.SetLeft(volumeThumb, centerX - volumeThumbSize / 2.0)

        updateVolumeSliderVisual appSettings.VolumePercent

        ToolTip.SetTip(volumeSlider, "Volume")

        let volumeHost =
            Grid(
                ColumnDefinitions = ColumnDefinitions("14.4,8,88"),
                Width = 110.4,
                Height = 24.0,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            )

        Grid.SetColumn(volumeIconHost, 0)
        Grid.SetColumn(volumeSlider, 2)
        volumeHost.Children.Add volumeIconHost |> ignore
        volumeHost.Children.Add volumeSlider |> ignore

        let statusBar =
            Border(
                Height = 32.0,
                Background = SolidColorBrush(Color.Parse("#F8F9FB")),
                BorderBrush = SolidColorBrush(Color.Parse("#C8CED8")),
                BorderThickness = Thickness(0.0, 1.0, 0.0, 0.0),
                Padding = Thickness(6.0, 0.0, 16.0, 0.0),
                IsVisible = not isFloating
            )

        let statusGrid =
            Grid(
                ColumnDefinitions = ColumnDefinitions("Auto,*,Auto"),
                RowDefinitions = RowDefinitions("32"),
                VerticalAlignment = VerticalAlignment.Center
            )

        Grid.SetColumn(runIndicatorHost, 0)
        Grid.SetColumn(volumeHost, 2)
        Grid.SetRow(runIndicatorHost, 0)
        Grid.SetRow(volumeHost, 0)
        statusGrid.Children.Add runIndicatorHost |> ignore
        statusGrid.Children.Add volumeHost |> ignore
        statusBar.Child <- statusGrid

        let toastText =
            TextBlock(
                FontSize = 13.0,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320.0
            )

        let toast =
            Border(
                Child = toastText,
                Background = SolidColorBrush(Color.Parse("#263448")),
                CornerRadius = CornerRadius(6.0),
                Padding = Thickness(12.0, 8.0),
                Margin = Thickness(12.0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsVisible = false
            )

        let toastTimer = DispatcherTimer(Interval = TimeSpan.FromSeconds(3.0))

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
                FontFamily = FontFamily("Menlo, Consolas, monospace"),
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
                toastText.Text <- message
                toast.IsVisible <- true
                toastTimer.Stop()
                toastTimer.Start()
            else
                lastSaveStatus <- Some message

        notify <- showToast

        toastTimer.Tick.Add(fun _ ->
            toastTimer.Stop()
            toast.IsVisible <- false)

        let openInputMapping () =
            task {
                let! result = InputMappingWindow.Show(this, appSettings.KeyboardMapping)

                match result with
                | Some keyboardMapping ->
                    appSettings <- AppSettings.withKeyboardMapping keyboardMapping appSettings
                    lock inputGate (fun () -> desiredButtons <- Set.empty)
                    saveSettings ()
                    showToast "Input mapping saved."
                | None -> ()
            }
            |> ignore

        settingsLoadError
        |> Option.iter (fun message -> showToast $"Settings error: {message}")

        let isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        let mutable refreshMenus = fun () -> ()
        let mutable updateContentRows = fun () -> ()

        let menuBar = Menu()
        menuBar.IsVisible <- not isMacOS && not isFloating

        let applyWindowChrome () =
            if isFloating then
                if this.WindowState = WindowState.FullScreen then
                    this.WindowState <- WindowState.Normal

                this.WindowDecorations <- WindowDecorations.None
                this.CanResize <- false
                statusBar.IsVisible <- false
                statusBar.Height <- 0.0
                menuBar.IsVisible <- false
                toast.IsVisible <- false
            else
                this.WindowDecorations <- WindowDecorations.Full
                this.CanResize <- false
                statusBar.IsVisible <- true
                statusBar.Height <- 32.0
                menuBar.IsVisible <- not isMacOS

            updateContentRows ()

        let applySelectedScale resizeWindow =
            let videoWidth = float Hardware.ScreenWidth * float selectedScale
            let videoHeight = float Hardware.ScreenHeight * float selectedScale
            framebuffer.Width <- videoWidth
            framebuffer.Height <- videoHeight
            framebufferImage.Width <- videoWidth
            framebufferImage.Height <- videoHeight

            if resizeWindow && this.WindowState <> WindowState.FullScreen then
                let menuHeight =
                    if isMacOS || isFloating then 0.0 else 28.0

                let statusHeight =
                    if isFloating then 0.0 else 32.0

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
            appSettings <- AppSettings.withFloating enabled appSettings
            applyWindowChrome ()
            applySelectedScale true
            refreshMenus ()
            saveSettings ()

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
        let applyInput (session: Emulator.Session) =
            let desired = lock inputGate (fun () -> desiredButtons)

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

        let runOneFrame () =
            let session = lock sessionGate (fun () -> currentSession)

            match session with
            | None ->
                viewModel.DebugDetails <- "Load a ROM before running frames."
                stopRunning ()
            | Some session ->
                let stopwatch = Stopwatch.StartNew()
                let result = Emulator.runFrame maxStepsPerFrame (applyInput session)
                stopwatch.Stop()
                recordEmulatedFrame ()
                lock sessionGate (fun () -> currentSession <- Some result.Session)
                recordFrameTime stopwatch.Elapsed.TotalMilliseconds
                recordDisplayedFrame ()
                updateFrame result

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
                    | Error message -> $"Could not reset ROM: {message}"

                showToast resetMessage

                viewModel.DebugDetails <-
                    match sessionResult with
                    | Ok _ -> "Reset complete."
                    | Error message -> message

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
                        | Error message, _ -> $"Could not start ROM: {message}"

                    showToast loadMessage

                    viewModel.RomDetails <-
                        $"Title: {header.Title}\nCGB: {header.CgbSupport}\nSGB: {header.SgbSupport}\nCartridge: {header.CartridgeKind} (0x{header.CartridgeTypeCode:X2})\nROM: {HeaderDisplay.formatRomSize header.RomSizeCode} (0x{header.RomSizeCode:X2})\nRAM: {HeaderDisplay.formatRamSize header.RamSizeCode} (0x{header.RamSizeCode:X2})"

                    viewModel.DebugDetails <-
                        match sessionResult with
                        | Ok _ -> "Ready to run frames."
                        | Error message -> message
                | Error message ->
                    loadedRom <- None
                    lock sessionGate (fun () ->
                        currentSession <- None
                        pendingFrames.Clear())
                    updateSessionState ()

                    stopRunning ()
                    showToast $"Could not load ROM: {message}"
                    viewModel.RomDetails <- message
                    viewModel.DebugDetails <- "Frame stepping is available after loading a ROM."

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
        stepFrameHandler <- runOneFrame
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

        let platformModifier =
            if isMacOS then KeyModifiers.Meta else KeyModifiers.Control

        let gesture key modifiers =
            KeyGesture(key, modifiers)

        let nativeItem header key modifiers action =
            let item = NativeMenuItem(header)
            item.Gesture <- gesture key modifiers
            item.Click.Add(fun _ -> action ())
            item

        let nativePlain header action =
            let item = NativeMenuItem(header)
            item.Click.Add(fun _ -> action ())
            item

        let nativeCommandItem header key modifiers command =
            let item = NativeMenuItem(header)
            item.Gesture <- gesture key modifiers
            item.Command <- command
            item

        let nativePlainCommandItem header command =
            let item = NativeMenuItem(header)
            item.Command <- command
            item

        let menuItem header key modifiers action =
            let item = MenuItem(Header = header)
            item.InputGesture <- gesture key modifiers
            item.Click.Add(fun _ -> action ())
            item

        let plainMenuItem header action =
            let item = MenuItem(Header = header)
            item.Click.Add(fun _ -> action ())
            item

        let commandMenuItem header key modifiers command =
            let item = MenuItem(Header = header)
            item.InputGesture <- gesture key modifiers
            item.Command <- command
            item

        let plainCommandMenuItem header command =
            MenuItem(Header = header, Command = command)

        let nativeOpenRecentMenu = NativeMenu()
        let nativeOpenRecentItem = NativeMenuItem("Open Recent")
        nativeOpenRecentItem.Menu <- nativeOpenRecentMenu
        let nativeClearRecentItem = nativePlainCommandItem "Clear Recent" viewModel.ClearRecentCommand
        let nativeRunPauseItem = nativeCommandItem "Run" Key.Space KeyModifiers.None viewModel.RunPauseCommand
        let nativeStepFrameItem = nativeCommandItem "Step Frame" Key.F10 KeyModifiers.None viewModel.StepFrameCommand
        let nativeResetItem = nativeCommandItem "Reset" Key.R platformModifier viewModel.ResetCommand
        let nativeInputMappingItem = nativePlain "Input Mapping..." openInputMapping
        let nativeFullscreenItem = nativeItem "Full Screen" Key.F platformModifier (fun () ->
            if isFloating then
                setFloating false

            this.WindowState <-
                if this.WindowState = WindowState.FullScreen then
                    WindowState.Normal
                else
                    WindowState.FullScreen

            refreshMenus ())
        let nativeFloatingItem = nativeItem "Floating Mode" Key.F (platformModifier ||| KeyModifiers.Shift) (fun () -> setFloating (not isFloating))
        let nativeScaleItems =
            [ 1, nativeItem "Scale x1" Key.D1 platformModifier (fun () -> setScale 1)
              2, nativeItem "Scale x2" Key.D2 platformModifier (fun () -> setScale 2)
              4, nativeItem "Scale x4" Key.D4 platformModifier (fun () -> setScale 4)
              8, nativeItem "Scale x8" Key.D8 platformModifier (fun () -> setScale 8) ]

        let openRecentMenu = MenuItem(Header = "Open Recent")
        let clearRecentItem = plainCommandMenuItem "Clear Recent" viewModel.ClearRecentCommand
        let runPauseItem = commandMenuItem "Run" Key.Space KeyModifiers.None viewModel.RunPauseCommand
        let stepFrameItem = commandMenuItem "Step Frame" Key.F10 KeyModifiers.None viewModel.StepFrameCommand
        let resetMenuItem = commandMenuItem "Reset" Key.R platformModifier viewModel.ResetCommand
        let inputMappingItem = plainMenuItem "Input Mapping..." openInputMapping
        let fullscreenItem = menuItem "Full Screen" Key.F platformModifier (fun () ->
            if isFloating then
                setFloating false

            this.WindowState <-
                if this.WindowState = WindowState.FullScreen then
                    WindowState.Normal
                else
                    WindowState.FullScreen

            refreshMenus ())
        let floatingItem = menuItem "Floating Mode" Key.F (platformModifier ||| KeyModifiers.Shift) (fun () -> setFloating (not isFloating))
        let scaleItems =
            [ 1, menuItem "Scale x1" Key.D1 platformModifier (fun () -> setScale 1)
              2, menuItem "Scale x2" Key.D2 platformModifier (fun () -> setScale 2)
              4, menuItem "Scale x4" Key.D4 platformModifier (fun () -> setScale 4)
              8, menuItem "Scale x8" Key.D8 platformModifier (fun () -> setScale 8) ]

        let rebuildRecentMenus () =
            nativeOpenRecentMenu.Items.Clear()
            openRecentMenu.Items.Clear()

            if List.isEmpty appSettings.RecentRoms then
                let nativeEmpty = NativeMenuItem("(Empty)")
                nativeEmpty.IsEnabled <- false
                nativeOpenRecentMenu.Items.Add nativeEmpty |> ignore
                let empty = MenuItem(Header = "(Empty)", IsEnabled = false)
                openRecentMenu.Items.Add empty |> ignore
            else
                for path in appSettings.RecentRoms do
                    let label = IO.Path.GetFileName path
                    let nativeRecent = nativePlain label (fun () -> loadRomPath path true)
                    nativeRecent.ToolTip <- path
                    nativeOpenRecentMenu.Items.Add nativeRecent |> ignore
                    let recent = plainMenuItem label (fun () -> loadRomPath path true)
                    openRecentMenu.Items.Add recent |> ignore

            nativeClearRecentItem.IsEnabled <- not (List.isEmpty appSettings.RecentRoms)
            clearRecentItem.IsEnabled <- nativeClearRecentItem.IsEnabled

        let updateMenuState () =
            nativeRunPauseItem.Header <- viewModel.RunPauseHeader
            runPauseItem.Header <- viewModel.RunPauseHeader
            nativeRunPauseItem.IsEnabled <- viewModel.HasSession
            runPauseItem.IsEnabled <- viewModel.HasSession
            nativeStepFrameItem.IsEnabled <- viewModel.HasSession && not viewModel.IsRunning
            stepFrameItem.IsEnabled <- viewModel.HasSession && not viewModel.IsRunning
            nativeResetItem.IsEnabled <- viewModel.HasLoadedRom
            resetMenuItem.IsEnabled <- viewModel.HasLoadedRom
            nativeFullscreenItem.IsChecked <- this.WindowState = WindowState.FullScreen
            fullscreenItem.IsChecked <- this.WindowState = WindowState.FullScreen
            nativeFloatingItem.IsChecked <- viewModel.IsFloating
            floatingItem.IsChecked <- viewModel.IsFloating

            for scale, item in nativeScaleItems do
                item.IsChecked <- (scale = viewModel.SelectedScale)

            for scale, item in scaleItems do
                item.IsChecked <- (scale = viewModel.SelectedScale)

        refreshMenus <-
            fun () ->
                rebuildRecentMenus ()
                updateMenuState ()

        this.GetObservable(Window.WindowStateProperty).Subscribe(fun _ -> refreshMenus ())
        |> ignore

        let nativeMenu = NativeMenu()
        let nativeFileMenu = NativeMenuItem("File")
        let nativeFileSubmenu = NativeMenu()
        nativeFileSubmenu.Items.Add(nativeCommandItem "Open ROM..." Key.O platformModifier viewModel.OpenRomCommand) |> ignore
        nativeFileSubmenu.Items.Add nativeOpenRecentItem |> ignore
        nativeFileSubmenu.Items.Add nativeClearRecentItem |> ignore
        nativeFileMenu.Menu <- nativeFileSubmenu
        let nativeEmulationMenu = NativeMenuItem("Emulation")
        let nativeEmulationSubmenu = NativeMenu()
        nativeEmulationSubmenu.Items.Add nativeRunPauseItem |> ignore
        nativeEmulationSubmenu.Items.Add nativeStepFrameItem |> ignore
        nativeEmulationSubmenu.Items.Add nativeResetItem |> ignore
        nativeEmulationSubmenu.Items.Add(NativeMenuItemSeparator()) |> ignore
        nativeEmulationSubmenu.Items.Add nativeInputMappingItem |> ignore
        nativeEmulationMenu.Menu <- nativeEmulationSubmenu
        let nativeViewMenu = NativeMenuItem("View")
        let nativeViewSubmenu = NativeMenu()

        for _, item in nativeScaleItems do
            nativeViewSubmenu.Items.Add item |> ignore

        nativeViewSubmenu.Items.Add(NativeMenuItemSeparator()) |> ignore
        nativeViewSubmenu.Items.Add nativeFullscreenItem |> ignore
        nativeViewSubmenu.Items.Add nativeFloatingItem |> ignore
        nativeViewMenu.Menu <- nativeViewSubmenu

        nativeMenu.Items.Add nativeFileMenu |> ignore
        nativeMenu.Items.Add nativeEmulationMenu |> ignore
        nativeMenu.Items.Add nativeViewMenu |> ignore
        NativeMenu.SetMenu(this, nativeMenu)

        let fileMenu = MenuItem(Header = "File")
        fileMenu.Items.Add(commandMenuItem "Open ROM..." Key.O platformModifier viewModel.OpenRomCommand) |> ignore
        fileMenu.Items.Add openRecentMenu |> ignore
        fileMenu.Items.Add clearRecentItem |> ignore
        fileMenu.Items.Add(Separator()) |> ignore
        fileMenu.Items.Add(plainMenuItem "Quit" (fun () -> this.Close())) |> ignore
        let emulationMenu = MenuItem(Header = "Emulation")
        emulationMenu.Items.Add runPauseItem |> ignore
        emulationMenu.Items.Add stepFrameItem |> ignore
        emulationMenu.Items.Add resetMenuItem |> ignore
        emulationMenu.Items.Add(Separator()) |> ignore
        emulationMenu.Items.Add inputMappingItem |> ignore
        let viewMenu = MenuItem(Header = "View")

        for _, item in scaleItems do
            viewMenu.Items.Add item |> ignore

        viewMenu.Items.Add(Separator()) |> ignore
        viewMenu.Items.Add fullscreenItem |> ignore
        viewMenu.Items.Add floatingItem |> ignore
        let helpMenu = MenuItem(Header = "Help")
        helpMenu.Items.Add(plainMenuItem "About BubiBoy" this.ShowAbout) |> ignore
        menuBar.Items.Add fileMenu |> ignore
        menuBar.Items.Add emulationMenu |> ignore
        menuBar.Items.Add viewMenu |> ignore

        if not isMacOS then
            menuBar.Items.Add helpMenu |> ignore

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
                    desiredButtons <-
                        if pressed then
                            desiredButtons.Add button
                        else
                            desiredButtons.Remove button)

                true
            | _ -> false

        this.KeyDown.Add(fun args ->
            if args.Key = Key.Space then
                executeCommand viewModel.RunPauseCommand
                args.Handled <- true
            elif updateButtonState args.Key true then
                args.Handled <- true)

        this.KeyUp.Add(fun args ->
            if updateButtonState args.Key false then
                args.Handled <- true)

        let setVolumePercent percent =
            let clamped = Math.Clamp(percent, 0, 100)
            lock volumeGate (fun () -> outputVolume <- volumeGainFromPercent clamped)
            viewModel.VolumePercent <- clamped
            appSettings <- AppSettings.withVolumePercent clamped appSettings
            updateVolumeSliderVisual clamped
            saveSettings ()

        let setVolumeFromPointer (args: PointerEventArgs) =
            let position = args.GetPosition(volumeSlider)
            let fraction = Math.Clamp((position.X - volumeTrackLeft) / volumeTrackWidth, 0.0, 1.0)
            setVolumePercent (int (Math.Round(fraction * 100.0)))

        let mutable isDraggingVolume = false

        volumeSlider.PointerPressed.Add(fun args ->
            isDraggingVolume <- true
            volumeSlider.Focus() |> ignore
            args.Pointer.Capture(volumeSlider) |> ignore
            setVolumeFromPointer args
            args.Handled <- true)

        volumeSlider.PointerMoved.Add(fun args ->
            if isDraggingVolume then
                setVolumeFromPointer args
                args.Handled <- true)

        volumeSlider.PointerReleased.Add(fun args ->
            if isDraggingVolume then
                isDraggingVolume <- false
                args.Pointer.Capture(null) |> ignore
                setVolumeFromPointer args
                args.Handled <- true)

        volumeSlider.KeyDown.Add(fun args ->
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
        this.Closed.Add(fun _ -> PerfTrace.close perfTrace)

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
            Grid(Background = SolidColorBrush(Color.Parse("#F4F5F7")))

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
        overlay.Children.Add toast |> ignore

        this.Content <- overlay

    member this.ShowAbout() =
        let version =
            this.GetType().Assembly.GetName().Version
            |> Option.ofObj
            |> Option.map string
            |> Option.defaultValue "development"

        let dialog = AboutWindow(version)
        dialog.ShowDialog(this) |> ignore
