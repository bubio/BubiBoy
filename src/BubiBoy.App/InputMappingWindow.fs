namespace BubiBoy.App

open System
open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open BubiBoy.Core
open ControllerInput

type InputMappingResult =
    { KeyboardMapping: Map<string, string>
      ControllerMapping: Map<string, string> }

type private CaptureTarget =
    | Keyboard of Joypad.Button
    | Controller of Joypad.Button

type InputMappingWindow(initialKeyboardMapping: Map<string, string>, initialControllerMapping: Map<string, string>, controllerHost: GamepadHost) as this =
    inherit Window()

    let mutable keyboardMapping = InputMapping.normalizeKeyMapping initialKeyboardMapping
    let mutable controllerMapping = InputMapping.normalizeControllerMapping initialControllerMapping
    let mutable captureTarget: CaptureTarget option = None
    let keyboardCells = Dictionary<Joypad.Button, Border>()
    let controllerCells = Dictionary<Joypad.Button, Border>()
    let keyboardLabels = Dictionary<Joypad.Button, TextBlock>()
    let controllerLabels = Dictionary<Joypad.Button, TextBlock>()
    let controllerCaptureTimer = DispatcherTimer(Interval = TimeSpan.FromMilliseconds(16.0))
    let mutable controllerCaptureBaseline: Set<GamepadControl> = Set.empty

    let statusText =
        TextBlock(
            Text = "Select a keyboard or controller cell, then press an input.",
            FontSize = 12.0,
            Foreground = SolidColorBrush(Color.Parse("#526173")),
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 20.0
        )

    let keyFor button =
        InputMapping.keyForButton keyboardMapping button

    let controlFor button =
        InputMapping.controllerControlForButton controllerMapping button

    let duplicateKeyOwner key targetButton =
        InputMapping.allJoypadButtons
        |> List.tryFind (fun button -> button <> targetButton && keyFor button = key)

    let duplicateControlOwner control targetButton =
        InputMapping.allJoypadButtons
        |> List.tryFind (fun button -> button <> targetButton && controlFor button = control)

    let activeBrush = SolidColorBrush(Color.Parse("#EEF6FF"))
    let idleBrush = SolidColorBrush(Color.Parse("#ECEEF2"))

    let refreshRows () =
        for button in InputMapping.allJoypadButtons do
            let keyboardLabel = keyboardLabels[button]
            let controllerLabel = controllerLabels[button]

            keyboardLabel.Text <-
                match captureTarget with
                | Some(Keyboard target) when target = button -> "Press a key..."
                | _ -> keyFor button |> InputMapping.keyDisplayName

            controllerLabel.Text <-
                match captureTarget with
                | Some(Controller target) when target = button -> "Press control..."
                | _ -> controlFor button |> InputMapping.controllerControlDisplayName

            keyboardCells[button].Background <-
                match captureTarget with
                | Some(Keyboard target) when target = button -> activeBrush
                | _ -> idleBrush

            controllerCells[button].Background <-
                match captureTarget with
                | Some(Controller target) when target = button -> activeBrush
                | _ -> idleBrush

    let stopControllerCapture () =
        controllerCaptureTimer.Stop()
        controllerCaptureBaseline <- Set.empty

    let pressedControllerControls () =
        controllerHost.Poll()
        |> Seq.choose InputMapping.firstPressedControllerControl
        |> Set.ofSeq

    let assignControllerControl button control =
        match duplicateControlOwner control button with
        | Some owner ->
            statusText.Text <-
                $"{InputMapping.controllerControlDisplayName control} is already assigned to {InputMapping.buttonDisplayName owner}."
        | None ->
            controllerMapping <- InputMapping.setControllerControl button control controllerMapping
            captureTarget <- None
            stopControllerCapture ()
            statusText.Text <-
                $"{InputMapping.buttonDisplayName button} assigned to {InputMapping.controllerControlDisplayName control}."
            refreshRows ()

    let pollControllerCapture () =
        match captureTarget with
        | Some(Controller button) ->
            try
                let pressed = pressedControllerControls ()
                let newlyPressed = Set.difference pressed controllerCaptureBaseline
                controllerCaptureBaseline <- Set.intersect controllerCaptureBaseline pressed

                newlyPressed
                |> Seq.tryHead
                |> Option.iter (assignControllerControl button)
            with ex ->
                captureTarget <- None
                stopControllerCapture ()
                statusText.Text <- $"Controller input unavailable: {ex.Message}"
                refreshRows ()
        | _ -> ()

    do
        this.Title <- "Input Mapping"
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.Width <- 620.0
        this.Height <- 500.0
        this.MinWidth <- 620.0
        this.MinHeight <- 500.0
        this.CanResize <- false
        this.Background <- SolidColorBrush(Color.Parse("#F4F5F7"))
        this.FontFamily <- AppFonts.ui
        this.Focusable <- true

        controllerCaptureTimer.Tick.Add(fun _ -> pollControllerCapture ())

        let root =
            Grid(
                RowDefinitions = RowDefinitions("Auto,12,Auto,12,Auto,*,Auto"),
                Margin = Thickness(16.0)
            )

        let title =
            TextBlock(
                Text = "Input Mapping",
                FontSize = 20.0,
                FontWeight = FontWeight.SemiBold,
                Foreground = SolidColorBrush(Color.Parse("#17202B"))
            )

        let list =
            Border(
                Background = SolidColorBrush(Color.Parse("#FFFFFF")),
                BorderBrush = SolidColorBrush(Color.Parse("#D6DCE5")),
                BorderThickness = Thickness(1.0),
                CornerRadius = CornerRadius(8.0),
                ClipToBounds = true
            )

        let rows =
            StackPanel(Spacing = 0.0)

        let header =
            Grid(
                ColumnDefinitions = ColumnDefinitions("*,122,170"),
                Height = 32.0,
                Background = SolidColorBrush(Color.Parse("#F7F9FC"))
            )

        let addHeader column text =
            let label =
                TextBlock(
                    Text = text,
                    FontSize = 12.0,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = SolidColorBrush(Color.Parse("#526173")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = Thickness(12.0, 0.0)
                )

            Grid.SetColumn(label, column)
            header.Children.Add label |> ignore

        addHeader 0 "Game Boy"
        addHeader 1 "Keyboard"
        addHeader 2 "Controller"
        rows.Children.Add header |> ignore

        for index, button in InputMapping.allJoypadButtons |> List.indexed do
            let rowGrid =
                Grid(
                    ColumnDefinitions = ColumnDefinitions("*,122,170"),
                    Height = 42.0,
                    Margin = Thickness(0.0)
                )

            let buttonLabel =
                TextBlock(
                    Text = InputMapping.buttonDisplayName button,
                    FontSize = 13.0,
                    Foreground = SolidColorBrush(Color.Parse("#1F2933")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = Thickness(12.0, 0.0)
                )

            let createCell width =
                let label =
                    TextBlock(
                        FontSize = 13.0,
                        Foreground = SolidColorBrush(Color.Parse("#222222")),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    )

                let cell =
                    Border(
                        Child = label,
                        Width = width,
                        Height = 26.0,
                        Padding = Thickness(10.0, 0.0),
                        Background = idleBrush,
                        CornerRadius = CornerRadius(6.0),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = Thickness(8.0, 0.0),
                        Cursor = new Cursor(StandardCursorType.Hand)
                    )

                cell, label

            let keyboardCell, keyboardLabel = createCell 106.0
            let controllerCell, controllerLabel = createCell 154.0

            keyboardCell.PointerPressed.Add(fun args ->
                captureTarget <- Some(Keyboard button)
                stopControllerCapture ()
                statusText.Text <- $"Press a key for {InputMapping.buttonDisplayName button}. Escape cancels capture."
                args.Handled <- true
                refreshRows ()
                this.Focus() |> ignore)

            controllerCell.PointerPressed.Add(fun args ->
                captureTarget <- Some(Controller button)
                controllerCaptureBaseline <-
                    try
                        pressedControllerControls ()
                    with _ ->
                        Set.empty

                statusText.Text <- $"Press a controller input for {InputMapping.buttonDisplayName button}. Escape cancels capture."
                args.Handled <- true
                refreshRows ()
                this.Focus() |> ignore
                controllerCaptureTimer.Start())

            Grid.SetColumn(buttonLabel, 0)
            Grid.SetColumn(keyboardCell, 1)
            Grid.SetColumn(controllerCell, 2)
            rowGrid.Children.Add buttonLabel |> ignore
            rowGrid.Children.Add keyboardCell |> ignore
            rowGrid.Children.Add controllerCell |> ignore

            let row =
                Border(
                    Child = rowGrid,
                    Background = SolidColorBrush(Color.Parse("#FFFFFF")),
                    BorderBrush = SolidColorBrush(Color.Parse("#E2E6EC")),
                    BorderThickness =
                        if index = InputMapping.allJoypadButtons.Length - 1 then
                            Thickness(0.0)
                        else
                            Thickness(0.0, 0.0, 0.0, 1.0)
                )

            keyboardCells.Add(button, keyboardCell)
            controllerCells.Add(button, controllerCell)
            keyboardLabels.Add(button, keyboardLabel)
            controllerLabels.Add(button, controllerLabel)
            rows.Children.Add row |> ignore

        list.Child <- rows

        let buttonBar =
            Grid(
                ColumnDefinitions = ColumnDefinitions("Auto,*,Auto,8,Auto"),
                Height = 34.0
            )

        let resetButton = Button(Content = "Reset Defaults", MinWidth = 112.0)
        let cancelButton = Button(Content = "Cancel", MinWidth = 78.0)
        let saveButton = Button(Content = "Save", MinWidth = 78.0)

        Grid.SetColumn(resetButton, 0)
        Grid.SetColumn(cancelButton, 2)
        Grid.SetColumn(saveButton, 4)
        buttonBar.Children.Add resetButton |> ignore
        buttonBar.Children.Add cancelButton |> ignore
        buttonBar.Children.Add saveButton |> ignore

        Grid.SetRow(title, 0)
        Grid.SetRow(list, 2)
        Grid.SetRow(statusText, 4)
        Grid.SetRow(buttonBar, 6)
        root.Children.Add title |> ignore
        root.Children.Add list |> ignore
        root.Children.Add statusText |> ignore
        root.Children.Add buttonBar |> ignore
        this.Content <- root

        resetButton.Click.Add(fun _ ->
            keyboardMapping <- InputMapping.resetDefaults ()
            controllerMapping <- InputMapping.resetControllerDefaults ()
            captureTarget <- None
            stopControllerCapture ()
            statusText.Text <- "Default mapping restored."
            refreshRows ())

        cancelButton.Click.Add(fun _ -> this.Close(None))

        saveButton.Click.Add(fun _ ->
            this.Close(
                Some
                    { KeyboardMapping = keyboardMapping
                      ControllerMapping = controllerMapping }
            ))

        this.KeyDown.Add(fun args ->
            match captureTarget with
            | Some(Keyboard button) ->
                args.Handled <- true

                if args.Key = Key.Escape then
                    captureTarget <- None
                    statusText.Text <- "Capture canceled."
                    refreshRows ()
                elif args.Key = Key.None then
                    ()
                else
                    match duplicateKeyOwner args.Key button with
                    | Some owner ->
                        statusText.Text <-
                            $"{InputMapping.keyDisplayName args.Key} is already assigned to {InputMapping.buttonDisplayName owner}."
                    | None ->
                        keyboardMapping <- InputMapping.setKey button args.Key keyboardMapping
                        captureTarget <- None
                        statusText.Text <- $"{InputMapping.buttonDisplayName button} assigned to {InputMapping.keyDisplayName args.Key}."
                        refreshRows ()
            | Some(Controller _) ->
                if args.Key = Key.Escape then
                    args.Handled <- true
                    captureTarget <- None
                    stopControllerCapture ()
                    statusText.Text <- "Capture canceled."
                    refreshRows ()
            | None ->
                if args.Key = Key.Escape then
                    args.Handled <- true
                    this.Close(None))

        this.Closed.Add(fun _ -> stopControllerCapture ())

        refreshRows ()

    static member Show(owner: Window, keyboardMapping: Map<string, string>, controllerMapping: Map<string, string>, controllerHost: GamepadHost) =
        let dialog = InputMappingWindow(keyboardMapping, controllerMapping, controllerHost)
        dialog.ShowDialog<InputMappingResult option>(owner)
