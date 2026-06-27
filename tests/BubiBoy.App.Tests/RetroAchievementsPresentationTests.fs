namespace BubiBoy.App.Tests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open BubiBoy.App
open BubiBoy.IO
open BubiBoy.RetroAchievements
open Xunit

type private StubHandler(send: HttpRequestMessage * CancellationToken -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()

    override _.SendAsync(request, cancellationToken) = send (request, cancellationToken)

module RetroAchievementsPresentationTests =

    let private achievement bucket label =
        { Bucket = bucket
          BucketLabel = label
          Id = uint32 bucket
          Title = label
          Description = ""
          Points = 5u
          MeasuredProgress = ""
          MeasuredPercent = 0.0f
          Rarity = 1.0f
          State = 1uy
          Unlocked = 0uy
          ImageUrl = "" }

    [<Fact>]
    let ``achievement and completion events produce toast messages`` () =
        let event eventType title =
            { EventType = eventType
              RelatedId = 1u
              Title = title
              Description = "failure"
              ImageUrl = ""
              MeasuredProgress = ""
              MeasuredPercent = 0.0f
              Value = ""
              BestScore = ""
              Rank = 0u
              TotalEntries = 0u
              LeaderboardEntries = []
              Generation = 1L
              GameId = 1u }

        Assert.Equal(
            Some "Achievement unlocked: First Clear",
            RetroAchievementsPresentation.notificationMessage (event 1u "First Clear")
        )

        Assert.Equal(
            Some "All achievements completed.",
            RetroAchievementsPresentation.notificationMessage (event 15u "")
        )

        Assert.Equal(
            RetroAchievementsPresentation.ResetRequested,
            RetroAchievementsPresentation.hostAction (event 14u "")
        )

    [<Fact>]
    let ``operation policy checks pause only for active RA session`` () =
        let mutable calls = 0

        let denied () =
            calls <- calls + 1
            PauseDenied 120u

        match RetroAchievementsOperations.evaluateStatus Active denied Pause with
        | OperationDenied message -> Assert.Contains("3 more second", message)
        | OperationAllowed -> Assert.Fail "Pause was unexpectedly allowed."

        Assert.Equal(1, calls)
        Assert.Equal(OperationAllowed, RetroAchievementsOperations.evaluateStatus Ready denied Pause)
        Assert.Equal(OperationAllowed, RetroAchievementsOperations.evaluateStatus Active denied SaveState)
        Assert.Equal(1, calls)

    [<Fact>]
    let ``disabling RetroAchievements preserves the hardcore preference`` () =
        Assert.True(SettingsWindowSelection.hardcorePreference true)
        Assert.False(SettingsWindowSelection.hardcorePreference false)

    [<Fact>]
    let ``boot ROM selections have concise descriptions`` () =
        let selections =
            [ AppSettings.Disabled
              AppSettings.Automatic
              AppSettings.Cgb
              AppSettings.Dmg ]

        for selection in selections do
            let description = SettingsWindowSelection.bootRomDescription selection
            Assert.False(String.IsNullOrWhiteSpace description)
            Assert.True(description.Length < 120)

    [<Fact>]
    let ``achievement status sort puts recent and active items before locked items`` () =
        let input =
            [ achievement 3uy "Unsupported"
              achievement 7uy "Almost There"
              achievement 1uy "Locked"
              achievement 6uy "Active Challenge"
              achievement 5uy "Recently Unlocked"
              achievement 2uy "Unlocked" ]

        let labels =
            input
            |> RetroAchievementsPresentation.sortAchievements
                RetroAchievementsPresentation.Status
                RetroAchievementsPresentation.Ascending
            |> List.map (snd >> _.BucketLabel)

        Assert.Equal<string list>(
            [ "Recently Unlocked"
              "Active Challenge"
              "Almost There"
              "Unlocked"
              "Locked"
              "Unsupported" ],
            labels
        )

    [<Fact>]
    let ``achievement table columns sort and toggle direction`` () =
        let low =
            { achievement 1uy "Beta" with
                Points = 1u
                Rarity = 5.0f }

        let high =
            { achievement 2uy "Alpha" with
                Points = 10u
                Rarity = 75.0f }

        let byPoints =
            RetroAchievementsPresentation.sortAchievements
                RetroAchievementsPresentation.Points
                RetroAchievementsPresentation.Descending
                [ low; high ]

        Assert.Equal<uint32 list>([ 10u; 1u ], byPoints |> List.map (snd >> _.Points))

        Assert.Equal(
            (RetroAchievementsPresentation.Status, RetroAchievementsPresentation.Descending),
            RetroAchievementsPresentation.nextSort
                RetroAchievementsPresentation.Status
                RetroAchievementsPresentation.Ascending
                RetroAchievementsPresentation.Status
        )

        Assert.Equal(
            (RetroAchievementsPresentation.Title, RetroAchievementsPresentation.Ascending),
            RetroAchievementsPresentation.nextSort
                RetroAchievementsPresentation.Status
                RetroAchievementsPresentation.Descending
                RetroAchievementsPresentation.Title
        )

        let original = [ low; high ]

        Assert.Equal<RaAchievement list>(
            List.rev original,
            RetroAchievementsPresentation.sortAchievements
                RetroAchievementsPresentation.OriginalOrder
                RetroAchievementsPresentation.Descending
                original
            |> List.map snd
        )

    let private snapshot generation gameId =
        { Status = Active
          HardcoreEnabled = false
          User = None
          Game =
            Some
                { Id = gameId
                  Title = "Game"
                  Hash = "hash"
                  ImageUrl = "" }
          Achievements = []
          Leaderboards = []
          RichPresence = None
          Generation = generation }

    [<Fact>]
    let ``hardcore mode blocks all disallowed emulator operations`` () =
        let current =
            { snapshot 1L 1u with
                HardcoreEnabled = true }

        let allowed () = PauseAllowed

        Assert.Equal(OperationAllowed, RetroAchievementsOperations.evaluateSnapshot current allowed SaveState)

        let blockedOperations =
            [ LoadState; Rewind; SlowMotion; FrameAdvance; Cheats; InputPlayback; Debugger ]

        for operation in blockedOperations do
            match RetroAchievementsOperations.evaluateSnapshot current allowed operation with
            | OperationDenied message -> Assert.Contains("Hardcore Mode", message)
            | OperationAllowed -> Assert.Fail $"{operation} was unexpectedly allowed in Hardcore Mode."

        Assert.Equal(OperationAllowed, RetroAchievementsOperations.evaluateSnapshot current allowed Reset)
        Assert.Equal(OperationAllowed, RetroAchievementsOperations.evaluateSnapshot current allowed ChangeGame)

    [<Fact>]
    let ``future hardcore restrictions remain available outside active hardcore sessions`` () =
        let allowed () = PauseAllowed

        let restrictedOperations =
            [ LoadState; Rewind; SlowMotion; FrameAdvance; Cheats; InputPlayback; Debugger ]

        let softcore = snapshot 1L 1u
        let ready = { softcore with Status = Ready }

        for operation in restrictedOperations do
            Assert.Equal(OperationAllowed, RetroAchievementsOperations.evaluateSnapshot softcore allowed operation)

            Assert.Equal(OperationAllowed, RetroAchievementsOperations.evaluateSnapshot ready allowed operation)

    let private indicatorEvent eventType id generation gameId progress percent =
        { EventType = eventType
          RelatedId = id
          Title = $"Achievement {id}"
          Description = "Description"
          ImageUrl = $"https://example.test/{id}.png"
          MeasuredProgress = progress
          MeasuredPercent = percent
          Value = ""
          BestScore = ""
          Rank = 0u
          TotalEntries = 0u
          LeaderboardEntries = []
          Generation = generation
          GameId = gameId }

    [<Fact>]
    let ``challenge reducer supports multiple updates hides and unlocks`` () =
        let current = snapshot 8L 42u
        let first = indicatorEvent 5u 1u 8L 42u "" 0.0f
        let second = indicatorEvent 5u 2u 8L 42u "" 0.0f

        let shown =
            RetroAchievementsPresentation.emptyOverlayState
            |> RetroAchievementsPresentation.reduceOverlay current first
            |> RetroAchievementsPresentation.reduceOverlay current second

        Assert.Equal<uint32 list>([ 1u; 2u ], shown.Challenges |> List.map _.AchievementId)

        let reshown =
            RetroAchievementsPresentation.reduceOverlay current { first with Title = "Updated" } shown

        Assert.Equal(2, reshown.Challenges.Length)
        Assert.Equal("Updated", reshown.Challenges.Head.Title)
        Assert.NotEqual(shown.Challenges.Head.Revision, reshown.Challenges.Head.Revision)
        Assert.False(RetroAchievementsPresentation.containsIndicator shown.Challenges.Head reshown)
        Assert.True(RetroAchievementsPresentation.containsIndicator reshown.Challenges.Head reshown)

        let hidden =
            RetroAchievementsPresentation.reduceOverlay current (indicatorEvent 6u 2u 8L 42u "" 0.0f) reshown

        Assert.Single hidden.Challenges |> ignore
        Assert.False(RetroAchievementsPresentation.containsIndicator shown.Challenges[1] hidden)

        let unlocked =
            RetroAchievementsPresentation.reduceOverlay current (indicatorEvent 1u 1u 8L 42u "" 100.0f) hidden

        Assert.Empty unlocked.Challenges

    [<Fact>]
    let ``progress reducer preserves request identity across updates and handles hide`` () =
        let current = snapshot 8L 42u

        let shown =
            RetroAchievementsPresentation.reduceOverlay
                current
                (indicatorEvent 7u 3u 8L 42u "1/10" 10.0f)
                RetroAchievementsPresentation.emptyOverlayState

        let initial = shown.Progress.Value

        let updated =
            RetroAchievementsPresentation.reduceOverlay current (indicatorEvent 9u 3u 8L 42u "2/10" 20.0f) shown

        Assert.Equal("2/10", updated.Progress.Value.MeasuredProgress)
        Assert.Equal(initial.Revision, updated.Progress.Value.Revision)
        Assert.Equal(Some 20.0, RetroAchievementsPresentation.measuredPercent updated.Progress.Value)

        let textOnly =
            { updated.Progress.Value with
                MeasuredPercent = Single.NaN }

        Assert.True(RetroAchievementsPresentation.measuredPercent textOnly |> Option.isNone)

        let hidden =
            RetroAchievementsPresentation.reduceOverlay current (indicatorEvent 8u 3u 8L 42u "" 0.0f) updated

        Assert.True(hidden.Progress.IsNone)

    [<Fact>]
    let ``leaderboard reducer tracks values and scoreboard results`` () =
        let current = snapshot 8L 42u

        let tracker =
            { indicatorEvent 10u 3u 8L 42u "" 0.0f with
                Value = "1,234" }

        let shown =
            RetroAchievementsPresentation.reduceOverlay current tracker RetroAchievementsPresentation.emptyOverlayState

        Assert.Equal("1,234", Assert.Single(shown.LeaderboardTrackers).Display)

        let updated =
            RetroAchievementsPresentation.reduceOverlay
                current
                { tracker with
                    EventType = 12u
                    Value = "1,500" }
                shown

        Assert.Equal("1,500", Assert.Single(updated.LeaderboardTrackers).Display)

        let scoreboard =
            { tracker with
                EventType = 13u
                RelatedId = 9u
                Title = "High Score"
                Value = "1,500"
                BestScore = "2,000"
                Rank = 12u
                TotalEntries = 300u
                LeaderboardEntries =
                    [ { Username = "top"
                        Rank = 1u
                        Score = "9,999" } ] }

        let completed =
            RetroAchievementsPresentation.reduceOverlay current scoreboard updated

        Assert.Equal(12u, completed.Scoreboard.Value.Rank)
        Assert.Single(completed.Scoreboard.Value.TopEntries) |> ignore
        Assert.True((RetroAchievementsPresentation.clearScoreboard completed).Scoreboard.IsNone)

        let hidden =
            RetroAchievementsPresentation.reduceOverlay current { tracker with EventType = 11u } completed

        Assert.Empty hidden.LeaderboardTrackers

    [<Fact>]
    let ``overlay ignores stale events and clears outside active session`` () =
        let current = snapshot 8L 42u

        let shown =
            RetroAchievementsPresentation.emptyOverlayState
            |> RetroAchievementsPresentation.reduceOverlay current (indicatorEvent 5u 1u 8L 42u "" 0.0f)
            |> RetroAchievementsPresentation.reduceOverlay
                current
                { indicatorEvent 10u 3u 8L 42u "" 0.0f with
                    Value = "1,500" }
            |> RetroAchievementsPresentation.reduceOverlay
                current
                { indicatorEvent 13u 9u 8L 42u "" 0.0f with
                    Title = "High Score"
                    Value = "1,500" }

        let stale =
            RetroAchievementsPresentation.reduceOverlay current (indicatorEvent 5u 2u 7L 42u "" 0.0f) shown

        Assert.Single stale.Challenges |> ignore
        Assert.Single stale.LeaderboardTrackers |> ignore
        Assert.True(stale.Scoreboard.IsSome)

        let loggedOut =
            RetroAchievementsPresentation.synchronizeOverlay
                { current with
                    Status = LoggedOut
                    Game = None
                    Generation = 9L }
                stale

        Assert.Empty loggedOut.Challenges
        Assert.True(loggedOut.Progress.IsNone)
        Assert.Empty loggedOut.LeaderboardTrackers
        Assert.True(loggedOut.Scoreboard.IsNone)

    [<Fact>]
    let ``overlay view creates independent achievement and leaderboard controls`` () =
        let current = snapshot 8L 42u

        let state =
            RetroAchievementsPresentation.emptyOverlayState
            |> RetroAchievementsPresentation.reduceOverlay current (indicatorEvent 5u 1u 8L 42u "" 0.0f)
            |> RetroAchievementsPresentation.reduceOverlay current (indicatorEvent 5u 2u 8L 42u "" 0.0f)
            |> RetroAchievementsPresentation.reduceOverlay current (indicatorEvent 7u 3u 8L 42u "3/10" 30.0f)
            |> RetroAchievementsPresentation.reduceOverlay
                current
                { indicatorEvent 10u 4u 8L 42u "" 0.0f with
                    Value = "1,500" }
            |> RetroAchievementsPresentation.reduceOverlay
                current
                { indicatorEvent 13u 5u 8L 42u "" 0.0f with
                    Title = "High Score"
                    Value = "1,500"
                    BestScore = "2,000"
                    Rank = 12u
                    TotalEntries = 300u }

        let build () =
            RetroAchievementsOverlay.buildView state (fun _ -> None) ignore

        let first = build ()
        let second = build ()
        let firstHost = Grid()
        let secondHost = Grid()
        firstHost.Children.Add first |> ignore
        secondHost.Children.Add second |> ignore
        Assert.Equal(4, first.Children.Count)
        Assert.Equal(4, second.Children.Count)
        Assert.NotSame(first, second)

    [<Fact>]
    let ``image response requires HTTPS matching generation and game`` () =
        let current = snapshot 8L 42u

        Assert.True(RetroAchievementsPresentation.canRequestImage "https://example.test/image.png")
        Assert.False(RetroAchievementsPresentation.canRequestImage "http://example.test/image.png")
        Assert.False(RetroAchievementsPresentation.canRequestImage "not a URL")
        Assert.True(RetroAchievementsPresentation.isCurrentImage 8L 42u current)
        Assert.False(RetroAchievementsPresentation.isCurrentImage 7L 42u current)
        Assert.False(RetroAchievementsPresentation.isCurrentImage 8L 41u current)

    [<Fact>]
    let ``rebuilding achievement controls creates independent visual trees`` () =
        let items = [ achievement 1uy "Locked" ]

        let build () =
            let panel = StackPanel()

            RetroAchievementsPresentation.populateAchievementGroups
                panel
                (fun label -> TextBlock(Text = label))
                (fun item -> TextBlock(Text = item.Title))
                items

            panel

        let first = build ()
        let second = build ()
        let firstHost = ScrollViewer(Content = first)
        let secondHost = ScrollViewer(Content = second)
        Assert.NotSame(first, second)
        Assert.Equal(2, first.Children.Count)
        Assert.Equal(2, second.Children.Count)
        Assert.Same(first, firstHost.Content)
        Assert.Same(second, secondHost.Content)

    [<Fact>]
    let ``delayed image is discarded after game generation changes`` () =
        task {
            let response = TaskCompletionSource<HttpResponseMessage>()
            use http = new HttpClient(new StubHandler(fun _ -> response.Task))
            let mutable current = snapshot 8L 42u
            let loader = RaImageLoader(http, fun () -> current)
            let pending = loader.Load(8L, 42u, "https://example.test/image.png")
            current <- snapshot 9L 43u

            let png =
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="
                )

            response.SetResult(new HttpResponseMessage(HttpStatusCode.OK, Content = new ByteArrayContent(png)))
            let! result = pending
            Assert.True(result.IsNone)
        }

    [<Fact>]
    let ``decoded image cache enforces a total pixel budget`` () =
        let used = RetroAchievementsPresentation.imagePixelCountForDimensions 2048 2048
        Assert.True(RetroAchievementsPresentation.canCacheImageDimensions 0L 2048 2048)
        Assert.True(RetroAchievementsPresentation.canCacheImageDimensions used 2048 2048)
        Assert.False(RetroAchievementsPresentation.canCacheImageDimensions (used * 2L) 2048 2048)

    [<Fact>]
    let ``image loader rejects malformed and oversized responses`` () =
        task {
            let mutable body = [| 1uy; 2uy; 3uy |]

            use http =
                new HttpClient(
                    new StubHandler(fun _ ->
                        let content = new ByteArrayContent(body)

                        if body.Length > 1024 * 1024 then
                            content.Headers.ContentLength <- Nullable()

                        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK, Content = content)))
                )

            let loader = RaImageLoader(http, fun () -> snapshot 1L 1u)
            let! malformed = loader.Load(1L, 1u, "https://example.test/bad.png")
            Assert.True(malformed.IsNone)
            body <- Array.zeroCreate (1024 * 1024 + 1)
            let! oversized = loader.Load(1L, 1u, "https://example.test/large.png")
            Assert.True(oversized.IsNone)
        }

    [<Fact>]
    let ``RA state metadata rejects another game ROM and rcheevos version`` () =
        let game =
            { Id = 42u
              Title = "Game"
              Hash = "abcdef"
              ImageUrl = "" }

        let decoded gameId hash version : RaStateCodec.Decoded =
            { GameId = gameId
              RomHash = hash
              RcheevosVersion = version
              CoreState = Array.empty
              Progress = Array.empty }

        let error =
            function
            | Error message -> message
            | Ok() -> ""

        Assert.True(
            RaStateWorkflow.validateMetadata game 12003000u (decoded 42u "ABCDEF" 12003000u)
            |> Result.isOk
        )

        Assert.Contains(
            "another game",
            RaStateWorkflow.validateMetadata game 12003000u (decoded 41u "abcdef" 12003000u)
            |> error
        )

        Assert.Contains(
            "another ROM",
            RaStateWorkflow.validateMetadata game 12003000u (decoded 42u "other" 12003000u)
            |> error
        )

        Assert.Contains(
            "another rcheevos version",
            RaStateWorkflow.validateMetadata game 12003000u (decoded 42u "abcdef" 11000000u)
            |> error
        )
