namespace BubiBoy.App

open Avalonia.Controls
open Avalonia.Platform.Storage
open ControllerInput

/// Dialog helpers owned by the Avalonia shell.
module AppDialogs =
    let private romFileType =
        FilePickerFileType(
            "Game Boy ROM",
            Patterns = [| "*.gb"; "*.gbc" |],
            MimeTypes = [| "application/octet-stream" |]
        )

    /// Opens the ROM picker and returns the selected local path, if any.
    let pickRomPath (owner: Window) =
        async {
            let options =
                FilePickerOpenOptions(
                    Title = "Open Game Boy ROM",
                    AllowMultiple = false,
                    FileTypeFilter = [| romFileType; FilePickerFileTypes.All |]
                )

            let! files = owner.StorageProvider.OpenFilePickerAsync(options) |> Async.AwaitTask

            if files.Count > 0 then
                return files[0].TryGetLocalPath() |> Option.ofObj
            else
                return None
        }

    /// Shows the input mapping editor.
    let showInputMapping
        (owner: Window)
        (keyboardMapping: Map<string, string>)
        (controllerMapping: Map<string, string>)
        (controllerHost: GamepadHost)
        =
        InputMappingWindow.Show(owner, keyboardMapping, controllerMapping, controllerHost)

    /// Shows the boot ROM settings editor.
    let showSettings owner selection = SettingsWindow.Show(owner, selection)
