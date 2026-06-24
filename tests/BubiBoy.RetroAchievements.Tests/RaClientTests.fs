module BubiBoy.RetroAchievements.Tests.RaClientTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open BubiBoy.RetroAchievements
open BubiBoy.RetroAchievements.Tests
open BubiBoy.RetroAchievements.Tests.TestHelpers
open Xunit

let private createClient backend handler timeout =
    let credentials, saved, deleted, _ = credentials ()
    let clock = FakeTimeProvider()
    let logs = ResizeArray<string>()
    let http = new HttpClient(handler)
    http.Timeout <- Threading.Timeout.InfiniteTimeSpan
    let client = client backend credentials http clock timeout logs
    client, clock, logs, saved, deleted

let private successfulHandler () =
    new StubHttpHandler(fun _ -> Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))

[<Fact>]
let ``achievement event refreshes snapshot and moves unlocked achievement bucket`` () =
    let backend = FakeNativeBackend()
    backend.Achievements <- [ achievement 1uy "Locked" 0uy ]

    let client, _, logs, saved, _ =
        createClient backend (successfulHandler ()) (TimeSpan.FromSeconds 30.0)

    use client = client
    activate backend client
    let events = ResizeArray<RaEvent>()
    let snapshots = ResizeArray<RaSnapshot>()
    client.EventRaised.Add events.Add
    client.Changed.Add snapshots.Add

    backend.DoFrameAction <-
        fun () ->
            backend.Achievements <- [ achievement 3uy "Recently Unlocked" 1uy ]
            backend.RaiseEvent(1u, 7u, "First Clear", "Clear the first stage")

    client.ProcessFrame(session ())

    Assert.Equal(1, backend.DoFrameCount)
    Assert.Single events |> ignore
    Assert.Equal("First Clear", events[0].Title)
    let updated = Assert.Single(client.Snapshot.Achievements)
    Assert.Equal("Recently Unlocked", updated.BucketLabel)
    Assert.Equal(1uy, updated.Unlocked)
    Assert.Contains(snapshots, fun snapshot -> snapshot.Achievements |> List.exists (fun item -> item.Unlocked = 1uy))
    Assert.Contains(("player", "secret-token"), saved)
    Assert.DoesNotContain(logs, fun line -> line.Contains("secret-password") || line.Contains("secret-token"))

[<Fact>]
let ``achievement event refreshes user softcore score`` () =
    let backend = FakeNativeBackend()
    backend.Achievements <- [ achievement 1uy "Locked" 0uy ]

    let client, _, _, _, _ =
        createClient backend (successfulHandler ()) (TimeSpan.FromSeconds 30.0)

    use client = client
    activate backend client

    backend.DoFrameAction <-
        fun () ->
            backend.User <-
                Some
                    { Username = "player"
                      DisplayName = "Player"
                      Token = "secret-token"
                      Score = 0u
                      SoftcoreScore = 1u }

            backend.Achievements <- [ achievement 3uy "Recently Unlocked" 1uy ]
            backend.RaiseEvent(1u, 7u, "Happy Birthday", "Complete the Personality Test.")

    client.ProcessFrame(session ())

    Assert.Equal(1u, client.Snapshot.User.Value.SoftcoreScore)

[<Fact>]
let ``indicator events preserve progress and active game identity`` () =
    let backend = FakeNativeBackend()

    let client, _, _, _, _ =
        createClient backend (successfulHandler ()) (TimeSpan.FromSeconds 30.0)

    use client = client
    activate backend client
    let events = ResizeArray<RaEvent>()
    client.EventRaised.Add events.Add

    backend.DoFrameAction <-
        fun () ->
            backend.RaiseIndicatorEvent(
                7u,
                9u,
                "Collector",
                "Collect ten items",
                "https://example.test/badge.png",
                "4/10",
                40.0f
            )

    client.ProcessFrame(session ())
    let event = Assert.Single events
    Assert.Equal(7u, event.EventType)
    Assert.Equal("4/10", event.MeasuredProgress)
    Assert.Equal(40.0f, event.MeasuredPercent)
    Assert.Equal(client.Snapshot.Generation, event.Generation)
    Assert.Equal(123u, event.GameId)

[<Fact>]
let ``challenge show and hide refresh achievement buckets but progress update does not`` () =
    let backend = FakeNativeBackend()
    let mutable enumerations = 0

    let api =
        { backend.Api with
            EnumerateAchievements =
                fun args ->
                    enumerations <- enumerations + 1
                    backend.Api.EnumerateAchievements args }

    let credentials, _, _, _ = credentials ()
    let logs = ResizeArray<string>()
    use http = new HttpClient(successfulHandler ())

    use client =
        new RaClient(api, credentials, http, FakeTimeProvider(), TimeSpan.FromSeconds 30.0, logs.Add)

    activate backend client
    let baseline = enumerations

    backend.DoFrameAction <- fun () -> backend.RaiseEvent(5u, 7u, "Challenge", "")
    client.ProcessFrame(session ())
    Assert.Equal(baseline + 1, enumerations)

    backend.DoFrameAction <- fun () -> backend.RaiseEvent(9u, 7u, "Progress", "")
    client.ProcessFrame(session ())
    Assert.Equal(baseline + 1, enumerations)

    backend.DoFrameAction <- fun () -> backend.RaiseEvent(6u, 7u, "Challenge", "")
    client.ProcessFrame(session ())
    Assert.Equal(baseline + 2, enumerations)

[<Fact>]
let ``frame processing and paused idle only run in active state`` () =
    let backend = FakeNativeBackend()

    let client, clock, _, _, _ =
        createClient backend (successfulHandler ()) (TimeSpan.FromSeconds 30.0)

    use client = client
    client.ProcessFrame(session ())
    Assert.Equal(0, backend.DoFrameCount)
    activate backend client
    client.ProcessFrame(session ())
    client.ProcessFrame(session ())
    Assert.Equal(2, backend.DoFrameCount)
    client.Pump(false)
    clock.Advance(TimeSpan.FromSeconds 1.0)
    client.Pump(false)
    Assert.Equal(0, backend.IdleCount)
    client.Pump(true)
    Assert.Equal(1, backend.IdleCount)
    client.Pump(true)
    Assert.Equal(1, backend.IdleCount)
    clock.Advance(TimeSpan.FromSeconds 1.0)
    client.Pump(true)
    Assert.Equal(2, backend.IdleCount)

[<Fact>]
let ``rich presence loads and refreshes once per second only when changed`` () =
    let backend = FakeNativeBackend()
    backend.RichPresence <- Some "Exploring Reeve"

    let client, clock, _, _, _ =
        createClient backend (successfulHandler ()) (TimeSpan.FromSeconds 30.0)

    use client = client
    let snapshots = ResizeArray<RaSnapshot>()
    client.Changed.Add snapshots.Add
    activate backend client
    Assert.Equal(Some "Exploring Reeve", client.Snapshot.RichPresence)
    let initialReads = backend.RichPresenceReadCount
    let initialSnapshots = snapshots.Count

    clock.Advance(TimeSpan.FromMilliseconds 999.0)
    client.ProcessFrame(session ())
    Assert.Equal(initialReads, backend.RichPresenceReadCount)

    clock.Advance(TimeSpan.FromMilliseconds 1.0)
    client.ProcessFrame(session ())
    Assert.Equal(initialReads + 1, backend.RichPresenceReadCount)
    Assert.Equal(initialSnapshots, snapshots.Count)

    backend.RichPresence <- Some "Level 3"
    clock.Advance(TimeSpan.FromSeconds 1.0)
    client.ProcessFrame(session ())
    Assert.Equal(Some "Level 3", client.Snapshot.RichPresence)
    Assert.Equal(initialSnapshots + 1, snapshots.Count)

[<Fact>]
let ``rich presence clears when active session ends`` () =
    let backend = FakeNativeBackend()
    backend.RichPresence <- Some "Playing"

    let client, _, _, _, _ =
        createClient backend (successfulHandler ()) (TimeSpan.FromSeconds 30.0)

    use client = client
    activate backend client
    Assert.Equal(Some "Playing", client.Snapshot.RichPresence)
    client.UnloadGame()
    Assert.Equal(None, client.Snapshot.RichPresence)

    activate backend client
    client.SetOffline("Disconnected")
    Assert.Equal(None, client.Snapshot.RichPresence)

    client.UnloadGame()
    activate backend client
    client.Logout()
    Assert.Equal(None, client.Snapshot.RichPresence)

[<Fact>]
let ``memory callback supports repeated reads and stops at the mapped range`` () =
    let backend = FakeNativeBackend()

    let client, _, _, _, _ =
        createClient backend (successfulHandler ()) (TimeSpan.FromSeconds 30.0)

    use client = client
    activate backend client

    backend.DoFrameAction <-
        fun () ->
            Assert.Equal<byte array>([| 0uy; 0uy; 0uy; 0uy |], backend.ReadMemory(0xC000u, 4))
            Assert.Equal<byte array>([| 0xFFuy; 0xFFuy |], backend.ReadMemory(0x33FFEu, 4))

            for _ = 1 to 100 do
                Assert.Single(backend.ReadMemory(0xC000u, 1)) |> ignore

    client.ProcessFrame(session ())
    Assert.Equal(1, backend.DoFrameCount)

[<Fact>]
let ``pause decision delegates to native only for active game`` () =
    let backend = FakeNativeBackend()
    backend.CanPauseResult <- false, 120u

    let client, _, _, _, _ =
        createClient backend (successfulHandler ()) (TimeSpan.FromSeconds 30.0)

    use client = client
    Assert.Equal(PauseAllowed, client.CanPause())
    activate backend client
    Assert.Equal(PauseDenied 120u, client.CanPause())
    backend.CanPauseResult <- true, 0u
    Assert.Equal(PauseAllowed, client.CanPause())

[<Fact>]
let ``HTTP preserves method status and empty response body`` () =
    let requests = ResizeArray<HttpMethod * string>()
    let userAgents = ResizeArray<string>()

    let handler =
        new StubHttpHandler(fun (request, _) ->
            task {
                let! body =
                    if isNull request.Content then
                        Task.FromResult ""
                    else
                        request.Content.ReadAsStringAsync()

                requests.Add(request.Method, body)
                userAgents.Add(request.Headers.UserAgent.ToString())
                return new HttpResponseMessage(HttpStatusCode.NotFound, Content = new ByteArrayContent(Array.empty))
            })

    let backend = FakeNativeBackend()
    let client, _, _, _, _ = createClient backend handler (TimeSpan.FromSeconds 30.0)
    use client = client
    backend.Request(unativeint 1, "https://example.test/get", null, null)
    backend.Request(unativeint 2, "https://example.test/post", "", "application/x-www-form-urlencoded")

    waitFor (fun () ->
        client.Pump(false)
        backend.Completions.Length = 2)

    Assert.Contains((HttpMethod.Get, ""), requests)
    Assert.Contains((HttpMethod.Post, ""), requests)

    Assert.All(
        userAgents,
        fun value ->
            Assert.Contains("BubiBoy/1.1", value)
            Assert.Contains("rcheevos/12.3.0", value)
    )

    Assert.All(
        backend.Completions,
        fun (_, status, body) ->
            Assert.Equal(404, status)
            Assert.Empty body
    )

[<Fact>]
let ``HTTP preserves success and server error bodies`` () =
    let handler =
        new StubHttpHandler(fun (request, _) ->
            let status, body =
                if request.RequestUri.AbsolutePath = "/ok" then
                    HttpStatusCode.OK, [| 1uy; 2uy |]
                else
                    HttpStatusCode.InternalServerError, [| 3uy |]

            Task.FromResult(new HttpResponseMessage(status, Content = new ByteArrayContent(body))))

    let backend = FakeNativeBackend()
    let client, _, _, _, _ = createClient backend handler (TimeSpan.FromSeconds 30.0)
    use client = client
    backend.Request(unativeint 11, "https://example.test/ok", null, null)
    backend.Request(unativeint 12, "https://example.test/error", null, null)

    waitFor (fun () ->
        client.Pump(false)
        backend.Completions.Length = 2)

    Assert.Contains(backend.Completions, fun (_, status, body) -> status = 200 && body = [| 1uy; 2uy |])
    Assert.Contains(backend.Completions, fun (_, status, body) -> status = 500 && body = [| 3uy |])

[<Fact>]
let ``HTTP response larger than limit reports client error`` () =
    let oversized = Array.zeroCreate<byte> (8 * 1024 * 1024 + 1)

    let handler =
        new StubHttpHandler(fun _ ->
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK, Content = new ByteArrayContent(oversized))))

    let backend = FakeNativeBackend()
    let client, _, _, _, _ = createClient backend handler (TimeSpan.FromSeconds 30.0)
    use client = client
    backend.Request(unativeint 13, "https://example.test/large", null, null)

    waitFor (fun () ->
        client.Pump(false)
        backend.Completions.Length = 1)

    let _, status, body = backend.Completions.Head
    Assert.Equal(-1, status)
    Assert.Empty body

[<Fact>]
let ``HTTP exception and timeout report client error without leaking request data`` () =
    let handler =
        new StubHttpHandler(fun (_, cancellationToken) ->
            task {
                do! Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken)
                return new HttpResponseMessage(HttpStatusCode.OK)
            })

    let backend = FakeNativeBackend()

    let client, _, logs, _, _ =
        createClient backend handler (TimeSpan.FromMilliseconds 20.0)

    use client = client
    backend.Request(unativeint 4, "https://example.test/timeout", "private-post-data", "text/plain")

    waitFor (fun () ->
        client.Pump(false)
        backend.Completions.Length = 1)

    let _, status, body = backend.Completions.Head
    Assert.Equal(-2, status)
    Assert.Empty body
    Assert.DoesNotContain(logs, fun line -> line.Contains("private-post-data"))

[<Fact>]
let ``old generation HTTP completion is discarded after unload`` () =
    let response = TaskCompletionSource<HttpResponseMessage>()
    let handler = new StubHttpHandler(fun _ -> response.Task)
    let backend = FakeNativeBackend()
    let client, _, _, _, _ = createClient backend handler (TimeSpan.FromSeconds 30.0)
    use client = client
    activate backend client
    backend.Request(unativeint 9, "https://example.test/slow", null, null)
    client.UnloadGame()
    response.SetResult(new HttpResponseMessage(HttpStatusCode.OK))
    Threading.Thread.Sleep 20
    client.Pump(false)
    Assert.Empty backend.Completions
    Assert.Equal(Ready, client.Snapshot.Status)

[<Fact>]
let ``failed login deletes token and returns to logged out`` () =
    let backend = FakeNativeBackend()

    let client, _, _, _, deleted =
        createClient backend (successfulHandler ()) (TimeSpan.FromSeconds 30.0)

    use client = client
    client.LoginWithPassword("player", "password")
    backend.CompleteOperation(1, "Rejected")
    Assert.Equal(LoggedOut, client.Snapshot.Status)
    Assert.Contains("player", deleted)

[<Fact>]
let ``failed game load enters offline session and unload returns ready`` () =
    let backend = FakeNativeBackend()

    let client, _, _, _, _ =
        createClient backend (successfulHandler ()) (TimeSpan.FromSeconds 30.0)

    use client = client

    backend.User <-
        Some
            { Username = "player"
              DisplayName = "Player"
              Token = "token"
              Score = 0u
              SoftcoreScore = 0u }

    client.LoginWithPassword("player", "password")
    backend.CompleteOperation(0, "")
    client.LoadGame(4u, [| 0uy |], session ())
    backend.CompleteOperation(1, "Unknown game")
    Assert.Equal(OfflineSession "Unknown game", client.Snapshot.Status)
    client.UnloadGame()
    Assert.Equal(Ready, client.Snapshot.Status)

[<Fact>]
let ``serialize rejects native progress larger than maximum`` () =
    let backend = FakeNativeBackend()

    let oversizedApi =
        { backend.Api with
            ProgressSize = fun _ -> unativeint (RaStateCodec.MaxProgressSize + 1) }

    let credentials, _, _, _ = credentials ()
    let logs = ResizeArray<string>()
    let http = new HttpClient(successfulHandler ())

    use client =
        new RaClient(oversizedApi, credentials, http, FakeTimeProvider(), TimeSpan.FromSeconds 30.0, logs.Add)

    backend.User <-
        Some
            { Username = "player"
              DisplayName = "Player"
              Token = "token"
              Score = 0u
              SoftcoreScore = 0u }

    client.LoginWithPassword("player", "password")
    backend.CompleteOperation(0, "")

    backend.Game <-
        Some
            { Id = 1u
              Title = "Game"
              Hash = "hash"
              ImageUrl = "" }

    client.LoadGame(4u, [| 0uy |], session ())
    backend.CompleteOperation(0, "")

    match client.SerializeProgress() with
    | Ok _ -> Assert.Fail "Oversized progress was accepted."
    | Error message -> Assert.Contains("exceeds", message)
