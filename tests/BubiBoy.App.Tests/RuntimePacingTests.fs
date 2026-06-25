namespace BubiBoy.App.Tests

open System
open System.Collections.Concurrent
open System.Threading.Tasks
open BubiBoy.App
open Xunit

type private FakeTimeProvider() =
    inherit TimeProvider()

    let mutable timestamp = 0L

    override _.TimestampFrequency = TimeSpan.TicksPerSecond
    override _.GetTimestamp() = timestamp

    member _.Advance(value: TimeSpan) = timestamp <- timestamp + value.Ticks

type private FakeAnimationScheduler() =
    let callbacks = ConcurrentQueue<Action<TimeSpan>>()
    let mutable available = true
    let mutable requests = 0

    member _.Available
        with get () = available
        and set value = available <- value

    member _.Requests = requests

    member _.RunNext() =
        match callbacks.TryDequeue() with
        | true, callback ->
            callback.Invoke TimeSpan.Zero
            true
        | _ -> false

    interface IAnimationFrameScheduler with
        member _.TryRequestFrame(callback) =
            requests <- requests + 1

            if available then
                callbacks.Enqueue callback
                true
            else
                false

module RuntimePacingTests =
    [<Fact>]
    let ``latest frame mailbox overwrites pending values and clears state`` () =
        let mailbox = LatestFrameMailbox<int>()
        Assert.Equal((0, 0L), mailbox.Snapshot())
        mailbox.Publish 1
        mailbox.Publish 2
        let value, pending, overwritten = mailbox.Take()
        Assert.Equal(Some 2, value)
        Assert.Equal(1, pending)
        Assert.Equal(1L, overwritten)
        Assert.Equal((0, 1L), mailbox.Snapshot())
        mailbox.Clear()
        Assert.Equal((0, 0L), mailbox.Snapshot())

    [<Fact>]
    let ``latest frame mailbox stays bounded when producer outruns consumer`` () =
        let mailbox = LatestFrameMailbox<int>()
        let received = ResizeArray<int>()

        for value = 1 to 100 do
            mailbox.Publish value
            let pending, _ = mailbox.Snapshot()
            Assert.InRange(pending, 0, 1)

            if value % 10 = 0 then
                let frame, _, _ = mailbox.Take()
                frame |> Option.iter received.Add

        Assert.Equal<int list>([ 10; 20; 30; 40; 50; 60; 70; 80; 90; 100 ], Seq.toList received)

    [<Fact>]
    let ``latest frame mailbox supports concurrent publish and take`` () =
        let mailbox = LatestFrameMailbox<int>()

        let producer =
            Task.Run(fun () ->
                for value = 1 to 10_000 do
                    mailbox.Publish value)

        let consumer =
            Task.Run(fun () ->
                while not producer.IsCompleted do
                    mailbox.Take() |> ignore)

        Task.WaitAll(producer, consumer)
        let pending, _ = mailbox.Snapshot()
        Assert.InRange(pending, 0, 1)

    [<Fact>]
    let ``animation coordinator reschedules and ignores stale callbacks after stop`` () =
        let scheduler = FakeAnimationScheduler()
        let mutable presented = 0

        let coordinator =
            FramePresentationCoordinator(scheduler, fun () -> presented <- presented + 1)

        coordinator.Start()
        coordinator.Start()
        Assert.Equal(1, scheduler.Requests)
        Assert.True(scheduler.RunNext())
        Assert.Equal(1, presented)
        Assert.Equal(2, scheduler.Requests)
        coordinator.FallbackTick()
        Assert.Equal(1, presented)
        coordinator.Stop()
        Assert.True(scheduler.RunNext())
        Assert.Equal(1, presented)

    [<Fact>]
    let ``animation coordinator falls back and returns to animation frames`` () =
        let scheduler = FakeAnimationScheduler()
        scheduler.Available <- false
        let mutable presented = 0

        let coordinator =
            FramePresentationCoordinator(scheduler, fun () -> presented <- presented + 1)

        coordinator.Start()
        scheduler.Available <- true
        coordinator.FallbackTick()
        Assert.Equal(1, presented)
        Assert.True(scheduler.RunNext())
        Assert.Equal(2, presented)

    [<Fact>]
    let ``audio pacing promotes target after delayed underrun`` () =
        let clock = FakeTimeProvider()
        let pacing = AudioPacing(3216, 4824, clock)
        pacing.Reset 100L
        Assert.Equal(3216, pacing.Update 101L)
        clock.Advance(TimeSpan.FromSeconds 1.0)
        Assert.Equal(4824, pacing.Update 101L)
        Assert.Equal(4824, pacing.Update 100L)
