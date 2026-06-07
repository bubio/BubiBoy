namespace BubiBoy.Audio

open System
open System.IO
open BubiBoy.Core

module AudioHost =
    type AudioFormat = { SampleRate: int; Channels: int }

    type BufferWriteResult =
        { AcceptedFrames: int
          DroppedFrames: int }

    type BufferReadResult =
        { Samples: Apu.Sample[]
          FramesRead: int
          UnderrunFrames: int }

    type AudioDiagnostics =
        { BufferedFrames: int
          UnderrunFrames: int64
          DroppedFrames: int64
          IsRunning: bool }

    type AudioDevice =
        abstract Start: unit -> unit
        abstract Stop: unit -> unit
        abstract Enqueue: Apu.Sample[] -> BufferWriteResult
        abstract Diagnostics: unit -> AudioDiagnostics

    let private clampSample value = Math.Clamp(value, -1.0f, 1.0f)

    let private toPcm16 value =
        let clamped = clampSample value
        int16 (MathF.Round(clamped * 32767.0f))

    let toPcm16StereoBytes (samples: Apu.Sample[]) =
        let bytes = Array.zeroCreate<byte> (samples.Length * 4)

        for index in 0 .. samples.Length - 1 do
            let sample = samples[index]
            let left = toPcm16 sample.Left
            let right = toPcm16 sample.Right
            let offset = index * 4

            bytes[offset] <- byte (uint16 left &&& 0x00FFus)
            bytes[offset + 1] <- byte ((uint16 left >>> 8) &&& 0x00FFus)
            bytes[offset + 2] <- byte (uint16 right &&& 0x00FFus)
            bytes[offset + 3] <- byte ((uint16 right >>> 8) &&& 0x00FFus)

        bytes

    let writeWav path format (samples: Apu.Sample[]) =
        if format.Channels <> 2 then
            invalidArg (nameof format) "Only stereo output is supported."

        let pcm = toPcm16StereoBytes samples
        let byteRate = format.SampleRate * format.Channels * 2
        let blockAlign = int16 (format.Channels * 2)

        use stream = File.Create(path)
        use writer = new BinaryWriter(stream)

        writer.Write([| byte 'R'; byte 'I'; byte 'F'; byte 'F' |])
        writer.Write(36 + pcm.Length)
        writer.Write([| byte 'W'; byte 'A'; byte 'V'; byte 'E' |])
        writer.Write([| byte 'f'; byte 'm'; byte 't'; byte ' ' |])
        writer.Write(16)
        writer.Write(int16 1)
        writer.Write(int16 format.Channels)
        writer.Write(format.SampleRate)
        writer.Write(byteRate)
        writer.Write(blockAlign)
        writer.Write(int16 16)
        writer.Write([| byte 'd'; byte 'a'; byte 't'; byte 'a' |])
        writer.Write(pcm.Length)
        writer.Write(pcm)

    type SampleBuffer(capacityFrames: int) =
        do
            if capacityFrames <= 0 then
                invalidArg (nameof capacityFrames) "Audio buffer capacity must be positive."

        let gate = obj ()
        let buffer = Array.zeroCreate<Apu.Sample> capacityFrames
        let mutable readIndex = 0
        let mutable count = 0
        let mutable underrunFrames = 0L
        let mutable droppedFrames = 0L

        member _.CapacityFrames = capacityFrames

        member _.Count = lock gate (fun () -> count)

        member _.Clear() =
            lock gate (fun () ->
                readIndex <- 0
                count <- 0)

        member _.DroppedFrames = lock gate (fun () -> droppedFrames)

        member _.UnderrunFrames = lock gate (fun () -> underrunFrames)

        member _.Enqueue(samples: Apu.Sample[]) =
            lock gate (fun () ->
                let mutable dropped = 0

                for sample in samples do
                    if count = capacityFrames then
                        readIndex <- (readIndex + 1) % capacityFrames
                        count <- count - 1
                        dropped <- dropped + 1
                        droppedFrames <- droppedFrames + 1L

                    let writeIndex = (readIndex + count) % capacityFrames
                    buffer[writeIndex] <- sample
                    count <- count + 1

                { AcceptedFrames = samples.Length
                  DroppedFrames = dropped })

        member _.Read(requestedFrames: int) =
            if requestedFrames < 0 then
                invalidArg (nameof requestedFrames) "Requested frame count must not be negative."

            lock gate (fun () ->
                let samples = Array.zeroCreate<Apu.Sample> requestedFrames
                let framesRead = min requestedFrames count

                for offset in 0 .. framesRead - 1 do
                    samples[offset] <- buffer[(readIndex + offset) % capacityFrames]

                readIndex <- (readIndex + framesRead) % capacityFrames
                count <- count - framesRead
                underrunFrames <- underrunFrames + int64 (requestedFrames - framesRead)

                { Samples = samples
                  FramesRead = framesRead
                  UnderrunFrames = requestedFrames - framesRead })

    type BufferedAudioDevice(format: AudioFormat, capacityFrames: int) =
        let buffer = SampleBuffer capacityFrames
        let mutable running = false

        member _.Format = format
        member _.Buffer = buffer
        member _.IsRunning = running
        member _.Read(frames) = buffer.Read frames

        interface AudioDevice with
            member _.Start() = running <- true

            member _.Stop() =
                running <- false
                buffer.Clear()

            member _.Enqueue(samples) =
                if running then
                    buffer.Enqueue samples
                else
                    { AcceptedFrames = 0
                      DroppedFrames = samples.Length }

            member _.Diagnostics() =
                { BufferedFrames = buffer.Count
                  UnderrunFrames = buffer.UnderrunFrames
                  DroppedFrames = buffer.DroppedFrames
                  IsRunning = running }

    let defaultFormat = { SampleRate = 48_000; Channels = 2 }

    let createBufferedDevice capacityFrames =
        BufferedAudioDevice(defaultFormat, capacityFrames)
