module BubiBoy.Core.Tests.ApuTests

open BubiBoy.Core
open Xunit

let private emptyIo () =
    let io = Array.zeroCreate<byte> Bus.IoSize
    io[0x26] <- 0x80uy
    io

let private write index value (io, state) = Apu.writeRegister index value io state

let private makeSession cgb =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    rom[0x0143] <- if cgb then 0xC0uy else 0x00uy
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy

    match Emulator.createSession rom with
    | Ok session -> session
    | Error message -> failwith message

let private enablePulse bus =
    bus
    |> Bus.writeByte 0xFF26us 0x80uy
    |> Bus.writeByte 0xFF24us 0x77uy
    |> Bus.writeByte 0xFF25us 0x11uy
    |> Bus.writeByte 0xFF11us 0x80uy
    |> Bus.writeByte 0xFF12us 0xF0uy
    |> Bus.writeByte 0xFF13us 0x00uy
    |> Bus.writeByte 0xFF14us 0x80uy

let private triggerPulse1 nr10 nr11 nr12 nr13 nr14 =
    (emptyIo (), Apu.initial)
    |> write 0x24 0x77uy
    |> write 0x25 0x11uy
    |> write 0x10 nr10
    |> write 0x11 nr11
    |> write 0x12 nr12
    |> write 0x13 nr13
    |> write 0x14 nr14

let private triggerWave () =
    let mutable io, state =
        (emptyIo (), Apu.initial)
        |> write 0x24 0x77uy
        |> write 0x25 0x44uy
        |> write 0x1A 0x80uy
        |> write 0x1B 0x00uy
        |> write 0x1C 0x20uy
        |> write 0x1D 0x00uy

    for index in 0x30..0x3F do
        let nextIo, nextState = write index 0xF0uy (io, state)
        io <- nextIo
        state <- nextState

    write 0x1E 0x80uy (io, state)

let private triggerShortWave () =
    let mutable io, state =
        (emptyIo (), Apu.initial)
        |> write 0x24 0x77uy
        |> write 0x25 0x44uy
        |> write 0x1A 0x80uy
        |> write 0x1B 0xFFuy
        |> write 0x1C 0x20uy
        |> write 0x1D 0x00uy

    for index in 0x30..0x3F do
        let nextIo, nextState = write index 0xF0uy (io, state)
        io <- nextIo
        state <- nextState

    write 0x1E 0xC0uy (io, state)

let private triggerNoise nr42 nr43 nr44 =
    (emptyIo (), Apu.initial)
    |> write 0x24 0x77uy
    |> write 0x25 0x88uy
    |> write 0x20 0x00uy
    |> write 0x21 nr42
    |> write 0x22 nr43
    |> write 0x23 nr44

let private triggerShortNoise nr42 nr43 nr44 =
    (emptyIo (), Apu.initial)
    |> write 0x24 0x77uy
    |> write 0x25 0x88uy
    |> write 0x20 0x3Fuy
    |> write 0x21 nr42
    |> write 0x22 nr43
    |> write 0x23 nr44

[<Fact>]
let ``powered off APU produces no samples`` () =
    let io = emptyIo ()
    io[0x26] <- 0x00uy

    let result = Apu.tick Hardware.DmgClockHz io Apu.initial

    Assert.Empty(Apu.pendingSamples result)
    Assert.False(result.Pulse1.Enabled)

[<Fact>]
let ``triggered pulse channel produces deterministic one second sample count`` () =
    let io, state = triggerPulse1 0uy 0x80uy 0xF0uy 0x00uy 0x80uy

    let result = Apu.tick Hardware.DmgClockHz io state
    let samples = Apu.pendingSamples result

    Assert.Equal(Apu.SampleRate, samples.Length)
    Assert.Contains(samples, fun sample -> sample.Left <> 0.0f || sample.Right <> 0.0f)

[<Fact>]
let ``fresh APU states keep independent pending sample buffers`` () =
    let poweredIo, poweredState = triggerPulse1 0uy 0x80uy 0xF0uy 0x00uy 0x80uy
    let silentIo = emptyIo ()

    let cyclesPerSample =
        int ((int64 Hardware.DmgClockHz + int64 Apu.SampleRate - 1L) / int64 Apu.SampleRate)

    let powered = Apu.tick cyclesPerSample poweredIo poweredState
    let silent = Apu.tick cyclesPerSample silentIo Apu.initial

    let poweredSamples = Apu.pendingSamples powered
    let silentSamples = Apu.pendingSamples silent

    Assert.Equal(1, poweredSamples.Length)
    Assert.Equal(1, silentSamples.Length)
    Assert.Contains(poweredSamples, fun sample -> sample.Left <> 0.0f || sample.Right <> 0.0f)

    Assert.All(
        silentSamples,
        fun sample ->
            Assert.Equal(0.0f, sample.Left)
            Assert.Equal(0.0f, sample.Right)
    )

[<Fact>]
let ``length counter disables pulse channel`` () =
    let io, state = triggerPulse1 0uy 0x3Fuy 0xF0uy 0x00uy 0xC0uy

    let result = Apu.tick 8192 io state

    Assert.False(result.Pulse1.Enabled)
    Assert.Equal(0, result.Pulse1.LengthCounter)

[<Fact>]
let ``envelope advances on frame sequencer step seven`` () =
    let io, state = triggerPulse1 0uy 0x80uy 0x19uy 0x00uy 0x80uy

    let result = Apu.tick (8 * 8192) io state

    Assert.Equal(2, result.Pulse1.Envelope.Volume)

[<Fact>]
let ``sweep overflow disables pulse channel`` () =
    let io, state = triggerPulse1 0x11uy 0x80uy 0xF0uy 0xF8uy 0x87uy

    let result = Apu.tick (3 * 8192) io state

    Assert.False(result.Pulse1.Enabled)

[<Fact>]
let ``zero shift sweep still disables pulse channel on overflow`` () =
    let io, state = triggerPulse1 0x20uy 0x40uy 0xF0uy 0x00uy 0x84uy

    let result = Apu.tick (7 * 8192) io state

    Assert.False(result.Pulse1.Enabled)

[<Fact>]
let ``clearing pulse DAC disables pulse channel`` () =
    let io, state = triggerPulse1 0uy 0x80uy 0xF0uy 0x00uy 0x80uy

    let _, disabled = write 0x12 0x00uy (io, state)

    Assert.False(disabled.Pulse1.Enabled)
    Assert.False(disabled.Pulse1.DacEnabled)

[<Fact>]
let ``triggered wave channel contributes samples`` () =
    let io, state = triggerWave ()

    let result = Apu.tick Hardware.DmgClockHz io state
    let samples = Apu.pendingSamples result

    Assert.True(result.Wave.Enabled)
    Assert.Equal(Apu.SampleRate, samples.Length)
    Assert.Contains(samples, fun sample -> sample.Left <> 0.0f || sample.Right <> 0.0f)

[<Fact>]
let ``ultrasonic wave channel uses cycle average instead of aliasing`` () =
    let waveBytes =
        [| 0x76uy
           0x54uy
           0x32uy
           0x10uy
           0x24uy
           0x45uy
           0x56uy
           0x67uy
           0x89uy
           0xABuy
           0xCDuy
           0xEFuy
           0xDBuy
           0xBAuy
           0xA9uy
           0x98uy |]

    let mutable io, state =
        (emptyIo (), Apu.initial)
        |> write 0x24 0x77uy
        |> write 0x25 0x44uy
        |> write 0x1A 0x80uy
        |> write 0x1C 0x20uy
        |> write 0x1D 0xFFuy

    for index in 0..15 do
        let nextIo, nextState = write (0x30 + index) waveBytes[index] (io, state)
        io <- nextIo
        state <- nextState

    let io, state = write 0x1E 0x87uy (io, state)
    let result = Apu.tick Hardware.DmgClockHz io state

    Assert.All(
        Apu.pendingSamples result,
        fun sample ->
            Assert.Equal(0.0f, sample.Left)
            Assert.Equal(0.0f, sample.Right)
    )

[<Fact>]
let ``clearing wave DAC disables wave channel`` () =
    let io, state = triggerWave ()

    let _, disabled = write 0x1A 0x00uy (io, state)

    Assert.False(disabled.Wave.Enabled)
    Assert.False(disabled.Wave.DacEnabled)

[<Fact>]
let ``length counter disables wave channel`` () =
    let io, state = triggerShortWave ()

    let result = Apu.tick 8192 io state

    Assert.False(result.Wave.Enabled)
    Assert.Equal(0, result.Wave.LengthCounter)

[<Fact>]
let ``length register writes load channel counters`` () =
    let _, pulseState = write 0x11 0x3Fuy (emptyIo (), Apu.initial)
    let _, waveState = write 0x1B 0xFFuy (emptyIo (), Apu.initial)
    let _, noiseState = write 0x20 0x3Fuy (emptyIo (), Apu.initial)

    Assert.Equal(1, pulseState.Pulse1.LengthCounter)
    Assert.Equal(1, waveState.Wave.LengthCounter)
    Assert.Equal(1, noiseState.Noise.LengthCounter)

[<Fact>]
let ``retrigger preserves nonzero wave length counter`` () =
    let io, state =
        (emptyIo (), Apu.initial)
        |> write 0x1A 0x80uy
        |> write 0x1B 0xF0uy
        |> write 0x1C 0x20uy
        |> write 0x1D 0x00uy
        |> write 0x1E 0xC0uy

    let advanced = Apu.tick 8192 io state
    let _, retriggered = write 0x1E 0xC0uy (io, advanced)

    Assert.Equal(15, advanced.Wave.LengthCounter)
    Assert.Equal(advanced.Wave.LengthCounter, retriggered.Wave.LengthCounter)

[<Fact>]
let ``triggered noise channel contributes samples`` () =
    let io, state = triggerNoise 0xF0uy 0x00uy 0x80uy

    let result = Apu.tick Hardware.DmgClockHz io state
    let samples = Apu.pendingSamples result

    Assert.True(result.Noise.Enabled)
    Assert.Equal(Apu.SampleRate, samples.Length)
    Assert.Contains(samples, fun sample -> sample.Left <> 0.0f || sample.Right <> 0.0f)

[<Fact>]
let ``short period noise is averaged across output sample intervals`` () =
    let io, state = triggerNoise 0xF0uy 0x08uy 0x80uy

    let result = Apu.tick Hardware.DmgClockHz io state
    let samples = Apu.pendingSamples result

    Assert.Contains(
        samples,
        fun sample ->
            let magnitude = abs sample.Left
            magnitude > 0.0f && magnitude < 0.20f
    )

[<Fact>]
let ``clearing noise DAC disables noise channel`` () =
    let io, state = triggerNoise 0xF0uy 0x00uy 0x80uy

    let _, disabled = write 0x21 0x00uy (io, state)

    Assert.False(disabled.Noise.Enabled)
    Assert.False(disabled.Noise.DacEnabled)

[<Fact>]
let ``length counter disables noise channel`` () =
    let io, state = triggerShortNoise 0xF0uy 0x00uy 0xC0uy

    let result = Apu.tick 8192 io state

    Assert.False(result.Noise.Enabled)
    Assert.Equal(0, result.Noise.LengthCounter)

[<Fact>]
let ``noise envelope advances on frame sequencer step seven`` () =
    let io, state = triggerNoise 0x19uy 0x00uy 0x80uy

    let result = Apu.tick (8 * 8192) io state

    Assert.Equal(2, result.Noise.Envelope.Volume)

[<Fact>]
let ``powering off APU clears channels and pending samples`` () =
    let io, state = triggerPulse1 0uy 0x80uy 0xF0uy 0x00uy 0x80uy
    let withSamples = Apu.tick Hardware.DmgClockHz io state

    let offIo, offState = write 0x26 0x00uy (io, withSamples)

    Assert.Equal(0x00uy, offIo[0x26])
    Assert.False(offState.Pulse1.Enabled)
    Assert.False(offState.Pulse2.Enabled)
    Assert.False(offState.Wave.Enabled)
    Assert.False(offState.Noise.Enabled)
    Assert.Empty(Apu.pendingSamples offState)

[<Fact>]
let ``powering off APU clears audio registers`` () =
    let io, state = triggerPulse1 0uy 0x80uy 0xF0uy 0x00uy 0x80uy

    let offIo, _ = write 0x26 0x00uy (io, state)

    for index in 0x10..0x25 do
        Assert.Equal(0x00uy, offIo[index])

    Assert.Equal(0x00uy, offIo[0x26])

[<Fact>]
let ``powered off APU ignores audio register writes`` () =
    let io = emptyIo ()
    io[0x26] <- 0x00uy

    let nextIo, nextState = write 0x12 0xF0uy (io, Apu.initial)

    Assert.Equal(0x00uy, nextIo[0x12])
    Assert.Equal(Apu.initial, nextState)

[<Fact>]
let ``bus clears pulse trigger bit after write`` () =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy

    let cartridge =
        match CartridgeMemory.create rom with
        | Ok cartridge -> cartridge
        | Error message -> failwith message

    let bus =
        Bus.create cartridge
        |> Bus.writeByte 0xFF26us 0x80uy
        |> Bus.writeByte 0xFF11us 0x80uy
        |> Bus.writeByte 0xFF12us 0xF0uy
        |> Bus.writeByte 0xFF13us 0x00uy
        |> Bus.writeByte 0xFF14us 0x80uy

    Assert.Equal(0x00uy, Bus.readByte 0xFF14us bus &&& 0x80uy)
    Assert.Equal(0x01uy, Bus.readByte 0xFF26us bus &&& 0x01uy)

[<Fact>]
let ``NR52 channel status clears when length disables pulse`` () =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy

    let cartridge =
        match CartridgeMemory.create rom with
        | Ok cartridge -> cartridge
        | Error message -> failwith message

    let bus =
        Bus.create cartridge
        |> Bus.writeByte 0xFF26us 0x80uy
        |> Bus.writeByte 0xFF11us 0x3Fuy
        |> Bus.writeByte 0xFF12us 0xF0uy
        |> Bus.writeByte 0xFF13us 0x00uy
        |> Bus.writeByte 0xFF14us 0xC0uy

    let advanced = Bus.tick 8192 bus

    Assert.Equal(0x00uy, Bus.readByte 0xFF26us advanced &&& 0x01uy)

[<Fact>]
let ``writing DIV clocks APU frame sequencer on divider bit twelve falling edge`` () =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy

    let cartridge =
        match CartridgeMemory.create rom with
        | Ok cartridge -> cartridge
        | Error message -> failwith message

    let bus =
        Bus.create cartridge
        |> Bus.writeByte 0xFF26us 0x80uy
        |> Bus.writeByte 0xFF11us 0x3Fuy
        |> Bus.writeByte 0xFF12us 0xF0uy
        |> Bus.writeByte 0xFF13us 0x00uy
        |> Bus.writeByte 0xFF14us 0xC0uy
        |> Bus.tick 4096

    let reset = Bus.writeByte 0xFF04us 0x00uy bus

    Assert.Equal(0x00uy, Bus.readByte 0xFF26us reset &&& 0x01uy)

[<Fact>]
let ``runFrame returns generated audio and drains session buffer`` () =
    let rom = Array.zeroCreate<byte> (2 * 16 * 1024)
    rom[0x0147] <- 0x00uy
    rom[0x0148] <- 0x00uy
    rom[0x0149] <- 0x00uy

    let session =
        match Emulator.createSession rom with
        | Ok session -> session
        | Error message -> failwith message

    let bus =
        session.Bus
        |> Bus.writeByte 0xFF26us 0x80uy
        |> Bus.writeByte 0xFF24us 0x77uy
        |> Bus.writeByte 0xFF25us 0x11uy
        |> Bus.writeByte 0xFF11us 0x80uy
        |> Bus.writeByte 0xFF12us 0xF0uy
        |> Bus.writeByte 0xFF13us 0x00uy
        |> Bus.writeByte 0xFF14us 0x80uy

    let result = Emulator.runFrame 20_000 { session with Bus = bus }

    Assert.NotEmpty(result.AudioSamples)
    Assert.Empty(Bus.pendingAudioSamples result.Session.Bus)

[<Fact>]
let ``batched APU clocks match immediate bus ticking`` () =
    let batchedSession = makeSession false
    let immediateSession = makeSession false
    let steps = 2_000

    let batchedResult =
        Emulator.run
            steps
            { batchedSession with
                Bus = enablePulse batchedSession.Bus }

    let immediate =
        { immediateSession with
            Bus =
                immediateSession.Bus
                |> enablePulse
                |> Bus.tick (int batchedResult.Session.TotalCycles)
            TotalCycles = batchedResult.Session.TotalCycles
            Steps = steps }
        |> SaveState.capture

    let batched = SaveState.capture batchedResult.Session
    Assert.Equal(immediate.Bus.ApuSnapshot, batched.Bus.ApuSnapshot)

[<Fact>]
let ``APU register read synchronizes clocks accumulated across instructions`` () =
    let session = makeSession false

    let bus =
        session.Bus
        |> Bus.writeByte 0xFF26us 0x80uy
        |> Bus.writeByte 0xFF11us 0x3Fuy
        |> Bus.writeByte 0xFF12us 0xF0uy
        |> Bus.writeByte 0xFF13us 0x00uy
        |> Bus.writeByte 0xFF14us 0xC0uy

    let advanced =
        Emulator.run 2_048 { session with Bus = bus }
        |> fun result -> result.Session.Bus

    Assert.Equal(0x00uy, Bus.readByte 0xFF26us advanced &&& 0x01uy)

[<Fact>]
let ``DIV write synchronizes pending APU clocks before divider reset`` () =
    let session = makeSession false

    let bus =
        session.Bus
        |> Bus.writeByte 0xFF26us 0x80uy
        |> Bus.writeByte 0xFF11us 0x3Fuy
        |> Bus.writeByte 0xFF12us 0xF0uy
        |> Bus.writeByte 0xFF13us 0x00uy
        |> Bus.writeByte 0xFF14us 0xC0uy

    let advanced =
        Emulator.run 1_024 { session with Bus = bus }
        |> fun result -> result.Session.Bus

    let reset = Bus.writeByte 0xFF04us 0x00uy advanced

    Assert.Equal(0x00uy, Bus.readByte 0xFF26us reset &&& 0x01uy)

[<Fact>]
let ``double speed batched APU clocks use hardware cycle count`` () =
    let batchedSession = makeSession true
    let immediateSession = makeSession true

    let batchedBus =
        batchedSession.Bus |> enablePulse |> Bus.writeByte 0xFF4Dus 0x01uy |> Bus.stop

    let immediateBus =
        immediateSession.Bus |> enablePulse |> Bus.writeByte 0xFF4Dus 0x01uy |> Bus.stop

    let steps = 2_000

    let batchedResult = Emulator.run steps { batchedSession with Bus = batchedBus }

    let immediate =
        { immediateSession with
            Bus = immediateBus |> Bus.tick (int batchedResult.Session.TotalCycles * 2)
            TotalCycles = batchedResult.Session.TotalCycles
            Steps = steps }
        |> SaveState.capture

    let batched = SaveState.capture batchedResult.Session
    Assert.Equal(immediate.Bus.ApuSnapshot, batched.Bus.ApuSnapshot)

[<Fact>]
let ``save capture synchronizes pending APU clocks without changing format version`` () =
    let session = makeSession false
    let bus = enablePulse session.Bus

    let advanced =
        Emulator.run 2_000 { session with Bus = bus } |> fun result -> result.Session

    let snapshot = SaveState.capture advanced
    let encoded = SaveState.encode snapshot

    Assert.Equal(7, SaveState.CurrentVersion)
    Assert.NotEmpty(snapshot.Bus.ApuSnapshot.SnapshotPendingSamples.Samples)

    match SaveState.decode encoded with
    | Error message -> Assert.Fail message
    | Ok decoded -> Assert.Equal(snapshot.Bus.ApuSnapshot, decoded.Bus.ApuSnapshot)
