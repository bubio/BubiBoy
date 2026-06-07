namespace BubiBoy.App

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Media
open Avalonia.Styling
open Avalonia.Themes.Fluent

type App() =
    inherit Application()

    override this.Initialize() =
        this.Name <- "BubiBoy"
        this.RequestedThemeVariant <- ThemeVariant.Default

        let fluentTheme = FluentTheme()
        let lightPalette = ColorPaletteResources()
        lightPalette.Accent <- Color.Parse("#9E3364")
        let darkPalette = ColorPaletteResources()
        darkPalette.Accent <- Color.Parse("#9E3364")
        fluentTheme.Palettes.Add(ThemeVariant.Light, lightPalette)
        fluentTheme.Palettes.Add(ThemeVariant.Dark, darkPalette)
        this.Styles.Add(fluentTheme)
        this.Resources <- AppTheme.createResources ()

        let appMenu = NativeMenu()
        let aboutItem = NativeMenuItem("About BubiBoy...")
        aboutItem.Click.Add(fun _ ->
            match this.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                match desktop.MainWindow with
                | :? MainWindow as mainWindow -> mainWindow.ShowAbout()
                | _ -> ()
            | _ -> ())
        appMenu.Items.Add aboutItem |> ignore
        NativeMenu.SetMenu(this, appMenu)

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let mainWindow = MainWindow()
            desktop.MainWindow <- mainWindow
        | _ -> ()

        base.OnFrameworkInitializationCompleted()
