namespace BubiBoy.App.Tests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open BubiBoy.App
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
              ImageUrl = "" }

        Assert.Equal(
            Some "Achievement unlocked: First Clear",
            RetroAchievementsPresentation.notificationMessage (event 1u "First Clear")
        )

        Assert.Equal(
            Some "All achievements completed.",
            RetroAchievementsPresentation.notificationMessage (event 15u "")
        )

    [<Fact>]
    let ``achievement buckets use the documented display order`` () =
        let input =
            [ achievement 3uy "Unsupported"
              achievement 7uy "Almost There"
              achievement 1uy "Locked"
              achievement 6uy "Active Challenge"
              achievement 5uy "Recently Unlocked"
              achievement 2uy "Unlocked" ]

        let labels =
            RetroAchievementsPresentation.achievementGroups input
            |> List.map (fun ((_, label), _) -> label)

        Assert.Equal<string list>(
            [ "Locked"
              "Unlocked"
              "Recently Unlocked"
              "Active Challenge"
              "Almost There"
              "Unsupported" ],
            labels
        )

    let private snapshot generation gameId =
        { Status = Active
          User = None
          Game =
            Some
                { Id = gameId
                  Title = "Game"
                  Hash = "hash"
                  ImageUrl = "" }
          Achievements = []
          Generation = generation }

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
    let ``image loader rejects malformed and oversized responses`` () =
        task {
            let mutable body = [| 1uy; 2uy; 3uy |]

            use http =
                new HttpClient(
                    new StubHandler(fun _ ->
                        Task.FromResult(
                            new HttpResponseMessage(HttpStatusCode.OK, Content = new ByteArrayContent(body))
                        ))
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
