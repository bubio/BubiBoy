open System
open System.Threading
open BubiBoy.Audio
open BubiBoy.Core

let private makeTone frequency seconds =
    let sampleCount = int (single AudioHost.defaultFormat.SampleRate * seconds)
    let samples = Array.zeroCreate<Apu.Sample> sampleCount

    for index in 0 .. sampleCount - 1 do
        let phase = 2.0 * Math.PI * frequency * float index / float AudioHost.defaultFormat.SampleRate
        let value = single (Math.Sin phase) * 0.20f
        samples[index] <- { Left = value; Right = value }

    samples

[<EntryPoint>]
let main _ =
    match Miniaudio.tryCreateDevice AudioHost.defaultFormat (AudioHost.defaultFormat.SampleRate / 2) with
    | Error message ->
        eprintfn $"miniaudio unavailable: {message}"
        1
    | Ok device ->
        use device = device
        let audio = device :> AudioHost.AudioDevice
        let tone = makeTone 440.0 1.5f
        let chunkFrames = 1024

        audio.Start()

        for offset in 0 .. chunkFrames .. tone.Length - 1 do
            let length = min chunkFrames (tone.Length - offset)
            let chunk = Array.zeroCreate<Apu.Sample> length
            Array.Copy(tone, offset, chunk, 0, length)
            audio.Enqueue chunk |> ignore
            Thread.Sleep(int (1000.0 * float length / float AudioHost.defaultFormat.SampleRate))

        Thread.Sleep 100
        let diagnostics = audio.Diagnostics()
        audio.Stop()
        printfn $"buffered={diagnostics.BufferedFrames} underrun={diagnostics.UnderrunFrames} dropped={diagnostics.DroppedFrames}"
        0
