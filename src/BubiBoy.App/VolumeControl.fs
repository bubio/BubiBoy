namespace BubiBoy.App

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media

module VolumeControl =
    type Elements =
        { Host: Grid
          Slider: Canvas
          SetVisual: int -> unit
          PercentFromPointer: PointerEventArgs -> int }

    let gainFromPercent percent =
        let normalized = single (Math.Clamp(percent, 0, 100)) / 100.0f
        normalized * normalized

    let create initialPercent =
        let volumeIcon =
            Path(
                Width = 14.4,
                Height = 14.4,
                Data =
                    Geometry.Parse(
                        "M2,8 L6,8 L11,3 L11,21 L6,16 L2,16 Z M14,8 C15.4,9.3 16.2,10.7 16.2,12 C16.2,13.3 15.4,14.7 14,16 L15.5,17.6 C17.4,15.8 18.5,14 18.5,12 C18.5,10 17.4,8.2 15.5,6.4 Z"
                    ),
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            )

        AppTheme.bindBrush volumeIcon Shape.FillProperty AppTheme.SecondaryText

        ToolTip.SetTip(volumeIcon, "Volume")

        let volumeIconHost =
            Border(
                Width = 14.4,
                Height = 24.0,
                Child = volumeIcon,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            )

        let sliderWidth = 88.0
        let sliderHeight = 24.0
        let thumbSize = 12.0
        let trackHeight = 4.0
        let trackLeft = thumbSize / 2.0
        let trackWidth = sliderWidth - thumbSize

        let slider =
            Canvas(
                Width = sliderWidth,
                Height = sliderHeight,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Focusable = true,
                ClipToBounds = false,
                VerticalAlignment = VerticalAlignment.Center
            )

        let track =
            Border(Width = trackWidth, Height = trackHeight, CornerRadius = CornerRadius(trackHeight / 2.0))

        AppTheme.bindBrush track Border.BackgroundProperty AppTheme.SliderTrack

        let fill =
            Border(
                Height = trackHeight,
                Background = SolidColorBrush(Color.Parse("#9E3364")),
                CornerRadius = CornerRadius(trackHeight / 2.0)
            )

        let thumb =
            Ellipse(Width = thumbSize, Height = thumbSize, Fill = SolidColorBrush(Color.Parse("#9E3364")))

        let trackTop = (sliderHeight - trackHeight) / 2.0
        let thumbTop = (sliderHeight - thumbSize) / 2.0

        Canvas.SetLeft(track, trackLeft)
        Canvas.SetTop(track, trackTop)
        Canvas.SetLeft(fill, trackLeft)
        Canvas.SetTop(fill, trackTop)
        Canvas.SetTop(thumb, thumbTop)
        slider.Children.Add track |> ignore
        slider.Children.Add fill |> ignore
        slider.Children.Add thumb |> ignore
        ToolTip.SetTip(slider, "Volume")

        let setVisual percent =
            let clamped = Math.Clamp(percent, 0, 100)
            let fraction = float clamped / 100.0
            let centerX = trackLeft + trackWidth * fraction
            fill.Width <- trackWidth * fraction
            Canvas.SetLeft(thumb, centerX - thumbSize / 2.0)

        setVisual initialPercent

        let percentFromPointer (args: PointerEventArgs) =
            let position = args.GetPosition(slider)
            let fraction = Math.Clamp((position.X - trackLeft) / trackWidth, 0.0, 1.0)
            int (Math.Round(fraction * 100.0))

        let host =
            Grid(
                ColumnDefinitions = ColumnDefinitions("14.4,8,88"),
                Width = 110.4,
                Height = 24.0,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            )

        Grid.SetColumn(volumeIconHost, 0)
        Grid.SetColumn(slider, 2)
        host.Children.Add volumeIconHost |> ignore
        host.Children.Add slider |> ignore

        { Host = host
          Slider = slider
          SetVisual = setVisual
          PercentFromPointer = percentFromPointer }

    let bind (elements: Elements) (currentPercent: unit -> int) (setPercent: int -> unit) =
        let mutable isDragging = false

        let setFromPointer (args: PointerEventArgs) =
            setPercent (elements.PercentFromPointer args)

        elements.Slider.PointerPressed.Add(fun args ->
            isDragging <- true
            elements.Slider.Focus() |> ignore
            args.Pointer.Capture(elements.Slider) |> ignore
            setFromPointer args
            args.Handled <- true)

        elements.Slider.PointerMoved.Add(fun args ->
            if isDragging then
                setFromPointer args
                args.Handled <- true)

        elements.Slider.PointerReleased.Add(fun args ->
            if isDragging then
                isDragging <- false
                args.Pointer.Capture(null) |> ignore
                setFromPointer args
                args.Handled <- true)

        elements.Slider.KeyDown.Add(fun args ->
            let delta =
                match args.Key with
                | Key.Left
                | Key.Down -> Some -5
                | Key.Right
                | Key.Up -> Some 5
                | Key.Home -> Some -100
                | Key.End -> Some 100
                | _ -> None

            match delta with
            | Some change ->
                let next =
                    match args.Key with
                    | Key.Home -> 0
                    | Key.End -> 100
                    | _ -> currentPercent () + change

                setPercent next
                args.Handled <- true
            | None -> ())
