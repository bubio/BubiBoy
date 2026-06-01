namespace BubiBoy.App

open System.Collections.Generic
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open BubiBoy.Core

type InputMappingWindow(initialMapping: Map<string, string>) as this =
    inherit Window()

    let mutable mapping = InputMapping.normalizeKeyMapping initialMapping
    let mutable captureTarget: Joypad.Button option = None
    let rowBorders = Dictionary<Joypad.Button, Border>()
    let keyLabels = Dictionary<Joypad.Button, TextBlock>()

    let statusText =
        TextBlock(
            Text = "Select a button row, then press a key.",
            FontSize = 12.0,
            Foreground = SolidColorBrush(Color.Parse("#526173")),
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 20.0
        )

    let keyFor button =
        InputMapping.keyForButton mapping button

    let duplicateOwner key targetButton =
        InputMapping.allJoypadButtons
        |> List.tryFind (fun button -> button <> targetButton && keyFor button = key)

    let refreshRows () =
        for button in InputMapping.allJoypadButtons do
            let row = rowBorders[button]
            let keyLabel = keyLabels[button]
            let keyText =
                match captureTarget with
                | Some target when target = button -> "Press a key..."
                | _ -> keyFor button |> InputMapping.keyDisplayName

            keyLabel.Text <- keyText

            row.Background <-
                match captureTarget with
                | Some target when target = button -> SolidColorBrush(Color.Parse("#EEF6FF"))
                | _ -> SolidColorBrush(Color.Parse("#FFFFFF"))

    do
        this.Title <- "Input Mapping"
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.Width <- 380.0
        this.Height <- 460.0
        this.MinWidth <- 380.0
        this.MinHeight <- 460.0
        this.CanResize <- false
        this.Background <- SolidColorBrush(Color.Parse("#F4F5F7"))
        this.Focusable <- true

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

        for index, button in InputMapping.allJoypadButtons |> List.indexed do
            let rowGrid =
                Grid(
                    ColumnDefinitions = ColumnDefinitions("*,Auto"),
                    Height = 42.0,
                    Margin = Thickness(0.0),
                    Cursor = new Cursor(StandardCursorType.Hand)
                )

            let buttonLabel =
                TextBlock(
                    Text = InputMapping.buttonDisplayName button,
                    FontSize = 13.0,
                    Foreground = SolidColorBrush(Color.Parse("#1F2933")),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = Thickness(12.0, 0.0)
                )

            let keyLabel =
                TextBlock(
                    FontSize = 13.0,
                    Foreground = SolidColorBrush(Color.Parse("#222222")),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                )

            let keyPill =
                Border(
                    Child = keyLabel,
                    MinWidth = 86.0,
                    Height = 26.0,
                    Padding = Thickness(10.0, 0.0),
                    Background = SolidColorBrush(Color.Parse("#ECEEF2")),
                    CornerRadius = CornerRadius(6.0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = Thickness(12.0, 0.0)
                )

            Grid.SetColumn(buttonLabel, 0)
            Grid.SetColumn(keyPill, 1)
            rowGrid.Children.Add buttonLabel |> ignore
            rowGrid.Children.Add keyPill |> ignore

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

            row.PointerPressed.Add(fun args ->
                captureTarget <- Some button
                statusText.Text <- $"Press a key for {InputMapping.buttonDisplayName button}. Escape cancels capture."
                args.Handled <- true
                refreshRows ()
                this.Focus() |> ignore)

            rowBorders.Add(button, row)
            keyLabels.Add(button, keyLabel)
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
            mapping <- InputMapping.resetDefaults ()
            captureTarget <- None
            statusText.Text <- "Default mapping restored."
            refreshRows ())

        cancelButton.Click.Add(fun _ -> this.Close(None))
        saveButton.Click.Add(fun _ -> this.Close(Some mapping))

        this.KeyDown.Add(fun args ->
            match captureTarget with
            | Some button ->
                args.Handled <- true

                if args.Key = Key.Escape then
                    captureTarget <- None
                    statusText.Text <- "Capture canceled."
                    refreshRows ()
                elif args.Key = Key.None then
                    ()
                else
                    match duplicateOwner args.Key button with
                    | Some owner ->
                        statusText.Text <-
                            $"{InputMapping.keyDisplayName args.Key} is already assigned to {InputMapping.buttonDisplayName owner}."
                    | None ->
                        mapping <- InputMapping.setKey button args.Key mapping
                        captureTarget <- None
                        statusText.Text <- $"{InputMapping.buttonDisplayName button} assigned to {InputMapping.keyDisplayName args.Key}."
                        refreshRows ()
            | None ->
                if args.Key = Key.Escape then
                    args.Handled <- true
                    this.Close(None))

        refreshRows ()

    static member Show(owner: Window, mapping: Map<string, string>) =
        let dialog = InputMappingWindow(mapping)
        dialog.ShowDialog<Map<string, string> option>(owner)
