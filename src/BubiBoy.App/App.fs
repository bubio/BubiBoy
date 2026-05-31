namespace BubiBoy.App

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Styling
open Avalonia.Themes.Fluent

type App() =
    inherit Application()

    override this.Initialize() =
        this.Name <- "BubiBoy"
        this.RequestedThemeVariant <- ThemeVariant.Light
        this.Styles.Add(FluentTheme())

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let mainWindow = MainWindow()
            desktop.MainWindow <- mainWindow

            let appMenu = NativeMenu()
            let aboutItem = NativeMenuItem("About BubiBoy...")
            aboutItem.Click.Add(fun _ -> mainWindow.ShowAbout())
            appMenu.Items.Add aboutItem |> ignore
            NativeMenu.SetMenu(this, appMenu)
        | _ -> ()

        base.OnFrameworkInitializationCompleted()
