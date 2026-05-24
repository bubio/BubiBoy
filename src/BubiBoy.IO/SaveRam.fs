namespace BubiBoy.IO

open System
open System.IO
open BubiBoy.Core

module SaveRam =
    let defaultSavePath romPath =
        if String.IsNullOrWhiteSpace romPath then
            Error "ROM path is empty."
        else
            Ok(Path.ChangeExtension(romPath, ".sav"))

    let loadFromPath savePath image =
        if String.IsNullOrWhiteSpace savePath then
            Error "Save RAM path is empty."
        elif not (File.Exists savePath) then
            Ok image
        else
            let bytes = File.ReadAllBytes savePath
            CartridgeMemory.importSaveRam bytes image

    let loadForRom romPath image =
        match defaultSavePath romPath with
        | Error message -> Error message
        | Ok savePath -> loadFromPath savePath image

    let saveToPath savePath image =
        if String.IsNullOrWhiteSpace savePath then
            Error "Save RAM path is empty."
        else
            match CartridgeMemory.exportSaveRam image with
            | None -> Ok false
            | Some saveRam ->
                let directory = Path.GetDirectoryName savePath

                if not (String.IsNullOrWhiteSpace directory) then
                    Directory.CreateDirectory directory |> ignore

                File.WriteAllBytes(savePath, saveRam)
                Ok true

    let saveForRom romPath image =
        match defaultSavePath romPath with
        | Error message -> Error message
        | Ok savePath -> saveToPath savePath image
