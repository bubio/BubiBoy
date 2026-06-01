namespace BubiBoy.IO

open System
open System.IO
open BubiBoy.Core

module SaveStateFile =
    let defaultStatePath romPath =
        if String.IsNullOrWhiteSpace romPath then
            Error "ROM path is empty."
        else
            let directory = Path.GetDirectoryName romPath
            let fileName = Path.GetFileNameWithoutExtension romPath + ".state"

            if String.IsNullOrWhiteSpace directory then
                Ok fileName
            else
                Ok(Path.Combine(directory, fileName))

    let saveToPath path session =
        if String.IsNullOrWhiteSpace path then
            Error "Save state path is empty."
        else
            try
                let directory = Path.GetDirectoryName path

                if not (String.IsNullOrWhiteSpace directory) then
                    Directory.CreateDirectory directory |> ignore

                session
                |> SaveState.capture
                |> SaveState.encode
                |> fun bytes -> File.WriteAllBytes(path, bytes)

                Ok()
            with
            | :? IOException as ex -> Error $"Could not write save state: {ex.Message}"
            | :? UnauthorizedAccessException as ex -> Error $"Could not write save state: {ex.Message}"

    let loadFromPath path session =
        if String.IsNullOrWhiteSpace path then
            Error "Save state path is empty."
        elif not (File.Exists path) then
            Error $"Save state file does not exist: {path}"
        else
            try
                File.ReadAllBytes path
                |> SaveState.restoreBytes
                <| session
            with
            | :? IOException as ex -> Error $"Could not read save state: {ex.Message}"
            | :? UnauthorizedAccessException as ex -> Error $"Could not read save state: {ex.Message}"

    let saveForRom romPath session =
        defaultStatePath romPath
        |> Result.bind (fun path -> saveToPath path session)

    let loadForRom romPath session =
        defaultStatePath romPath
        |> Result.bind (fun path -> loadFromPath path session)
