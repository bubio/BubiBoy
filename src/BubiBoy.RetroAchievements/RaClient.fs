namespace BubiBoy.RetroAchievements

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Net.Http
open System.Runtime.InteropServices
open System.Text
open System.Threading
open System.Threading.Tasks
open BubiBoy.Core

type private PendingOperation =
    | NoOperation
    | Login of username: string
    | LoadGame

type RaClient private (httpClient: HttpClient, log: string -> unit) =
    let gate = obj ()
    let completions = ConcurrentQueue<unit -> unit>()

    let requests =
        ConcurrentDictionary<struct (unativeint * int64), CancellationTokenSource>()

    let changed = Event<RaSnapshot>()
    let eventRaised = Event<RaEvent>()
    let mutable handle = nativeint 0
    let mutable disposed = false
    let mutable status = LoggedOut
    let mutable user: RaUser option = None
    let mutable game: RaGame option = None
    let mutable achievements: RaAchievement list = []
    let mutable currentSession: Emulator.Session option = None
    let mutable generation = 0L
    let mutable pendingOperation = NoOperation
    let mutable lastIdle = Stopwatch.GetTimestamp()
    let mutable achievementsDirty = false

    let snapshot () =
        { Status = status
          User = user
          Game = game
          Achievements = achievements
          Generation = generation }

    let publish () = changed.Trigger(snapshot ())

    let stringBuffer (capacity: int) = StringBuilder(capacity)

    let refreshUser () =
        let username = stringBuffer 256
        let displayName = stringBuffer 256
        let token = stringBuffer 512
        let mutable score = 0u
        let mutable softcoreScore = 0u

        if
            NativeInterop.Native.bubi_ra_get_user (
                handle,
                username,
                unativeint username.Capacity,
                displayName,
                unativeint displayName.Capacity,
                token,
                unativeint token.Capacity,
                &score,
                &softcoreScore
            )
            <> 0
        then
            let next =
                { Username = username.ToString()
                  DisplayName = displayName.ToString()
                  Score = score
                  SoftcoreScore = softcoreScore }

            user <- Some next

            match RaCredentialStore.saveToken next.Username (token.ToString()) with
            | Ok() -> ()
            | Error message -> log $"RetroAchievements token was not saved: {message}"
        else
            user <- None

    let refreshGame () =
        let mutable gameId = 0u
        let title = stringBuffer 512
        let hash = stringBuffer 64
        let imageUrl = stringBuffer 2048

        if
            NativeInterop.Native.bubi_ra_get_game (
                handle,
                &gameId,
                title,
                unativeint title.Capacity,
                hash,
                unativeint hash.Capacity,
                imageUrl,
                unativeint imageUrl.Capacity
            )
            <> 0
        then
            game <-
                Some
                    { Id = gameId
                      Title = title.ToString()
                      Hash = hash.ToString()
                      ImageUrl = imageUrl.ToString() }
        else
            game <- None

    let mutable achievementBuffer = ResizeArray<RaAchievement>()

    let achievementCallback =
        NativeInterop.AchievementCallback
            (fun
                _
                bucket
                bucketLabel
                id
                title
                description
                points
                measuredProgress
                measuredPercent
                rarity
                state
                unlocked
                imageUrl ->
                achievementBuffer.Add
                    { Bucket = bucket
                      BucketLabel = Option.ofObj bucketLabel |> Option.defaultValue ""
                      Id = id
                      Title = Option.ofObj title |> Option.defaultValue ""
                      Description = Option.ofObj description |> Option.defaultValue ""
                      Points = points
                      MeasuredProgress = Option.ofObj measuredProgress |> Option.defaultValue ""
                      MeasuredPercent = measuredPercent
                      Rarity = rarity
                      State = state
                      Unlocked = unlocked
                      ImageUrl = Option.ofObj imageUrl |> Option.defaultValue "" })

    let refreshAchievements () =
        achievementBuffer <- ResizeArray()
        NativeInterop.Native.bubi_ra_enumerate_achievements (handle, achievementCallback)
        achievements <- achievementBuffer |> Seq.toList

    let operationCallback =
        NativeInterop.OperationCallback(fun _ result errorMessage ->
            let errorMessage =
                Option.ofObj errorMessage
                |> Option.defaultValue "RetroAchievements operation failed."

            match pendingOperation with
            | Login username ->
                pendingOperation <- NoOperation

                if result = 0 then
                    refreshUser ()
                    status <- Ready
                    log $"RetroAchievements login succeeded for {username}."
                else
                    RaCredentialStore.deleteToken username
                    user <- None
                    status <- LoggedOut
                    log $"RetroAchievements login failed: {errorMessage}"

                publish ()
            | LoadGame ->
                pendingOperation <- NoOperation

                if result = 0 then
                    refreshGame ()
                    refreshAchievements ()
                    status <- Active
                else
                    game <- None
                    achievements <- []
                    status <- OfflineSession errorMessage
                    log $"RetroAchievements game load failed: {errorMessage}"

                currentSession <- None
                publish ()
            | NoOperation -> ())

    let readMemoryCallback =
        NativeInterop.ReadMemoryCallback(fun _ address destination count ->
            match currentSession with
            | None -> 0u
            | Some session ->
                let buffer = Array.zeroCreate<byte> (int count)
                let copied = Emulator.readInspectionMemory address buffer 0 buffer.Length session

                if copied > 0 then
                    Marshal.Copy(buffer, 0, destination, copied)

                uint32 copied)

    let completeHttp requestId statusCode (body: byte[]) =
        lock gate (fun () ->
            if not disposed then
                NativeInterop.Native.bubi_ra_complete_server_request (
                    handle,
                    requestId,
                    statusCode,
                    body,
                    unativeint body.Length
                ))

    let cancelHttpRequests () =
        for request in requests.Values do
            request.Cancel()

    let serverRequestCallback =
        NativeInterop.ServerRequestCallback(fun _ requestId url postData contentType ->
            let requestGeneration = generation
            let requestKey = struct (requestId, requestGeneration)
            let cts = new CancellationTokenSource(TimeSpan.FromSeconds 30.0)
            requests[requestKey] <- cts

            let runRequest () =
                task {
                    let mutable statusCode = -2
                    let mutable body = Array.empty<byte>

                    try
                        let method = if isNull postData then HttpMethod.Get else HttpMethod.Post
                        use request = new HttpRequestMessage(method, url)

                        if not (isNull postData) then
                            request.Content <-
                                new StringContent(
                                    postData,
                                    Encoding.UTF8,
                                    Option.ofObj contentType
                                    |> Option.defaultValue "application/x-www-form-urlencoded"
                                )

                        use! response =
                            httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)

                        let! bytes = response.Content.ReadAsByteArrayAsync(cts.Token)

                        if bytes.Length > 8 * 1024 * 1024 then
                            statusCode <- -1
                        else
                            statusCode <- int response.StatusCode
                            body <- bytes
                    with
                    | :? OperationCanceledException -> statusCode <- -2
                    | ex ->
                        log $"RetroAchievements HTTP request failed: {ex.Message}"
                        statusCode <- -2

                    requests.TryRemove requestKey |> ignore
                    cts.Dispose()

                    completions.Enqueue(fun () ->
                        if requestGeneration = generation then
                            completeHttp requestId statusCode body)
                }

            Task.Run(Func<Task>(fun () -> runRequest () :> Task)) |> ignore)

    let eventCallback =
        NativeInterop.EventCallback(fun _ eventType relatedId title description imageUrl ->
            let event =
                { EventType = eventType
                  RelatedId = relatedId
                  Title = Option.ofObj title |> Option.defaultValue ""
                  Description = Option.ofObj description |> Option.defaultValue ""
                  ImageUrl = Option.ofObj imageUrl |> Option.defaultValue "" }

            if eventType = 1u then
                achievementsDirty <- true

            eventRaised.Trigger event)

    let logCallback =
        NativeInterop.LogCallback(fun _ _ message ->
            message |> Option.ofObj |> Option.iter (fun value -> log $"rcheevos: {value}"))

    // Native code retains these function pointers for the lifetime of the client.
    // Keep explicit managed roots so the delegates cannot be collected between calls.
    let nativeCallbackRoots: Delegate array =
        [| readMemoryCallback
           serverRequestCallback
           eventCallback
           logCallback
           operationCallback |]

    let drainCompletions () =
        let mutable action = Unchecked.defaultof<unit -> unit>

        while completions.TryDequeue(&action) do
            action ()

    do
        handle <-
            NativeInterop.Native.bubi_ra_create (
                readMemoryCallback,
                serverRequestCallback,
                eventCallback,
                logCallback,
                nativeint 0
            )

        if handle = nativeint 0 then
            invalidOp "Could not create the RetroAchievements client."

        let clause = StringBuilder(128)

        NativeInterop.Native.bubi_ra_user_agent (handle, clause, unativeint clause.Capacity)
        |> ignore

        if clause.Length > 0 then
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(clause.ToString())

    member _.Snapshot = lock gate snapshot
    member _.Changed = changed.Publish
    member _.EventRaised = eventRaised.Publish
    member _.Version = NativeInterop.Native.bubi_ra_version ()

    member _.LoginWithPassword(username: string, password: string) =
        lock gate (fun () ->
            if disposed then
                invalidOp "RetroAchievements client is disposed."
            elif pendingOperation <> NoOperation then
                invalidOp "Another RetroAchievements operation is in progress."
            else
                status <- Authenticating
                pendingOperation <- Login username
                publish ()
                NativeInterop.Native.bubi_ra_login_password (handle, username, password, operationCallback))

    member _.LoginWithStoredToken(username: string) =
        match RaCredentialStore.tryLoadToken username with
        | None -> false
        | Some token ->
            lock gate (fun () ->
                status <- Authenticating
                pendingOperation <- Login username
                publish ()
                NativeInterop.Native.bubi_ra_login_token (handle, username, token, operationCallback))

            true

    member _.Logout() =
        lock gate (fun () ->
            user |> Option.iter (fun value -> RaCredentialStore.deleteToken value.Username)
            generation <- generation + 1L
            cancelHttpRequests ()
            NativeInterop.Native.bubi_ra_cancel_operation handle
            NativeInterop.Native.bubi_ra_abort_server_requests handle
            pendingOperation <- NoOperation
            NativeInterop.Native.bubi_ra_logout handle
            currentSession <- None
            user <- None
            game <- None
            achievements <- []
            status <- LoggedOut
            publish ())

    member _.LoadGame(consoleId: uint32, rom: byte[], session: Emulator.Session) =
        lock gate (fun () ->
            if status <> Ready then
                invalidOp "RetroAchievements login must complete before loading a game."

            generation <- generation + 1L
            status <- LoadingGame
            pendingOperation <- LoadGame
            currentSession <- Some session
            publish ()
            NativeInterop.Native.bubi_ra_load_game (handle, consoleId, rom, unativeint rom.Length, operationCallback))

    member _.UnloadGame() =
        lock gate (fun () ->
            generation <- generation + 1L
            cancelHttpRequests ()
            NativeInterop.Native.bubi_ra_cancel_operation handle
            NativeInterop.Native.bubi_ra_abort_server_requests handle
            pendingOperation <- NoOperation
            NativeInterop.Native.bubi_ra_unload_game handle
            currentSession <- None
            game <- None
            achievements <- []
            status <- if user.IsSome then Ready else LoggedOut
            publish ())

    member _.SetOffline(reason: string) =
        lock gate (fun () ->
            currentSession <- None
            game <- None
            achievements <- []
            status <- OfflineSession reason
            publish ())

    member _.ProcessFrame(session: Emulator.Session) =
        lock gate (fun () ->
            drainCompletions ()

            if status = Active then
                currentSession <- Some session

                try
                    NativeInterop.Native.bubi_ra_do_frame handle
                finally
                    currentSession <- None

                if achievementsDirty then
                    achievementsDirty <- false
                    refreshAchievements ()
                    publish ())

    member _.Pump(isPaused: bool) =
        lock gate (fun () ->
            drainCompletions ()

            if isPaused && status = Active then
                let now = Stopwatch.GetTimestamp()

                if Stopwatch.GetElapsedTime(lastIdle, now) >= TimeSpan.FromSeconds 1.0 then
                    NativeInterop.Native.bubi_ra_idle handle
                    lastIdle <- now)

    member _.SerializeProgress() =
        lock gate (fun () ->
            if status <> Active then
                Error "RetroAchievements is not active."
            else
                let size = NativeInterop.Native.bubi_ra_progress_size handle |> uint64

                if size > uint64 RaStateCodec.MaxProgressSize then
                    Error $"RetroAchievements progress exceeds {RaStateCodec.MaxProgressSize} bytes."
                else
                    let bytes = Array.zeroCreate<byte> (int size)

                    if
                        NativeInterop.Native.bubi_ra_serialize_progress (handle, bytes, unativeint bytes.Length) = 0
                    then
                        Ok bytes
                    else
                        Error "Could not serialize RetroAchievements progress.")

    member _.DeserializeProgress(progress: byte[]) =
        lock gate (fun () ->
            NativeInterop.Native.bubi_ra_deserialize_progress (handle, progress, unativeint progress.Length) = 0)

    member _.Reset() =
        lock gate (fun () -> NativeInterop.Native.bubi_ra_reset handle)

    interface IDisposable with
        member _.Dispose() =
            lock gate (fun () ->
                if not disposed then
                    disposed <- true
                    generation <- generation + 1L
                    cancelHttpRequests ()

                    NativeInterop.Native.bubi_ra_cancel_operation handle
                    NativeInterop.Native.bubi_ra_abort_server_requests handle
                    NativeInterop.Native.bubi_ra_destroy handle
                    handle <- nativeint 0
                    requests.Clear()
                    httpClient.Dispose()
                    GC.KeepAlive nativeCallbackRoots)

    static member TryCreate(log: string -> unit) =
        try
            if not (NativeInterop.isAvailable ()) then
                Error "bubi_rcheevos native library was not found."
            else
                let httpClient = new HttpClient()
                httpClient.Timeout <- Timeout.InfiniteTimeSpan
                httpClient.MaxResponseContentBufferSize <- 8L * 1024L * 1024L
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd "BubiBoy/1.1"
                Ok(new RaClient(httpClient, log))
        with
        | :? DllNotFoundException as ex -> Error ex.Message
        | :? EntryPointNotFoundException as ex -> Error ex.Message
        | ex -> Error ex.Message
