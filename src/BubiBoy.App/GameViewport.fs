namespace BubiBoy.App

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open BubiBoy.Core

module GameViewport =
    type Elements =
        { Host: Grid
          Framebuffer: Border
          PresentFrame: uint32[] -> unit
          ApplyScale: int -> WindowState -> unit
          ApplyBackground: WindowState -> unit }

    let create (owner: Window) =
        let fullscreenBackground = Brushes.Black :> IBrush

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

        let host = Grid()
        AppTheme.bindBrush host Panel.BackgroundProperty AppTheme.WindowBackground

        host.PointerPressed.Add(fun args ->
            let pointer = args.GetCurrentPoint(host)

            if
                not args.Handled
                && pointer.Properties.IsLeftButtonPressed
                && owner.WindowState <> WindowState.FullScreen
            then
                owner.BeginMoveDrag(args)
                args.Handled <- true)

        let applyBackground windowState =
            if windowState = WindowState.FullScreen then
                host.Background <- fullscreenBackground
            else
                AppTheme.bindBrush host Panel.BackgroundProperty AppTheme.WindowBackground

        let applyScale scale windowState =
            let videoWidth = float Hardware.ScreenWidth * float scale
            let videoHeight = float Hardware.ScreenHeight * float scale

            if windowState = WindowState.FullScreen then
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

            applyBackground windowState

        let presentFrame (pixels: uint32[]) =
            FramebufferBitmap.writeInto pixels displayBitmap displayBytes
            framebufferImage.InvalidateVisual()

        host.Children.Add framebuffer |> ignore

        { Host = host
          Framebuffer = framebuffer
          PresentFrame = presentFrame
          ApplyScale = applyScale
          ApplyBackground = applyBackground }
