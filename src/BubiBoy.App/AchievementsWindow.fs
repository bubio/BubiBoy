namespace BubiBoy.App

open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media.Imaging
open Avalonia.Threading
open BubiBoy.RetroAchievements

type AchievementsWindow(client: RaClient) as this =
    inherit Window()

    let http = new HttpClient(MaxResponseContentBufferSize = 1024L * 1024L)
    let imageCache = Dictionary<string, Bitmap>()
    let imageCacheOrder = Queue<string>()
    let root = StackPanel(Spacing = 12.0, Margin = Thickness(18.0))
    let mutable closed = false

    let statusText status =
        match status with
        | Disabled -> "Disabled"
        | LoggedOut -> "Not logged in"
        | Authenticating -> "Logging in..."
        | Ready -> "Logged in; no achievement game loaded"
        | LoadingGame -> "Identifying game..."
        | Active -> "Active (Softcore)"
        | OfflineSession reason -> $"Offline session: {reason}"

    let loadImage generation url (image: Image) =
        if not (String.IsNullOrWhiteSpace url) then
            match imageCache.TryGetValue url with
            | true, bitmap -> image.Source <- bitmap
            | _ ->
                task {
                    try
                        let uri = Uri url

                        if uri.Scheme = Uri.UriSchemeHttps then
                            let! bytes = http.GetByteArrayAsync uri

                            if bytes.Length <= 1024 * 1024 then
                                use stream = new MemoryStream(bytes)
                                let bitmap = new Bitmap(stream)

                                if bitmap.PixelSize.Width <= 2048 && bitmap.PixelSize.Height <= 2048 then
                                    Dispatcher.UIThread.Post(fun () ->
                                        if not closed && client.Snapshot.Generation = generation then
                                            if imageCache.Count >= 128 then
                                                let expired = imageCacheOrder.Dequeue()
                                                imageCache.Remove expired |> ignore

                                            imageCache[url] <- bitmap
                                            imageCacheOrder.Enqueue url
                                            image.Source <- bitmap)
                                else
                                    bitmap.Dispose()
                    with _ ->
                        ()
                }
                |> ignore

    let achievementRow generation (achievement: RaAchievement) =
        let image = Image(Width = 48.0, Height = 48.0)
        loadImage generation achievement.ImageUrl image

        let title =
            TextBlock(
                Text = $"{achievement.Title}  ({achievement.Points} pts)",
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            )

        let rarity = achievement.Rarity.ToString("F2")

        let progress =
            if String.IsNullOrWhiteSpace achievement.MeasuredProgress then
                $"Rarity: {rarity}" + "%"
            else
                $"{achievement.MeasuredProgress}  |  Rarity: {rarity}" + "%"

        let details =
            TextBlock(
                Text = $"{achievement.Description}\n{progress}",
                Opacity = 0.78,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            )

        let text = StackPanel(Spacing = 3.0)
        text.Children.Add title |> ignore
        text.Children.Add details |> ignore

        let grid = Grid(ColumnDefinitions = ColumnDefinitions("56,*"))
        Grid.SetColumn(text, 1)
        grid.Children.Add image |> ignore
        grid.Children.Add text |> ignore
        DialogLayout.surface grid (Thickness(10.0))

    let rebuild () =
        let snapshot = client.Snapshot
        let scrollContent = StackPanel(Spacing = 10.0)
        root.Children.Clear()

        let header = DialogLayout.title "RetroAchievements"
        root.Children.Add header |> ignore
        root.Children.Add(DialogLayout.bodyText (statusText snapshot.Status)) |> ignore

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
            root.Children.Add(DialogLayout.surface panel (Thickness(12.0))) |> ignore
        | Some user ->
            let logout = DialogLayout.actionButton "Log Out" 100.0
            logout.Click.Add(fun _ -> client.Logout())

            root.Children.Add(DialogLayout.bodyText ($"{user.DisplayName}  |  Softcore score: {user.SoftcoreScore}"))
            |> ignore

            root.Children.Add logout |> ignore

        match snapshot.Game with
        | Some game ->
            let gameImage = Image(Width = 64.0, Height = 64.0)
            loadImage snapshot.Generation game.ImageUrl gameImage
            let gameTitle = DialogLayout.title game.Title
            let gameHeader = Grid(ColumnDefinitions = ColumnDefinitions("72,*"))
            Grid.SetColumn(gameTitle, 1)
            gameHeader.Children.Add gameImage |> ignore
            gameHeader.Children.Add gameTitle |> ignore
            scrollContent.Children.Add gameHeader |> ignore

            snapshot.Achievements
            |> List.groupBy (fun achievement -> achievement.Bucket, achievement.BucketLabel)
            |> List.iter (fun ((_, label), items) ->
                scrollContent.Children.Add(DialogLayout.title label) |> ignore

                items
                |> List.iter (achievementRow snapshot.Generation >> scrollContent.Children.Add >> ignore))
        | None -> ()

        if scrollContent.Children.Count > 0 then
            root.Children.Add(ScrollViewer(Content = scrollContent, MaxHeight = 500.0))
            |> ignore

    do
        this.Title <- "RetroAchievements"
        this.Width <- 680.0
        this.Height <- 720.0
        this.MinWidth <- 480.0
        this.MinHeight <- 360.0
        this.WindowStartupLocation <- WindowStartupLocation.CenterOwner
        this.FontFamily <- AppFonts.ui
        AppTheme.bindBrush this Window.BackgroundProperty AppTheme.WindowBackground
        this.Content <- ScrollViewer(Content = root)

        client.Changed.Add(fun _ ->
            Dispatcher.UIThread.Post(fun () ->
                if not closed then
                    rebuild ()))

        this.Closed.Add(fun _ ->
            closed <- true
            http.Dispose())

        rebuild ()

    static member Show(owner: Window, client: RaClient) = AchievementsWindow(client).Show(owner)
