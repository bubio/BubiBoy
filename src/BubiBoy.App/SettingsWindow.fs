namespace BubiBoy.App

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open BubiBoy.IO

type SettingsResult =
    { BootRomSelection: AppSettings.BootRomSelection
      RetroAchievementsEnabled: bool
      RetroAchievementsHardcore: bool
      RetroAchievementsUsername: string }

module internal SettingsWindowSelection =
    let hardcorePreference isChecked = isChecked

type SettingsWindow(initialSettings: AppSettings.Settings) as this =
    inherit Window()

    let choices =
        [| "Disabled", AppSettings.Disabled
           "Automatic", AppSettings.Automatic
           "CGB", AppSettings.Cgb
           "DMG", AppSettings.Dmg |]

    do
        this.Title <- "Settings"
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.Width <- 520.0
        this.Height <- 390.0
        this.CanResize <- false
        this.FontFamily <- AppFonts.ui
        AppTheme.bindBrush this Window.BackgroundProperty AppTheme.WindowBackground

        let bootTitle = DialogLayout.title "Boot ROM"

        let bootDescription =
            DialogLayout.bodyText "Select the boot ROM used when a ROM is opened or reset."

        let selectedIndex =
            choices
            |> Array.tryFindIndex (fun (_, value) -> value = initialSettings.BootRomSelection)
            |> Option.defaultValue 0

        let radioButtons =
            choices
            |> Array.mapi (fun index (label, _) ->
                let button =
                    RadioButton(
                        Content = label,
                        GroupName = "BootRomSelection",
                        IsChecked = (index = selectedIndex),
                        HorizontalAlignment = HorizontalAlignment.Center
                    )

                DialogLayout.styleRadioButton button
                button)

        let bootOptions =
            Grid(ColumnDefinitions = ColumnDefinitions("*,*,*,*"), Height = 40.0)

        radioButtons
        |> Array.iteri (fun index button ->
            Grid.SetColumn(button, index)
            bootOptions.Children.Add button |> ignore)

        let raTitle = DialogLayout.title "RetroAchievements"

        let enabled =
            CheckBox(Content = "Enable RetroAchievements", IsChecked = initialSettings.RetroAchievementsEnabled)

        let hardcore =
            CheckBox(
                Content = "Hardcore Mode (prevents loading save states)",
                IsChecked = initialSettings.RetroAchievementsHardcore,
                IsEnabled = initialSettings.RetroAchievementsEnabled
            )

        let username =
            TextBox(
                PlaceholderText = "RetroAchievements username",
                Text = initialSettings.RetroAchievementsUsername,
                IsEnabled = initialSettings.RetroAchievementsEnabled
            )

        enabled.IsCheckedChanged.Add(fun _ ->
            let isEnabled = enabled.IsChecked.GetValueOrDefault()
            hardcore.IsEnabled <- isEnabled
            username.IsEnabled <- isEnabled)

        let raPanel = StackPanel(Spacing = 8.0)
        raPanel.Children.Add enabled |> ignore
        raPanel.Children.Add hardcore |> ignore
        raPanel.Children.Add username |> ignore

        let cancelButton = DialogLayout.actionButton "Cancel" 80.0
        let saveButton = DialogLayout.actionButton "Save" 80.0
        let buttons = DialogLayout.actionBar None cancelButton saveButton

        let content =
            Grid(
                RowDefinitions = RowDefinitions("Auto,8,Auto,8,Auto,18,Auto,8,Auto,*,Auto"),
                Margin = DialogLayout.contentMargin
            )

        let add row control =
            Grid.SetRow(control, row)
            content.Children.Add control |> ignore

        add 0 bootTitle
        add 2 bootDescription
        add 4 (DialogLayout.surface bootOptions (Thickness(12.0, 8.0)))
        add 6 raTitle
        add 8 (DialogLayout.surface raPanel (Thickness(12.0, 10.0)))
        add 10 buttons
        this.Content <- content

        cancelButton.Click.Add(fun _ -> this.Close(None))

        saveButton.Click.Add(fun _ ->
            let selection =
                radioButtons
                |> Array.tryFindIndex (fun button -> button.IsChecked.GetValueOrDefault())
                |> Option.map (fun index -> snd choices[index])
                |> Option.defaultValue AppSettings.Disabled

            this.Close(
                Some
                    { BootRomSelection = selection
                      RetroAchievementsEnabled = enabled.IsChecked.GetValueOrDefault()
                      RetroAchievementsHardcore =
                        SettingsWindowSelection.hardcorePreference (hardcore.IsChecked.GetValueOrDefault())
                      RetroAchievementsUsername = username.Text }
            ))

    static member Show(owner: Window, settings: AppSettings.Settings) =
        SettingsWindow(settings).ShowDialog<SettingsResult option>(owner)
