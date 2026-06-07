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

    let private parseOptionalEnvironmentDuration name =
        let value = Environment.GetEnvironmentVariable name

        if String.IsNullOrWhiteSpace value then
            Ok None
        else
            parseDuration value |> Result.map Some

    let private capture romPath outputPath seconds pressAAt =
        RomFile.load romPath
        |> Result.bind (fun rom -> Emulator.createSession rom.Bytes)
        |> Result.bind (fun initialSession ->
            let statePath = Environment.GetEnvironmentVariable("BUBIBOY_STATE_PATH")

            let initialSessionResult =
                if String.IsNullOrWhiteSpace statePath then
                    Ok initialSession
                else
                    SaveStateFile.loadFromPath statePath initialSession

            let rightAtResult = parseOptionalEnvironmentDuration "BUBIBOY_RIGHT_AT"
            let rightDurationResult = parseOptionalEnvironmentDuration "BUBIBOY_RIGHT_DURATION"

            match initialSessionResult, rightAtResult, rightDurationResult with
            | Ok session, Ok rightAt, Ok rightDuration -> Ok(session, rightAt, rightDuration)
            | Error message, _, _
            | _, Error message, _
            | _, _, Error message -> Error message)
        |> Result.bind (fun (initialSession, rightAt, rightDuration) ->
            let traceApu = Environment.GetEnvironmentVariable("BUBIBOY_APU_LOG") = "1"
            let targetSamples = int (Math.Ceiling(seconds * float Apu.SampleRate))

            let pressStartSample =
                pressAAt |> Option.map (fun time -> int (time * float Apu.SampleRate))

            let pressEndSample =
                pressStartSample |> Option.map (fun start -> start + Apu.SampleRate / 10)

            let rightStartSample =
                rightAt |> Option.map (fun time -> int (time * float Apu.SampleRate))

            let rightEndSample =
                match rightStartSample, rightDuration with
                | Some start, Some duration -> Some(start + int (duration * float Apu.SampleRate))
                | Some _, None -> Some targetSamples
                | None, _ -> None

            let samples = ResizeArray<Apu.Sample>(targetSamples)
            let mutable session = initialSession
            let mutable stopReason = Emulator.FrameCompleted
            let mutable lastNoiseState = None
            let mutable lastChannelState = None

            while samples.Count < targetSamples && stopReason = Emulator.FrameCompleted do
                let shouldPressA =
                    match pressStartSample, pressEndSample with
                    | Some startSample, Some endSample -> samples.Count >= startSample && samples.Count < endSample
                    | _ -> false

                let shouldPressRight =
                    match rightStartSample, rightEndSample with
                    | Some startSample, Some endSample -> samples.Count >= startSample && samples.Count < endSample
                    | _ -> false

                let bus =
                    session.Bus
                    |> Bus.setButton Joypad.A shouldPressA
                    |> Bus.setButton Joypad.Right shouldPressRight

                session <- { session with Bus = bus }

                let result = Emulator.runFrame MaxStepsPerFrame session
                session <- result.Session
                stopReason <- result.StopReason

                if traceApu then
                    let apu = (SaveState.capture session).Bus.ApuSnapshot

                    let registers =
                        Bus.readByte 0xFF20us session.Bus,
                        Bus.readByte 0xFF21us session.Bus,
                        Bus.readByte 0xFF22us session.Bus,
                        Bus.readByte 0xFF23us session.Bus,
                        Bus.readByte 0xFF25us session.Bus,
                        Bus.readByte 0xFF26us session.Bus

                    let noiseState =
                        registers,
                        apu.SnapshotNoise.Enabled,
                        apu.SnapshotNoise.LengthCounter,
                        apu.SnapshotNoise.LengthEnabled,
                        apu.SnapshotNoise.Envelope.Volume,
                        apu.SnapshotNoise.Envelope.Timer

                    if Some noiseState <> lastNoiseState then
                        let nr41, nr42, nr43, nr44, nr51, nr52 = registers
                        let time = float samples.Count / float Apu.SampleRate

                        eprintfn
                            $"apu t={time:F6} NR41={nr41:X2} NR42={nr42:X2} NR43={nr43:X2} NR44={nr44:X2} NR51={nr51:X2} NR52={nr52:X2} enabled={apu.SnapshotNoise.Enabled} length={apu.SnapshotNoise.LengthCounter} lengthEnabled={apu.SnapshotNoise.LengthEnabled} volume={apu.SnapshotNoise.Envelope.Volume} envelopeTimer={apu.SnapshotNoise.Envelope.Timer}"

                        lastNoiseState <- Some noiseState

                    let channelState =
                        Bus.readByte 0xFF10us session.Bus,
                        Bus.readByte 0xFF11us session.Bus,
                        Bus.readByte 0xFF12us session.Bus,
                        Bus.readByte 0xFF13us session.Bus,
                        Bus.readByte 0xFF14us session.Bus,
                        Bus.readByte 0xFF1Aus session.Bus,
                        Bus.readByte 0xFF1Bus session.Bus,
                        Bus.readByte 0xFF1Cus session.Bus,
                        Bus.readByte 0xFF1Dus session.Bus,
                        Bus.readByte 0xFF1Eus session.Bus,
                        apu.SnapshotPulse1.Enabled,
                        apu.SnapshotPulse1.Envelope.Volume,
                        apu.SnapshotPulse1.Frequency,
                        apu.SnapshotPulse1.LengthCounter,
                        apu.SnapshotPulse1.LengthEnabled,
                        apu.SnapshotPulse1.Sweep,
                        apu.SnapshotPulse2.Enabled,
                        apu.SnapshotPulse2.Envelope.Volume,
                        apu.SnapshotPulse2.Frequency,
                        apu.SnapshotPulse2.LengthCounter,
                        apu.SnapshotPulse2.LengthEnabled,
                        apu.SnapshotWave.Enabled,
                        apu.SnapshotWave.Frequency,
                        apu.SnapshotWave.Position,
                        apu.SnapshotWave.LengthCounter,
                        apu.SnapshotWave.LengthEnabled

                    if Some channelState <> lastChannelState then
                        let time = float samples.Count / float Apu.SampleRate
                        let nr10 = Bus.readByte 0xFF10us session.Bus
                        let nr11 = Bus.readByte 0xFF11us session.Bus
                        let nr12 = Bus.readByte 0xFF12us session.Bus
                        let nr13 = Bus.readByte 0xFF13us session.Bus
                        let nr14 = Bus.readByte 0xFF14us session.Bus
                        let nr30 = Bus.readByte 0xFF1Aus session.Bus
                        let nr31 = Bus.readByte 0xFF1Bus session.Bus
                        let nr32 = Bus.readByte 0xFF1Cus session.Bus
                        let nr33 = Bus.readByte 0xFF1Dus session.Bus
                        let nr34 = Bus.readByte 0xFF1Eus session.Bus

                        let waveRam =
                            [| for address in 0xFF30us .. 0xFF3Fus -> Bus.readByte address session.Bus |]
                            |> Array.map (fun value -> value.ToString("X2"))
                            |> String.concat ""

                        eprintfn
                            $"channels t={time:F6} NR10={nr10:X2} NR11={nr11:X2} NR12={nr12:X2} NR13={nr13:X2} NR14={nr14:X2} NR30={nr30:X2} NR31={nr31:X2} NR32={nr32:X2} NR33={nr33:X2} NR34={nr34:X2} wave={waveRam} ch1={apu.SnapshotPulse1.Enabled}/{apu.SnapshotPulse1.Envelope.Volume}/f{apu.SnapshotPulse1.Frequency}/l{apu.SnapshotPulse1.LengthCounter}/{apu.SnapshotPulse1.LengthEnabled}/s{apu.SnapshotPulse1.Sweep} ch2={apu.SnapshotPulse2.Enabled}/{apu.SnapshotPulse2.Envelope.Volume}/f{apu.SnapshotPulse2.Frequency}/l{apu.SnapshotPulse2.LengthCounter}/{apu.SnapshotPulse2.LengthEnabled} ch3={apu.SnapshotWave.Enabled}/f{apu.SnapshotWave.Frequency}/p{apu.SnapshotWave.Position}/l{apu.SnapshotWave.LengthCounter}/{apu.SnapshotWave.LengthEnabled}/v{apu.SnapshotWave.OutputLevel}"

                        lastChannelState <- Some channelState

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
            let durationResult = if args.Length >= 3 then parseDuration args[2] else Ok 10.0

            let pressAAtResult =
                if args.Length = 4 then
                    parseDuration args[3] |> Result.map Some
                else
                    Ok None

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
