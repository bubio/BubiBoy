namespace BubiBoy.App

open System
open Avalonia.Input
open BubiBoy.Core
open BubiBoy.IO

module InputMapping =
    let allJoypadButtons =
        [ Joypad.Right; Joypad.Left; Joypad.Up; Joypad.Down
          Joypad.A; Joypad.B; Joypad.Select; Joypad.Start ]

    let buttonName button =
        match button with
        | Joypad.Right -> "Right"
        | Joypad.Left -> "Left"
        | Joypad.Up -> "Up"
        | Joypad.Down -> "Down"
        | Joypad.A -> "A"
        | Joypad.B -> "B"
        | Joypad.Select -> "Select"
        | Joypad.Start -> "Start"

    let buttonDisplayName button =
        match button with
        | Joypad.Right -> "Right"
        | Joypad.Left -> "Left"
        | Joypad.Up -> "Up"
        | Joypad.Down -> "Down"
        | Joypad.A -> "A"
        | Joypad.B -> "B"
        | Joypad.Select -> "Select"
        | Joypad.Start -> "Start"

    let private tryParseKey (keyName: string) =
        match Enum.TryParse<Key>(keyName, true) with
        | true, key -> Some key
        | _ -> None

    let keyDisplayName key =
        match key with
        | Key.Back -> "Backspace"
        | Key.Enter -> "Enter"
        | Key.Space -> "Space"
        | Key.Left -> "Left Arrow"
        | Key.Right -> "Right Arrow"
        | Key.Up -> "Up Arrow"
        | Key.Down -> "Down Arrow"
        | _ -> key.ToString()

    let keyStorageName (key: Key) = key.ToString()

    let private defaultKeyFor button =
        AppSettings.defaultKeyboardMapping[buttonName button]
        |> tryParseKey
        |> Option.defaultValue Key.None

    let normalizeKeyMapping (mapping: Map<string, string>) =
        let usedKeys = Collections.Generic.HashSet<Key>()

        allJoypadButtons
        |> List.map (fun button ->
            let name = buttonName button
            let configuredKey =
                mapping
                |> Map.tryFind name
                |> Option.bind tryParseKey

            let defaultKey = defaultKeyFor button
            let key =
                match configuredKey with
                | Some key when key <> Key.None && usedKeys.Add key -> key
                | _ ->
                    usedKeys.Add defaultKey |> ignore
                    defaultKey

            name, keyStorageName key)
        |> Map.ofList

    let keyForButton mapping button =
        let normalized = normalizeKeyMapping mapping

        normalized
        |> Map.tryFind (buttonName button)
        |> Option.bind tryParseKey
        |> Option.defaultWith (fun () -> defaultKeyFor button)

    let setKey button key mapping =
        mapping
        |> normalizeKeyMapping
        |> Map.add (buttonName button) (keyStorageName key)
        |> normalizeKeyMapping

    let resetDefaults () =
        normalizeKeyMapping AppSettings.defaultKeyboardMapping

    let mapKey mapping key =
        let normalized = normalizeKeyMapping mapping

        allJoypadButtons
        |> List.tryFind (fun button -> keyForButton normalized button = key)
