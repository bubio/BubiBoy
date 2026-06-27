namespace BubiBoy.RetroAchievements

open System
open System.Collections.Generic
open System.Collections.Concurrent
open System.Diagnostics
open System.IO
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

type RaClient
    internal
    (
        nativeApi: NativeInterop.Api,
        credentialStore: RaCredentialStore.Store,
        httpClient: HttpClient,
        timeProvider: TimeProvider,
        requestTimeout: TimeSpan,
        log: string -> unit
    ) =
    let gate = obj ()
    let completions = ConcurrentQueue<unit -> unit>()

    let requests =
        ConcurrentDictionary<struct (unativeint * int64), CancellationTokenSource>()

    let changed = Event<RaSnapshot>()
    let eventRaised = Event<RaEvent>()
    let mutable handle = nativeint 0
    let mutable disposed = false
    let mutable status = LoggedOut
    let mutable desiredHardcore = true
    let mutable hardcoreEnabled = true
    let mutable user: RaUser option = None
    let mutable game: RaGame option = None
    let mutable achievements: RaAchievement list = []
    let mutable leaderboards: RaLeaderboard list = []
    let mutable richPresence: string option = None
    let mutable currentSession: Emulator.Session option = None
    let mutable generation = 0L
    let mutable pendingOperation = NoOperation
    let mutable lastIdle = timeProvider.GetTimestamp()
    let mutable lastRichPresence = timeProvider.GetTimestamp()
    let mutable achievementsDirty = false
    let mutable leaderboardsDirty = false
    let mutable userDirty = false
    let mutable memoryBuffer = Array.zeroCreate<byte> 256
    let leaderboardEntryBuffers = Dictionary<uint32, ResizeArray<RaLeaderboardEntry>>()
    let fetchedLeaderboardEntries = HashSet<struct (int64 * uint32)>()

    let snapshot () =
        { Status = status
          HardcoreEnabled = hardcoreEnabled
          User = user
          Game = game
          Achievements = achievements
          Leaderboards = leaderboards
          RichPresence = richPresence
          Generation = generation }

    let publish () = changed.Trigger(snapshot ())

    let refreshUser saveCredential =
        match nativeApi.GetUser handle with
        | Some nativeUser ->
            let next =
                { Username = nativeUser.Username
                  DisplayName = nativeUser.DisplayName
                  Score = nativeUser.Score
                  SoftcoreScore = nativeUser.SoftcoreScore }

            user <- Some next

            if saveCredential then
                match credentialStore.SaveToken next.Username nativeUser.Token with
                | Ok() -> ()
                | Error message -> log $"RetroAchievements token was not saved: {message}"
        | None -> user <- None

    let refreshGame () =
        match nativeApi.GetGame handle with
        | Some nativeGame ->
            game <-
                Some
                    { Id = nativeGame.Id
                      Title = nativeGame.Title
                      Hash = nativeGame.Hash
                      ImageUrl = nativeGame.ImageUrl }
        | None -> game <- None

    let refreshHardcore () =
        hardcoreEnabled <- nativeApi.GetHardcoreEnabled handle

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
        nativeApi.EnumerateAchievements(handle, achievementCallback)
        achievements <- achievementBuffer |> Seq.toList

    let mutable leaderboardBuffer = ResizeArray<RaLeaderboard>()

    let leaderboardEntryMap () =
        leaderboards
        |> List.map (fun leaderboard -> leaderboard.Id, leaderboard.TopEntries)
        |> Map.ofList

    let leaderboardCallback =
        NativeInterop.LeaderboardCallback
            (fun _ bucket bucketLabel id title description trackerValue state format lowerIsBetter ->
                let existingEntries =
                    leaderboardEntryMap () |> Map.tryFind id |> Option.defaultValue []

                leaderboardBuffer.Add
                    { Bucket = bucket
                      BucketLabel = Option.ofObj bucketLabel |> Option.defaultValue ""
                      Id = id
                      Title = Option.ofObj title |> Option.defaultValue ""
                      Description = Option.ofObj description |> Option.defaultValue ""
                      TrackerValue = Option.ofObj trackerValue |> Option.defaultValue ""
                      State = state
                      Format = format
                      LowerIsBetter = lowerIsBetter <> 0uy
                      TopEntries = existingEntries })

    let leaderboardEntryCallback =
        NativeInterop.LeaderboardEntryCallback(fun _ leaderboardId username rank score ->
            let mutable buffer = Unchecked.defaultof<ResizeArray<RaLeaderboardEntry>>

            if not (leaderboardEntryBuffers.TryGetValue(leaderboardId, &buffer)) then
                buffer <- ResizeArray()
                leaderboardEntryBuffers[leaderboardId] <- buffer

            buffer.Add
                { Username = Option.ofObj username |> Option.defaultValue ""
                  Rank = rank
                  Score = Option.ofObj score |> Option.defaultValue "" })

    let leaderboardEntriesCallback =
        NativeInterop.LeaderboardEntriesCallback(fun _ leaderboardId result errorMessage _ _ ->
            let key = struct (generation, leaderboardId)

            if status = Active && fetchedLeaderboardEntries.Contains key then
                let entries =
                    match leaderboardEntryBuffers.TryGetValue leaderboardId with
                    | true, value -> value |> Seq.toList
                    | false, _ -> []

                leaderboardEntryBuffers.Remove leaderboardId |> ignore

                if result = 0 then
                    leaderboards <-
                        leaderboards
                        |> List.map (fun leaderboard ->
                            if leaderboard.Id = leaderboardId then
                                { leaderboard with
                                    TopEntries = entries }
                            else
                                leaderboard)

                    publish ()
                else
                    let message =
                        Option.ofObj errorMessage
                        |> Option.defaultValue "leaderboard entries request failed"

                    log $"RetroAchievements leaderboard entries fetch failed for {leaderboardId}: {message}")

    let requestLeaderboardTopEntries () =
        if status = Active then
            leaderboards
            |> List.iter (fun leaderboard ->
                let key = struct (generation, leaderboard.Id)

                if fetchedLeaderboardEntries.Add key then
                    leaderboardEntryBuffers[leaderboard.Id] <- ResizeArray()

                    nativeApi.FetchLeaderboardEntries(
                        handle,
                        leaderboard.Id,
                        1u,
                        1u,
                        leaderboardEntryCallback,
                        leaderboardEntriesCallback
                    ))

    let refreshLeaderboards () =
        leaderboardBuffer <- ResizeArray()
        nativeApi.EnumerateLeaderboards(handle, leaderboardCallback)
        leaderboards <- leaderboardBuffer |> Seq.toList
        requestLeaderboardTopEntries ()

    let refreshRichPresence () =
        let next =
            nativeApi.GetRichPresence handle
            |> Option.bind (fun value ->
                let trimmed = value.Trim()

                if String.IsNullOrWhiteSpace trimmed then
                    None
                else
                    Some trimmed)

        let changed = next <> richPresence
        richPresence <- next
        changed

    let operationCallback =
        NativeInterop.OperationCallback(fun _ result errorMessage ->
            let errorMessage =
                Option.ofObj errorMessage
                |> Option.defaultValue "RetroAchievements operation failed."

            match pendingOperation with
            | Login username ->
                pendingOperation <- NoOperation

                if result = 0 then
                    refreshUser true
                    status <- Ready
                    log $"RetroAchievements login succeeded for {username}."
                else
                    credentialStore.DeleteToken username
                    user <- None
                    status <- LoggedOut
                    log $"RetroAchievements login failed: {errorMessage}"

                publish ()
            | LoadGame ->
                pendingOperation <- NoOperation

                if result = 0 then
                    refreshHardcore ()
                    refreshGame ()
                    status <- Active
                    refreshAchievements ()
                    refreshLeaderboards ()
                    refreshRichPresence () |> ignore
                    lastRichPresence <- timeProvider.GetTimestamp()
                else
                    game <- None
                    achievements <- []
                    leaderboards <- []
                    leaderboardEntryBuffers.Clear()
                    fetchedLeaderboardEntries.Clear()
                    richPresence <- None
                    status <- OfflineSession errorMessage
                    log $"RetroAchievements game load failed: {errorMessage}"

                currentSession <- None
                publish ()
            | NoOperation -> ())

    let readMemoryCallback =
        NativeInterop.ReadMemoryCallback(fun _ address destination count ->
            if destination = nativeint 0 || count = 0u || address > 0x33FFFu then
                0u
            else
                match currentSession with
                | None -> 0u
                | Some session ->
                    let available = 0x34000u - address
                    let requested = int (min count available)

                    if memoryBuffer.Length < requested then
                        memoryBuffer <- Array.zeroCreate<byte> (max requested (memoryBuffer.Length * 2))

                    let copied = Emulator.readInspectionMemory address memoryBuffer 0 requested session

                    if copied > 0 then
                        Marshal.Copy(memoryBuffer, 0, destination, copied)

                    uint32 copied)

    let completeHttp requestId statusCode (body: byte[]) =
        lock gate (fun () ->
            if not disposed then
                nativeApi.CompleteServerRequest(handle, requestId, statusCode, body, unativeint body.Length))

    let cancelHttpRequests () =
        for request in requests.Values do
            try
                request.Cancel()
            with :? ObjectDisposedException ->
                ()

    let readResponseBody (response: HttpResponseMessage) maxBytes cancellationToken =
        task {
            let contentLength = response.Content.Headers.ContentLength

            if contentLength.HasValue && contentLength.Value > int64 maxBytes then
                return None
            else
                use! source = response.Content.ReadAsStreamAsync(cancellationToken)
                use destination = new MemoryStream()
                let buffer = Array.zeroCreate<byte> 81920
                let mutable total = 0
                let mutable exceeded = false

                while not exceeded do
                    let! read = source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)

                    if read = 0 then
                        exceeded <- true
                    elif total + read > maxBytes then
                        total <- maxBytes + 1
                        exceeded <- true
                    else
                        do! destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        total <- total + read

                if total > maxBytes then
                    return None
                else
                    return Some(destination.ToArray())
        }

    let serverRequestCallback =
        NativeInterop.ServerRequestCallback(fun _ requestId url postData contentType ->
            let requestGeneration = generation
            let requestKey = struct (requestId, requestGeneration)
            let cts = new CancellationTokenSource(requestTimeout)
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

                        let! bytes = readResponseBody response (8 * 1024 * 1024) cts.Token

                        match bytes with
                        | None -> statusCode <- -1
                        | Some bytes ->
                            statusCode <- int response.StatusCode
                            body <- bytes
                    with
                    | :? OperationCanceledException -> statusCode <- -2
                    | ex ->
                        log $"RetroAchievements HTTP request failed: {ex.Message}"
                        statusCode <- -2

                    match requests.TryRemove requestKey with
                    | true, owned -> owned.Dispose()
                    | false, _ -> cts.Dispose()

                    completions.Enqueue(fun () ->
                        if requestGeneration = generation then
                            completeHttp requestId statusCode body)
                }

            Task.Run(Func<Task>(fun () -> runRequest () :> Task)) |> ignore)

    let mutable scoreboardEntryBuffer = ResizeArray<RaLeaderboardEntry>()

    let scoreboardEntryCallback =
        NativeInterop.ScoreboardEntryCallback(fun _ username rank score ->
            scoreboardEntryBuffer.Add
                { Username = Option.ofObj username |> Option.defaultValue ""
                  Rank = rank
                  Score = Option.ofObj score |> Option.defaultValue "" })

    let eventCallback =
        NativeInterop.LeaderboardEventCallback
            (fun
                _
                eventType
                relatedId
                title
                description
                imageUrl
                measuredProgress
                measuredPercent
                value
                bestScore
                rank
                totalEntries ->
                let eventGameId =
                    game |> Option.map (fun value -> value.Id) |> Option.defaultValue 0u

                let leaderboardEntries =
                    if eventType = 13u then
                        let entries = scoreboardEntryBuffer |> Seq.toList
                        scoreboardEntryBuffer <- ResizeArray()
                        entries
                    else
                        []

                let event =
                    { EventType = eventType
                      RelatedId = relatedId
                      Title = Option.ofObj title |> Option.defaultValue ""
                      Description = Option.ofObj description |> Option.defaultValue ""
                      ImageUrl = Option.ofObj imageUrl |> Option.defaultValue ""
                      MeasuredProgress = Option.ofObj measuredProgress |> Option.defaultValue ""
                      MeasuredPercent = measuredPercent
                      Value = Option.ofObj value |> Option.defaultValue ""
                      BestScore = Option.ofObj bestScore |> Option.defaultValue ""
                      Rank = rank
                      TotalEntries = totalEntries
                      LeaderboardEntries = leaderboardEntries
                      Generation = generation
                      GameId = eventGameId }

                if eventType = 1u then
                    userDirty <- true

                if eventType = 1u || eventType = 5u || eventType = 6u then
                    achievementsDirty <- true

                if eventType = 2u || eventType = 3u || eventType = 4u then
                    leaderboardsDirty <- true

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
           scoreboardEntryCallback
           leaderboardEntryCallback
           leaderboardEntriesCallback
           logCallback
           operationCallback |]

    let drainCompletions () =
        let mutable action = Unchecked.defaultof<unit -> unit>

        while completions.TryDequeue(&action) do
            action ()

    do
        if not (httpClient.DefaultRequestHeaders.UserAgent.ToString().Contains("BubiBoy/")) then
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd "BubiBoy/1.1"

        handle <-
            nativeApi.Create(
                readMemoryCallback,
                serverRequestCallback,
                eventCallback,
                scoreboardEntryCallback,
                logCallback,
                nativeint 0
            )

        if handle = nativeint 0 then
            invalidOp "Could not create the RetroAchievements client."

        nativeApi.SetHardcoreEnabled(handle, true)
        refreshHardcore ()

        let clause = StringBuilder(128)

        nativeApi.UserAgent(handle, clause, unativeint clause.Capacity) |> ignore

        if clause.Length > 0 then
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(clause.ToString())

    member _.Snapshot = lock gate snapshot
    member _.Changed = changed.Publish
    member _.EventRaised = eventRaised.Publish
    member _.Version = nativeApi.Version()

    member _.SetHardcoreEnabled(enabled: bool) =
        lock gate (fun () ->
            if disposed then
                invalidOp "RetroAchievements client is disposed."

            desiredHardcore <- enabled
            nativeApi.SetHardcoreEnabled(handle, enabled)
            refreshHardcore ()

            if status = Active then
                refreshAchievements ()

            publish ())

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
                nativeApi.LoginPassword(handle, username, password, operationCallback))

    member _.LoginWithStoredToken(username: string) =
        match credentialStore.TryLoadToken username with
        | None -> false
        | Some token ->
            lock gate (fun () ->
                status <- Authenticating
                pendingOperation <- Login username
                publish ()
                nativeApi.LoginToken(handle, username, token, operationCallback))

            true

    member _.Logout() =
        lock gate (fun () ->
            user |> Option.iter (fun value -> credentialStore.DeleteToken value.Username)
            generation <- generation + 1L
            cancelHttpRequests ()
            nativeApi.CancelOperation handle
            nativeApi.AbortServerRequests handle
            pendingOperation <- NoOperation
            nativeApi.Logout handle
            currentSession <- None
            user <- None
            game <- None
            achievements <- []
            leaderboards <- []
            scoreboardEntryBuffer <- ResizeArray()
            leaderboardEntryBuffers.Clear()
            fetchedLeaderboardEntries.Clear()
            achievementsDirty <- false
            leaderboardsDirty <- false
            userDirty <- false
            richPresence <- None
            status <- LoggedOut
            publish ())

    member _.LoadGame(consoleId: uint32, rom: byte[], session: Emulator.Session) =
        lock gate (fun () ->
            if status <> Ready then
                invalidOp "RetroAchievements login must complete before loading a game."

            generation <- generation + 1L
            status <- LoadingGame
            pendingOperation <- LoadGame
            achievementsDirty <- false
            leaderboardsDirty <- false
            userDirty <- false
            scoreboardEntryBuffer <- ResizeArray()
            leaderboardEntryBuffers.Clear()
            fetchedLeaderboardEntries.Clear()
            currentSession <- Some session
            nativeApi.SetHardcoreEnabled(handle, desiredHardcore)
            refreshHardcore ()
            publish ()
            nativeApi.LoadGame(handle, consoleId, rom, unativeint rom.Length, operationCallback))

    member _.UnloadGame() =
        lock gate (fun () ->
            generation <- generation + 1L
            cancelHttpRequests ()
            nativeApi.CancelOperation handle
            nativeApi.AbortServerRequests handle
            pendingOperation <- NoOperation
            nativeApi.UnloadGame handle
            currentSession <- None
            game <- None
            achievements <- []
            leaderboards <- []
            scoreboardEntryBuffer <- ResizeArray()
            leaderboardEntryBuffers.Clear()
            fetchedLeaderboardEntries.Clear()
            achievementsDirty <- false
            leaderboardsDirty <- false
            userDirty <- false
            richPresence <- None
            status <- if user.IsSome then Ready else LoggedOut
            publish ())

    member _.SetOffline(reason: string) =
        lock gate (fun () ->
            currentSession <- None
            game <- None
            achievements <- []
            leaderboards <- []
            scoreboardEntryBuffer <- ResizeArray()
            leaderboardEntryBuffers.Clear()
            fetchedLeaderboardEntries.Clear()
            achievementsDirty <- false
            leaderboardsDirty <- false
            userDirty <- false
            richPresence <- None
            status <- OfflineSession reason
            publish ())

    member _.ProcessFrame(session: Emulator.Session) =
        lock gate (fun () ->
            if status = Active then
                currentSession <- Some session

                try
                    nativeApi.DoFrame handle

                    if achievementsDirty || leaderboardsDirty || userDirty then
                        let shouldRefreshUser = userDirty
                        let shouldRefreshAchievements = achievementsDirty
                        let shouldRefreshLeaderboards = leaderboardsDirty
                        userDirty <- false
                        achievementsDirty <- false
                        leaderboardsDirty <- false

                        if shouldRefreshUser then
                            refreshUser false

                        if shouldRefreshAchievements then
                            refreshAchievements ()

                        if shouldRefreshLeaderboards then
                            refreshLeaderboards ()

                        publish ()

                    let now = timeProvider.GetTimestamp()

                    if timeProvider.GetElapsedTime(lastRichPresence, now) >= TimeSpan.FromSeconds 1.0 then
                        lastRichPresence <- now

                        if refreshRichPresence () then
                            publish ()
                finally
                    currentSession <- None)

    member _.Pump(isPaused: bool) =
        if isPaused || not completions.IsEmpty then
            lock gate (fun () ->
                drainCompletions ()

                if isPaused && status = Active then
                    let now = timeProvider.GetTimestamp()

                    if timeProvider.GetElapsedTime(lastIdle, now) >= TimeSpan.FromSeconds 1.0 then
                        nativeApi.Idle handle
                        lastIdle <- now)

    member _.CanPause() =
        lock gate (fun () ->
            if status <> Active then
                PauseAllowed
            else
                match nativeApi.CanPause handle with
                | true, _ -> PauseAllowed
                | false, framesRemaining -> PauseDenied framesRemaining)

    member _.SerializeProgress() =
        lock gate (fun () ->
            if status <> Active then
                Error "RetroAchievements is not active."
            else
                let size = nativeApi.ProgressSize handle |> uint64

                if size > uint64 RaStateCodec.MaxProgressSize then
                    Error $"RetroAchievements progress exceeds {RaStateCodec.MaxProgressSize} bytes."
                else
                    let bytes = Array.zeroCreate<byte> (int size)

                    if nativeApi.SerializeProgress(handle, bytes, unativeint bytes.Length) = 0 then
                        Ok bytes
                    else
                        Error "Could not serialize RetroAchievements progress.")

    member _.DeserializeProgress(progress: byte[]) =
        lock gate (fun () -> nativeApi.DeserializeProgress(handle, progress, unativeint progress.Length) = 0)

    member _.Reset() =
        lock gate (fun () -> nativeApi.Reset handle)

    interface IDisposable with
        member _.Dispose() =
            lock gate (fun () ->
                if not disposed then
                    disposed <- true
                    generation <- generation + 1L
                    cancelHttpRequests ()

                    nativeApi.CancelOperation handle
                    nativeApi.AbortServerRequests handle
                    nativeApi.Destroy handle
                    handle <- nativeint 0
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

                Ok(
                    new RaClient(
                        NativeInterop.api,
                        RaCredentialStore.store,
                        httpClient,
                        TimeProvider.System,
                        TimeSpan.FromSeconds 30.0,
                        log
                    )
                )
        with
        | :? DllNotFoundException as ex -> Error ex.Message
        | :? EntryPointNotFoundException as ex -> Error ex.Message
        | ex -> Error ex.Message
