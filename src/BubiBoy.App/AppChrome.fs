namespace BubiBoy.App

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading

module AppChrome =
    [<Literal>]
    let StatusBarHeight = 32.0

    type RunIndicator =
        { Host: Border
          SetRunning: bool -> unit }

    type Toast =
        { Host: Border
          Text: TextBlock
          Timer: DispatcherTimer }

    let createRunIndicator () =
        let indicator =
            Ellipse(
                Width = 9.0,
                Height = 9.0,
                Fill = SolidColorBrush(Color.Parse("#8692A3")),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            )

        let host =
            Border(
                Width = 28.0,
                Height = 28.0,
                Child = indicator,
                VerticalAlignment = VerticalAlignment.Center
            )

        let setRunning running =
            indicator.Fill <-
                if running then
                    SolidColorBrush(Color.Parse("#18A058"))
                else
                    SolidColorBrush(Color.Parse("#8692A3"))

            ToolTip.SetTip(host, if running then "Running" else "Paused")

        setRunning false

        { Host = host
          SetRunning = setRunning }

    let createStatusBar isFloating runIndicatorHost volumeHost =
        let statusBar =
            Border(
                Height = StatusBarHeight,
                MinHeight = StatusBarHeight,
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
        statusBar

    let createToast () =
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

        { Host = toast
          Text = toastText
          Timer = DispatcherTimer(Interval = TimeSpan.FromSeconds(3.0)) }
