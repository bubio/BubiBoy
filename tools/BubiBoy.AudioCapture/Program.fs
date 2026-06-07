namespace BubiBoy.AudioCapture

open System
open System.Collections.Generic
open System.Globalization
open BubiBoy.Audio
open BubiBoy.Core
open BubiBoy.IO

module Program =
    [<Literal>]
    let private MaxStepsPerFrame = 250_000

    let private usage () =
        eprintfn "Usage: BubiBoy.AudioCapture <rom-path> <output-wav> [seconds] [press-a-at-seconds]"
        2

    let private parseDuration (value: string) =
        match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, seconds when Double.IsFinite(seconds) && seconds > 0.0 -> Ok seconds
        | _ -> Error $"Invalid duration: {value}"

    let private capture romPath outputPath seconds pressAAt =
        RomFile.load romPath
        |> Result.bind (fun rom -> Emulator.createSession rom.Bytes)
        |> Result.bind (fun initialSession ->
            let traceApu = Environment.GetEnvironmentVariable("BUBIBOY_APU_LOG") = "1"
            let targetSamples = int (Math.Ceiling(seconds * float Apu.SampleRate))
            let pressStartSample = pressAAt |> Option.map (fun time -> int (time * float Apu.SampleRate))
            let pressEndSample = pressStartSample |> Option.map (fun start -> start + Apu.SampleRate / 10)
            let samples = ResizeArray<Apu.Sample>(targetSamples)
            let mutable session = initialSession
            let mutable stopReason = Emulator.FrameCompleted
            let mutable lastNoiseRegisters = None

            while samples.Count < targetSamples && stopReason = Emulator.FrameCompleted do
                let shouldPressA =
                    match pressStartSample, pressEndSample with
                    | Some startSample, Some endSample ->
                        samples.Count >= startSample && samples.Count < endSample
                    | _ -> false

                session <-
                    { session with
                        Bus = Bus.setButton Joypad.A shouldPressA session.Bus }

                let result = Emulator.runFrame MaxStepsPerFrame session
                session <- result.Session
                stopReason <- result.StopReason

                if traceApu then
                    let registers =
                        Bus.readByte 0xFF21us session.Bus,
                        Bus.readByte 0xFF22us session.Bus,
                        Bus.readByte 0xFF23us session.Bus,
                        Bus.readByte 0xFF25us session.Bus,
                        Bus.readByte 0xFF26us session.Bus

                    if Some registers <> lastNoiseRegisters then
                        let nr42, nr43, nr44, nr51, nr52 = registers
                        let time = float samples.Count / float Apu.SampleRate
                        eprintfn
                            $"apu t={time:F6} NR42={nr42:X2} NR43={nr43:X2} NR44={nr44:X2} NR51={nr51:X2} NR52={nr52:X2}"

                        lastNoiseRegisters <- Some registers

                let remaining = targetSamples - samples.Count
                let count = min remaining result.AudioSamples.Length

                for index in 0 .. count - 1 do
                    samples.Add result.AudioSamples[index]

            if stopReason <> Emulator.FrameCompleted then
                Error $"Emulation stopped before capture completed: {stopReason}"
            else
                AudioHost.writeWav outputPath AudioHost.defaultFormat (samples.ToArray())
                Ok(samples.Count, session.Steps, session.TotalCycles))

    [<EntryPoint>]
    let main args =
        if args.Length < 2 || args.Length > 4 then
            usage ()
        else
            let durationResult =
                if args.Length >= 3 then parseDuration args[2] else Ok 10.0

            let pressAAtResult =
                if args.Length = 4 then parseDuration args[3] |> Result.map Some else Ok None

            match durationResult, pressAAtResult with
            | Error message, _
            | _, Error message ->
                eprintfn $"{message}"
                usage ()
            | Ok seconds, Ok pressAAt ->
                match capture args[0] args[1] seconds pressAAt with
                | Error message ->
                    eprintfn $"{message}"
                    1
                | Ok(sampleCount, steps, cycles) ->
                    printfn
                        $"Captured {sampleCount} stereo frames ({seconds:F3} seconds) after {steps} steps and {cycles} hardware cycles."

                    0
