namespace BubiBoy.Core

module Apu =
    [<Literal>]
    let SampleRate = 48_000

    [<Literal>]
    let private FrameSequencerPeriodCycles = 8192

    [<Literal>]
    let private MaxVolume = 15

    [<Struct>]
    type Sample =
        { Left: single
          Right: single }

    type PendingSamples =
        private
            { Buffer: Sample[]
              Count: int }

    type EnvelopeDirection =
        | Decrease
        | Increase

    type Envelope =
        { InitialVolume: int
          Direction: EnvelopeDirection
          Period: int
          Timer: int
          Volume: int }

    type Sweep =
        { Period: int
          Negate: bool
          Shift: int
          Timer: int
          ShadowFrequency: int
          Enabled: bool }

    type PulseChannel =
        { Enabled: bool
          DacEnabled: bool
          Duty: int
          DutyStep: int
          LengthCounter: int
          LengthEnabled: bool
          Frequency: int
          Timer: int
          Envelope: Envelope
          Sweep: Sweep option }

    type WaveChannel =
        { Enabled: bool
          DacEnabled: bool
          LengthCounter: int
          LengthEnabled: bool
          Frequency: int
          Timer: int
          Position: int
          OutputLevel: int }

    type NoiseChannel =
        { Enabled: bool
          DacEnabled: bool
          LengthCounter: int
          LengthEnabled: bool
          Timer: int
          Lfsr: uint16
          Envelope: Envelope }

    type State =
        { FrameSequencerStep: int
          FrameSequencerCycles: int
          SkipNextFrameSequencerClock: bool
          SampleCycles: int64
          Pulse1: PulseChannel
          Pulse2: PulseChannel
          Wave: WaveChannel
          Noise: NoiseChannel
          PendingSamples: PendingSamples }

    let private emptyPendingSamples () =
        { Buffer = Array.zeroCreate<Sample> 2048
          Count = 0 }

    let private appendSample sample pending =
        let buffer =
            if pending.Count < pending.Buffer.Length then
                pending.Buffer
            else
                let next = Array.zeroCreate<Sample> (pending.Buffer.Length * 2)
                System.Array.Copy(pending.Buffer, next, pending.Buffer.Length)
                next

        buffer[pending.Count] <- sample

        { Buffer = buffer
          Count = pending.Count + 1 }

    let private emptyEnvelope =
        { InitialVolume = 0
          Direction = Decrease
          Period = 0
          Timer = 0
          Volume = 0 }

    let private emptyPulse =
        { Enabled = false
          DacEnabled = false
          Duty = 0
          DutyStep = 0
          LengthCounter = 0
          LengthEnabled = false
          Frequency = 0
          Timer = 0
          Envelope = emptyEnvelope
          Sweep = None }

    let initial =
        { FrameSequencerStep = 0
          FrameSequencerCycles = 0
          SkipNextFrameSequencerClock = false
          SampleCycles = 0L
          Pulse1 = emptyPulse
          Pulse2 = emptyPulse
          Wave =
            { Enabled = false
              DacEnabled = false
              LengthCounter = 0
              LengthEnabled = false
              Frequency = 0
              Timer = 0
              Position = 0
              OutputLevel = 0 }
          Noise =
            { Enabled = false
              DacEnabled = false
              LengthCounter = 0
              LengthEnabled = false
              Timer = 0
              Lfsr = 0x7FFFus
              Envelope = emptyEnvelope }
          PendingSamples = emptyPendingSamples () }

    let private bitSet bit value =
        value &&& (1uy <<< bit) <> 0uy

    let private envelopeFromNrx2 value =
        let initialVolume = int (value >>> 4)

        { InitialVolume = initialVolume
          Direction = if bitSet 3 value then Increase else Decrease
          Period = int (value &&& 0x07uy)
          Timer = int (value &&& 0x07uy)
          Volume = initialVolume }

    let private pulseTimer frequency =
        (2048 - frequency) * 4

    let private waveTimer frequency =
        (2048 - frequency) * 2

    let private noiseDivisors =
        [| 8; 16; 32; 48; 64; 80; 96; 112 |]

    let private noiseTimer nr43 =
        let divisor = noiseDivisors[int (nr43 &&& 0x07uy)]
        let shift = int (nr43 >>> 4)
        divisor <<< shift

    let private sweepFromNr10 value frequency : Sweep =
        let period = int ((value >>> 4) &&& 0x07uy)
        let shift = int (value &&& 0x07uy)

        { Period = period
          Negate = bitSet 3 value
          Shift = shift
          Timer = if period = 0 then 8 else period
          ShadowFrequency = frequency
          Enabled = period <> 0 || shift <> 0 }

    let private updatePulseFromRegisters hasSweep trigger nr10 nr11 nr12 nr13 nr14 (pulse: PulseChannel) : PulseChannel =
        let frequency = int nr13 ||| ((int nr14 &&& 0x07) <<< 8)
        let dacEnabled = nr12 &&& 0xF8uy <> 0uy
        let lengthCounter =
            if trigger then
                let loaded = 64 - int (nr11 &&& 0x3Fuy)
                if loaded = 0 then 64 else loaded
            else
                pulse.LengthCounter

        let next =
            { pulse with
                DacEnabled = dacEnabled
                Duty = int (nr11 >>> 6)
                LengthCounter = lengthCounter
                LengthEnabled = bitSet 6 nr14
                Frequency = frequency
                Envelope = if trigger then envelopeFromNrx2 nr12 else pulse.Envelope
                Sweep =
                    if hasSweep && trigger then
                        Some(sweepFromNr10 nr10 frequency)
                    elif hasSweep then
                        pulse.Sweep
                    else
                        None }

        if trigger && dacEnabled then
            { next with
                Enabled = true
                Timer = pulseTimer frequency
                DutyStep = 0 }
        elif not dacEnabled then
            { next with Enabled = false }
        else
            next

    let private updateWaveFromRegisters trigger nr30 nr31 nr32 nr33 nr34 (wave: WaveChannel) : WaveChannel =
        let frequency = int nr33 ||| ((int nr34 &&& 0x07) <<< 8)
        let dacEnabled = bitSet 7 nr30
        let lengthCounter =
            if trigger then
                let loaded = 256 - int nr31
                if loaded = 0 then 256 else loaded
            else
                wave.LengthCounter

        let next =
            { wave with
                DacEnabled = dacEnabled
                LengthCounter = lengthCounter
                LengthEnabled = bitSet 6 nr34
                Frequency = frequency
                OutputLevel = int ((nr32 >>> 5) &&& 0x03uy) }

        if trigger && dacEnabled then
            { next with
                Enabled = true
                Timer = waveTimer frequency
                Position = 0 }
        elif not dacEnabled then
            { next with Enabled = false }
        else
            next

    let private updateNoiseFromRegisters trigger nr41 nr42 nr43 nr44 (noise: NoiseChannel) : NoiseChannel =
        let dacEnabled = nr42 &&& 0xF8uy <> 0uy
        let lengthCounter =
            if trigger then
                let loaded = 64 - int (nr41 &&& 0x3Fuy)
                if loaded = 0 then 64 else loaded
            else
                noise.LengthCounter

        let next =
            { noise with
                DacEnabled = dacEnabled
                LengthCounter = lengthCounter
                LengthEnabled = bitSet 6 nr44
                Timer = noiseTimer nr43
                Envelope = if trigger then envelopeFromNrx2 nr42 else noise.Envelope }

        if trigger && dacEnabled then
            { next with
                Enabled = true
                Lfsr = 0x7FFFus }
        elif not dacEnabled then
            { next with Enabled = false }
        else
            next

    let applyRegisters (io: byte[]) (state: State) : State =
        let nr52 = io[0x26]

        if nr52 &&& 0x80uy = 0uy then
            initial
        else
            { state with
                Pulse1 = updatePulseFromRegisters true false io[0x10] io[0x11] io[0x12] io[0x13] io[0x14] state.Pulse1
                Pulse2 = updatePulseFromRegisters false false 0uy io[0x16] io[0x17] io[0x18] io[0x19] state.Pulse2
                Wave = updateWaveFromRegisters false io[0x1A] io[0x1B] io[0x1C] io[0x1D] io[0x1E] state.Wave
                Noise = updateNoiseFromRegisters false io[0x20] io[0x21] io[0x22] io[0x23] state.Noise }

    let private clearTriggerBit index value =
        match index with
        | 0x14
        | 0x19
        | 0x1E
        | 0x23 -> value &&& 0x7Fuy
        | _ -> value

    let private clearPoweredOffRegisters (io: byte[]) =
        let nextIo = Array.copy io

        for index in 0x10..0x25 do
            nextIo[index] <- 0uy

        nextIo[0x26] <- 0uy
        nextIo

    let private audioRegister index =
        index >= 0x10 && index <= 0x25

    let writeRegister index value (io: byte[]) (state: State) : byte[] * State =
        if index = 0x26 && value &&& 0x80uy = 0uy then
            clearPoweredOffRegisters io, initial
        elif index <> 0x26 && audioRegister index && io[0x26] &&& 0x80uy = 0uy then
            Array.copy io, state
        else
            let nextIo = Array.copy io
            nextIo[index] <- clearTriggerBit index value

            let nextState =
                match index with
                | 0x14 ->
                    applyRegisters nextIo
                        { state with
                            Pulse1 = updatePulseFromRegisters true (bitSet 7 value) nextIo[0x10] nextIo[0x11] nextIo[0x12] nextIo[0x13] value state.Pulse1 }
                | 0x19 ->
                    applyRegisters nextIo
                        { state with
                            Pulse2 = updatePulseFromRegisters false (bitSet 7 value) 0uy nextIo[0x16] nextIo[0x17] nextIo[0x18] value state.Pulse2 }
                | 0x1E ->
                    applyRegisters nextIo
                        { state with
                            Wave = updateWaveFromRegisters (bitSet 7 value) nextIo[0x1A] nextIo[0x1B] nextIo[0x1C] nextIo[0x1D] value state.Wave }
                | 0x23 ->
                    applyRegisters nextIo
                        { state with
                            Noise = updateNoiseFromRegisters (bitSet 7 value) nextIo[0x20] nextIo[0x21] nextIo[0x22] value state.Noise }
                | _ -> applyRegisters nextIo state

            nextIo[0x26] <-
                if nextIo[0x26] &&& 0x80uy = 0uy then
                    0uy
                else
                    let channelBits =
                        (if nextState.Pulse1.Enabled then 0x01uy else 0uy)
                        ||| (if nextState.Pulse2.Enabled then 0x02uy else 0uy)
                        ||| (if nextState.Wave.Enabled then 0x04uy else 0uy)
                        ||| (if nextState.Noise.Enabled then 0x08uy else 0uy)

                    0xF0uy ||| channelBits

            nextIo, nextState

    let statusRegister (io: byte[]) (state: State) =
        if io[0x26] &&& 0x80uy = 0uy then
            0uy
        else
            let channelBits =
                (if state.Pulse1.Enabled then 0x01uy else 0uy)
                ||| (if state.Pulse2.Enabled then 0x02uy else 0uy)
                ||| (if state.Wave.Enabled then 0x04uy else 0uy)
                ||| (if state.Noise.Enabled then 0x08uy else 0uy)

            0xF0uy ||| channelBits

    let pendingSamples (state: State) =
        let samples = Array.zeroCreate<Sample> state.PendingSamples.Count
        System.Array.Copy(state.PendingSamples.Buffer, samples, state.PendingSamples.Count)
        samples

    let clearPendingSamples (state: State) =
        { state with
            PendingSamples =
                { state.PendingSamples with
                    Count = 0 } }

    let private clockLengthPulse (channel: PulseChannel) : PulseChannel =
        if channel.Enabled && channel.LengthEnabled && channel.LengthCounter > 0 then
            let lengthCounter = channel.LengthCounter - 1
            { channel with
                LengthCounter = lengthCounter
                Enabled = lengthCounter <> 0 }
        else
            channel

    let private clockLengthWave (channel: WaveChannel) : WaveChannel =
        if channel.Enabled && channel.LengthEnabled && channel.LengthCounter > 0 then
            let lengthCounter = channel.LengthCounter - 1
            { channel with
                LengthCounter = lengthCounter
                Enabled = lengthCounter <> 0 }
        else
            channel

    let private clockLengthNoise (channel: NoiseChannel) : NoiseChannel =
        if channel.Enabled && channel.LengthEnabled && channel.LengthCounter > 0 then
            let lengthCounter = channel.LengthCounter - 1
            { channel with
                LengthCounter = lengthCounter
                Enabled = lengthCounter <> 0 }
        else
            channel

    let private clockEnvelope (envelope: Envelope) : Envelope =
        if envelope.Period = 0 then
            envelope
        else
            let timer = envelope.Timer - 1

            if timer > 0 then
                { envelope with Timer = timer }
            else
                let volumeDelta =
                    match envelope.Direction with
                    | Increase -> 1
                    | Decrease -> -1

                let nextVolume = envelope.Volume + volumeDelta
                let volume =
                    if nextVolume < 0 || nextVolume > MaxVolume then
                        envelope.Volume
                    else
                        nextVolume

                { envelope with
                    Timer = envelope.Period
                    Volume = volume }

    let private clockSweep (channel: PulseChannel) : PulseChannel =
        match channel.Sweep with
        | None -> channel
        | Some sweep when channel.Enabled && sweep.Enabled ->
            let timer = sweep.Timer - 1

            if timer > 0 then
                { channel with Sweep = Some { sweep with Timer = timer } }
            else
                let delta = sweep.ShadowFrequency >>> sweep.Shift
                let nextFrequency =
                    if sweep.Negate then
                        sweep.ShadowFrequency - delta
                    else
                        sweep.ShadowFrequency + delta

                let nextSweep =
                    { sweep with
                        Timer = if sweep.Period = 0 then 8 else sweep.Period
                        ShadowFrequency = nextFrequency }

                if sweep.Shift <> 0 && (nextFrequency < 0 || nextFrequency > 2047) then
                    { channel with Enabled = false; Sweep = Some nextSweep }
                elif sweep.Shift <> 0 then
                    { channel with
                        Frequency = nextFrequency
                        Timer = pulseTimer nextFrequency
                        Sweep = Some nextSweep }
                else
                    { channel with Sweep = Some nextSweep }
        | _ -> channel

    let private clockFrameSequencer (state: State) : State =
        let nextStep = (state.FrameSequencerStep + 1) &&& 0x07
        let shouldClockLength = state.FrameSequencerStep = 0 || state.FrameSequencerStep = 2 || state.FrameSequencerStep = 4 || state.FrameSequencerStep = 6
        let shouldClockSweep = state.FrameSequencerStep = 2 || state.FrameSequencerStep = 6
        let shouldClockEnvelope = state.FrameSequencerStep = 7

        let pulse1 =
            state.Pulse1
            |> (if shouldClockLength then clockLengthPulse else id)
            |> (if shouldClockSweep then clockSweep else id)

        let pulse2 =
            state.Pulse2
            |> (if shouldClockLength then clockLengthPulse else id)

        { state with
            FrameSequencerStep = nextStep
            Pulse1 =
                { pulse1 with
                    Envelope = if shouldClockEnvelope then clockEnvelope pulse1.Envelope else pulse1.Envelope }
            Pulse2 =
                { pulse2 with
                    Envelope = if shouldClockEnvelope then clockEnvelope pulse2.Envelope else pulse2.Envelope }
            Wave = if shouldClockLength then clockLengthWave state.Wave else state.Wave
            Noise =
                let noise = if shouldClockLength then clockLengthNoise state.Noise else state.Noise
                { noise with Envelope = if shouldClockEnvelope then clockEnvelope noise.Envelope else noise.Envelope } }

    let private clockDivApuEvent (state: State) =
        if state.SkipNextFrameSequencerClock then
            { state with SkipNextFrameSequencerClock = false }
        else
            clockFrameSequencer state

    let skipNextFrameSequencerClock (state: State) =
        { state with SkipNextFrameSequencerClock = true }

    let resetDiv divider (io: byte[]) (state: State) =
        if io[0x26] &&& 0x80uy = 0uy then
            initial
        else
            let clocked =
                if divider &&& 0x1000us <> 0us then
                    clockDivApuEvent state
                else
                    state

            { clocked with FrameSequencerCycles = 0 }

    let private dutyPatterns =
        [| [| 0; 0; 0; 0; 0; 0; 0; 1 |]
           [| 1; 0; 0; 0; 0; 0; 0; 1 |]
           [| 1; 0; 0; 0; 0; 1; 1; 1 |]
           [| 0; 1; 1; 1; 1; 1; 1; 0 |] |]

    let private tickPulse cycles (channel: PulseChannel) : PulseChannel =
        if not channel.Enabled then
            channel
        else
            let mutable timer = channel.Timer - cycles
            let mutable dutyStep = channel.DutyStep

            while timer <= 0 do
                timer <- timer + pulseTimer channel.Frequency
                dutyStep <- (dutyStep + 1) &&& 0x07

            { channel with Timer = timer; DutyStep = dutyStep }

    let private tickWave cycles (channel: WaveChannel) : WaveChannel =
        if not channel.Enabled then
            channel
        else
            let mutable timer = channel.Timer - cycles
            let mutable position = channel.Position

            while timer <= 0 do
                timer <- timer + waveTimer channel.Frequency
                position <- (position + 1) &&& 0x1F

            { channel with Timer = timer; Position = position }

    let private tickNoise cycles nr43 (channel: NoiseChannel) : NoiseChannel =
        if not channel.Enabled then
            channel
        else
            let mutable timer = channel.Timer - cycles
            let mutable lfsr = channel.Lfsr
            let period = noiseTimer nr43
            let widthMode = bitSet 3 nr43

            while timer <= 0 do
                timer <- timer + period
                let feedback = (lfsr &&& 0x0001us) ^^^ ((lfsr >>> 1) &&& 0x0001us)
                lfsr <- (lfsr >>> 1) ||| (feedback <<< 14)

                if widthMode then
                    lfsr <- (lfsr &&& ~~~0x0040us) ||| (feedback <<< 6)

            { channel with Timer = timer; Lfsr = lfsr }

    let private pulseOutput (channel: PulseChannel) =
        if not channel.Enabled || not channel.DacEnabled then
            0.0f
        else
            let bit = dutyPatterns[channel.Duty][channel.DutyStep]
            if bit = 0 then -single channel.Envelope.Volume / single MaxVolume else single channel.Envelope.Volume / single MaxVolume

    let private waveOutput (io: byte[]) (channel: WaveChannel) =
        if not channel.Enabled || not channel.DacEnabled || channel.OutputLevel = 0 then
            0.0f
        else
            let sampleByte = io[0x30 + (channel.Position / 2)]
            let sample =
                if channel.Position &&& 1 = 0 then
                    int (sampleByte >>> 4)
                else
                    int (sampleByte &&& 0x0Fuy)

            let shifted =
                match channel.OutputLevel with
                | 1 -> sample
                | 2 -> sample >>> 1
                | _ -> sample >>> 2

            (single shifted - 7.5f) / 7.5f

    let private noiseOutput (channel: NoiseChannel) =
        if not channel.Enabled || not channel.DacEnabled then
            0.0f
        else if channel.Lfsr &&& 0x0001us = 0us then
            single channel.Envelope.Volume / single MaxVolume
        else
            -single channel.Envelope.Volume / single MaxVolume

    let private mixSample (io: byte[]) (state: State) =
        let nr50 = io[0x24]
        let nr51 = io[0x25]
        let leftVolume = single ((int (nr50 >>> 4) &&& 0x07) + 1) / 8.0f
        let rightVolume = single ((int nr50 &&& 0x07) + 1) / 8.0f
        let channel1 = pulseOutput state.Pulse1
        let channel2 = pulseOutput state.Pulse2
        let channel3 = waveOutput io state.Wave
        let channel4 = noiseOutput state.Noise
        let left =
            ((if nr51 &&& 0x10uy <> 0uy then channel1 else 0.0f)
             + (if nr51 &&& 0x20uy <> 0uy then channel2 else 0.0f)
             + (if nr51 &&& 0x40uy <> 0uy then channel3 else 0.0f)
             + (if nr51 &&& 0x80uy <> 0uy then channel4 else 0.0f))
            * 0.25f
            * leftVolume
        let right =
            ((if nr51 &&& 0x01uy <> 0uy then channel1 else 0.0f)
             + (if nr51 &&& 0x02uy <> 0uy then channel2 else 0.0f)
             + (if nr51 &&& 0x04uy <> 0uy then channel3 else 0.0f)
             + (if nr51 &&& 0x08uy <> 0uy then channel4 else 0.0f))
            * 0.25f
            * rightVolume

        { Left = left; Right = right }

    let tick cycles (io: byte[]) (state: State) =
        if io[0x26] &&& 0x80uy = 0uy then
            initial
        else
            let mutable current = state
            let mutable frameCycles = current.FrameSequencerCycles + cycles

            while frameCycles >= FrameSequencerPeriodCycles do
                frameCycles <- frameCycles - FrameSequencerPeriodCycles
                current <- clockDivApuEvent current

            current <-
                { current with
                    Pulse1 = tickPulse cycles current.Pulse1
                    Pulse2 = tickPulse cycles current.Pulse2
                    Wave = tickWave cycles current.Wave
                    Noise = tickNoise cycles io[0x22] current.Noise
                    FrameSequencerCycles = frameCycles }

            let mutable sampleCycles = current.SampleCycles + int64 cycles * int64 SampleRate
            let mutable pendingSamples = current.PendingSamples

            while sampleCycles >= int64 Hardware.DmgClockHz do
                sampleCycles <- sampleCycles - int64 Hardware.DmgClockHz
                pendingSamples <- appendSample (mixSample io current) pendingSamples

            { current with
                SampleCycles = sampleCycles
                PendingSamples = pendingSamples }
