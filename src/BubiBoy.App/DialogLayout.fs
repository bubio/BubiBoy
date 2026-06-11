namespace BubiBoy.App

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media

module DialogLayout =
    [<Literal>]
    let TitleFontSize = 20.0

    [<Literal>]
    let BodyFontSize = 13.0

    [<Literal>]
    let CaptionFontSize = 12.0

    let contentMargin = Thickness(16.0)

    let title text =
        let title =
            TextBlock(Text = text, FontSize = TitleFontSize, FontWeight = FontWeight.SemiBold)

        AppTheme.bindBrush title TextBlock.ForegroundProperty AppTheme.PrimaryText
        title

    let bodyText text =
        let body =
            TextBlock(Text = text, FontSize = BodyFontSize, TextWrapping = TextWrapping.Wrap)

        AppTheme.bindBrush body TextBlock.ForegroundProperty AppTheme.SecondaryText
        body

    let styleRadioButton (radioButton: RadioButton) =
        radioButton.FontSize <- BodyFontSize
        AppTheme.bindBrush radioButton RadioButton.ForegroundProperty AppTheme.PrimaryText

    let styleCaption (textBlock: TextBlock) =
        textBlock.FontSize <- CaptionFontSize
        AppTheme.bindBrush textBlock TextBlock.ForegroundProperty AppTheme.SecondaryText

    let actionButton title minWidth =
        let button =
            Button(
                Content = title,
                MinWidth = minWidth,
                Height = 32.0,
                FontSize = BodyFontSize,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            )

        button

    let actionBar leadingButton cancelButton primaryButton =
        let bar =
            Grid(
                ColumnDefinitions = ColumnDefinitions("Auto,*,Auto,8,Auto"),
                Height = 32.0,
                VerticalAlignment = VerticalAlignment.Bottom
            )

        leadingButton
        |> Option.iter (fun button ->
            Grid.SetColumn(button, 0)
            bar.Children.Add button |> ignore)

        Grid.SetColumn(cancelButton, 2)
        Grid.SetColumn(primaryButton, 4)
        bar.Children.Add cancelButton |> ignore
        bar.Children.Add primaryButton |> ignore
        bar

    let surface child padding =
        let surface =
            Border(Child = child, Padding = padding, BorderThickness = Thickness(1.0), CornerRadius = CornerRadius(8.0))

        AppTheme.bindBrush surface Border.BackgroundProperty AppTheme.SurfaceBackground
        AppTheme.bindBrush surface Border.BorderBrushProperty AppTheme.SurfaceBorder
        surface
