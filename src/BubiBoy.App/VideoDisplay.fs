namespace BubiBoy.App

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Platform
open BubiBoy.Core
open BubiBoy.IO

type internal VideoDisplay(initialFilter: AppSettings.VideoFilter) as this =
    inherit Control()

    [<Literal>]
    let LcdGridScale = 2

    [<Literal>]
    let LcdSubpixelScale = 3

    let displayBitmap = FramebufferBitmap.createBitmap ()

    let displayBytes =
        Array.zeroCreate<byte> (Hardware.ScreenWidth * Hardware.ScreenHeight * 4)

    let lcdGridWidth = Hardware.ScreenWidth * LcdGridScale
    let lcdGridHeight = Hardware.ScreenHeight * LcdGridScale

    let lcdGridBitmap =
        new WriteableBitmap(
            PixelSize(lcdGridWidth, lcdGridHeight),
            Vector(96.0, 96.0),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul
        )

    let lcdGridBytes = Array.zeroCreate<byte> (lcdGridWidth * lcdGridHeight * 4)
    let lcdSubpixelWidth = Hardware.ScreenWidth * LcdSubpixelScale
    let lcdSubpixelHeight = Hardware.ScreenHeight * LcdSubpixelScale

    let lcdSubpixelBitmap =
        new WriteableBitmap(
            PixelSize(lcdSubpixelWidth, lcdSubpixelHeight),
            Vector(96.0, 96.0),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul
        )

    let lcdSubpixelBytes =
        Array.zeroCreate<byte> (lcdSubpixelWidth * lcdSubpixelHeight * 4)

    let mutable selectedFilter = initialFilter
    let mutable disposed = false

    let adjustChannel value =
        let normalized = float value / 255.0
        let gammaAdjusted = Math.Pow(normalized, 0.94)
        let contrasted = Math.Clamp((gammaAdjusted - 0.5) * 1.04 + 0.5, 0.0, 1.0)
        byte (Math.Round(contrasted * 255.0))

    let scaleChannel value factor =
        byte (Math.Clamp(Math.Round(float value * factor), 0.0, 255.0))

    let writeLcdGridBytes () =
        for sourceY in 0 .. Hardware.ScreenHeight - 1 do
            for sourceX in 0 .. Hardware.ScreenWidth - 1 do
                let sourceOffset = (sourceY * Hardware.ScreenWidth + sourceX) * 4
                let blue = adjustChannel displayBytes[sourceOffset]
                let green = adjustChannel displayBytes[sourceOffset + 1]
                let red = adjustChannel displayBytes[sourceOffset + 2]
                let alpha = displayBytes[sourceOffset + 3]

                for pixelY in 0 .. LcdGridScale - 1 do
                    for pixelX in 0 .. LcdGridScale - 1 do
                        let gridFactor =
                            if pixelX = LcdGridScale - 1 || pixelY = LcdGridScale - 1 then
                                0.94
                            else
                                1.0

                        let outputX = sourceX * LcdGridScale + pixelX
                        let outputY = sourceY * LcdGridScale + pixelY
                        let outputOffset = (outputY * lcdGridWidth + outputX) * 4
                        lcdGridBytes[outputOffset] <- scaleChannel blue gridFactor
                        lcdGridBytes[outputOffset + 1] <- scaleChannel green gridFactor
                        lcdGridBytes[outputOffset + 2] <- scaleChannel red gridFactor
                        lcdGridBytes[outputOffset + 3] <- alpha

        FramebufferBitmap.writeBytesInto lcdGridBytes lcdGridBitmap

    let writeLcdSubpixelBytes () =
        for sourceY in 0 .. Hardware.ScreenHeight - 1 do
            for sourceX in 0 .. Hardware.ScreenWidth - 1 do
                let sourceOffset = (sourceY * Hardware.ScreenWidth + sourceX) * 4
                let blue = adjustChannel displayBytes[sourceOffset]
                let green = adjustChannel displayBytes[sourceOffset + 1]
                let red = adjustChannel displayBytes[sourceOffset + 2]
                let alpha = displayBytes[sourceOffset + 3]

                for pixelY in 0 .. LcdSubpixelScale - 1 do
                    let gridFactor = if pixelY = LcdSubpixelScale - 1 then 0.94 else 1.0

                    for pixelX in 0 .. LcdSubpixelScale - 1 do
                        let redFactor = gridFactor * (if pixelX = 0 then 1.04 else 0.96)
                        let greenFactor = gridFactor * (if pixelX = 1 then 1.04 else 0.96)
                        let blueFactor = gridFactor * (if pixelX = 2 then 1.04 else 0.96)
                        let outputX = sourceX * LcdSubpixelScale + pixelX
                        let outputY = sourceY * LcdSubpixelScale + pixelY
                        let outputOffset = (outputY * lcdSubpixelWidth + outputX) * 4
                        lcdSubpixelBytes[outputOffset] <- scaleChannel blue blueFactor
                        lcdSubpixelBytes[outputOffset + 1] <- scaleChannel green greenFactor
                        lcdSubpixelBytes[outputOffset + 2] <- scaleChannel red redFactor
                        lcdSubpixelBytes[outputOffset + 3] <- alpha

        FramebufferBitmap.writeBytesInto lcdSubpixelBytes lcdSubpixelBitmap

    let writeLcdBytes () =
        writeLcdGridBytes ()
        writeLcdSubpixelBytes ()

    let destinationRect () =
        let width = this.Bounds.Width
        let height = this.Bounds.Height

        if width <= 0.0 || height <= 0.0 then
            Rect()
        else
            let scale =
                Math.Min(width / float Hardware.ScreenWidth, height / float Hardware.ScreenHeight)

            let displayWidth = float Hardware.ScreenWidth * scale
            let displayHeight = float Hardware.ScreenHeight * scale
            Rect((width - displayWidth) / 2.0, (height - displayHeight) / 2.0, displayWidth, displayHeight)

    let drawBitmap (context: DrawingContext) bitmap sourceWidth sourceHeight destination =
        context.DrawImage(bitmap, Rect(0.0, 0.0, sourceWidth, sourceHeight), destination)

    do
        this.ClipToBounds <- true
        this.Focusable <- false
        this.IsHitTestVisible <- false
        FramebufferBitmap.writeBytesInto displayBytes displayBitmap
        writeLcdBytes ()

    member _.SetVideoFilter(filter: AppSettings.VideoFilter) =
        selectedFilter <- filter

        match filter with
        | AppSettings.Off -> RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None)
        | AppSettings.Smooth
        | AppSettings.Lcd -> RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality)

        this.InvalidateVisual()

    member _.PresentFrame(pixels: uint32[]) =
        FramebufferBitmap.copyToBgraBytes pixels displayBytes
        FramebufferBitmap.writeBytesInto displayBytes displayBitmap

        if selectedFilter = AppSettings.Lcd then
            writeLcdBytes ()

        this.InvalidateVisual()

    member _.Dispose() =
        if not disposed then
            disposed <- true
            lcdGridBitmap.Dispose()
            lcdSubpixelBitmap.Dispose()
            displayBitmap.Dispose()

    override _.Render(context: DrawingContext) =
        let destination = destinationRect ()

        if destination.Width > 0.0 && destination.Height > 0.0 then
            match selectedFilter with
            | AppSettings.Lcd ->
                let displayScale = destination.Width / float Hardware.ScreenWidth

                if displayScale <= 1.0 then
                    drawBitmap
                        context
                        displayBitmap
                        (float Hardware.ScreenWidth)
                        (float Hardware.ScreenHeight)
                        destination
                elif displayScale < float LcdSubpixelScale then
                    drawBitmap context lcdGridBitmap (float lcdGridWidth) (float lcdGridHeight) destination
                else
                    drawBitmap context lcdSubpixelBitmap (float lcdSubpixelWidth) (float lcdSubpixelHeight) destination
            | _ ->
                drawBitmap context displayBitmap (float Hardware.ScreenWidth) (float Hardware.ScreenHeight) destination

    override _.MeasureOverride(availableSize: Size) =
        let desiredWidth =
            if Double.IsInfinity availableSize.Width then
                float Hardware.ScreenWidth
            else
                availableSize.Width

        let desiredHeight =
            if Double.IsInfinity availableSize.Height then
                float Hardware.ScreenHeight
            else
                availableSize.Height

        Size(desiredWidth, desiredHeight)
