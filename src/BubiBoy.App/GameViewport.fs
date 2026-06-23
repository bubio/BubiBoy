namespace BubiBoy.App

open System
open System.Diagnostics
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Shapes
open Avalonia.Input
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Threading
open BubiBoy.Core
open BubiBoy.IO

module GameViewport =
    [<Literal>]
    let private MinimumPanelWidth = 96.0

    [<Literal>]
    let private FullPanelWidth = 180.0

    [<Literal>]
    let private PanelTravel = 48.0

    [<Literal>]
    let private PanelMovementMilliseconds = 2000.0

    [<Literal>]
    let private PanelFadeMilliseconds = 1500.0

    [<Literal>]
    let private PanelOpacity = 0.82

    type Elements =
        { Host: Grid
          Framebuffer: Border
          RetroAchievementsOverlayHost: Grid
          PresentFrame: uint32[] -> unit
          SetVideoFilter: AppSettings.VideoFilter -> unit
          ApplyScale: int -> WindowState -> unit
          ApplyBackground: WindowState -> unit
          UpdateSessionInfo: string option -> bool -> unit
          SetSidePanelsEnabled: bool -> unit
          StopTimers: unit -> unit }

    let create (owner: Window) initialSidePanelsEnabled (initialVideoFilter: AppSettings.VideoFilter) =
        let fullscreenBackground = Brushes.Black :> IBrush
        let primaryText = SolidColorBrush(Color.Parse("#D7DBE0"))
        let secondaryText = SolidColorBrush(Color.Parse("#7E858E"))
        let runningBrush = SolidColorBrush(Color.Parse("#3BA55D"))
        let pausedBrush = SolidColorBrush(Color.Parse("#626973"))

        let framebuffer =
            Border(
                Width = float Hardware.ScreenWidth * 2.0,
                Height = float Hardware.ScreenHeight * 2.0,
                Background = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            )

        let videoDisplay =
            VideoDisplay(
                initialVideoFilter,
                Width = float Hardware.ScreenWidth * 2.0,
                Height = float Hardware.ScreenHeight * 2.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            )

        videoDisplay.PresentFrame(Video.blankFrame ())
        videoDisplay.SetVideoFilter initialVideoFilter
        framebuffer.Child <- videoDisplay

        let host = Grid()
        AppTheme.bindBrush host Panel.BackgroundProperty AppTheme.WindowBackground

        let romTitle =
            TextBlock(
                FontFamily = AppFonts.ui,
                FontSize = 19.0,
                FontWeight = FontWeight.Medium,
                Foreground = primaryText,
                TextAlignment = TextAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 240.0
            )

        let statusDot =
            Ellipse(Width = 7.0, Height = 7.0, Fill = pausedBrush, VerticalAlignment = VerticalAlignment.Center)

        let statusText =
            TextBlock(
                Text = "PAUSED",
                FontFamily = AppFonts.ui,
                FontSize = 11.0,
                FontWeight = FontWeight.Medium,
                Foreground = secondaryText,
                LetterSpacing = 1.2,
                VerticalAlignment = VerticalAlignment.Center
            )

        let statusRow =
            StackPanel(
                Orientation = Orientation.Horizontal,
                Spacing = 7.0,
                HorizontalAlignment = HorizontalAlignment.Right
            )

        statusRow.Children.Add statusDot |> ignore
        statusRow.Children.Add statusText |> ignore

        let leftContent =
            StackPanel(
                Spacing = 10.0,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            )

        leftContent.Children.Add romTitle |> ignore
        leftContent.Children.Add statusRow |> ignore

        let leftPanel =
            Border(Child = leftContent, Padding = Thickness(12.0, 0.0, 28.0, 0.0))

        let leftTransform = TranslateTransform(-PanelTravel, 0.0)
        leftPanel.RenderTransform <- leftTransform

        let clockText =
            TextBlock(
                FontFamily = AppFonts.ui,
                FontSize = 48.0,
                FontWeight = FontWeight.Light,
                Foreground = primaryText
            )

        let dateText =
            TextBlock(
                FontFamily = AppFonts.ui,
                FontSize = 13.0,
                Foreground = secondaryText,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 240.0
            )

        let rightContent =
            StackPanel(
                Spacing = 5.0,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            )

        rightContent.Children.Add clockText |> ignore
        rightContent.Children.Add dateText |> ignore

        let rightPanel =
            Border(Child = rightContent, Padding = Thickness(28.0, 0.0, 12.0, 0.0))

        let rightTransform = TranslateTransform(PanelTravel, 0.0)
        rightPanel.RenderTransform <- rightTransform

        let sideOverlay =
            Grid(
                ColumnDefinitions = ColumnDefinitions("0,*,0"),
                IsVisible = false,
                IsHitTestVisible = false,
                Opacity = 0.0
            )

        Grid.SetColumn(leftPanel, 0)
        Grid.SetColumn(rightPanel, 2)
        sideOverlay.Children.Add leftPanel |> ignore
        sideOverlay.Children.Add rightPanel |> ignore

        let updateClock () =
            let now = DateTime.Now
            clockText.Text <- now.ToString("HH:mm", Globalization.CultureInfo.CurrentCulture)
            dateText.Text <- now.ToString("D", Globalization.CultureInfo.CurrentCulture)

        let clockTimer = DispatcherTimer(Interval = TimeSpan.FromMinutes(1.0))

        clockTimer.Tick.Add(fun _ -> updateClock ())

        let mutable isFullScreen = false
        let mutable sidePanelsEnabled = initialSidePanelsEnabled
        let mutable panelsAreEligible = false

        let fadeClock = Stopwatch()
        let fadeTimer = DispatcherTimer(Interval = TimeSpan.FromMilliseconds(16.0))

        fadeTimer.Tick.Add(fun _ ->
            let movementProgress =
                Math.Clamp(fadeClock.Elapsed.TotalMilliseconds / PanelMovementMilliseconds, 0.0, 1.0)

            let fadeProgress =
                Math.Clamp(fadeClock.Elapsed.TotalMilliseconds / PanelFadeMilliseconds, 0.0, 1.0)

            let movementRemaining = 1.0 - movementProgress
            let movementEased = 1.0 - movementRemaining * movementRemaining * movementRemaining
            let fadeRemaining = 1.0 - fadeProgress
            let fadeEased = 1.0 - fadeRemaining * fadeRemaining * fadeRemaining
            sideOverlay.Opacity <- PanelOpacity * fadeEased
            leftTransform.X <- -PanelTravel * (1.0 - movementEased)
            rightTransform.X <- PanelTravel * (1.0 - movementEased)

            if movementProgress >= 1.0 && fadeProgress >= 1.0 then
                fadeTimer.Stop()
                fadeClock.Stop())

        let startFadeIn () =
            fadeTimer.Stop()
            fadeClock.Restart()
            sideOverlay.Opacity <- 0.0
            leftTransform.X <- -PanelTravel
            rightTransform.X <- PanelTravel
            fadeTimer.Start()

        let hideImmediately () =
            fadeTimer.Stop()
            fadeClock.Reset()
            sideOverlay.Opacity <- 0.0
            leftTransform.X <- -PanelTravel
            rightTransform.X <- PanelTravel
            sideOverlay.IsVisible <- false

        let revealTimer = DispatcherTimer(Interval = TimeSpan.FromSeconds(3.0))

        revealTimer.Tick.Add(fun _ ->
            revealTimer.Stop()

            if panelsAreEligible then
                startFadeIn ())

        let updateSideLayout () =
            let bounds = host.Bounds
            let aspectRatio = float Hardware.ScreenWidth / float Hardware.ScreenHeight
            let displayedWidth = Math.Min(bounds.Width, bounds.Height * aspectRatio)
            let panelWidth = Math.Max(0.0, (bounds.Width - displayedWidth) / 2.0)

            let showPanels =
                isFullScreen && sidePanelsEnabled && panelWidth >= MinimumPanelWidth

            let showFullDetails = panelWidth >= FullPanelWidth

            if showPanels then
                let columns = ColumnDefinitions()
                columns.Add(ColumnDefinition(GridLength(panelWidth)))
                columns.Add(ColumnDefinition(GridLength(1.0, GridUnitType.Star)))
                columns.Add(ColumnDefinition(GridLength(panelWidth)))
                sideOverlay.ColumnDefinitions <- columns

                romTitle.IsVisible <- showFullDetails
                dateText.IsVisible <- showFullDetails
                clockText.FontSize <- if showFullDetails then 48.0 else 32.0

            if showPanels <> panelsAreEligible then
                panelsAreEligible <- showPanels
                revealTimer.Stop()

                if showPanels then
                    sideOverlay.Opacity <- 0.0
                    leftTransform.X <- -PanelTravel
                    rightTransform.X <- PanelTravel
                    sideOverlay.IsVisible <- true
                    revealTimer.Start()
                else
                    hideImmediately ()

        host.SizeChanged.Add(fun _ -> updateSideLayout ())

        host.PointerPressed.Add(fun args ->
            let pointer = args.GetCurrentPoint(host)

            if
                not args.Handled
                && pointer.Properties.IsLeftButtonPressed
                && owner.WindowState <> WindowState.FullScreen
            then
                owner.BeginMoveDrag(args)
                args.Handled <- true)

        let applyBackground windowState =
            if windowState = WindowState.FullScreen then
                host.Background <- fullscreenBackground
            else
                AppTheme.bindBrush host Panel.BackgroundProperty AppTheme.WindowBackground

        let applyScale scale windowState =
            let videoWidth = float Hardware.ScreenWidth * float scale
            let videoHeight = float Hardware.ScreenHeight * float scale
            isFullScreen <- windowState = WindowState.FullScreen

            if isFullScreen then
                framebuffer.Width <- Double.NaN
                framebuffer.Height <- Double.NaN
                framebuffer.HorizontalAlignment <- HorizontalAlignment.Stretch
                framebuffer.VerticalAlignment <- VerticalAlignment.Stretch
                videoDisplay.Width <- Double.NaN
                videoDisplay.Height <- Double.NaN
                videoDisplay.HorizontalAlignment <- HorizontalAlignment.Stretch
                videoDisplay.VerticalAlignment <- VerticalAlignment.Stretch
            else
                framebuffer.Width <- videoWidth
                framebuffer.Height <- videoHeight
                framebuffer.HorizontalAlignment <- HorizontalAlignment.Center
                framebuffer.VerticalAlignment <- VerticalAlignment.Center
                videoDisplay.Width <- videoWidth
                videoDisplay.Height <- videoHeight
                videoDisplay.HorizontalAlignment <- HorizontalAlignment.Center
                videoDisplay.VerticalAlignment <- VerticalAlignment.Center

            if isFullScreen && sidePanelsEnabled then
                updateClock ()

                if not clockTimer.IsEnabled then
                    clockTimer.Start()
            else
                clockTimer.Stop()

            applyBackground windowState
            updateSideLayout ()

        let setSidePanelsEnabled enabled =
            sidePanelsEnabled <- enabled

            if isFullScreen && enabled then
                updateClock ()

                if not clockTimer.IsEnabled then
                    clockTimer.Start()
            else
                clockTimer.Stop()

            updateSideLayout ()

            if isFullScreen && enabled && panelsAreEligible then
                revealTimer.Stop()
                startFadeIn ()

        let presentFrame (pixels: uint32[]) = videoDisplay.PresentFrame pixels

        let updateSessionInfo romDisplayName running =
            match romDisplayName with
            | Some title when not (String.IsNullOrWhiteSpace title) ->
                romTitle.Text <- title
                statusRow.IsVisible <- true
            | _ ->
                romTitle.Text <- "NO GAME LOADED"
                romTitle.IsVisible <- true
                statusRow.IsVisible <- false

            if running then
                statusDot.Fill <- runningBrush
                statusText.Text <- "RUNNING"
            else
                statusDot.Fill <- pausedBrush
                statusText.Text <- "PAUSED"

            updateSideLayout ()

        let retroAchievementsOverlayHost =
            Grid(IsHitTestVisible = false, ClipToBounds = true)

        host.Children.Add framebuffer |> ignore
        host.Children.Add sideOverlay |> ignore
        host.Children.Add retroAchievementsOverlayHost |> ignore
        updateSessionInfo None false

        { Host = host
          Framebuffer = framebuffer
          RetroAchievementsOverlayHost = retroAchievementsOverlayHost
          PresentFrame = presentFrame
          SetVideoFilter = videoDisplay.SetVideoFilter
          ApplyScale = applyScale
          ApplyBackground = applyBackground
          UpdateSessionInfo = updateSessionInfo
          SetSidePanelsEnabled = setSidePanelsEnabled
          StopTimers =
            fun () ->
                revealTimer.Stop()
                fadeTimer.Stop()
                clockTimer.Stop()
                videoDisplay.Dispose() }
