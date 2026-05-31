namespace BubiBoy.App

open System
open Avalonia
open Avalonia.Media.Imaging
open Avalonia.Platform
open BubiBoy.Audio
open BubiBoy.Core
open System.Runtime.InteropServices

module HeaderDisplay =
    let private formatByteSize bytes =
        if bytes = 0 then
            "none"
        elif bytes % (1024 * 1024) = 0 then
            $"{bytes / (1024 * 1024)} MiB"
        elif bytes % 1024 = 0 then
            $"{bytes / 1024} KiB"
        else
            $"{bytes} bytes"

    let private formatBanks banks =
        match banks with
        | 0 -> "0 banks"
        | 1 -> "1 bank"
        | _ -> $"{banks} banks"

    let formatRomSize code =
        match Cartridge.romSizeFromCode code with
        | Ok size -> $"{formatByteSize size.Bytes} / {formatBanks size.Banks}"
        | Error _ -> $"unknown (0x{code:X2})"

    let formatRamSize code =
        match Cartridge.ramSizeFromCode code with
        | Ok size -> $"{formatByteSize size.Bytes} / {formatBanks size.Banks}"
        | Error _ -> $"unknown (0x{code:X2})"

module DebugDisplay =
    let private formatStopReason reason =
        match reason with
        | Emulator.StepLimitReached -> "step limit reached"
        | Emulator.FrameCompleted -> "frame completed"
        | Emulator.Halted -> "CPU halted"
        | Emulator.UnsupportedOpcode(opcode, pc) -> $"unsupported opcode 0x{opcode:X2} at PC 0x{pc:X4}"

    let formatRunResult (result: Emulator.RunResult) =
        let registers = result.Session.Cpu.Registers

        $"Run stopped: {formatStopReason result.StopReason}\nSteps: {result.Session.Steps}    Cycles: {result.Session.TotalCycles}\nPC: 0x{registers.PC:X4}    SP: 0x{registers.SP:X4}\nA: 0x{registers.A:X2}  F: 0x{registers.F:X2}  B: 0x{registers.B:X2}  C: 0x{registers.C:X2}  D: 0x{registers.D:X2}  E: 0x{registers.E:X2}  H: 0x{registers.H:X2}  L: 0x{registers.L:X2}"

    let formatFrameResult (result: Emulator.FrameResult) =
        let registers = result.Session.Cpu.Registers

        $"Frame stopped: {formatStopReason result.StopReason}\nSteps: {result.Session.Steps}    Cycles: {result.Session.TotalCycles}\nPC: 0x{registers.PC:X4}    SP: 0x{registers.SP:X4}\nA: 0x{registers.A:X2}  F: 0x{registers.F:X2}  B: 0x{registers.B:X2}  C: 0x{registers.C:X2}  D: 0x{registers.D:X2}  E: 0x{registers.E:X2}  H: 0x{registers.H:X2}  L: 0x{registers.L:X2}"

    let formatAudioDiagnostics (diagnostics: AudioHost.AudioDiagnostics) =
        $"Audio buffered: {diagnostics.BufferedFrames} frames    underrun: {diagnostics.UnderrunFrames}    dropped: {diagnostics.DroppedFrames}"

    let formatPerformance displayFps emulationFps frameMilliseconds =
        $"FPS: display {displayFps:F1}    emu {emulationFps:F1}    frame {frameMilliseconds:F2} ms"

module FramebufferBitmap =
    let private copyToBgraBytes (pixels: uint32[]) (bytes: byte[]) : unit =
        for index in 0 .. pixels.Length - 1 do
            let color = pixels[index]
            let offset = index * 4
            bytes[offset] <- byte (color &&& 0x000000FFu)
            bytes[offset + 1] <- byte ((color >>> 8) &&& 0x000000FFu)
            bytes[offset + 2] <- byte ((color >>> 16) &&& 0x000000FFu)
            bytes[offset + 3] <- byte ((color >>> 24) &&& 0x000000FFu)

    let writeInto (pixels: uint32[]) (bitmap: WriteableBitmap) (bytes: byte[]) : unit =
        copyToBgraBytes pixels bytes

        use locked = bitmap.Lock()
        let rowBytes = Hardware.ScreenWidth * 4

        if locked.RowBytes = rowBytes then
            Marshal.Copy(bytes, 0, locked.Address, bytes.Length)
        else
            for y in 0 .. Hardware.ScreenHeight - 1 do
                Marshal.Copy(bytes, y * rowBytes, IntPtr.Add(locked.Address, y * locked.RowBytes), rowBytes)

    /// Creates the single, reusable display bitmap. The pixel buffer is written into
    /// it in place each frame via writeInto, so no per-frame bitmap is ever allocated.
    let createBitmap () : WriteableBitmap =
        new WriteableBitmap(
            PixelSize(Hardware.ScreenWidth, Hardware.ScreenHeight),
            Vector(96.0, 96.0),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul
        )
