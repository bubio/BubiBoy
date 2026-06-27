namespace BubiBoy.App

open System
open System.Collections.Generic
open System.Net.Http
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Threading
open BubiBoy.RetroAchievements
open RetroAchievementsPresentation

module internal RetroAchievementsOverlay =
    let private cardBackground = SolidColorBrush(Color.Parse("#D9263448"))
    let private borderBrush = SolidColorBrush(Color.Parse("#669EADBF"))

    let private badge size (item: IndicatorItem) (tryGetBitmap: string -> Bitmap option) requestImage =
        let image = Image(Width = size, Height = size, Stretch = Stretch.Uniform)

        match tryGetBitmap item.ImageUrl with
        | Some bitmap -> image.Source <- bitmap
        | None -> requestImage item

        image

    let private challengeRow tryGetBitmap requestImage item =
        let image = badge 36.0 item tryGetBitmap requestImage

        let border =
            Border(
                Child = image,
                Width = 44.0,
                Height = 44.0,
                Padding = Thickness(4.0),
                Background = cardBackground,
                BorderBrush = borderBrush,
                BorderThickness = Thickness(1.0),
                CornerRadius = CornerRadius(6.0)
            )

        ToolTip.SetTip(border, item.Title)
        border

    let private progressCard tryGetBitmap requestImage item =
        let image = badge 48.0 item tryGetBitmap requestImage

        let title =
            TextBlock(
                Text = item.Title,
                FontSize = 13.0,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 230.0
            )

        let details =
            StackPanel(Spacing = 4.0, VerticalAlignment = VerticalAlignment.Center)

        details.Children.Add title |> ignore

        if not (String.IsNullOrWhiteSpace item.MeasuredProgress) then
            details.Children.Add(TextBlock(Text = item.MeasuredProgress, FontSize = 12.0, Foreground = Brushes.White))
            |> ignore

        match measuredPercent item with
        | Some percent ->
            details.Children.Add(ProgressBar(Minimum = 0.0, Maximum = 100.0, Value = percent, Height = 6.0))
            |> ignore
        | None -> ()

        let content =
            Grid(ColumnDefinitions = ColumnDefinitions("Auto,*"), ColumnSpacing = 10.0)

        Grid.SetColumn(image, 0)
        Grid.SetColumn(details, 1)
        content.Children.Add image |> ignore
        content.Children.Add details |> ignore

        Border(
            Child = content,
            MinWidth = 220.0,
            MaxWidth = 330.0,
            Padding = Thickness(10.0),
            Background = cardBackground,
            BorderBrush = borderBrush,
            BorderThickness = Thickness(1.0),
            CornerRadius = CornerRadius(7.0)
        )

    let private leaderboardTracker (tracker: LeaderboardTracker) =
        Border(
            Child =
                TextBlock(
                    Text = tracker.Display,
                    FontSize = 14.0,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brushes.White
                ),
            MinWidth = 100.0,
            Padding = Thickness(10.0, 6.0),
            Background = cardBackground,
            BorderBrush = borderBrush,
            BorderThickness = Thickness(1.0),
            CornerRadius = CornerRadius(6.0)
        )

    let private scoreboardCard (scoreboard: LeaderboardScoreboard) =
        let details = StackPanel(Spacing = 3.0)

        details.Children.Add(
            TextBlock(
                Text = scoreboard.Title,
                FontSize = 13.0,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White
            )
        )
        |> ignore

        details.Children.Add(
            TextBlock(
                Text =
                    $"Rank {scoreboard.Rank} / {scoreboard.TotalEntries}  |  {scoreboard.SubmittedScore}  |  Best {scoreboard.BestScore}",
                FontSize = 12.0,
                Foreground = Brushes.White
            )
        )
        |> ignore

        scoreboard.TopEntries
        |> List.truncate 3
        |> List.iter (fun entry ->
            details.Children.Add(
                TextBlock(
                    Text = $"#{entry.Rank} {entry.Username}  {entry.Score}",
                    FontSize = 11.0,
                    Foreground = Brushes.White
                )
            )
            |> ignore)

        Border(
            Child = details,
            MinWidth = 250.0,
            MaxWidth = 400.0,
            Padding = Thickness(10.0),
            Background = cardBackground,
            BorderBrush = borderBrush,
            BorderThickness = Thickness(1.0),
            CornerRadius = CornerRadius(7.0)
        )

    let buildView state tryGetBitmap requestImage =
        let view = Grid(IsHitTestVisible = false, ClipToBounds = true)

        let challenges =
            StackPanel(
                Spacing = 6.0,
                Margin = Thickness(10.0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top
            )

        state.Challenges
        |> List.iter (challengeRow tryGetBitmap requestImage >> challenges.Children.Add >> ignore)

        challenges.IsVisible <- challenges.Children.Count > 0
        view.Children.Add challenges |> ignore

        let trackers =
            StackPanel(
                Spacing = 6.0,
                Margin = Thickness(10.0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            )

        state.LeaderboardTrackers
        |> List.iter (leaderboardTracker >> trackers.Children.Add >> ignore)

        trackers.IsVisible <- trackers.Children.Count > 0
        view.Children.Add trackers |> ignore

        state.Progress
        |> Option.iter (fun item ->
            let progress = progressCard tryGetBitmap requestImage item
            progress.Margin <- Thickness(10.0)
            progress.HorizontalAlignment <- HorizontalAlignment.Right
            progress.VerticalAlignment <- VerticalAlignment.Bottom
            view.Children.Add progress |> ignore)

        state.Scoreboard
        |> Option.iter (fun item ->
            let scoreboard = scoreboardCard item
            scoreboard.Margin <- Thickness(10.0)
            scoreboard.HorizontalAlignment <- HorizontalAlignment.Left
            scoreboard.VerticalAlignment <- VerticalAlignment.Bottom
            view.Children.Add scoreboard |> ignore)

        view

type internal RetroAchievementsOverlayController(host: Grid, client: RaClient) =
    let http = new HttpClient()
    let loader = RaImageLoader(http, fun () -> client.Snapshot)
    let images = Dictionary<string, Bitmap>()
    let pending = HashSet<struct (uint32 * int64 * string)>()
    let mutable state = synchronizeOverlay client.Snapshot emptyOverlayState
    let mutable disposed = false
    let mutable cachedImagePixels = 0L

    let scoreboardTimer =
        DispatcherTimer(Interval = RetroAchievementsPresentation.ScoreboardDisplayDuration)

    let dispatch action =
        if Dispatcher.UIThread.CheckAccess() then
            action ()
        else
            Dispatcher.UIThread.Post(Action action)

    let disposeUnusedImages () =
        let activeUrls =
            seq {
                for item in state.Challenges do
                    yield item.ImageUrl

                match state.Progress with
                | Some item -> yield item.ImageUrl
                | None -> ()
            }
            |> Set.ofSeq

        images.Keys
        |> Seq.filter (activeUrls.Contains >> not)
        |> Seq.toArray
        |> Array.iter (fun url ->
            cachedImagePixels <- cachedImagePixels - RetroAchievementsPresentation.imagePixelCount images[url]

            images[url].Dispose()
            images.Remove url |> ignore)

    let rec render () =
        if not disposed then
            host.Children.Clear()
            disposeUnusedImages ()

            let tryGet url =
                match images.TryGetValue url with
                | true, bitmap -> Some bitmap
                | _ -> None

            let requestImage (item: IndicatorItem) =
                if not (String.IsNullOrWhiteSpace item.ImageUrl) then
                    let key = struct (item.AchievementId, item.Revision, item.ImageUrl)

                    if pending.Add key then
                        match state.Session with
                        | Some session ->
                            task {
                                let! result = loader.Load(session.Generation, session.GameId, item.ImageUrl)

                                dispatch (fun () ->
                                    pending.Remove key |> ignore

                                    match result with
                                    | Some bitmap when not disposed && containsIndicator item state ->
                                        match images.TryGetValue item.ImageUrl with
                                        | true, existing ->
                                            bitmap.Dispose()
                                            GC.KeepAlive existing
                                        | _ ->
                                            if
                                                RetroAchievementsPresentation.canCacheImage cachedImagePixels bitmap
                                            then
                                                images[item.ImageUrl] <- bitmap

                                                cachedImagePixels <-
                                                    cachedImagePixels
                                                    + RetroAchievementsPresentation.imagePixelCount bitmap
                                            else
                                                bitmap.Dispose()

                                        render ()
                                    | Some bitmap -> bitmap.Dispose()
                                    | None -> ())
                            }
                            |> ignore
                        | None -> pending.Remove key |> ignore

            host.Children.Add(RetroAchievementsOverlay.buildView state tryGet requestImage)
            |> ignore

    let synchronize snapshot =
        let previousSession = state.Session
        state <- synchronizeOverlay snapshot state

        if state.Session <> previousSession then
            scoreboardTimer.Stop()
            host.Children.Clear()
            pending.Clear()

            for bitmap in images.Values do
                bitmap.Dispose()

            images.Clear()
            cachedImagePixels <- 0L

        render ()

    let changedSubscription =
        client.Changed.Subscribe(fun snapshot -> dispatch (fun () -> synchronize snapshot))

    let eventSubscription =
        client.EventRaised.Subscribe(fun event ->
            dispatch (fun () ->
                state <- reduceOverlay client.Snapshot event state

                if
                    event.EventType = 13u
                    && RetroAchievementsPresentation.isCurrentOverlayEvent event state
                    && state.Scoreboard.IsSome
                then
                    scoreboardTimer.Stop()
                    scoreboardTimer.Start()

                render ()))

    do
        scoreboardTimer.Tick.Add(fun _ ->
            scoreboardTimer.Stop()
            state <- RetroAchievementsPresentation.clearScoreboard state
            render ())

        render ()

    member internal _.State = state

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                scoreboardTimer.Stop()
                changedSubscription.Dispose()
                eventSubscription.Dispose()
                pending.Clear()

                for bitmap in images.Values do
                    bitmap.Dispose()

                images.Clear()
                cachedImagePixels <- 0L
                host.Children.Clear()
                http.Dispose()
