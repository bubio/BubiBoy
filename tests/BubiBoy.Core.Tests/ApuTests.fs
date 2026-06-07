module BubiBoy.Core.Tests.ApuTests

open BubiBoy.Core
open Xunit

let private emptyIo () =
    let io = Array.zeroCreate<byte> Bus.IoSize
    io[0x26] <- 0x80uy
    io

let private write index value (io, state) =
    Apu.writeRegister index value io state

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
