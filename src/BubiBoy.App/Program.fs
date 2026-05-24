namespace BubiBoy.App

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open Avalonia.Platform.Storage
open Avalonia.Styling
open Avalonia.Themes.Fluent
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
        | Emulator.Halted -> "CPU halted"
        | Emulator.UnsupportedOpcode(opcode, pc) -> $"unsupported opcode 0x{opcode:X2} at PC 0x{pc:X4}"

    let formatRunResult (result: Emulator.RunResult) =
        let registers = result.Session.Cpu.Registers

        $"Run stopped: {formatStopReason result.StopReason}\nSteps: {result.Session.Steps}    Cycles: {result.Session.TotalCycles}\nPC: 0x{registers.PC:X4}    SP: 0x{registers.SP:X4}\nA: 0x{registers.A:X2}  F: 0x{registers.F:X2}  B: 0x{registers.B:X2}  C: 0x{registers.C:X2}  D: 0x{registers.D:X2}  E: 0x{registers.E:X2}  H: 0x{registers.H:X2}  L: 0x{registers.L:X2}"

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

        let runButton =
            Button(
                Content = "Run 2000 steps",
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = Thickness(18.0, 8.0),
                Background = SolidColorBrush(Color.Parse("#17202B")),
                Foreground = Brushes.White,
                BorderBrush = SolidColorBrush(Color.Parse("#17202B")),
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
                        match RomFile.load path with
                        | Ok loaded ->
                            let header = loaded.Header
                            loadedRom <- Some loaded
                            runButton.IsEnabled <- true
                            framebufferImage.Source <- FramebufferBitmap.create (Video.blankFrame ())

                            status.Text <- $"Loaded {IO.Path.GetFileName loaded.Path}"
                            romDetails.Text <-
                                $"Title: {header.Title}\nCGB: {header.CgbSupport}\nSGB: {header.SgbSupport}\nCartridge: {header.CartridgeKind} (0x{header.CartridgeTypeCode:X2})\nROM: {HeaderDisplay.formatRomSize header.RomSizeCode} (0x{header.RomSizeCode:X2})\nRAM: {HeaderDisplay.formatRamSize header.RamSizeCode} (0x{header.RamSizeCode:X2})"
                            debugDetails.Text <- "Ready to run CPU debug steps."
                        | Error message ->
                            loadedRom <- None
                            runButton.IsEnabled <- false
                            status.Text <- "Could not load ROM."
                            romDetails.Text <- message
                            debugDetails.Text <- "CPU debug run is available after loading a ROM."
            }
            |> Async.StartImmediate)

        runButton.Click.Add(fun _ ->
            match loadedRom with
            | None ->
                debugDetails.Text <- "Load a ROM before running CPU steps."
            | Some rom ->
                match Emulator.createSession rom.Bytes with
                | Error message ->
                    debugDetails.Text <- message
                | Ok session ->
                    let result = Emulator.run 2000 session
                    framebufferImage.Source <- FramebufferBitmap.create (Video.renderFrame result.Session.Bus)
                    debugDetails.Text <- DebugDisplay.formatRunResult result)

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
        panel.Children.Add openButton |> ignore
        panel.Children.Add runButton |> ignore
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
