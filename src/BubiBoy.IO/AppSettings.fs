namespace BubiBoy.IO

open System
open System.Collections.Generic
open System.IO
open System.Text.Json

module AppSettings =
    [<Literal>]
    let CurrentVersion = 4

    [<Literal>]
    let MaxRecentRoms = 10

    let KeyboardButtonOrder =
        [ "Right"; "Left"; "Up"; "Down"; "A"; "B"; "Select"; "Start" ]

    let defaultKeyboardMapping =
        [ "Right", "Right"
          "Left", "Left"
          "Up", "Up"
          "Down", "Down"
          "A", "Z"
          "B", "X"
          "Select", "Back"
          "Start", "Enter" ]
        |> Map.ofList

    let ControllerControlNames =
        [ "DPadUp"
          "DPadDown"
          "DPadLeft"
          "DPadRight"
          "South"
          "East"
          "West"
          "North"
          "Start"
          "Select"
          "LeftShoulder"
          "RightShoulder"
          "LeftTrigger"
          "RightTrigger"
          "LeftStickUp"
          "LeftStickDown"
          "LeftStickLeft"
          "LeftStickRight" ]

    let defaultControllerMapping =
        [ "Right", "DPadRight"
          "Left", "DPadLeft"
          "Up", "DPadUp"
          "Down", "DPadDown"
          "A", "South"
          "B", "East"
          "Select", "Select"
          "Start", "Start" ]
        |> Map.ofList

    [<CLIMutable>]
    type SettingsFile =
        { Version: int
          VolumePercent: int
          RecentRoms: string[]
          Scale: int
          KeyboardMapping: Dictionary<string, string>
          ControllerMapping: Dictionary<string, string> }

    type Settings =
        { VolumePercent: int
          RecentRoms: string list
          Scale: int
          KeyboardMapping: Map<string, string>
          ControllerMapping: Map<string, string> }

    let defaults =
        { VolumePercent = 50
          RecentRoms = []
          Scale = 2
          KeyboardMapping = defaultKeyboardMapping
          ControllerMapping = defaultControllerMapping }

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

    let private normalizeKeyboardMapping (mapping: Map<string, string>) =
        let input =
            mapping
            |> Map.toSeq
            |> Seq.choose (fun (button, key) ->
                if String.IsNullOrWhiteSpace button || String.IsNullOrWhiteSpace key then
                    None
                else
                    Some(button.Trim(), key.Trim()))
            |> Seq.fold
                (fun (known: Map<string, string>) (button, key) ->
                    match KeyboardButtonOrder |> List.tryFind (fun knownButton -> String.Equals(knownButton, button, StringComparison.OrdinalIgnoreCase)) with
                    | Some knownButton -> known.Add(knownButton, key)
                    | None -> known)
                Map.empty

        KeyboardButtonOrder
        |> List.fold
            (fun normalized button ->
                let defaultKey = defaultKeyboardMapping[button]
                let candidate = input |> Map.tryFind button |> Option.defaultValue defaultKey
                let keysAssignedToOtherButtons =
                    normalized
                    |> Map.toSeq
                    |> Seq.filter (fun (existingButton, _) -> existingButton <> button)
                    |> Seq.map snd
                    |> fun keys -> HashSet<string>(keys, StringComparer.OrdinalIgnoreCase)

                if keysAssignedToOtherButtons.Contains candidate then
                    normalized.Add(button, defaultKey)
                else
                    normalized.Add(button, candidate))
            defaultKeyboardMapping

    let private normalizeControllerMapping (mapping: Map<string, string>) =
        let validControls = HashSet<string>(ControllerControlNames, StringComparer.OrdinalIgnoreCase)

        let input =
            mapping
            |> Map.toSeq
            |> Seq.choose (fun (button, control) ->
                if String.IsNullOrWhiteSpace button || String.IsNullOrWhiteSpace control then
                    None
                else
                    Some(button.Trim(), control.Trim()))
            |> Seq.fold
                (fun (known: Map<string, string>) (button, control) ->
                    match KeyboardButtonOrder |> List.tryFind (fun knownButton -> String.Equals(knownButton, button, StringComparison.OrdinalIgnoreCase)) with
                    | Some knownButton -> known.Add(knownButton, control)
                    | None -> known)
                Map.empty

        KeyboardButtonOrder
        |> List.fold
            (fun normalized button ->
                let defaultControl = defaultControllerMapping[button]
                let candidate = input |> Map.tryFind button |> Option.defaultValue defaultControl
                let controlsAssignedToOtherButtons =
                    normalized
                    |> Map.toSeq
                    |> Seq.filter (fun (existingButton, _) -> existingButton <> button)
                    |> Seq.map snd
                    |> fun controls -> HashSet<string>(controls, StringComparer.OrdinalIgnoreCase)

                if validControls.Contains candidate && not (controlsAssignedToOtherButtons.Contains candidate) then
                    normalized.Add(button, candidate)
                else
                    normalized.Add(button, defaultControl))
            defaultControllerMapping

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
          KeyboardMapping = normalizeKeyboardMapping settings.KeyboardMapping
          ControllerMapping = normalizeControllerMapping settings.ControllerMapping }

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
                elif file.Version <> CurrentVersion && file.Version <> 3 && file.Version <> 2 && file.Version <> 1 then
                    raise (InvalidDataException $"Unsupported settings version {file.Version}.")
                else
                    let keyboardMapping =
                        if file.Version < 3 || isNull file.KeyboardMapping then
                            defaultKeyboardMapping
                        else
                            file.KeyboardMapping
                            |> Seq.map (fun pair -> pair.Key, pair.Value)
                            |> Map.ofSeq

                    let controllerMapping =
                        if file.Version < 4 || isNull file.ControllerMapping then
                            defaultControllerMapping
                        else
                            file.ControllerMapping
                            |> Seq.map (fun pair -> pair.Key, pair.Value)
                            |> Map.ofSeq

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
                          KeyboardMapping = keyboardMapping
                          ControllerMapping = controllerMapping })

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
                  KeyboardMapping = Dictionary<string, string>(normalized.KeyboardMapping)
                  ControllerMapping = Dictionary<string, string>(normalized.ControllerMapping) }

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

    let withKeyboardMapping mapping settings =
        normalize { settings with KeyboardMapping = mapping }

    let withControllerMapping mapping settings =
        normalize { settings with ControllerMapping = mapping }
