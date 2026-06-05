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

module UserMessage =
    let private contains (text: string) (message: string) =
        if isNull message then
            false
        else
            message.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0

    let private withDetails message text =
        if String.IsNullOrWhiteSpace message then
            text
        else
            $"{text}\n\nDetails: {message}"

    let formatRomLoadError message =
        if contains "ROM path is empty" message then
            "No ROM file was selected."
        elif contains "AppleDouble" message then
            "The selected file is macOS metadata, not a Game Boy ROM. Choose the matching file without the '._' prefix."
        elif contains "Unsupported ROM file extension" message then
            withDetails message "BubiBoy opens .gb and .gbc files. Choose a Game Boy or Game Boy Color ROM file."
        elif contains "does not exist" message then
            withDetails message "The selected ROM file no longer exists. It may have been moved or removed."
        elif contains "too small to contain a Game Boy cartridge header" message then
            withDetails message "This file is too small to be a valid Game Boy ROM."
        elif contains "Unsupported ROM size code" message then
            withDetails message "The ROM header declares a size value BubiBoy does not support yet."
        elif contains "Unsupported RAM size code" message then
            withDetails message "The ROM header declares a save-RAM size value BubiBoy does not support yet."
        elif contains "smaller than the size declared" message then
            withDetails message "The ROM appears to be truncated or incomplete."
        else
            message

    let formatRomStartError message =
        if contains "Save RAM size mismatch" message then
            withDetails message "The existing .sav file does not match this ROM. Move or rename the .sav file next to the ROM, then try again."
        elif contains "RTC data has an unsupported" message then
            withDetails message "The existing .rtc file could not be used. Move or rename the .rtc file next to the ROM, then try again."
        elif contains "Cartridge does not have an MBC3 RTC" message then
            withDetails message "The existing .rtc file is for a cartridge with a real-time clock, but this ROM does not use one."
        else
            formatRomLoadError message

    let formatSaveStateError message =
        if contains "does not exist" message then
            "No save state exists yet for this ROM. Use Save State before loading one."
        elif contains "identity does not match" message then
            withDetails message "This save state belongs to a different ROM."
        elif contains "Unsupported save state version" message then
            withDetails message "This save state was written by an incompatible BubiBoy version."
        elif contains "not a BubiBoy save state" message then
            withDetails message "The .state file next to this ROM is not a BubiBoy save state."
        elif contains "truncated" message || contains "Invalid save state data" message then
            withDetails message "The save state file is corrupt or incomplete."
        else
            message

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
