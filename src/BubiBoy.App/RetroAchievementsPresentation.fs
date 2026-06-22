namespace BubiBoy.App

open System
open System.IO
open System.Net.Http
open Avalonia.Controls
open Avalonia.Media.Imaging
open BubiBoy.RetroAchievements

module internal RetroAchievementsPresentation =
    let notificationMessage (event: RaEvent) =
        match event.EventType with
        | 1u -> Some $"Achievement unlocked: {event.Title}"
        | 15u -> Some "All achievements completed."
        | 16u -> Some $"RetroAchievements server error: {event.Description}"
        | 17u -> Some "RetroAchievements disconnected; unlocks are pending."
        | 18u -> Some "RetroAchievements reconnected."
        | _ -> None

    let private bucketOrder bucket =
        match bucket with
        | 1uy -> 0 // Locked
        | 2uy -> 1 // Unlocked
        | 5uy -> 2 // Recently Unlocked
        | 6uy -> 3 // Active Challenge
        | 7uy -> 4 // Almost There
        | 3uy -> 5 // Unsupported
        | 4uy -> 6 // Unofficial
        | 8uy -> 7 // Unsynced
        | _ -> 8

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

    member _.Load(generation: int64, gameId: uint32, url: string) =
        task {
            if not (RetroAchievementsPresentation.canRequestImage url) then
                return None
            else
                try
                    let! bytes = http.GetByteArrayAsync(Uri url)

                    if bytes.Length > maxBytes then
                        return None
                    else
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
