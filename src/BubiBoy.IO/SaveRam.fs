namespace BubiBoy.IO

open System
open System.IO
open BubiBoy.Core

module SaveRam =
    let private rtcMagic = [| byte 'B'; byte 'B'; byte 'R'; byte 'T'; byte 'C' |]
    let private rtcVersion = 1uy

    let defaultSavePath romPath =
        if String.IsNullOrWhiteSpace romPath then
            Error "ROM path is empty."
        else
            Ok(Path.ChangeExtension(romPath, ".sav"))

    let defaultRtcPath romPath =
        if String.IsNullOrWhiteSpace romPath then
            Error "ROM path is empty."
        else
            Ok(Path.ChangeExtension(romPath, ".rtc"))

    let private protect action =
        try
            Ok(action ())
        with
        | :? IOException as ex -> Error ex.Message
        | :? UnauthorizedAccessException as ex -> Error ex.Message
        | :? System.Security.SecurityException as ex -> Error ex.Message

    let private writeBytesWithBackup (path: string) (bytes: byte[]) =
        let directory = Path.GetDirectoryName path

        if not (String.IsNullOrWhiteSpace directory) then
            Directory.CreateDirectory directory |> ignore

        let tempPath = $"{path}.tmp-{Guid.NewGuid():N}"
        File.WriteAllBytes(tempPath, bytes)

        try
            if File.Exists path then
                File.Copy(path, $"{path}.bak", true)

            File.Move(tempPath, path, true)
        with ex ->
            if File.Exists tempPath then
                File.Delete tempPath

            raise ex

    let private encodeRtc (rtc: CartridgeMemory.RtcSave) =
        let bytes = Array.zeroCreate<byte> 18
        Array.Copy(rtcMagic, 0, bytes, 0, rtcMagic.Length)
        bytes[5] <- rtcVersion
        Array.Copy(rtc.Registers, 0, bytes, 6, 5)

        match rtc.LatchedRegisters with
        | Some latched ->
            bytes[11] <- 1uy
            Array.Copy(latched, 0, bytes, 12, 5)
        | None -> bytes[11] <- 0uy

        bytes[17] <- if rtc.LatchPrepared then 1uy else 0uy
        bytes

    let private decodeRtc (bytes: byte[]) : Result<CartridgeMemory.RtcSave, string> =
        if isNull bytes || bytes.Length <> 18 then
            Error "RTC data has an unsupported size."
        elif bytes[0..4] <> rtcMagic || bytes[5] <> rtcVersion then
            Error "RTC data has an unsupported format."
        else
            let registers = Array.zeroCreate<byte> 5
            Array.Copy(bytes, 6, registers, 0, 5)

            let latched =
                if bytes[11] = 0uy then
                    None
                else
                    let latchedRegisters = Array.zeroCreate<byte> 5
                    Array.Copy(bytes, 12, latchedRegisters, 0, 5)
                    Some latchedRegisters

            Ok
                { Registers = registers
                  LatchedRegisters = latched
                  LatchPrepared = bytes[17] <> 0uy }

    let loadFromPath savePath image =
        if String.IsNullOrWhiteSpace savePath then
            Error "Save RAM path is empty."
        elif not (File.Exists savePath) then
            Ok image
        else
            match protect (fun () -> File.ReadAllBytes savePath) with
            | Error message -> Error message
            | Ok bytes -> CartridgeMemory.importSaveRam bytes image

    let loadRtcFromPath rtcPath image =
        if String.IsNullOrWhiteSpace rtcPath then
            Error "RTC path is empty."
        elif not (File.Exists rtcPath) then
            Ok image
        else
            match protect (fun () -> File.ReadAllBytes rtcPath) with
            | Error message -> Error message
            | Ok bytes -> decodeRtc bytes |> Result.bind (fun rtc -> CartridgeMemory.importRtc rtc image)

    let loadForRom romPath image =
        match defaultSavePath romPath with
        | Error message -> Error message
        | Ok savePath ->
            loadFromPath savePath image
            |> Result.bind (fun image ->
                match defaultRtcPath romPath with
                | Error message -> Error message
                | Ok rtcPath -> loadRtcFromPath rtcPath image)

    let saveToPath savePath image =
        if String.IsNullOrWhiteSpace savePath then
            Error "Save RAM path is empty."
        else
            match CartridgeMemory.exportSaveRam image with
            | None -> Ok false
            | Some saveRam ->
                protect (fun () ->
                    writeBytesWithBackup savePath saveRam
                    true)

    let saveRtcToPath rtcPath image =
        if String.IsNullOrWhiteSpace rtcPath then
            Error "RTC path is empty."
        else
            match CartridgeMemory.exportRtc image with
            | None -> Ok false
            | Some rtc ->
                protect (fun () ->
                    writeBytesWithBackup rtcPath (encodeRtc rtc)
                    true)

    let saveForRom romPath image =
        match defaultSavePath romPath with
        | Error message -> Error message
        | Ok savePath ->
            saveToPath savePath image
            |> Result.bind (fun saveWritten ->
                match defaultRtcPath romPath with
                | Error message -> Error message
                | Ok rtcPath ->
                    saveRtcToPath rtcPath image
                    |> Result.map (fun rtcWritten -> saveWritten || rtcWritten))
