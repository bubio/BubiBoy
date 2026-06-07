namespace BubiBoy.App

open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml.MarkupExtensions
open Avalonia.Media
open Avalonia.Styling

module AppTheme =
    [<Literal>]
    let WindowBackground = "BubiBoy.WindowBackground"

    [<Literal>]
    let SurfaceBackground = "BubiBoy.SurfaceBackground"

    [<Literal>]
    let SurfaceBorder = "BubiBoy.SurfaceBorder"

    [<Literal>]
    let HeaderBackground = "BubiBoy.HeaderBackground"

    [<Literal>]
    let PrimaryText = "BubiBoy.PrimaryText"

    [<Literal>]
    let SecondaryText = "BubiBoy.SecondaryText"

    [<Literal>]
    let MutedText = "BubiBoy.MutedText"

    [<Literal>]
    let CellBackground = "BubiBoy.CellBackground"

    [<Literal>]
    let ActiveCellBackground = "BubiBoy.ActiveCellBackground"

    [<Literal>]
    let StatusBackground = "BubiBoy.StatusBackground"

    [<Literal>]
    let StatusBorder = "BubiBoy.StatusBorder"

    [<Literal>]
    let SliderTrack = "BubiBoy.SliderTrack"

    let private brush (value: string) =
        SolidColorBrush(Color.Parse(value)) :> obj

    let private createDictionary colors =
        let resources = ResourceDictionary()

        for key, value in colors do
            resources.Add(key, brush value)

        resources

    let createResources () =
        let resources = ResourceDictionary()

        let light =
            createDictionary
                [ WindowBackground, "#F4F5F7"
                  SurfaceBackground, "#FFFFFF"
                  SurfaceBorder, "#D6DCE5"
                  HeaderBackground, "#F7F9FC"
                  PrimaryText, "#17202B"
                  SecondaryText, "#425166"
                  MutedText, "#667386"
                  CellBackground, "#ECEEF2"
                  ActiveCellBackground, "#EEF6FF"
                  StatusBackground, "#F8F9FB"
                  StatusBorder, "#C8CED8"
                  SliderTrack, "#CBD2DC" ]

        let dark =
            createDictionary
                [ WindowBackground, "#17191D"
                  SurfaceBackground, "#202329"
                  SurfaceBorder, "#3A3F48"
                  HeaderBackground, "#252931"
                  PrimaryText, "#F1F3F5"
                  SecondaryText, "#C3CAD4"
                  MutedText, "#98A2B0"
                  CellBackground, "#30353E"
                  ActiveCellBackground, "#4A2A3B"
                  StatusBackground, "#202329"
                  StatusBorder, "#3A3F48"
                  SliderTrack, "#4B515C" ]

        resources.ThemeDictionaries.Add(ThemeVariant.Light, light)
        resources.ThemeDictionaries.Add(ThemeVariant.Dark, dark)
        resources

    let bindBrush (target: AvaloniaObject) property resourceKey =
        target.Bind(property, DynamicResourceExtension(resourceKey)) |> ignore
