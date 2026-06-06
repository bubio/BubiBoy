namespace BubiBoy.App

open System
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Threading
open BubiBoy.Core

/// Owns keyboard/controller input state and input-mapping UI.
type AppInputHost
    (
        owner: Window,
        settingsStore: AppSettingsStore,
        saveSettings: unit -> unit,
        notify: string -> unit
    ) =
    let inputState = InputStateController()
    let controllerHost = ControllerInput.GamepadHosts.createDefault ()
    let pollTimer = DispatcherTimer(Interval = TimeSpan.FromMilliseconds(16.0))

    let pollController () =
        inputState.PollController(controllerHost, settingsStore.Current.ControllerMapping)
        |> Option.iter notify

    do
        pollTimer.Tick.Add(fun _ ->
            try
                pollController ()
            with ex ->
                pollTimer.Stop()
                inputState.DisableController()
                notify $"Controller input disabled: {ex.Message}")

    /// Applies the latest host input state at an emulation frame boundary.
    member _.ApplyInput(session: Emulator.Session) =
        inputState.ApplyInput session

    /// Updates keyboard state and returns whether the key was mapped.
    member _.UpdateKeyboardKey(key: Key, pressed: bool) =
        inputState.UpdateKeyboardKey(settingsStore.Current.KeyboardMapping, key, pressed)

    /// Opens the input mapping editor.
    member _.OpenMapping() =
        task {
            let! result =
                AppDialogs.showInputMapping
                    owner
                    settingsStore.Current.KeyboardMapping
                    settingsStore.Current.ControllerMapping
                    controllerHost

            match result with
            | Some inputMapping ->
                settingsStore.SetInputMappings(
                    inputMapping.KeyboardMapping,
                    inputMapping.ControllerMapping
                )
                |> ignore

                inputState.ResetKeyboard()
                saveSettings ()
                notify "Input mapping saved."
            | None -> ()
        }
        |> ignore

    /// Starts polling connected controllers.
    member _.Start() =
        pollTimer.Start()

    /// Releases controller polling and native host resources.
    member _.Dispose() =
        pollTimer.Stop()
        controllerHost.Dispose()
