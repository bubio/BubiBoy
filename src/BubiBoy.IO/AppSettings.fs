namespace BubiBoy.IO

open System
open System.IO
open System.Text.Json

module AppSettings =
    [<Literal>]
    let CurrentVersion = 2

    [<Literal>]
    let MaxRecentRoms = 10

    [<CLIMutable>]
    type SettingsFile =
        { Version: int
          VolumePercent: int
          RecentRoms: string[]
          Scale: int
          IsFloating: bool }

    type Settings =
        { VolumePercent: int
          RecentRoms: string list
          Scale: int
          IsFloating: bool }

    let defaults =
        { VolumePercent = 50
          RecentRoms = []
          Scale = 2
          IsFloating = false }

    let private jsonOptions =
        JsonSerializerOptions(WriteIndented = true)

    let private protect action =
        try
            Ok(action ())
        with
        | :? IOException as ex -> Error ex.Message
        | :? InvalidDataException as ex -> Error ex.Message
        | :? UnauthorizedAccessException as ex -> Error ex.Message
        | :? System.Security.SecurityException as ex -> Error ex.Message
        | :? JsonException as ex -> Error ex.Message

    let private normalizePath path =
        if String.IsNullOrWhiteSpace path then
            None
        else
            Some(Path.GetFullPath path)

    let normalize settings =
        let recent =
            settings.RecentRoms
            |> List.choose normalizePath
            |> List.distinctBy (fun path -> path.ToUpperInvariant())
            |> List.truncate MaxRecentRoms

        let scale =
            match settings.Scale with
            | 1
            | 2
            | 4
            | 8 -> settings.Scale
            | _ -> defaults.Scale

        { VolumePercent = Math.Clamp(settings.VolumePercent, 0, 100)
          RecentRoms = recent
          Scale = scale
          IsFloating = settings.IsFloating }

    let defaultPath () =
        let root =
            match Environment.GetEnvironmentVariable("BUBIBOY_SETTINGS_PATH") with
            | path when not (String.IsNullOrWhiteSpace path) -> path
            | _ ->
                let appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)

                if String.IsNullOrWhiteSpace appData then
                    Path.Combine(Path.GetTempPath(), "BubiBoy")
                else
                    Path.Combine(appData, "BubiBoy")

        if Path.HasExtension root then
            root
        else
            Path.Combine(root, "settings.json")

    let loadFromPath path =
        if String.IsNullOrWhiteSpace path then
            Error "Settings path is empty."
        elif not (File.Exists path) then
            Ok defaults
        else
            protect (fun () ->
                let file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText path, jsonOptions)

                if isNull (box file) then
                    raise (InvalidDataException "Settings file is empty.")
                elif file.Version <> CurrentVersion && file.Version <> 1 then
                    raise (InvalidDataException $"Unsupported settings version {file.Version}.")
                else
                    normalize
                        { VolumePercent = file.VolumePercent
                          RecentRoms =
                            if isNull file.RecentRoms then
                                []
                            else
                                Array.toList file.RecentRoms
                          Scale =
                            if file.Version = 1 then
                                defaults.Scale
                            else
                                file.Scale
                          IsFloating =
                            if file.Version = 1 then
                                defaults.IsFloating
                            else
                                file.IsFloating })

    let saveToPath path settings =
        if String.IsNullOrWhiteSpace path then
            Error "Settings path is empty."
        else
            let normalized = normalize settings
            let file =
                { Version = CurrentVersion
                  VolumePercent = normalized.VolumePercent
                  RecentRoms = List.toArray normalized.RecentRoms
                  Scale = normalized.Scale
                  IsFloating = normalized.IsFloating }

            protect (fun () ->
                let directory = Path.GetDirectoryName path

                if not (String.IsNullOrWhiteSpace directory) then
                    Directory.CreateDirectory directory |> ignore

                File.WriteAllText(path, JsonSerializer.Serialize(file, jsonOptions)))

    let rememberRom romPath settings =
        match normalizePath romPath with
        | None -> normalize settings
        | Some fullPath ->
            let existing =
                settings.RecentRoms
                |> List.choose normalizePath
                |> List.filter (fun path -> not (String.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase)))

            normalize { settings with RecentRoms = fullPath :: existing }

    let withVolumePercent percent settings =
        normalize { settings with VolumePercent = percent }

    let withScale scale settings =
        normalize { settings with Scale = scale }

    let withFloating isFloating settings =
        normalize { settings with IsFloating = isFloating }
