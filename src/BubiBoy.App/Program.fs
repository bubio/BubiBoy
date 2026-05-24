namespace BubiBoy.App

open System
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
        this.Height <- 480.0
        this.MinWidth <- 480.0
        this.MinHeight <- 360.0
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
        let mutable isRunning = false
        let mutable lastSaveStatus: string option = None

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
                HorizontalAlignment = HorizontalAlignment.Center
            )

        let romDetails =
            TextBlock(
                Text = "Choose a .gb or .gbc file to inspect its cartridge header.",
                FontSize = 13.0,
                Foreground = SolidColorBrush(Color.Parse("#425166")),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            )

        let debugDetails =
            TextBlock(
                Text = "CPU debug run is available after loading a ROM.",
                FontFamily = FontFamily("Menlo, Consolas, monospace"),
                FontSize = 12.0,
                Foreground = SolidColorBrush(Color.Parse("#263448")),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            )

        let romFileType =
            FilePickerFileType(
                "Game Boy ROM",
                Patterns = [| "*.gb"; "*.gbc" |],
                MimeTypes = [| "application/octet-stream" |]
            )

        let stopRunning () =
            isRunning <- false
            startStopButton.Content <- "Start"

        let saveCurrentRam () =
            match loadedRom, currentSession with
            | Some rom, Some session ->
                match SaveRam.saveForRom rom.Path session.Bus.Cartridge with
                | Ok true -> lastSaveStatus <- Some "Save RAM written."
                | Ok false -> lastSaveStatus <- None
                | Error message -> lastSaveStatus <- Some $"Save RAM error: {message}"
            | _ -> ()

        let updateFrame (result: Emulator.FrameResult) =
            currentSession <- Some result.Session
            framebufferImage.Source <- FramebufferBitmap.create result.Framebuffer
            debugDetails.Text <- DebugDisplay.formatFrameResult result

            match result.StopReason with
            | Emulator.FrameCompleted -> ()
            | _ -> stopRunning ()

        let runOneFrame () =
            match currentSession with
            | None ->
                debugDetails.Text <- "Load a ROM before running frames."
                stopRunning ()
            | Some session ->
                session
                |> Emulator.runFrame 20_000
                |> updateFrame

        let frameTimer = DispatcherTimer()
        frameTimer.Interval <- TimeSpan.FromMilliseconds(1000.0 / 60.0)
        frameTimer.Tick.Add(fun _ ->
            if isRunning then
                runOneFrame ())
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
            match currentSession, mapKey key with
            | Some session, Some button ->
                currentSession <- Some { session with Bus = Bus.setButton button pressed session.Bus }
                true
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
                                    SaveRam.loadForRom loaded.Path session.Bus.Cartridge
                                    |> Result.map (fun cartridge ->
                                        { session with Bus = { session.Bus with Cartridge = cartridge } }))

                            loadedRom <- Some loaded
                            currentSession <- sessionResult |> Result.toOption
                            stepFrameButton.IsEnabled <- currentSession.IsSome
                            startStopButton.IsEnabled <- currentSession.IsSome
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
                            currentSession <- None
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
            if not isRunning then
                saveCurrentRam ()

            startStopButton.Content <- if isRunning then "Stop" else "Start"
            this.Focus() |> ignore)

        this.Closing.Add(fun _ -> saveCurrentRam ())

        let panel =
            StackPanel(
                Orientation = Orientation.Vertical,
                Spacing = 16.0,
                Margin = Thickness(24.0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
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
