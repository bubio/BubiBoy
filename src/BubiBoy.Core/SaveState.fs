namespace BubiBoy.Core

open System
open System.IO
open System.Text

/// Captures, serializes, validates, and restores deterministic emulator state.
module SaveState =
    /// The current binary save-state format version.
    [<Literal>]
    let CurrentVersion = 5

    /// Contains all session state stored in a save-state payload.
    type Snapshot =
        { Cpu: Cpu.State
          Bus: Bus.Snapshot
          Framebuffer: uint32[]
          TotalCycles: int64
          Steps: int }

    /// Captures a defensive snapshot of an emulator session.
    let capture (session: Emulator.Session) : Snapshot =
        { Cpu = session.Cpu
          Bus = Bus.snapshot session.Bus
          Framebuffer = Array.copy session.Framebuffer
          TotalCycles = session.TotalCycles
          Steps = session.Steps }

    /// Restores a validated snapshot into a session with the same cartridge.
    let restore (snapshot: Snapshot) (session: Emulator.Session) =
        if isNull snapshot.Framebuffer then
            Error "Save state framebuffer is null."
        elif snapshot.Framebuffer.Length <> Video.FramebufferPixels then
            Error
                $"Save state framebuffer size mismatch: expected {Video.FramebufferPixels} pixels, got {snapshot.Framebuffer.Length} pixels."
        else
            Bus.restoreSnapshot snapshot.Bus session.Bus
            |> Result.map (fun bus ->
                let restored: Emulator.Session =
                    { Cpu = snapshot.Cpu
                      Bus = bus
                      Framebuffer = Array.copy snapshot.Framebuffer
                      TotalCycles = snapshot.TotalCycles
                      Steps = snapshot.Steps }

                restored)

    type private PrimitiveWriter(writer: BinaryWriter) =
        member _.WriteBool value = writer.Write(value: bool)
        member _.WriteByte value = writer.Write(value: byte)
        member _.WriteInt value = writer.Write(value: int)
        member _.WriteInt64 value = writer.Write(value: int64)
        member _.WriteUInt16 value = writer.Write(value: uint16)
        member _.WriteUInt32 value = writer.Write(value: uint32)
        member _.WriteSingle value = writer.Write(value: single)
        member _.WriteRawBytes(bytes: byte[]) = writer.Write bytes

        member this.WriteBytes(bytes: byte[]) =
            if isNull bytes then
                this.WriteInt -1
            else
                this.WriteInt bytes.Length
                this.WriteRawBytes bytes

        member this.WriteString(value: string) =
            let bytes = Encoding.UTF8.GetBytes(if isNull value then "" else value)
            this.WriteInt bytes.Length
            this.WriteRawBytes bytes

        member this.WriteUInt32Array(values: uint32[]) =
            if isNull values then
                this.WriteInt -1
            else
                this.WriteInt values.Length
                values |> Array.iter this.WriteUInt32

    type private PrimitiveReader(reader: BinaryReader) =
        member _.ReadBool() = reader.ReadBoolean()
        member _.ReadByte() = reader.ReadByte()
        member _.ReadInt() = reader.ReadInt32()
        member _.ReadInt64() = reader.ReadInt64()
        member _.ReadUInt16() = reader.ReadUInt16()
        member _.ReadUInt32() = reader.ReadUInt32()
        member _.ReadSingle() = reader.ReadSingle()
        member _.ReadRawBytes length = reader.ReadBytes length

        member this.ReadBytes() =
            let length = this.ReadInt()

            if length < 0 then
                null
            else
                let data = this.ReadRawBytes length

                if data.Length <> length then
                    raise (EndOfStreamException("Save state ended inside a byte array."))

                data

        member this.ReadString() =
            let bytes = this.ReadBytes()
            if isNull bytes then "" else Encoding.UTF8.GetString bytes

        member this.ReadUInt32Array() =
            let length = this.ReadInt()

            if length < 0 then
                null
            else
                Array.init length (fun _ -> this.ReadUInt32())

    module private VersionHeader =
        let private magic =
            [| 0x42uy; 0x55uy; 0x42uy; 0x49uy; 0x53uy; 0x54uy; 0x41uy; 0x54uy; 0x45uy |]

        let write (writer: PrimitiveWriter) =
            writer.WriteRawBytes magic
            writer.WriteInt CurrentVersion

        let read (reader: PrimitiveReader) =
            let fileMagic = reader.ReadRawBytes magic.Length

            if fileMagic.Length <> magic.Length || fileMagic <> magic then
                Error "File is not a BubiBoy save state."
            else
                let version = reader.ReadInt()

                if version <> 2 && version <> 3 && version <> 4 && version <> CurrentVersion then
                    Error $"Unsupported save state version: {version}."
                else
                    Ok version

    type private DomainSnapshotWriter(primitives: PrimitiveWriter) =
        let writeBool value = primitives.WriteBool value
        let writeByte value = primitives.WriteByte value
        let writeInt value = primitives.WriteInt value
        let writeInt64 value = primitives.WriteInt64 value
        let writeUInt16 value = primitives.WriteUInt16 value
        let writeUInt32 value = primitives.WriteUInt32 value
        let writeSingle value = primitives.WriteSingle value
        let writeBytes bytes = primitives.WriteBytes bytes
        let writeString value = primitives.WriteString value
        let writeUInt32Array values = primitives.WriteUInt32Array values

        let writeGameBoyMode mode =
            match mode with
            | Hardware.Dmg -> writeByte 0uy
            | Hardware.CgbCompatibility -> writeByte 2uy
            | Hardware.Cgb -> writeByte 1uy

        let writeCgbSupport value =
            match value with
            | Cartridge.DmgOnly -> writeByte 0uy
            | Cartridge.CgbEnhanced -> writeByte 1uy
            | Cartridge.CgbOnly -> writeByte 2uy

        let writeSgbSupport value =
            match value with
            | Cartridge.NoSgb -> writeByte 0uy
            | Cartridge.SgbEnhanced -> writeByte 1uy

        let writeHeader (header: Cartridge.CartridgeHeader) =
            writeString header.Title
            writeCgbSupport header.CgbSupport
            writeSgbSupport header.SgbSupport
            writeByte header.CartridgeTypeCode
            writeByte header.RomSizeCode
            writeByte header.RamSizeCode
            writeByte header.DestinationCode
            writeByte header.HeaderChecksum

        let writeBankingMode value =
            match value with
            | CartridgeMemory.RomBanking -> writeByte 0uy
            | CartridgeMemory.RamBanking -> writeByte 1uy

        let writeMbcState value =
            match value with
            | CartridgeMemory.NoMbc -> writeByte 0uy
            | CartridgeMemory.Mbc1 state ->
                writeByte 1uy
                writeBool state.RamEnabled
                writeInt state.RomBankLow5
                writeInt state.BankHigh2
                writeBankingMode state.BankingMode
            | CartridgeMemory.Mbc2 state ->
                writeByte 2uy
                writeBool state.RamEnabled
                writeInt state.RomBank
            | CartridgeMemory.Mbc3 state ->
                writeByte 3uy
                writeBool state.RamEnabled
                writeInt state.RomBank
                writeInt state.RamOrRtcSelect
                writeBool state.HasRtc
                writeBytes state.RtcRegisters

                match state.LatchedRtcRegisters with
                | Some latched ->
                    writeBool true
                    writeBytes latched
                | None -> writeBool false

                writeBool state.RtcLatchPrepared
            | CartridgeMemory.Mbc5 state ->
                writeByte 5uy
                writeBool state.RamEnabled
                writeInt state.RomBankLow8
                writeInt state.RomBankHigh1
                writeInt state.RamBank

        let writeCartridgeSnapshot (snapshot: CartridgeMemory.Snapshot) =
            writeHeader snapshot.HeaderSnapshot
            writeInt snapshot.RomLengthSnapshot
            writeInt snapshot.RomBanksSnapshot
            writeBytes snapshot.RamSnapshot
            writeInt snapshot.RamBanksSnapshot
            writeMbcState snapshot.MbcSnapshot

        let writeTimerState (state: Timer.State) =
            writeUInt16 state.Divider
            writeInt state.TimaCounter

        let writeLcdMode mode =
            match mode with
            | Lcd.HBlank -> writeByte 0uy
            | Lcd.VBlank -> writeByte 1uy
            | Lcd.OamSearch -> writeByte 2uy
            | Lcd.Transfer -> writeByte 3uy

        let writeLcdState (state: Lcd.State) =
            writeByte state.Line
            writeInt state.DotCounter
            writeLcdMode state.Mode
            writeBool state.StatSignal

        let writeButton button =
            match button with
            | Joypad.Right -> writeByte 0uy
            | Joypad.Left -> writeByte 1uy
            | Joypad.Up -> writeByte 2uy
            | Joypad.Down -> writeByte 3uy
            | Joypad.A -> writeByte 4uy
            | Joypad.B -> writeByte 5uy
            | Joypad.Select -> writeByte 6uy
            | Joypad.Start -> writeByte 7uy

        let writeJoypadState (state: Joypad.State) =
            writeBool state.SelectAction
            writeBool state.SelectDirection
            writeInt state.Pressed.Count
            state.Pressed |> Set.iter writeButton

        let writeEnvelopeDirection value =
            match value with
            | Apu.Decrease -> writeByte 0uy
            | Apu.Increase -> writeByte 1uy

        let writeEnvelope (value: Apu.Envelope) =
            writeInt value.InitialVolume
            writeEnvelopeDirection value.Direction
            writeInt value.Period
            writeInt value.Timer
            writeInt value.Volume

        let writeSweep (value: Apu.Sweep) =
            writeInt value.Period
            writeBool value.Negate
            writeInt value.Shift
            writeInt value.Timer
            writeInt value.ShadowFrequency
            writeBool value.Enabled

        let writePulseChannel (value: Apu.PulseChannel) =
            writeBool value.Enabled
            writeBool value.DacEnabled
            writeInt value.Duty
            writeInt value.DutyStep
            writeInt value.LengthCounter
            writeBool value.LengthEnabled
            writeInt value.Frequency
            writeInt value.Timer
            writeEnvelope value.Envelope

            match value.Sweep with
            | Some sweep ->
                writeBool true
                writeSweep sweep
            | None -> writeBool false

        let writeWaveChannel (value: Apu.WaveChannel) =
            writeBool value.Enabled
            writeBool value.DacEnabled
            writeInt value.LengthCounter
            writeBool value.LengthEnabled
            writeInt value.Frequency
            writeInt value.Timer
            writeInt value.Position
            writeInt value.OutputLevel

        let writeNoiseChannel (value: Apu.NoiseChannel) =
            writeBool value.Enabled
            writeBool value.DacEnabled
            writeInt value.LengthCounter
            writeBool value.LengthEnabled
            writeInt value.Timer
            writeUInt16 value.Lfsr
            writeEnvelope value.Envelope

        let writeSample (value: Apu.Sample) =
            writeSingle value.Left
            writeSingle value.Right

        let writeApuSnapshot (value: Apu.StateSnapshot) =
            writeInt value.SnapshotFrameSequencerStep
            writeInt value.SnapshotFrameSequencerCycles
            writeBool value.SnapshotSkipNextFrameSequencerClock
            writeInt64 value.SnapshotSampleCycles
            writeInt64 value.SnapshotWaveSampleArea
            writeInt value.SnapshotWaveSampleCycles
            writeInt64 value.SnapshotNoiseSampleArea
            writeInt value.SnapshotNoiseSampleCycles
            writePulseChannel value.SnapshotPulse1
            writePulseChannel value.SnapshotPulse2
            writeWaveChannel value.SnapshotWave
            writeNoiseChannel value.SnapshotNoise
            writeInt value.SnapshotPendingSamples.Samples.Length
            value.SnapshotPendingSamples.Samples |> Array.iter writeSample

        let writeBusSnapshot (snapshot: Bus.Snapshot) =
            writeCartridgeSnapshot snapshot.CartridgeSnapshot
            writeGameBoyMode snapshot.ModeSnapshot
            writeBool snapshot.BootRomEnabledSnapshot

            match snapshot.BootRomSha256Snapshot with
            | Some sha256 ->
                writeBool true
                writeString sha256
            | None -> writeBool false

            writeBytes snapshot.VramSnapshot
            writeBytes snapshot.WramSnapshot
            writeBytes snapshot.OamSnapshot
            writeBytes snapshot.IoSnapshot
            writeBytes snapshot.HramSnapshot
            writeInt snapshot.VramBankSnapshot
            writeInt snapshot.WramBankSnapshot
            writeBytes snapshot.BgPaletteRamSnapshot
            writeBytes snapshot.ObjPaletteRamSnapshot
            writeBool snapshot.DoubleSpeedSnapshot
            writeBool snapshot.SpeedSwitchPreparedSnapshot
            writeUInt16 snapshot.HdmaSourceSnapshot
            writeUInt16 snapshot.HdmaDestinationSnapshot
            writeInt snapshot.HdmaRemainingSnapshot
            writeBool snapshot.HdmaActiveSnapshot
            writeTimerState snapshot.TimerSnapshot
            writeLcdState snapshot.LcdSnapshot
            writeJoypadState snapshot.JoypadSnapshot
            writeApuSnapshot snapshot.ApuSnapshot
            writeByte snapshot.InterruptEnableSnapshot

        let writeCpuRegisters (registers: Cpu.Registers) =
            writeByte registers.A
            writeByte registers.F
            writeByte registers.B
            writeByte registers.C
            writeByte registers.D
            writeByte registers.E
            writeByte registers.H
            writeByte registers.L
            writeUInt16 registers.SP
            writeUInt16 registers.PC

        let writeCpuState (state: Cpu.State) =
            writeCpuRegisters state.Registers
            writeBool state.Halted
            writeBool state.InterruptsEnabled

        member _.Write(snapshot: Snapshot) =
            writeCpuState snapshot.Cpu
            writeBusSnapshot snapshot.Bus
            writeUInt32Array snapshot.Framebuffer
            writeInt64 snapshot.TotalCycles
            writeInt snapshot.Steps

    type private DomainSnapshotReader(primitives: PrimitiveReader, version: int) =
        let readBool () = primitives.ReadBool()
        let readByte () = primitives.ReadByte()
        let readInt () = primitives.ReadInt()
        let readInt64 () = primitives.ReadInt64()
        let readUInt16 () = primitives.ReadUInt16()
        let readUInt32 () = primitives.ReadUInt32()
        let readSingle () = primitives.ReadSingle()
        let readBytes () = primitives.ReadBytes()
        let readString () = primitives.ReadString()
        let readUInt32Array () = primitives.ReadUInt32Array()

        member _.Read() =
            let readGameBoyMode () =
                match readByte () with
                | 0uy -> Hardware.Dmg
                | 1uy -> Hardware.Cgb
                | 2uy when version >= 5 -> Hardware.CgbCompatibility
                | value -> failwith $"Unsupported Game Boy mode in save state: {value}"

            let readCgbSupport () =
                match readByte () with
                | 0uy -> Cartridge.DmgOnly
                | 1uy -> Cartridge.CgbEnhanced
                | 2uy -> Cartridge.CgbOnly
                | value -> failwith $"Unsupported CGB support value in save state: {value}"

            let readSgbSupport () =
                match readByte () with
                | 0uy -> Cartridge.NoSgb
                | 1uy -> Cartridge.SgbEnhanced
                | value -> failwith $"Unsupported SGB support value in save state: {value}"

            let cartridgeKindFromCode code =
                match code with
                | 0x00uy -> Cartridge.RomOnly
                | 0x01uy -> Cartridge.Mbc1
                | 0x02uy -> Cartridge.Mbc1Ram
                | 0x03uy -> Cartridge.Mbc1RamBattery
                | 0x05uy -> Cartridge.Mbc2
                | 0x06uy -> Cartridge.Mbc2Battery
                | 0x0Fuy -> Cartridge.Mbc3TimerBattery
                | 0x10uy -> Cartridge.Mbc3TimerRamBattery
                | 0x11uy -> Cartridge.Mbc3
                | 0x12uy -> Cartridge.Mbc3Ram
                | 0x13uy -> Cartridge.Mbc3RamBattery
                | 0x19uy -> Cartridge.Mbc5
                | 0x1Auy -> Cartridge.Mbc5Ram
                | 0x1Buy -> Cartridge.Mbc5RamBattery
                | other -> Cartridge.Unknown other

            let readHeader () : Cartridge.CartridgeHeader =
                let title = readString ()
                let cgbSupport = readCgbSupport ()
                let sgbSupport = readSgbSupport ()
                let cartridgeTypeCode = readByte ()

                { Title = title
                  CgbSupport = cgbSupport
                  SgbSupport = sgbSupport
                  CartridgeTypeCode = cartridgeTypeCode
                  CartridgeKind = cartridgeKindFromCode cartridgeTypeCode
                  RomSizeCode = readByte ()
                  RamSizeCode = readByte ()
                  DestinationCode = readByte ()
                  HeaderChecksum = readByte () }

            let readBankingMode () =
                match readByte () with
                | 0uy -> CartridgeMemory.RomBanking
                | 1uy -> CartridgeMemory.RamBanking
                | value -> failwith $"Unsupported MBC1 banking mode in save state: {value}"

            let readMbcState () =
                match readByte () with
                | 0uy -> CartridgeMemory.NoMbc
                | 1uy ->
                    CartridgeMemory.Mbc1
                        { RamEnabled = readBool ()
                          RomBankLow5 = readInt ()
                          BankHigh2 = readInt ()
                          BankingMode = readBankingMode () }
                | 2uy ->
                    CartridgeMemory.Mbc2
                        { RamEnabled = readBool ()
                          RomBank = readInt () }
                | 3uy ->
                    let ramEnabled = readBool ()
                    let romBank = readInt ()
                    let ramOrRtcSelect = readInt ()
                    let hasRtc = readBool ()
                    let rtcRegisters = readBytes ()
                    let latched = if readBool () then Some(readBytes ()) else None

                    CartridgeMemory.Mbc3
                        { RamEnabled = ramEnabled
                          RomBank = romBank
                          RamOrRtcSelect = ramOrRtcSelect
                          HasRtc = hasRtc
                          RtcRegisters = rtcRegisters
                          LatchedRtcRegisters = latched
                          RtcLatchPrepared = readBool () }
                | 5uy ->
                    CartridgeMemory.Mbc5
                        { RamEnabled = readBool ()
                          RomBankLow8 = readInt ()
                          RomBankHigh1 = readInt ()
                          RamBank = readInt () }
                | value -> failwith $"Unsupported MBC state tag in save state: {value}"

            let readCartridgeSnapshot () : CartridgeMemory.Snapshot =
                { HeaderSnapshot = readHeader ()
                  RomLengthSnapshot = readInt ()
                  RomBanksSnapshot = readInt ()
                  RamSnapshot = readBytes ()
                  RamBanksSnapshot = readInt ()
                  MbcSnapshot = readMbcState () }

            let readTimerState () : Timer.State =
                { Divider = readUInt16 ()
                  TimaCounter = readInt () }

            let readLcdMode () =
                match readByte () with
                | 0uy -> Lcd.HBlank
                | 1uy -> Lcd.VBlank
                | 2uy -> Lcd.OamSearch
                | 3uy -> Lcd.Transfer
                | value -> failwith $"Unsupported LCD mode in save state: {value}"

            let readLcdState () : Lcd.State =
                { Line = readByte ()
                  DotCounter = readInt ()
                  Mode = readLcdMode ()
                  StatSignal = readBool () }

            let readButton () =
                match readByte () with
                | 0uy -> Joypad.Right
                | 1uy -> Joypad.Left
                | 2uy -> Joypad.Up
                | 3uy -> Joypad.Down
                | 4uy -> Joypad.A
                | 5uy -> Joypad.B
                | 6uy -> Joypad.Select
                | 7uy -> Joypad.Start
                | value -> failwith $"Unsupported joypad button in save state: {value}"

            let readJoypadState () : Joypad.State =
                let selectAction = readBool ()
                let selectDirection = readBool ()
                let pressedCount = readInt ()
                let pressed = [ for _ in 1..pressedCount -> readButton () ] |> Set.ofList

                { SelectAction = selectAction
                  SelectDirection = selectDirection
                  Pressed = pressed }

            let readEnvelopeDirection () =
                match readByte () with
                | 0uy -> Apu.Decrease
                | 1uy -> Apu.Increase
                | value -> failwith $"Unsupported APU envelope direction in save state: {value}"

            let readEnvelope () : Apu.Envelope =
                { InitialVolume = readInt ()
                  Direction = readEnvelopeDirection ()
                  Period = readInt ()
                  Timer = readInt ()
                  Volume = readInt () }

            let readSweep () : Apu.Sweep =
                { Period = readInt ()
                  Negate = readBool ()
                  Shift = readInt ()
                  Timer = readInt ()
                  ShadowFrequency = readInt ()
                  Enabled = readBool () }

            let readPulseChannel () : Apu.PulseChannel =
                { Enabled = readBool ()
                  DacEnabled = readBool ()
                  Duty = readInt ()
                  DutyStep = readInt ()
                  LengthCounter = readInt ()
                  LengthEnabled = readBool ()
                  Frequency = readInt ()
                  Timer = readInt ()
                  Envelope = readEnvelope ()
                  Sweep = if readBool () then Some(readSweep ()) else None }

            let readWaveChannel () : Apu.WaveChannel =
                { Enabled = readBool ()
                  DacEnabled = readBool ()
                  LengthCounter = readInt ()
                  LengthEnabled = readBool ()
                  Frequency = readInt ()
                  Timer = readInt ()
                  Position = readInt ()
                  OutputLevel = readInt () }

            let readNoiseChannel () : Apu.NoiseChannel =
                { Enabled = readBool ()
                  DacEnabled = readBool ()
                  LengthCounter = readInt ()
                  LengthEnabled = readBool ()
                  Timer = readInt ()
                  Lfsr = readUInt16 ()
                  Envelope = readEnvelope () }

            let readSample () : Apu.Sample =
                { Left = readSingle ()
                  Right = readSingle () }

            let readApuSnapshot () : Apu.StateSnapshot =
                let frameSequencerStep = readInt ()
                let frameSequencerCycles = readInt ()
                let skipNextFrameSequencerClock = readBool ()
                let sampleCycles = readInt64 ()

                let waveSampleArea, waveSampleCycles =
                    if version >= 3 then readInt64 (), readInt () else 0L, 0

                let noiseSampleArea = readInt64 ()
                let noiseSampleCycles = readInt ()
                let pulse1 = readPulseChannel ()
                let pulse2 = readPulseChannel ()
                let wave = readWaveChannel ()
                let noise = readNoiseChannel ()
                let samples = Array.init (readInt ()) (fun _ -> readSample ())

                { SnapshotFrameSequencerStep = frameSequencerStep
                  SnapshotFrameSequencerCycles = frameSequencerCycles
                  SnapshotSkipNextFrameSequencerClock = skipNextFrameSequencerClock
                  SnapshotSampleCycles = sampleCycles
                  SnapshotWaveSampleArea = waveSampleArea
                  SnapshotWaveSampleCycles = waveSampleCycles
                  SnapshotNoiseSampleArea = noiseSampleArea
                  SnapshotNoiseSampleCycles = noiseSampleCycles
                  SnapshotPulse1 = pulse1
                  SnapshotPulse2 = pulse2
                  SnapshotWave = wave
                  SnapshotNoise = noise
                  SnapshotPendingSamples = { Samples = samples } }

            let readBusSnapshot () : Bus.Snapshot =
                let cartridge = readCartridgeSnapshot ()
                let mode = readGameBoyMode ()

                let bootRomEnabled, bootRomSha256 =
                    if version >= 4 then
                        let enabled = readBool ()
                        let sha256 = if readBool () then Some(readString ()) else None
                        enabled, sha256
                    else
                        false, None

                { CartridgeSnapshot = cartridge
                  ModeSnapshot = mode
                  BootRomEnabledSnapshot = bootRomEnabled
                  BootRomSha256Snapshot = bootRomSha256
                  VramSnapshot = readBytes ()
                  WramSnapshot = readBytes ()
                  OamSnapshot = readBytes ()
                  IoSnapshot = readBytes ()
                  HramSnapshot = readBytes ()
                  VramBankSnapshot = readInt ()
                  WramBankSnapshot = readInt ()
                  BgPaletteRamSnapshot = readBytes ()
                  ObjPaletteRamSnapshot = readBytes ()
                  DoubleSpeedSnapshot = readBool ()
                  SpeedSwitchPreparedSnapshot = readBool ()
                  HdmaSourceSnapshot = readUInt16 ()
                  HdmaDestinationSnapshot = readUInt16 ()
                  HdmaRemainingSnapshot = readInt ()
                  HdmaActiveSnapshot = readBool ()
                  TimerSnapshot = readTimerState ()
                  LcdSnapshot = readLcdState ()
                  JoypadSnapshot = readJoypadState ()
                  ApuSnapshot = readApuSnapshot ()
                  InterruptEnableSnapshot = readByte () }

            let readCpuRegisters () : Cpu.Registers =
                { A = readByte ()
                  F = readByte ()
                  B = readByte ()
                  C = readByte ()
                  D = readByte ()
                  E = readByte ()
                  H = readByte ()
                  L = readByte ()
                  SP = readUInt16 ()
                  PC = readUInt16 () }

            let readCpuState () : Cpu.State =
                { Registers = readCpuRegisters ()
                  Halted = readBool ()
                  InterruptsEnabled = readBool () }

            { Cpu = readCpuState ()
              Bus = readBusSnapshot ()
              Framebuffer = readUInt32Array ()
              TotalCycles = readInt64 ()
              Steps = readInt () }

    /// Encodes a snapshot using the current binary save-state format.
    let encode (snapshot: Snapshot) =
        use stream = new MemoryStream()
        use binaryWriter = new BinaryWriter(stream, Encoding.UTF8, true)
        let writer = PrimitiveWriter binaryWriter
        VersionHeader.write writer
        DomainSnapshotWriter(writer).Write snapshot
        stream.ToArray()

    /// Decodes and validates a binary save-state payload.
    let decode (bytes: byte[]) =
        if isNull bytes then
            Error "Save state data is null."
        else
            try
                use stream = new MemoryStream(bytes)
                use binaryReader = new BinaryReader(stream, Encoding.UTF8, true)
                let reader = PrimitiveReader binaryReader

                VersionHeader.read reader
                |> Result.map (fun version -> DomainSnapshotReader(reader, version).Read())
            with
            | :? EndOfStreamException -> Error "Save state data is truncated."
            | :? IOException as ex -> Error $"Could not read save state data: {ex.Message}"
            | :? ArgumentException as ex -> Error $"Invalid save state data: {ex.Message}"
            | :? InvalidDataException as ex -> Error $"Invalid save state data: {ex.Message}"
            | :? NotSupportedException as ex -> Error $"Invalid save state data: {ex.Message}"
            | ex -> Error $"Invalid save state data: {ex.Message}"

    /// Decodes and restores a binary save-state payload into a session.
    let restoreBytes bytes session =
        decode bytes |> Result.bind (fun snapshot -> restore snapshot session)
