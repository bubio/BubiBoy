namespace BubiBoy.App

open Avalonia.Controls
open Avalonia.Platform
open BubiBoy.Core

/// Owns window chrome, scale, and floating-mode layout changes.
type WindowLayoutController
    (
        owner: Window,
        isMacOS: bool,
        initialScale: int,
        viewport: GameViewport.Elements,
        statusBar: Border,
        toast: AppChrome.Toast
    ) =
    let mutable selectedScale = initialScale
    let mutable isFloating = false
    let mutable menuBar: Menu option = None
    let mutable contentGrid: Grid option = None

    let updateContentRows () =
        contentGrid
        |> Option.iter (fun grid ->
            grid.RowDefinitions <-
                if isFloating then
                    RowDefinitions("0,*,0")
                else
                    RowDefinitions("Auto,*,Auto"))

    let applyWindowChrome () =
        if isFloating then
            if owner.WindowState = WindowState.FullScreen then
                owner.WindowState <- WindowState.Normal

            owner.WindowDecorations <- WindowDecorations.BorderOnly
            owner.ExtendClientAreaToDecorationsHint <- true
            owner.ExtendClientAreaTitleBarHeightHint <- 0.0
            owner.CanResize <- false
            statusBar.IsVisible <- false
            statusBar.MinHeight <- 0.0
            statusBar.Height <- 0.0
            menuBar |> Option.iter (fun menu -> menu.IsVisible <- false)
            toast.Host.IsVisible <- false
        else
            owner.ExtendClientAreaToDecorationsHint <- false
            owner.ExtendClientAreaTitleBarHeightHint <- -1.0
            owner.WindowDecorations <- WindowDecorations.Full
            owner.CanResize <- false
            statusBar.IsVisible <- true
            statusBar.MinHeight <- AppChrome.StatusBarHeight
            statusBar.Height <- AppChrome.StatusBarHeight
            menuBar |> Option.iter (fun menu -> menu.IsVisible <- not isMacOS)

        updateContentRows ()

    let applyScale resizeWindow =
        let videoWidth = float Hardware.ScreenWidth * float selectedScale
        let videoHeight = float Hardware.ScreenHeight * float selectedScale
        let isFullScreen = owner.WindowState = WindowState.FullScreen
        viewport.ApplyScale selectedScale owner.WindowState

        if resizeWindow && not isFullScreen then
            let menuHeight = if isMacOS || isFloating then 0.0 else 28.0

            let statusHeight = if isFloating then 0.0 else AppChrome.StatusBarHeight

            owner.Width <- videoWidth
            owner.Height <- videoHeight + menuHeight + statusHeight

    /// Gets the active integer viewport scale.
    member _.SelectedScale = selectedScale

    /// Gets whether floating mode is active.
    member _.IsFloating = isFloating

    /// Attaches controls that are created after menu actions are wired.
    member _.Attach(menu: Menu, content: Grid) =
        menuBar <- Some menu
        contentGrid <- Some content

    /// Applies the initial chrome and scale after all controls are attached.
    member _.ApplyInitialLayout() =
        applyWindowChrome ()
        applyScale true

    /// Applies a normalized viewport scale.
    member _.SetScale(scale: int) =
        selectedScale <- scale
        applyScale true

    /// Enables or disables floating mode.
    member _.SetFloating(enabled: bool) =
        isFloating <- enabled
        applyWindowChrome ()
        applyScale true

    /// Refreshes viewport sizing after an external window-state change.
    member _.HandleWindowStateChanged() = applyScale false

    /// Toggles native fullscreen state.
    member _.ToggleFullScreen() =
        owner.WindowState <-
            if owner.WindowState = WindowState.FullScreen then
                WindowState.Normal
            else
                WindowState.FullScreen
