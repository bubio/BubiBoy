namespace BubiBoy.App

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media

/// Application-specific About dialog used by the macOS application menu and Help menu.
type AboutWindow(version: string) as this =
    inherit Window()

    do
        this.Title <- "About BubiBoy"
        this.Width <- 320.0
        this.Height <- 190.0
        this.MinWidth <- 320.0
        this.MinHeight <- 190.0
        this.MaxWidth <- 320.0
        this.MaxHeight <- 190.0
        this.CanResize <- false
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner

        let title =
            TextBlock(
                Text = "BubiBoy",
                FontSize = 22.0,
                FontWeight = FontWeight.SemiBold,
                Foreground = SolidColorBrush(Color.Parse("#17202B")),
                HorizontalAlignment = HorizontalAlignment.Center
            )

        let description =
            TextBlock(
                Text = "Game Boy / Game Boy Color emulator",
                FontSize = 13.0,
                Foreground = SolidColorBrush(Color.Parse("#425166")),
                HorizontalAlignment = HorizontalAlignment.Center
            )

        let versionText =
            TextBlock(
                Text = $"Version {version}",
                FontSize = 12.0,
                Foreground = SolidColorBrush(Color.Parse("#667386")),
                HorizontalAlignment = HorizontalAlignment.Center
            )

        let closeButton =
            Button(
                Content = "OK",
                Width = 88.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = Thickness(14.0, 6.0)
            )

        let content =
            StackPanel(
                Orientation = Orientation.Vertical,
                Spacing = 10.0,
                Margin = Thickness(28.0),
                HorizontalAlignment = HorizontalAlignment.Center
            )

        content.Children.Add title |> ignore
        content.Children.Add description |> ignore
        content.Children.Add versionText |> ignore
        content.Children.Add closeButton |> ignore

        closeButton.Click.Add(fun _ -> this.Close())
        this.Content <- content
