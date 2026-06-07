module BubiBoy.Audio.Tests.AudioHostTests

open System.IO
open System
open BubiBoy.Audio
open BubiBoy.Core
open Xunit

let private sample value : Apu.Sample =
    { Left = single value
      Right = -single value }

[<Fact>]
let ``sample buffer preserves FIFO order`` () =
    let buffer = AudioHost.SampleBuffer 8

    let write = buffer.Enqueue [| sample 1; sample 2; sample 3 |]
    let read = buffer.Read 3

    Assert.Equal(3, write.AcceptedFrames)
    Assert.Equal(0, write.DroppedFrames)
    Assert.Equal(3, read.FramesRead)
    Assert.Equal(0, read.UnderrunFrames)
    Assert.Equal<Apu.Sample>([| sample 1; sample 2; sample 3 |], read.Samples)

[<Fact>]
let ``sample buffer pads underruns with silence`` () =
    let buffer = AudioHost.SampleBuffer 8
    buffer.Enqueue [| sample 7 |] |> ignore

    let read = buffer.Read 4

    Assert.Equal(1, read.FramesRead)
    Assert.Equal(3, read.UnderrunFrames)
    Assert.Equal(sample 7, read.Samples[0])
    Assert.Equal(Unchecked.defaultof<Apu.Sample>, read.Samples[1])
    Assert.Equal(Unchecked.defaultof<Apu.Sample>, read.Samples[2])
    Assert.Equal(Unchecked.defaultof<Apu.Sample>, read.Samples[3])

[<Fact>]
let ``sample buffer drops oldest frames when full`` () =
    let buffer = AudioHost.SampleBuffer 4

    buffer.Enqueue [| sample 1; sample 2; sample 3 |] |> ignore
    let write = buffer.Enqueue [| sample 4; sample 5; sample 6 |]
    let read = buffer.Read 4

    Assert.Equal(3, write.AcceptedFrames)
    Assert.Equal(2, write.DroppedFrames)
    Assert.Equal<Apu.Sample>([| sample 3; sample 4; sample 5; sample 6 |], read.Samples)

[<Fact>]
let ``buffered device rejects samples while stopped and clears on stop`` () =
    let device = AudioHost.createBufferedDevice 8
    let audioDevice = device :> AudioHost.AudioDevice

    let stoppedWrite = audioDevice.Enqueue [| sample 1; sample 2 |]
    audioDevice.Start()
    let runningWrite = audioDevice.Enqueue [| sample 3; sample 4 |]
    audioDevice.Stop()

    Assert.Equal(0, stoppedWrite.AcceptedFrames)
    Assert.Equal(2, stoppedWrite.DroppedFrames)
    Assert.Equal(2, runningWrite.AcceptedFrames)
    Assert.Equal(0, device.Buffer.Count)
    Assert.False(device.IsRunning)

[<Fact>]
let ``PCM conversion clips and writes little endian stereo frames`` () =
    let bytes =
        AudioHost.toPcm16StereoBytes [| { Left = -2.0f; Right = 0.0f }; { Left = 0.5f; Right = 2.0f } |]

    Assert.Equal<byte>([| 0x01uy; 0x80uy; 0x00uy; 0x00uy; 0x00uy; 0x40uy; 0xFFuy; 0x7Fuy |], bytes)

[<Fact>]
let ``WAV writer emits PCM header and payload`` () =
    let path =
        Path.Combine(Path.GetTempPath(), $"bubiboy-audio-{System.Guid.NewGuid():N}.wav")

    try
        AudioHost.writeWav path AudioHost.defaultFormat [| sample 1; sample -1 |]

        let bytes = File.ReadAllBytes path

        Assert.Equal(44 + 8, bytes.Length)
        Assert.Equal<byte>([| byte 'R'; byte 'I'; byte 'F'; byte 'F' |], bytes[0..3])
        Assert.Equal<byte>([| byte 'W'; byte 'A'; byte 'V'; byte 'E' |], bytes[8..11])
        Assert.Equal<byte>([| byte 'd'; byte 'a'; byte 't'; byte 'a' |], bytes[36..39])
        Assert.Equal(8, System.BitConverter.ToInt32(bytes, 40))
    finally
        if File.Exists path then
            File.Delete path

[<Fact>]
let ``miniaudio factory rejects invalid buffer size before native loading`` () =
    let result = Miniaudio.tryCreateDevice AudioHost.defaultFormat 0

    match result with
    | Ok _ -> Assert.Fail("Expected invalid miniaudio buffer size to fail.")
    | Error message -> Assert.Contains("positive", message)

[<Fact>]
let ``miniaudio availability probe is safe without native library`` () =
    Miniaudio.isNativeLibraryAvailable () |> ignore

[<Fact>]
let ``miniaudio native library is available when required by environment`` () =
    if Environment.GetEnvironmentVariable("BUBIBOY_EXPECT_NATIVE_AUDIO") = "1" then
        Assert.True(Miniaudio.isNativeLibraryAvailable ())

[<Fact>]
let ``miniaudio device accepts samples before start for priming when native library is available`` () =
    if Miniaudio.isNativeLibraryAvailable () then
        match Miniaudio.tryCreateDevice AudioHost.defaultFormat 1024 with
        | Ok device ->
            use _device = device
            let audioDevice = device :> AudioHost.AudioDevice

            let primingWrite = audioDevice.Enqueue [| sample 1 |]

            Assert.Equal(1, primingWrite.AcceptedFrames)
            Assert.Equal(0, primingWrite.DroppedFrames)
            Assert.Equal(1, audioDevice.Diagnostics().BufferedFrames)
        | Error _ -> ()
