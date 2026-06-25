namespace BubiBoy.App

open System
open System.Collections.Generic
open System.Net.Http
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Imaging
open Avalonia.Threading
open BubiBoy.RetroAchievements

type AchievementsWindow(client: RaClient) as this =
    inherit Window()

    let httpHandler = new SocketsHttpHandler(MaxConnectionsPerServer = 4)

    let http =
        new HttpClient(httpHandler, true, MaxResponseContentBufferSize = 1024L * 1024L)

    let imageLoader = RaImageLoader(http, fun () -> client.Snapshot)
    let imageCache = Dictionary<string, Bitmap>()

    let pendingImages =
        Dictionary<struct (int64 * uint32 * string), ResizeArray<WeakReference<Image>>>()

    let root = Grid(RowDefinitions = RowDefinitions("Auto,*"), Margin = Thickness(18.0))
    let header = StackPanel(Spacing = 10.0, Margin = Thickness(0.0, 0.0, 0.0, 12.0))
    let contentHost = ContentControl()
    let mutable closed = false
    let mutable imageCacheSession: struct (int64 * uint32) option = None
    let mutable cachedImagePixels = 0L
    let mutable sortColumn = RetroAchievementsPresentation.OriginalOrder
    let mutable sortDirection = RetroAchievementsPresentation.Ascending

    let clearImageCache () =
        for bitmap in imageCache.Values do
            bitmap.Dispose()

        imageCache.Clear()
        cachedImagePixels <- 0L

    let updateImageCacheSession snapshot =
        let session =
            snapshot.Game |> Option.map (fun game -> struct (snapshot.Generation, game.Id))

        if session <> imageCacheSession then
            clearImageCache ()
            imageCacheSession <- session

    let statusText status =
        match status with
        | Disabled -> "Disabled"
        | LoggedOut -> "Not logged in"
        | Authenticating -> "Logging in..."
        | Ready -> "Logged in; no achievement game loaded"
        | LoadingGame -> "Identifying game..."
        | Active when client.Snapshot.HardcoreEnabled -> "Active (Hardcore)"
        | Active -> "Active (Softcore)"
        | OfflineSession reason -> $"Offline session: {reason}"

    let loadImage generation gameId url (image: Image) =
        if not (String.IsNullOrWhiteSpace url) then
            match imageCache.TryGetValue url with
            | true, bitmap -> image.Source <- bitmap
            | _ ->
                let key = struct (generation, gameId, url)

                match pendingImages.TryGetValue key with
                | true, waiters -> waiters.Add(WeakReference<Image>(image))
                | _ ->
                    pendingImages[key] <- ResizeArray([ WeakReference<Image>(image) ])

                    task {
                        let! result = imageLoader.Load(generation, gameId, url)

                        Dispatcher.UIThread.Post(fun () ->
                            let waiters =
                                match pendingImages.TryGetValue key with
                                | true, value -> value
                                | _ -> ResizeArray()

                            pendingImages.Remove key |> ignore

                            result
                            |> Option.iter (fun bitmap ->
                                if
                                    not closed
                                    && RetroAchievementsPresentation.isCurrentImage generation gameId client.Snapshot
                                then
                                    match imageCache.TryGetValue url with
                                    | true, cached ->
                                        bitmap.Dispose()

                                        for waiter in waiters do
                                            match waiter.TryGetTarget() with
                                            | true, target -> target.Source <- cached
                                            | _ -> ()
                                    | _ ->
                                        if RetroAchievementsPresentation.canCacheImage cachedImagePixels bitmap then
                                            imageCache[url] <- bitmap

                                            cachedImagePixels <-
                                                cachedImagePixels
                                                + RetroAchievementsPresentation.imagePixelCount bitmap

                                            for waiter in waiters do
                                                match waiter.TryGetTarget() with
                                                | true, target -> target.Source <- bitmap
                                                | _ -> ()
                                        else
                                            bitmap.Dispose()
                                else
                                    bitmap.Dispose()))
                    }
                    |> ignore

    let tableColumns () =
        ColumnDefinitions("48,56,120,*,64,76,100")

    let cellText text =
        let value =
            TextBlock(
                Text = text,
                FontSize = DialogLayout.BodyFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            )

        AppTheme.bindBrush value TextBlock.ForegroundProperty AppTheme.SecondaryText
        value

    let addCell column (control: Control) (grid: Grid) =
        Grid.SetColumn(control, column)
        grid.Children.Add control |> ignore

    let achievementRow generation gameId (originalIndex, achievement: RaAchievement) =
        let image =
            Image(Width = 44.0, Height = 44.0, VerticalAlignment = VerticalAlignment.Top)

        loadImage generation gameId achievement.ImageUrl image

        let title =
            TextBlock(Text = achievement.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap)

        AppTheme.bindBrush title TextBlock.ForegroundProperty AppTheme.PrimaryText

        let description =
            TextBlock(
                Text = achievement.Description,
                FontSize = DialogLayout.CaptionFontSize,
                TextWrapping = TextWrapping.Wrap
            )

        AppTheme.bindBrush description TextBlock.ForegroundProperty AppTheme.SecondaryText

        let achievementText = StackPanel(Spacing = 2.0)
        achievementText.Children.Add title |> ignore
        achievementText.Children.Add description |> ignore

        let progress =
            if String.IsNullOrWhiteSpace achievement.MeasuredProgress then
                let percent = float achievement.MeasuredPercent

                if Double.IsNaN percent || Double.IsInfinity percent || percent <= 0.0 then
                    ""
                else
                    achievement.MeasuredPercent.ToString("F0") + "%"
            else
                achievement.MeasuredProgress

        let grid =
            Grid(ColumnDefinitions = tableColumns (), MinHeight = 58.0, Margin = Thickness(0.0, 0.0, 0.0, 6.0))

        addCell 0 (cellText (string (originalIndex + 1))) grid
        addCell 1 image grid
        addCell 2 (cellText achievement.BucketLabel) grid
        addCell 3 achievementText grid
        addCell 4 (cellText (string achievement.Points)) grid
        addCell 5 (cellText (achievement.Rarity.ToString("F2") + "%")) grid
        addCell 6 (cellText progress) grid
        DialogLayout.surface grid (Thickness(8.0))

    let mutable rebuild = fun () -> ()

    let sortHeader label column =
        let marker =
            if sortColumn = column then
                match sortDirection with
                | RetroAchievementsPresentation.Ascending -> "  ↑"
                | RetroAchievementsPresentation.Descending -> "  ↓"
            else
                ""

        let button =
            Button(
                Content = label + marker,
                Padding = Thickness(4.0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontWeight = FontWeight.SemiBold
            )

        button.Click.Add(fun _ ->
            let nextColumn, nextDirection =
                RetroAchievementsPresentation.nextSort sortColumn sortDirection column

            sortColumn <- nextColumn
            sortDirection <- nextDirection
            rebuild ())

        button

    let achievementTable generation gameId achievements =
        let table = Grid(RowDefinitions = RowDefinitions("Auto,*"))

        let headings =
            Grid(ColumnDefinitions = tableColumns (), Margin = Thickness(8.0, 0.0, 8.0, 6.0))

        addCell 0 (sortHeader "#" RetroAchievementsPresentation.OriginalOrder) headings
        addCell 2 (sortHeader "Status" RetroAchievementsPresentation.Status) headings
        addCell 3 (sortHeader "Achievement" RetroAchievementsPresentation.Title) headings
        addCell 4 (sortHeader "Pts" RetroAchievementsPresentation.Points) headings
        addCell 5 (sortHeader "Rarity" RetroAchievementsPresentation.Rarity) headings
        addCell 6 (sortHeader "Progress" RetroAchievementsPresentation.Progress) headings
        table.Children.Add headings |> ignore

        let rows = StackPanel()

        achievements
        |> RetroAchievementsPresentation.sortAchievements sortColumn sortDirection
        |> List.iter (achievementRow generation gameId >> rows.Children.Add >> ignore)

        let scroll =
            ScrollViewer(Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto)

        Grid.SetRow(scroll, 1)
        table.Children.Add scroll |> ignore
        table

    let rebuildContent () =
        let snapshot = client.Snapshot
        header.Children.Clear()
        contentHost.Content <- null
        updateImageCacheSession snapshot

        header.Children.Add(DialogLayout.title "RetroAchievements") |> ignore

        header.Children.Add(DialogLayout.bodyText (statusText snapshot.Status))
        |> ignore

        match snapshot.User with
        | None ->
            let username = TextBox(PlaceholderText = "Username")
            let password = TextBox(PlaceholderText = "Password", PasswordChar = '●')
            let login = DialogLayout.actionButton "Log In" 100.0
            login.IsEnabled <- snapshot.Status <> Authenticating

            login.Click.Add(fun _ ->
                if
                    not (String.IsNullOrWhiteSpace username.Text)
                    && not (String.IsNullOrEmpty password.Text)
                then
                    let secret = password.Text
                    password.Text <- ""
                    client.LoginWithPassword(username.Text.Trim(), secret))

            let panel = StackPanel(Spacing = 8.0)
            panel.Children.Add username |> ignore
            panel.Children.Add password |> ignore
            panel.Children.Add login |> ignore
            header.Children.Add(DialogLayout.surface panel (Thickness(12.0))) |> ignore
        | Some user ->
            let account = Grid(ColumnDefinitions = ColumnDefinitions("*,Auto"))

            let scoreLabel, score =
                if snapshot.HardcoreEnabled then
                    "Hardcore score", user.Score
                else
                    "Softcore score", user.SoftcoreScore

            account.Children.Add(DialogLayout.bodyText ($"{user.DisplayName}  |  {scoreLabel}: {score}"))
            |> ignore

            let logout = DialogLayout.actionButton "Log Out" 100.0
            logout.Click.Add(fun _ -> client.Logout())
            Grid.SetColumn(logout, 1)
            account.Children.Add logout |> ignore
            header.Children.Add account |> ignore

        match snapshot.Game with
        | Some game ->
            let gameImage = Image(Width = 56.0, Height = 56.0)
            loadImage snapshot.Generation game.Id game.ImageUrl gameImage
            let gameDetails = StackPanel(Spacing = 5.0)
            gameDetails.Children.Add(DialogLayout.title game.Title) |> ignore

            snapshot.RichPresence
            |> Option.iter (fun message ->
                let presence =
                    TextBlock(Text = message, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap)

                AppTheme.bindBrush presence TextBlock.ForegroundProperty AppTheme.SecondaryText
                gameDetails.Children.Add presence |> ignore)

            let gameHeader = Grid(ColumnDefinitions = ColumnDefinitions("64,*"))
            Grid.SetColumn(gameDetails, 1)
            gameHeader.Children.Add gameImage |> ignore
            gameHeader.Children.Add gameDetails |> ignore
            header.Children.Add(DialogLayout.surface gameHeader (Thickness(8.0))) |> ignore

            if List.isEmpty snapshot.Achievements then
                contentHost.Content <- DialogLayout.bodyText "No achievements are available for this game."
            else
                contentHost.Content <- achievementTable snapshot.Generation game.Id snapshot.Achievements
        | None -> ()

    do
        rebuild <- rebuildContent
        Grid.SetRow(contentHost, 1)
        root.Children.Add header |> ignore
        root.Children.Add contentHost |> ignore

        this.Title <- "RetroAchievements"
        this.Width <- 820.0
        this.Height <- 720.0
        this.MinWidth <- 700.0
        this.MinHeight <- 420.0
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.FontFamily <- AppFonts.ui
        AppTheme.bindBrush this Window.BackgroundProperty AppTheme.WindowBackground
        this.Content <- root

        let changedSubscription =
            client.Changed.Subscribe(fun _ ->
                Dispatcher.UIThread.Post(fun () ->
                    if not closed then
                        rebuild ()))

        this.Closed.Add(fun _ ->
            closed <- true
            changedSubscription.Dispose()
            clearImageCache ()
            pendingImages.Clear()
            http.Dispose())

        rebuild ()
