namespace BubiBoy.RetroAchievements.Tests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks
open BubiBoy.RetroAchievements
open Xunit

type internal FakeTimeProvider() =
    inherit TimeProvider()

    let mutable timestamp = 0L

    override _.TimestampFrequency = TimeSpan.TicksPerSecond
    override _.GetTimestamp() = timestamp

    member _.Advance(value: TimeSpan) = timestamp <- timestamp + value.Ticks

type internal StubHttpHandler(send: HttpRequestMessage * CancellationToken -> Task<HttpResponseMessage>) =
    inherit HttpMessageHandler()

    override _.SendAsync(request, cancellationToken) = send (request, cancellationToken)

type internal FakeNativeBackend() =
    let completions = ResizeArray<unativeint * int * byte[]>()
    let mutable operationCallback: NativeInterop.OperationCallback option = None
    let mutable readMemoryCallback: NativeInterop.ReadMemoryCallback option = None
    let mutable serverCallback: NativeInterop.ServerRequestCallback option = None
    let mutable eventCallback: NativeInterop.EventCallback option = None
    let mutable doFrameAction: unit -> unit = fun () -> ()
    let mutable user: NativeInterop.UserData option = None
    let mutable game: NativeInterop.GameData option = None
    let mutable achievements: RaAchievement list = []
    let mutable richPresence: string option = None
    let mutable richPresenceReadCount = 0
    let mutable doFrameCount = 0
    let mutable idleCount = 0
    let mutable canPause = true, 0u
    let mutable abortCount = 0

    let api: NativeInterop.Api =
        { Create =
            fun (readMemory, server, event, _, _) ->
                readMemoryCallback <- Some readMemory
                serverCallback <- Some server
                eventCallback <- Some event
                nativeint 1
          Destroy = ignore
          Version = fun () -> 12003000u
          UserAgent =
            fun (_, buffer, _) ->
                buffer.Append("rcheevos/12.3.0") |> ignore
                unativeint buffer.Length
          CompleteServerRequest =
            fun (_, requestId, status, body, _) -> completions.Add(requestId, status, Array.copy body)
          AbortServerRequests = fun _ -> abortCount <- abortCount + 1
          CancelOperation = ignore
          LoginPassword = fun (_, _, _, callback) -> operationCallback <- Some callback
          LoginToken = fun (_, _, _, callback) -> operationCallback <- Some callback
          Logout = ignore
          GetUser = fun _ -> user
          LoadGame = fun (_, _, _, _, callback) -> operationCallback <- Some callback
          UnloadGame = ignore
          GetGame = fun _ -> game
          GetRichPresence =
            fun _ ->
                richPresenceReadCount <- richPresenceReadCount + 1
                richPresence
          EnumerateAchievements =
            fun (_, callback) ->
                achievements
                |> List.iter (fun achievement ->
                    callback.Invoke(
                        nativeint 0,
                        achievement.Bucket,
                        achievement.BucketLabel,
                        achievement.Id,
                        achievement.Title,
                        achievement.Description,
                        achievement.Points,
                        achievement.MeasuredProgress,
                        achievement.MeasuredPercent,
                        achievement.Rarity,
                        achievement.State,
                        achievement.Unlocked,
                        achievement.ImageUrl
                    ))
          DoFrame =
            fun _ ->
                doFrameCount <- doFrameCount + 1
                doFrameAction ()
          Idle = fun _ -> idleCount <- idleCount + 1
          CanPause = fun _ -> canPause
          Reset = ignore
          ProgressSize = fun _ -> unativeint 0
          SerializeProgress = fun (_, _, _) -> 0
          DeserializeProgress = fun (_, _, _) -> 0 }

    member _.Api = api

    member _.User
        with get () = user
        and set value = user <- value

    member _.Game
        with get () = game
        and set value = game <- value

    member _.Achievements
        with get () = achievements
        and set value = achievements <- value

    member _.RichPresence
        with get () = richPresence
        and set value = richPresence <- value

    member _.RichPresenceReadCount = richPresenceReadCount

    member _.DoFrameAction
        with get () = doFrameAction
        and set value = doFrameAction <- value

    member _.DoFrameCount = doFrameCount
    member _.IdleCount = idleCount

    member _.CanPauseResult
        with get () = canPause
        and set value = canPause <- value

    member _.AbortCount = abortCount
    member _.Completions = completions |> Seq.toList

    member _.CompleteOperation(result: int, message: string) =
        operationCallback.Value.Invoke(nativeint 0, result, message)
        operationCallback <- None

    member _.RaiseEvent(eventType: uint32, id: uint32, title: string, description: string) =
        eventCallback.Value.Invoke(nativeint 0, eventType, id, title, description, "", "", 0.0f)

    member _.RaiseIndicatorEvent
        (
            eventType: uint32,
            id: uint32,
            title: string,
            description: string,
            imageUrl: string,
            measuredProgress: string,
            measuredPercent: float32
        ) =
        eventCallback.Value.Invoke(
            nativeint 0,
            eventType,
            id,
            title,
            description,
            imageUrl,
            measuredProgress,
            measuredPercent
        )

    member _.Request(requestId: unativeint, url: string, postData: string, contentType: string) =
        serverCallback.Value.Invoke(nativeint 0, requestId, url, postData, contentType)

    member _.ReadMemory(address: uint32, count: int) =
        let destination = Marshal.AllocHGlobal count

        try
            let copied =
                readMemoryCallback.Value.Invoke(nativeint 0, address, destination, uint32 count)

            let bytes = Array.zeroCreate<byte> (int copied)

            if copied > 0u then
                Marshal.Copy(destination, bytes, 0, int copied)

            bytes
        finally
            Marshal.FreeHGlobal destination

module internal TestHelpers =
    let credentials () =
        let saved = ResizeArray<string * string>()
        let deleted = ResizeArray<string>()
        let mutable token: string option = None

        let store: RaCredentialStore.Store =
            { SaveToken =
                fun username value ->
                    saved.Add(username, value)
                    Ok()
              TryLoadToken = fun _ -> token
              DeleteToken = deleted.Add }

        store, saved, deleted, (fun value -> token <- value)

    let client
        (backend: FakeNativeBackend)
        (credentials: RaCredentialStore.Store)
        (http: HttpClient)
        (timeProvider: TimeProvider)
        timeout
        (logs: ResizeArray<string>)
        =
        new RaClient(backend.Api, credentials, http, timeProvider, timeout, logs.Add)

    let session () =
        Array.zeroCreate<byte> 0x8000
        |> BubiBoy.Core.Emulator.createSession
        |> Result.defaultWith failwith

    let achievement bucket label unlocked =
        { Bucket = bucket
          BucketLabel = label
          Id = 7u
          Title = "First Clear"
          Description = "Clear the first stage"
          Points = 5u
          MeasuredProgress = ""
          MeasuredPercent = if unlocked = 0uy then 0.0f else 100.0f
          Rarity = 50.0f
          State = if unlocked = 0uy then 1uy else 3uy
          Unlocked = unlocked
          ImageUrl = "https://example.test/badge.png" }

    let activate (backend: FakeNativeBackend) (client: RaClient) =
        backend.User <-
            Some
                { Username = "player"
                  DisplayName = "Player"
                  Token = "secret-token"
                  Score = 0u
                  SoftcoreScore = 0u }

        client.LoginWithPassword("player", "secret-password")
        backend.CompleteOperation(0, "")

        backend.Game <-
            Some
                { Id = 123u
                  Title = "Test Game"
                  Hash = "hash"
                  ImageUrl = "https://example.test/game.png" }

        client.LoadGame(4u, [| 0uy |], session ())
        backend.CompleteOperation(0, "")

    let waitFor condition =
        Assert.True(SpinWait.SpinUntil((fun () -> condition ()), TimeSpan.FromSeconds 2.0))
