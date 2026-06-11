namespace BubiBoy.App

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open BubiBoy.IO

type SettingsWindow(initialSelection: AppSettings.BootRomSelection) as this =
    inherit Window()

    let choices =
        [| "Disabled", AppSettings.Disabled
           "Automatic", AppSettings.Automatic
           "CGB", AppSettings.Cgb
           "DMG", AppSettings.Dmg |]

    do
        this.Title <- "Settings"
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.Width <- 500.0
        this.Height <- 244.0
        this.CanResize <- false
        this.FontFamily <- AppFonts.ui
        AppTheme.bindBrush this Window.BackgroundProperty AppTheme.WindowBackground

        let title = DialogLayout.title "Boot ROM"

        let description =
            DialogLayout.bodyText "Select the boot ROM used when a ROM is opened or reset."

        let selectedIndex =
            choices
            |> Array.tryFindIndex (fun (_, value) -> value = initialSelection)
            |> Option.defaultValue 0

        let radioButtons =
            choices
            |> Array.mapi (fun index (label, _) ->
                let radioButton =
                    RadioButton(
                        Content = label,
                        GroupName = "BootRomSelection",
                        IsChecked = (index = selectedIndex),
                        HorizontalAlignment = HorizontalAlignment.Center
                    )

                DialogLayout.styleRadioButton radioButton
                radioButton)

        let options = Grid(ColumnDefinitions = ColumnDefinitions("*,*,*,*"), Height = 40.0)

        radioButtons
        |> Array.iteri (fun index radioButton ->
            Grid.SetColumn(radioButton, index)
            options.Children.Add radioButton |> ignore)

        let optionSurface = DialogLayout.surface options (Thickness(12.0, 8.0))
        let cancelButton = DialogLayout.actionButton "Cancel" 80.0
        let saveButton = DialogLayout.actionButton "Save" 80.0
        let buttons = DialogLayout.actionBar None cancelButton saveButton

        let content =
            Grid(RowDefinitions = RowDefinitions("Auto,10,Auto,20,Auto,*,Auto"), Margin = DialogLayout.contentMargin)

        Grid.SetRow(title, 0)
        Grid.SetRow(description, 2)
        Grid.SetRow(optionSurface, 4)
        Grid.SetRow(buttons, 6)
        content.Children.Add title |> ignore
        content.Children.Add description |> ignore
        content.Children.Add optionSurface |> ignore
        content.Children.Add buttons |> ignore
        this.Content <- content

        cancelButton.Click.Add(fun _ -> this.Close(None))

        saveButton.Click.Add(fun _ ->
            let selection =
                radioButtons
                |> Array.tryFindIndex (fun radioButton -> radioButton.IsChecked.GetValueOrDefault())
                |> Option.map (fun index -> snd choices[index])
                |> Option.defaultValue AppSettings.Disabled

            this.Close(Some selection))

    static member Show(owner: Window, selection: AppSettings.BootRomSelection) =
        let dialog = SettingsWindow(selection)
        dialog.ShowDialog<AppSettings.BootRomSelection option>(owner)
