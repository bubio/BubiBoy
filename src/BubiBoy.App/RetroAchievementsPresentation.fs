namespace BubiBoy.App

open System
open System.IO
open System.Net.Http
open System.Threading
open Avalonia.Controls
open Avalonia.Media.Imaging
open BubiBoy.RetroAchievements

module internal RetroAchievementsPresentation =
    [<Literal>]
    let MaxCachedImagePixels = 8 * 1024 * 1024

    type AchievementSortColumn =
        | OriginalOrder
        | Status
        | Title
        | Points
        | Rarity
        | Progress

    type SortDirection =
        | Ascending
        | Descending

    type HostAction =
        | Ignore
        | Notify of message: string
        | ResetRequested

    type IndicatorItem =
        { AchievementId: uint32
          Title: string
          ImageUrl: string
          MeasuredProgress: string
          MeasuredPercent: float32
          Revision: int64 }

    type OverlaySession = { Generation: int64; GameId: uint32 }

    type OverlayState =
        { Session: OverlaySession option
          Challenges: IndicatorItem list
          Progress: IndicatorItem option
          NextRevision: int64 }

    let emptyOverlayState =
        { Session = None
          Challenges = []
          Progress = None
          NextRevision = 1L }

    let private snapshotSession (snapshot: RaSnapshot) =
        match snapshot.Status, snapshot.Game with
        | Active, Some game ->
            Some
                { Generation = snapshot.Generation
                  GameId = game.Id }
        | _ -> None

    let synchronizeOverlay (snapshot: RaSnapshot) (state: OverlayState) =
        let session = snapshotSession snapshot

        if session = state.Session then
            state
        else
            { emptyOverlayState with
                Session = session }

    let private item revision (event: RaEvent) =
        { AchievementId = event.RelatedId
          Title = event.Title
          ImageUrl = event.ImageUrl
          MeasuredProgress = event.MeasuredProgress
          MeasuredPercent = event.MeasuredPercent
          Revision = revision }

    let private replaceChallenge (next: IndicatorItem) (challenges: IndicatorItem list) =
        let mutable replaced = false

        let updated =
            challenges
            |> List.map (fun current ->
                if current.AchievementId = next.AchievementId then
                    replaced <- true
                    next
                else
                    current)

        if replaced then updated else updated @ [ next ]

    let reduceOverlay (snapshot: RaSnapshot) (event: RaEvent) (state: OverlayState) =
        let current = synchronizeOverlay snapshot state

        match current.Session with
        | Some session when event.Generation = session.Generation && event.GameId = session.GameId ->
            match event.EventType with
            | 1u ->
                { current with
                    Challenges =
                        current.Challenges
                        |> List.filter (fun value -> value.AchievementId <> event.RelatedId)
                    Progress =
                        current.Progress
                        |> Option.filter (fun value -> value.AchievementId <> event.RelatedId) }
            | 5u ->
                let next = item current.NextRevision event

                { current with
                    Challenges = replaceChallenge next current.Challenges
                    NextRevision = current.NextRevision + 1L }
            | 6u ->
                { current with
                    Challenges =
                        current.Challenges
                        |> List.filter (fun value -> value.AchievementId <> event.RelatedId) }
            | 7u ->
                { current with
                    Progress = Some(item current.NextRevision event)
                    NextRevision = current.NextRevision + 1L }
            | 8u -> { current with Progress = None }
            | 9u ->
                let revision =
                    current.Progress
                    |> Option.filter (fun value ->
                        value.AchievementId = event.RelatedId && value.ImageUrl = event.ImageUrl)
                    |> Option.map (fun value -> value.Revision)
                    |> Option.defaultValue current.NextRevision

                { current with
                    Progress = Some(item revision event)
                    NextRevision = max current.NextRevision (revision + 1L) }
            | _ -> current
        | _ -> current

    let containsIndicator (item: IndicatorItem) state =
        let matches value =
            value.AchievementId = item.AchievementId
            && value.Revision = item.Revision
            && value.ImageUrl = item.ImageUrl

        state.Challenges |> List.exists matches
        || state.Progress |> Option.exists matches

    let measuredPercent (item: IndicatorItem) =
        let value = float item.MeasuredPercent

        if Double.IsNaN value || Double.IsInfinity value || value < 0.0 || value > 100.0 then
            None
        else
            Some value

    let hostAction (event: RaEvent) =
        match event.EventType with
        | 1u -> Notify $"Achievement unlocked: {event.Title}"
        | 14u -> ResetRequested
        | 15u -> Notify "All achievements completed."
        | 16u -> Notify $"RetroAchievements server error: {event.Description}"
        | 17u -> Notify "RetroAchievements disconnected; unlocks are pending."
        | 18u -> Notify "RetroAchievements reconnected."
        | _ -> Ignore

    let notificationMessage (event: RaEvent) =
        match hostAction event with
        | Notify message -> Some message
        | Ignore
        | ResetRequested -> None

    let private bucketOrder bucket =
        match bucket with
        | 5uy -> 0 // Recently Unlocked
        | 6uy -> 1 // Active Challenge
        | 7uy -> 2 // Almost There
        | 2uy -> 3 // Unlocked
        | 1uy -> 4 // Locked
        | 3uy -> 5 // Unsupported
        | 4uy -> 6 // Unofficial
        | 8uy -> 7 // Unsynced
        | _ -> 8

    let private normalizedProgress (achievement: RaAchievement) =
        if
            Single.IsNaN achievement.MeasuredPercent
            || Single.IsInfinity achievement.MeasuredPercent
        then
            -1.0f
        else
            achievement.MeasuredPercent

    let sortAchievements column direction achievements =
        let indexed = achievements |> List.indexed

        let sorted =
            match column with
            | OriginalOrder -> indexed
            | Status ->
                indexed
                |> List.sortBy (fun (_, achievement) ->
                    bucketOrder achievement.Bucket, achievement.Title.ToUpperInvariant(), achievement.Id)
            | Title ->
                indexed
                |> List.sortBy (fun (_, achievement) -> achievement.Title.ToUpperInvariant(), achievement.Id)
            | Points ->
                indexed
                |> List.sortBy (fun (_, achievement) -> achievement.Points, achievement.Title.ToUpperInvariant())
            | Rarity ->
                indexed
                |> List.sortBy (fun (_, achievement) -> achievement.Rarity, achievement.Title.ToUpperInvariant())
            | Progress ->
                indexed
                |> List.sortBy (fun (_, achievement) ->
                    normalizedProgress achievement, achievement.Title.ToUpperInvariant())

        match direction with
        | Ascending -> sorted
        | Descending -> List.rev sorted

    let nextSort currentColumn currentDirection selectedColumn =
        if currentColumn = selectedColumn then
            selectedColumn,
            match currentDirection with
            | Ascending -> Descending
            | Descending -> Ascending
        else
            selectedColumn, Ascending

    let achievementGroups achievements =
        achievements
        |> List.groupBy (fun achievement -> achievement.Bucket, achievement.BucketLabel)
        |> List.sortBy (fun ((bucket, _), _) -> bucketOrder bucket)

    let canRequestImage (url: string) =
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, uri -> uri.Scheme = Uri.UriSchemeHttps
        | _ -> false

    let isCurrentImage generation gameId (snapshot: RaSnapshot) =
        snapshot.Generation = generation
        && (snapshot.Game |> Option.exists (fun game -> game.Id = gameId))

    let imagePixelCountForDimensions width height = int64 width * int64 height

    let imagePixelCount (bitmap: Bitmap) =
        imagePixelCountForDimensions bitmap.PixelSize.Width bitmap.PixelSize.Height

    let canCacheImageDimensions currentPixels width height =
        currentPixels + imagePixelCountForDimensions width height
        <= int64 MaxCachedImagePixels

    let canCacheImage currentPixels (bitmap: Bitmap) =
        canCacheImageDimensions currentPixels bitmap.PixelSize.Width bitmap.PixelSize.Height

    let populateAchievementGroups
        (container: StackPanel)
        (createHeading: string -> Control)
        (createRow: RaAchievement -> Control)
        achievements
        =
        achievementGroups achievements
        |> List.iter (fun ((_, label), items) ->
            container.Children.Add(createHeading label) |> ignore
            items |> List.iter (createRow >> container.Children.Add >> ignore))

type internal RaImageLoader(http: HttpClient, getSnapshot: unit -> RaSnapshot) =
    static let maxBytes = 1024 * 1024

    let readBounded (response: HttpResponseMessage) cancellationToken =
        task {
            let contentLength = response.Content.Headers.ContentLength

            if contentLength.HasValue && contentLength.Value > int64 maxBytes then
                return None
            else
                use! source = response.Content.ReadAsStreamAsync(cancellationToken)
                use destination = new MemoryStream()
                let buffer = Array.zeroCreate<byte> 81920
                let mutable total = 0
                let mutable finished = false

                while not finished do
                    let! read = source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)

                    if read = 0 then
                        finished <- true
                    elif total + read > maxBytes then
                        total <- maxBytes + 1
                        finished <- true
                    else
                        do! destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        total <- total + read

                if total > maxBytes then
                    return None
                else
                    return Some(destination.ToArray())
        }

    member _.Load(generation: int64, gameId: uint32, url: string) =
        task {
            if not (RetroAchievementsPresentation.canRequestImage url) then
                return None
            else
                try
                    use request = new HttpRequestMessage(HttpMethod.Get, Uri url)

                    use! response =
                        http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None)

                    let! bytes = readBounded response CancellationToken.None

                    match bytes with
                    | None -> return None
                    | Some bytes ->
                        use stream = new MemoryStream(bytes)
                        let bitmap = new Bitmap(stream)

                        if
                            bitmap.PixelSize.Width > 2048
                            || bitmap.PixelSize.Height > 2048
                            || not (RetroAchievementsPresentation.isCurrentImage generation gameId (getSnapshot ()))
                        then
                            bitmap.Dispose()
                            return None
                        else
                            return Some bitmap
                with _ ->
                    return None
        }
