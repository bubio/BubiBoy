namespace BubiBoy.App

open System.IO
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Platform
open BubiBoy.IO

module MainWindowMenus =
    type Actions =
        { OpenSettings: unit -> unit
          OpenInputMapping: unit -> unit
          SaveState: unit -> unit
          LoadState: unit -> unit
          SetScale: int -> unit
          SetVideoFilter: AppSettings.VideoFilter -> unit
          ToggleFullScreen: unit -> unit
          ToggleFullScreenInfo: unit -> unit
          ToggleFloating: unit -> unit
          ToggleAlwaysOnTop: unit -> unit
          LoadRecent: string -> unit
          Close: unit -> unit
          ShowAbout: unit -> unit }

    type State =
        { RecentRoms: string list
          IsFloating: bool
          IsAlwaysOnTop: bool
          IsFullScreen: bool
          ShowFullScreenInfo: bool
          VideoFilter: AppSettings.VideoFilter }

    type Elements =
        { MenuBar: Menu
          Refresh: State -> unit }

    let private gesture key modifiers = KeyGesture(key, modifiers)

    let private nativeItem header key modifiers action =
        let item = NativeMenuItem(header)
        item.Gesture <- gesture key modifiers
        item.Click.Add(fun _ -> action ())
        item

    let private nativePlain header action =
        let item = NativeMenuItem(header)
        item.Click.Add(fun _ -> action ())
        item

    let private nativeToggleItem toggleType header action =
        let item = nativePlain header action
        item.ToggleType <- toggleType
        item

    let private nativeCommandItem header key modifiers command =
        let item = NativeMenuItem(header)
        item.Gesture <- gesture key modifiers
        item.Command <- command
        item

    let private nativePlainCommandItem header command =
        let item = NativeMenuItem(header)
        item.Command <- command
        item

    let private menuItem header key modifiers action =
        let item = MenuItem(Header = header)
        item.InputGesture <- gesture key modifiers
        item.Click.Add(fun _ -> action ())
        item

    let private plainMenuItem header action =
        let item = MenuItem(Header = header)
        item.Click.Add(fun _ -> action ())
        item

    let private toggleMenuItem toggleType header action =
        let item = plainMenuItem header action
        item.ToggleType <- toggleType
        item

    let private commandMenuItem header key modifiers command =
        let item = MenuItem(Header = header)
        item.InputGesture <- gesture key modifiers
        item.Command <- command
        item

    let private plainCommandMenuItem header command =
        MenuItem(Header = header, Command = command)

    let create owner isMacOS platformModifier (viewModel: MainWindowViewModel) actions =
        let menuBar = Menu()
        menuBar.IsVisible <- not isMacOS

        let nativeOpenRecentMenu = NativeMenu()
        let nativeOpenRecentItem = NativeMenuItem("Open Recent")
        nativeOpenRecentItem.Menu <- nativeOpenRecentMenu

        let nativeClearRecentItem =
            nativePlainCommandItem "Clear Recent" viewModel.ClearRecentCommand

        let nativeRunPauseItem =
            nativeCommandItem "Run" Key.P platformModifier viewModel.RunPauseCommand

        let nativeResetItem =
            nativeCommandItem "Reset" Key.R platformModifier viewModel.ResetCommand

        let nativeSaveStateItem =
            nativeItem "Save State" Key.S platformModifier actions.SaveState

        let nativeLoadStateItem =
            nativeItem "Load State" Key.L platformModifier actions.LoadState

        let nativeInputMappingItem = nativePlain "Input Mapping..." actions.OpenInputMapping
        let nativeSettingsItem = nativePlain "Settings..." actions.OpenSettings

        let nativeFullscreenItem =
            nativeItem "Full Screen" Key.F platformModifier actions.ToggleFullScreen

        nativeFullscreenItem.ToggleType <- MenuItemToggleType.CheckBox

        let nativeFullScreenInfoItem =
            nativeToggleItem MenuItemToggleType.CheckBox "Full Screen Info" actions.ToggleFullScreenInfo

        let nativeFloatingItem =
            nativeItem "Floating Mode" Key.F (platformModifier ||| KeyModifiers.Shift) actions.ToggleFloating

        nativeFloatingItem.ToggleType <- MenuItemToggleType.CheckBox

        let nativeAlwaysOnTopItem =
            nativeToggleItem MenuItemToggleType.CheckBox "Always on Top" actions.ToggleAlwaysOnTop

        let nativeScaleItems =
            [ 1, nativeItem "Scale x1" Key.D1 platformModifier (fun () -> actions.SetScale 1)
              2, nativeItem "Scale x2" Key.D2 platformModifier (fun () -> actions.SetScale 2)
              4, nativeItem "Scale x4" Key.D4 platformModifier (fun () -> actions.SetScale 4)
              8, nativeItem "Scale x8" Key.D8 platformModifier (fun () -> actions.SetScale 8) ]
            |> List.map (fun (scale, item) ->
                item.ToggleType <- MenuItemToggleType.Radio
                scale, item)

        let nativeVideoFilterMenu = NativeMenu()
        let nativeVideoFilterItem = NativeMenuItem("Image Filter")
        nativeVideoFilterItem.Menu <- nativeVideoFilterMenu

        let nativeVideoFilterItems =
            [ AppSettings.Off,
              nativeToggleItem MenuItemToggleType.Radio "Off" (fun () -> actions.SetVideoFilter AppSettings.Off)
              AppSettings.Smooth,
              nativeToggleItem MenuItemToggleType.Radio "Smooth" (fun () -> actions.SetVideoFilter AppSettings.Smooth)
              AppSettings.Lcd,
              nativeToggleItem MenuItemToggleType.Radio "LCD" (fun () -> actions.SetVideoFilter AppSettings.Lcd) ]

        for _, item in nativeVideoFilterItems do
            nativeVideoFilterMenu.Items.Add item |> ignore

        let openRecentMenu = MenuItem(Header = "Open Recent")

        let clearRecentItem =
            plainCommandMenuItem "Clear Recent" viewModel.ClearRecentCommand

        let runPauseItem =
            commandMenuItem "Run" Key.P platformModifier viewModel.RunPauseCommand

        let resetMenuItem =
            commandMenuItem "Reset" Key.R platformModifier viewModel.ResetCommand

        let saveStateItem = menuItem "Save State" Key.S platformModifier actions.SaveState
        let loadStateItem = menuItem "Load State" Key.L platformModifier actions.LoadState
        let inputMappingItem = plainMenuItem "Input Mapping..." actions.OpenInputMapping
        let settingsItem = plainMenuItem "Settings..." actions.OpenSettings

        let fullscreenItem =
            menuItem "Full Screen" Key.F platformModifier actions.ToggleFullScreen

        fullscreenItem.ToggleType <- MenuItemToggleType.CheckBox

        let fullScreenInfoItem =
            toggleMenuItem MenuItemToggleType.CheckBox "Full Screen Info" actions.ToggleFullScreenInfo

        let floatingItem =
            menuItem "Floating Mode" Key.F (platformModifier ||| KeyModifiers.Shift) actions.ToggleFloating

        floatingItem.ToggleType <- MenuItemToggleType.CheckBox

        let alwaysOnTopItem =
            toggleMenuItem MenuItemToggleType.CheckBox "Always on Top" actions.ToggleAlwaysOnTop

        let scaleItems =
            [ 1, menuItem "Scale x1" Key.D1 platformModifier (fun () -> actions.SetScale 1)
              2, menuItem "Scale x2" Key.D2 platformModifier (fun () -> actions.SetScale 2)
              4, menuItem "Scale x4" Key.D4 platformModifier (fun () -> actions.SetScale 4)
              8, menuItem "Scale x8" Key.D8 platformModifier (fun () -> actions.SetScale 8) ]
            |> List.map (fun (scale, item) ->
                item.ToggleType <- MenuItemToggleType.Radio
                scale, item)

        let videoFilterMenu = MenuItem(Header = "Image Filter")

        let videoFilterItems =
            [ AppSettings.Off,
              toggleMenuItem MenuItemToggleType.Radio "Off" (fun () -> actions.SetVideoFilter AppSettings.Off)
              AppSettings.Smooth,
              toggleMenuItem MenuItemToggleType.Radio "Smooth" (fun () -> actions.SetVideoFilter AppSettings.Smooth)
              AppSettings.Lcd,
              toggleMenuItem MenuItemToggleType.Radio "LCD" (fun () -> actions.SetVideoFilter AppSettings.Lcd) ]

        for _, item in videoFilterItems do
            videoFilterMenu.Items.Add item |> ignore

        let rebuildRecentMenus (recentRoms: string list) =
            nativeOpenRecentMenu.Items.Clear()
            openRecentMenu.Items.Clear()

            if List.isEmpty recentRoms then
                let nativeEmpty = NativeMenuItem("(Empty)")
                nativeEmpty.IsEnabled <- false
                nativeOpenRecentMenu.Items.Add nativeEmpty |> ignore
                let empty = MenuItem(Header = "(Empty)", IsEnabled = false)
                openRecentMenu.Items.Add empty |> ignore
            else
                for path in recentRoms do
                    let label = Path.GetFileName path
                    let nativeRecent = nativePlain label (fun () -> actions.LoadRecent path)
                    nativeRecent.ToolTip <- path
                    nativeOpenRecentMenu.Items.Add nativeRecent |> ignore
                    let recent = plainMenuItem label (fun () -> actions.LoadRecent path)
                    openRecentMenu.Items.Add recent |> ignore

            nativeClearRecentItem.IsEnabled <- not (List.isEmpty recentRoms)
            clearRecentItem.IsEnabled <- nativeClearRecentItem.IsEnabled

        let updateMenuState state =
            nativeRunPauseItem.Header <- viewModel.RunPauseHeader
            runPauseItem.Header <- viewModel.RunPauseHeader
            nativeRunPauseItem.IsEnabled <- viewModel.HasSession
            runPauseItem.IsEnabled <- viewModel.HasSession
            nativeResetItem.IsEnabled <- viewModel.HasLoadedRom
            resetMenuItem.IsEnabled <- viewModel.HasLoadedRom
            nativeSaveStateItem.IsEnabled <- viewModel.HasSession
            saveStateItem.IsEnabled <- viewModel.HasSession
            nativeLoadStateItem.IsEnabled <- viewModel.HasSession
            loadStateItem.IsEnabled <- viewModel.HasSession
            nativeFullscreenItem.IsChecked <- state.IsFullScreen
            fullscreenItem.IsChecked <- state.IsFullScreen
            nativeFullScreenInfoItem.IsChecked <- state.ShowFullScreenInfo
            fullScreenInfoItem.IsChecked <- state.ShowFullScreenInfo
            nativeFloatingItem.IsChecked <- state.IsFloating
            floatingItem.IsChecked <- state.IsFloating
            nativeAlwaysOnTopItem.IsChecked <- state.IsAlwaysOnTop
            alwaysOnTopItem.IsChecked <- state.IsAlwaysOnTop
            nativeAlwaysOnTopItem.IsEnabled <- state.IsFloating
            alwaysOnTopItem.IsEnabled <- state.IsFloating

            for scale, item in nativeScaleItems do
                item.IsChecked <- (scale = viewModel.SelectedScale)

            for scale, item in scaleItems do
                item.IsChecked <- (scale = viewModel.SelectedScale)

            for filter, item in nativeVideoFilterItems do
                item.IsChecked <- (filter = state.VideoFilter)

            for filter, item in videoFilterItems do
                item.IsChecked <- (filter = state.VideoFilter)

        let nativeMenu = NativeMenu()
        let nativeFileMenu = NativeMenuItem("File")
        let nativeFileSubmenu = NativeMenu()

        nativeFileSubmenu.Items.Add(nativeCommandItem "Open ROM..." Key.O platformModifier viewModel.OpenRomCommand)
        |> ignore

        nativeFileSubmenu.Items.Add nativeOpenRecentItem |> ignore
        nativeFileSubmenu.Items.Add nativeClearRecentItem |> ignore
        nativeFileMenu.Menu <- nativeFileSubmenu

        let nativeEmulationMenu = NativeMenuItem("Emulation")
        let nativeEmulationSubmenu = NativeMenu()
        nativeEmulationSubmenu.Items.Add nativeRunPauseItem |> ignore
        nativeEmulationSubmenu.Items.Add nativeResetItem |> ignore
        nativeEmulationSubmenu.Items.Add nativeSaveStateItem |> ignore
        nativeEmulationSubmenu.Items.Add nativeLoadStateItem |> ignore
        nativeEmulationSubmenu.Items.Add(NativeMenuItemSeparator()) |> ignore
        nativeEmulationSubmenu.Items.Add nativeSettingsItem |> ignore
        nativeEmulationSubmenu.Items.Add nativeInputMappingItem |> ignore
        nativeEmulationMenu.Menu <- nativeEmulationSubmenu

        let nativeViewMenu = NativeMenuItem("View")
        let nativeViewSubmenu = NativeMenu()

        for _, item in nativeScaleItems do
            nativeViewSubmenu.Items.Add item |> ignore

        nativeViewSubmenu.Items.Add nativeVideoFilterItem |> ignore
        nativeViewSubmenu.Items.Add(NativeMenuItemSeparator()) |> ignore
        nativeViewSubmenu.Items.Add nativeFullscreenItem |> ignore
        nativeViewSubmenu.Items.Add nativeFullScreenInfoItem |> ignore
        nativeViewSubmenu.Items.Add nativeFloatingItem |> ignore
        nativeViewSubmenu.Items.Add nativeAlwaysOnTopItem |> ignore
        nativeViewMenu.Menu <- nativeViewSubmenu

        nativeMenu.Items.Add nativeFileMenu |> ignore
        nativeMenu.Items.Add nativeEmulationMenu |> ignore
        nativeMenu.Items.Add nativeViewMenu |> ignore
        NativeMenu.SetMenu(owner, nativeMenu)

        let fileMenu = MenuItem(Header = "File")

        fileMenu.Items.Add(commandMenuItem "Open ROM..." Key.O platformModifier viewModel.OpenRomCommand)
        |> ignore

        fileMenu.Items.Add openRecentMenu |> ignore
        fileMenu.Items.Add clearRecentItem |> ignore
        fileMenu.Items.Add(Separator()) |> ignore
        fileMenu.Items.Add(plainMenuItem "Quit" actions.Close) |> ignore

        let emulationMenu = MenuItem(Header = "Emulation")
        emulationMenu.Items.Add runPauseItem |> ignore
        emulationMenu.Items.Add resetMenuItem |> ignore
        emulationMenu.Items.Add saveStateItem |> ignore
        emulationMenu.Items.Add loadStateItem |> ignore
        emulationMenu.Items.Add(Separator()) |> ignore
        emulationMenu.Items.Add settingsItem |> ignore
        emulationMenu.Items.Add inputMappingItem |> ignore

        let viewMenu = MenuItem(Header = "View")

        for _, item in scaleItems do
            viewMenu.Items.Add item |> ignore

        viewMenu.Items.Add videoFilterMenu |> ignore
        viewMenu.Items.Add(Separator()) |> ignore
        viewMenu.Items.Add fullscreenItem |> ignore
        viewMenu.Items.Add fullScreenInfoItem |> ignore
        viewMenu.Items.Add floatingItem |> ignore
        viewMenu.Items.Add alwaysOnTopItem |> ignore

        let helpMenu = MenuItem(Header = "Help")
        helpMenu.Items.Add(plainMenuItem "About BubiBoy" actions.ShowAbout) |> ignore

        menuBar.Items.Add fileMenu |> ignore
        menuBar.Items.Add emulationMenu |> ignore
        menuBar.Items.Add viewMenu |> ignore

        if not isMacOS then
            menuBar.Items.Add helpMenu |> ignore

        { MenuBar = menuBar
          Refresh =
            fun state ->
                menuBar.IsVisible <- not isMacOS && not state.IsFloating
                rebuildRecentMenus state.RecentRoms
                updateMenuState state }
