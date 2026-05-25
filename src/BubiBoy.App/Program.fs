namespace BubiBoy.App

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia.Platform.Storage
open Avalonia.Styling
open Avalonia.Themes.Fluent
open Avalonia.Threading
open BubiBoy.Audio
open BubiBoy.Core
open BubiBoy.IO
open System.Runtime.InteropServices

module private HeaderDisplay =
    let private formatByteSize bytes =
        if bytes = 0 then
            "none"
        elif bytes % (1024 * 1024) = 0 then
            $"{bytes / (1024 * 1024)} MiB"
        elif bytes % 1024 = 0 then
            $"{bytes / 1024} KiB"
        else
            $"{bytes} bytes"

    let private formatBanks banks =
        match banks with
        | 0 -> "0 banks"
        | 1 -> "1 bank"
        | _ -> $"{banks} banks"

    let formatRomSize code =
        match Cartridge.romSizeFromCode code with
        | Ok size -> $"{formatByteSize size.Bytes} / {formatBanks size.Banks}"
        | Error _ -> $"unknown (0x{code:X2})"

    let formatRamSize code =
        match Cartridge.ramSizeFromCode code with
        | Ok size -> $"{formatByteSize size.Bytes} / {formatBanks size.Banks}"
        | Error _ -> $"unknown (0x{code:X2})"

module private DebugDisplay =
    let private formatStopReason reason =
        match reason with
        | Emulator.StepLimitReached -> "step limit reached"
        | Emulator.FrameCompleted -> "frame completed"
        | Emulator.Halted -> "CPU halted"
        | Emulator.UnsupportedOpcode(opcode, pc) -> $"unsupported opcode 0x{opcode:X2} at PC 0x{pc:X4}"

    let formatRunResult (result: Emulator.RunResult) =
        let registers = result.Session.Cpu.Registers

        $"Run stopped: {formatStopReason result.StopReason}\nSteps: {result.Session.Steps}    Cycles: {result.Session.TotalCycles}\nPC: 0x{registers.PC:X4}    SP: 0x{registers.SP:X4}\nA: 0x{registers.A:X2}  F: 0x{registers.F:X2}  B: 0x{registers.B:X2}  C: 0x{registers.C:X2}  D: 0x{registers.D:X2}  E: 0x{registers.E:X2}  H: 0x{registers.H:X2}  L: 0x{registers.L:X2}"

    let formatFrameResult (result: Emulator.FrameResult) =
        let registers = result.Session.Cpu.Registers

        $"Frame stopped: {formatStopReason result.StopReason}\nSteps: {result.Session.Steps}    Cycles: {result.Session.TotalCycles}\nPC: 0x{registers.PC:X4}    SP: 0x{registers.SP:X4}\nA: 0x{registers.A:X2}  F: 0x{registers.F:X2}  B: 0x{registers.B:X2}  C: 0x{registers.C:X2}  D: 0x{registers.D:X2}  E: 0x{registers.E:X2}  H: 0x{registers.H:X2}  L: 0x{registers.L:X2}"

    let formatAudioDiagnostics (diagnostics: AudioHost.AudioDiagnostics) =
        $"Audio buffered: {diagnostics.BufferedFrames} frames    underrun: {diagnostics.UnderrunFrames}    dropped: {diagnostics.DroppedFrames}"

    let formatPerformance displayFps emulationFps frameMilliseconds =
        $"FPS: display {displayFps:F1}    emu {emulationFps:F1}    frame {frameMilliseconds:F2} ms"

module private FramebufferBitmap =
    let private toBgraBytes (pixels: uint32[]) =
        let bytes = Array.zeroCreate<byte> (pixels.Length * 4)

        for index in 0 .. pixels.Length - 1 do
            let color = pixels[index]
            let offset = index * 4
            bytes[offset] <- byte (color &&& 0x000000FFu)
            bytes[offset + 1] <- byte ((color >>> 8) &&& 0x000000FFu)
            bytes[offset + 2] <- byte ((color >>> 16) &&& 0x000000FFu)
            bytes[offset + 3] <- byte ((color >>> 24) &&& 0x000000FFu)

        bytes

    let create (pixels: uint32[]) =
        let bitmap =
            new WriteableBitmap(
                PixelSize(Hardware.ScreenWidth, Hardware.ScreenHeight),
                Vector(96.0, 96.0),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul
            )

        let bytes = toBgraBytes pixels

        use locked = bitmap.Lock()
        let rowBytes = Hardware.ScreenWidth * 4

        if locked.RowBytes = rowBytes then
            Marshal.Copy(bytes, 0, locked.Address, bytes.Length)
        else
            for y in 0 .. Hardware.ScreenHeight - 1 do
                Marshal.Copy(bytes, y * rowBytes, IntPtr.Add(locked.Address, y * locked.RowBytes), rowBytes)

        bitmap

type MainWindow() as this =
    inherit Window()

    do
        this.Title <- "BubiBoy"
        this.Width <- 640.0
        this.Height <- 620.0
        this.MinWidth <- 480.0
        this.MinHeight <- 560.0
        this.Background <- SolidColorBrush(Color.Parse("#F3F6FA"))
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
                Background = SolidColorBrush(Color.Parse("#E7F4D6")),
                BorderBrush = SolidColorBrush(Color.Parse("#AAB8C8")),
                BorderThickness = Thickness(1.0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            )

        let framebufferImage =
            Image(
                Width = float Hardware.ScreenWidth * 2.0,
                Height = float Hardware.ScreenHeight * 2.0,
                Stretch = Stretch.Fill,
                Source = FramebufferBitmap.create (Video.blankFrame ())
            )

        framebuffer.Child <- framebufferImage

        let mutable loadedRom: RomFile.LoadedRom option = None
        let mutable currentSession: Emulator.Session option = None
        let mutable latestFrame: Emulator.FrameResult option = None
        let mutable emulationLoop: CancellationTokenSource option = None
        let mutable isRunning = false
        let mutable lastSaveStatus: string option = None
        let mutable displayedFrames = 0
        let mutable emulatedFrames = 0
        let mutable measuredDisplayFps = 0.0
        let mutable measuredEmulationFps = 0.0
        let mutable lastFrameMilliseconds = 0.0
        let mutable lastFpsSample = DateTime.UtcNow
        let sessionGate = obj ()
        let perfGate = obj ()
        let nominalFrameMilliseconds = 1000.0 * float Hardware.CyclesPerFrame / float Hardware.DmgClockHz
        let audioFramesPerVideoFrame =
            int (Math.Round(float AudioHost.defaultFormat.SampleRate * float Hardware.CyclesPerFrame / float Hardware.DmgClockHz))

        let audioBufferLowWatermarkFrames = audioFramesPerVideoFrame * 12
        let audioBufferHighWatermarkFrames = audioFramesPerVideoFrame * 24
        let audioDevice =
            match Miniaudio.tryCreateDevice AudioHost.defaultFormat AudioHost.defaultFormat.SampleRate with
            | Ok device -> device :> AudioHost.AudioDevice
            | Error _ -> AudioHost.createBufferedDevice AudioHost.defaultFormat.SampleRate :> AudioHost.AudioDevice

        let audioOutput = audioDevice

        let openButton =
            Button(
                Content = "Open ROM",
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = Thickness(18.0, 8.0),
                Background = SolidColorBrush(Color.Parse("#FFFFFF")),
                Foreground = SolidColorBrush(Color.Parse("#17202B")),
                BorderBrush = SolidColorBrush(Color.Parse("#AAB8C8")),
                BorderThickness = Thickness(1.0)
            )

        let stepFrameButton =
            Button(
                Content = "Step Frame",
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = Thickness(18.0, 8.0),
                Background = SolidColorBrush(Color.Parse("#17202B")),
                Foreground = Brushes.White,
                BorderBrush = SolidColorBrush(Color.Parse("#17202B")),
                BorderThickness = Thickness(1.0),
                IsEnabled = false
            )

        let startStopButton =
            Button(
                Content = "Start",
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = Thickness(18.0, 8.0),
                Background = SolidColorBrush(Color.Parse("#FFFFFF")),
                Foreground = SolidColorBrush(Color.Parse("#17202B")),
                BorderBrush = SolidColorBrush(Color.Parse("#AAB8C8")),
                BorderThickness = Thickness(1.0),
                IsEnabled = false
            )

        let status =
            TextBlock(
                Text = "No ROM loaded.",
                FontSize = 13.0,
                Foreground = SolidColorBrush(Color.Parse("#566579")),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Width = 560.0,
                Height = 18.0
            )

        let romDetails =
            TextBlock(
                Text = "Choose a .gb or .gbc file to inspect its cartridge header.",
                FontSize = 13.0,
                Foreground = SolidColorBrush(Color.Parse("#425166")),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 560.0,
                Height = 50.0
            )

        let debugDetails =
            TextBlock(
                Text = "CPU debug run is available after loading a ROM.",
                FontFamily = FontFamily("Menlo, Consolas, monospace"),
                FontSize = 12.0,
                Foreground = SolidColorBrush(Color.Parse("#263448")),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = 560.0,
                Height = 72.0
            )

        let romFileType =
            FilePickerFileType(
                "Game Boy ROM",
                Patterns = [| "*.gb"; "*.gbc" |],
                MimeTypes = [| "application/octet-stream" |]
            )

        let stopRunning () =
            isRunning <- false
            match emulationLoop with
            | Some cts ->
                try
                    cts.Cancel()
                with
                | :? ObjectDisposedException -> ()

                emulationLoop <- None
            | None -> ()

            startStopButton.Content <- "Start"
            audioOutput.Stop()

        let saveCurrentRam () =
            let session = lock sessionGate (fun () -> currentSession)

            match loadedRom, session with
            | Some rom, Some session ->
                match SaveRam.saveForRom rom.Path (Bus.cartridge session.Bus) with
                | Ok true -> lastSaveStatus <- Some "Save RAM written."
                | Ok false -> lastSaveStatus <- None
                | Error message -> lastSaveStatus <- Some $"Save RAM error: {message}"
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
            framebufferImage.Source <- FramebufferBitmap.create result.Framebuffer
            debugDetails.Text <-
                if isRunning then
                    formatRuntimeDiagnostics ()
                else
                    $"{DebugDisplay.formatFrameResult result}\n{formatRuntimeDiagnostics ()}"

            match result.StopReason with
            | Emulator.FrameCompleted -> ()
            | _ -> stopRunning ()

        let enqueueFrameAudio (session: Emulator.Session) =
            let result = Emulator.runFrame 20_000 session
            recordEmulatedFrame ()
            audioOutput.Enqueue result.AudioSamples |> ignore
            result

        let waitForAudioHeadroom (token: CancellationToken) =
            let mutable waiting = true

            while waiting && not token.IsCancellationRequested do
                let diagnostics = audioOutput.Diagnostics()

                if (not diagnostics.IsRunning) || diagnostics.BufferedFrames <= audioBufferHighWatermarkFrames then
                    waiting <- false
                else
                    Thread.Sleep 1

        let paceFrame elapsedMilliseconds bufferedFrames =
            if bufferedFrames >= audioBufferLowWatermarkFrames then
                let remaining = nominalFrameMilliseconds - elapsedMilliseconds

                if remaining >= 1.0 then
                    Thread.Sleep(TimeSpan.FromMilliseconds remaining)

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
                                waitForAudioHeadroom token
                                let stopwatch = Stopwatch.StartNew()
                                let result = Emulator.runFrame 20_000 session
                                stopwatch.Stop()

                                recordEmulatedFrame ()
                                recordFrameTime stopwatch.Elapsed.TotalMilliseconds
                                audioOutput.Enqueue result.AudioSamples |> ignore

                                lock sessionGate (fun () ->
                                    currentSession <- Some result.Session
                                    latestFrame <- Some result)

                                if result.StopReason <> Emulator.FrameCompleted then
                                    token.ThrowIfCancellationRequested()
                                    cts.Cancel()
                                    Dispatcher.UIThread.Post(fun () -> stopRunning ())

                                let diagnostics = audioOutput.Diagnostics()

                                if diagnostics.BufferedFrames > AudioHost.defaultFormat.SampleRate / 2 then
                                    Thread.Sleep 1

                                paceFrame stopwatch.Elapsed.TotalMilliseconds diagnostics.BufferedFrames),
                    token
                )

            task.ContinueWith(fun (_: Task) -> cts.Dispose()) |> ignore

        let runOneFrame () =
            let session = lock sessionGate (fun () -> currentSession)

            match session with
            | None ->
                debugDetails.Text <- "Load a ROM before running frames."
                stopRunning ()
            | Some session ->
                let stopwatch = Stopwatch.StartNew()
                let result = Emulator.runFrame 20_000 session
                stopwatch.Stop()
                recordEmulatedFrame ()
                lock sessionGate (fun () -> currentSession <- Some result.Session)
                recordFrameTime stopwatch.Elapsed.TotalMilliseconds
                recordDisplayedFrame ()
                updateFrame result

        let primeAudioBuffer frameCount =
            let session = lock sessionGate (fun () -> currentSession)

            match session with
            | None -> ()
            | Some session ->
                let mutable current = session
                let mutable frames = frameCount
                let mutable keepGoing = true

                while frames > 0 && keepGoing do
                    let result = enqueueFrameAudio current
                    current <- result.Session
                    keepGoing <- result.StopReason = Emulator.FrameCompleted
                    frames <- frames - 1

                lock sessionGate (fun () -> currentSession <- Some current)

        let frameTimer = DispatcherTimer()
        frameTimer.Interval <- TimeSpan.FromMilliseconds(1000.0 * float Hardware.CyclesPerFrame / float Hardware.DmgClockHz)
        frameTimer.Tick.Add(fun _ ->
            if isRunning then
                let frame =
                    lock sessionGate (fun () ->
                        let frame = latestFrame
                        latestFrame <- None
                        frame)

                match frame with
                | Some result ->
                    recordDisplayedFrame ()
                    updateFrame result
                | None ->
                    debugDetails.Text <- formatRuntimeDiagnostics ())
        frameTimer.Start()

        let buttonRow =
            StackPanel(
                Orientation = Orientation.Horizontal,
                Spacing = 10.0,
                HorizontalAlignment = HorizontalAlignment.Center
            )

        buttonRow.Children.Add openButton |> ignore
        buttonRow.Children.Add stepFrameButton |> ignore
        buttonRow.Children.Add startStopButton |> ignore

        let mapKey key =
            match key with
            | Key.Z -> Some Joypad.A
            | Key.X -> Some Joypad.B
            | Key.Space -> Some Joypad.Select
            | Key.Enter -> Some Joypad.Start
            | Key.Right -> Some Joypad.Right
            | Key.Left -> Some Joypad.Left
            | Key.Up -> Some Joypad.Up
            | Key.Down -> Some Joypad.Down
            | _ -> None

        let updateButtonState key pressed =
            match mapKey key with
            | Some button ->
                lock sessionGate (fun () ->
                    match currentSession with
                    | Some session ->
                        currentSession <- Some { session with Bus = Bus.setButton button pressed session.Bus }
                        true
                    | None -> false)

            | _ -> false

        this.KeyDown.Add(fun args ->
            if updateButtonState args.Key true then
                args.Handled <- true)

        this.KeyUp.Add(fun args ->
            if updateButtonState args.Key false then
                args.Handled <- true)

        openButton.Click.Add(fun _ ->
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

                    if String.IsNullOrWhiteSpace path then
                        status.Text <- "Could not open the selected ROM path."
                    else
                        saveCurrentRam ()

                        match RomFile.load path with
                        | Ok loaded ->
                            let header = loaded.Header
                            let sessionResult =
                                Emulator.createSession loaded.Bytes
                                |> Result.bind (fun session ->
                                    SaveRam.loadForRom loaded.Path (Bus.cartridge session.Bus)
                                    |> Result.map (fun cartridge ->
                                        { session with Bus = Bus.withCartridge cartridge session.Bus }))

                            let session = sessionResult |> Result.toOption
                            loadedRom <- Some loaded
                            lock sessionGate (fun () ->
                                currentSession <- session
                                latestFrame <- None)

                            stepFrameButton.IsEnabled <- session.IsSome
                            startStopButton.IsEnabled <- session.IsSome
                            stopRunning ()
                            framebufferImage.Source <- FramebufferBitmap.create (Video.blankFrame ())

                            status.Text <-
                                match sessionResult, lastSaveStatus with
                                | Ok _, Some saveMessage -> $"Loaded {IO.Path.GetFileName loaded.Path}  {saveMessage}"
                                | Ok _, None -> $"Loaded {IO.Path.GetFileName loaded.Path}"
                                | Error message, _ -> $"Could not start ROM: {message}"
                            romDetails.Text <-
                                $"Title: {header.Title}\nCGB: {header.CgbSupport}\nSGB: {header.SgbSupport}\nCartridge: {header.CartridgeKind} (0x{header.CartridgeTypeCode:X2})\nROM: {HeaderDisplay.formatRomSize header.RomSizeCode} (0x{header.RomSizeCode:X2})\nRAM: {HeaderDisplay.formatRamSize header.RamSizeCode} (0x{header.RamSizeCode:X2})"
                            debugDetails.Text <-
                                match sessionResult with
                                | Ok _ -> "Ready to run frames."
                                | Error message -> message
                        | Error message ->
                            loadedRom <- None
                            lock sessionGate (fun () ->
                                currentSession <- None
                                latestFrame <- None)

                            stepFrameButton.IsEnabled <- false
                            startStopButton.IsEnabled <- false
                            stopRunning ()
                            status.Text <- "Could not load ROM."
                            romDetails.Text <- message
                            debugDetails.Text <- "Frame stepping is available after loading a ROM."
            }
            |> Async.StartImmediate)

        stepFrameButton.Click.Add(fun _ -> runOneFrame ())

        startStopButton.Click.Add(fun _ ->
            isRunning <- not isRunning

            if isRunning then
                resetPerformance ()
                primeAudioBuffer 16
                audioOutput.Start()
                startEmulationLoop ()
            else
                saveCurrentRam ()
                audioOutput.Stop()

            startStopButton.Content <- if isRunning then "Stop" else "Start"
            this.Focus() |> ignore)

        this.Closing.Add(fun _ -> saveCurrentRam ())

        let panel =
            StackPanel(
                Orientation = Orientation.Vertical,
                Spacing = 12.0,
                Margin = Thickness(24.0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            )

        panel.Children.Add title |> ignore
        panel.Children.Add subtitle |> ignore
        panel.Children.Add framebuffer |> ignore
        panel.Children.Add buttonRow |> ignore
        panel.Children.Add status |> ignore
        panel.Children.Add romDetails |> ignore
        panel.Children.Add debugDetails |> ignore

        this.Content <- panel

type App() =
    inherit Application()

    override this.Initialize() =
        this.RequestedThemeVariant <- ThemeVariant.Light
        this.Styles.Add(FluentTheme())

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            desktop.MainWindow <- MainWindow()
        | _ -> ()

        base.OnFrameworkInitializationCompleted()

module Program =
    [<EntryPoint>]
    [<STAThread>]
    let main argv =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(argv)
