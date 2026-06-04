namespace ControllerInput

open System.Runtime.InteropServices

module GamepadHosts =
    let createUnsupported reason : GamepadHost =
        new UnsupportedGamepadHost(reason) :> GamepadHost

    let createDefault () : GamepadHost =
        if RuntimeInformation.IsOSPlatform OSPlatform.OSX then
            match MacOSGamepadHost.TryCreate() with
            | Ok host -> host
            | Error reason -> createUnsupported reason
        elif RuntimeInformation.IsOSPlatform OSPlatform.Windows then
            match WindowsXInputGamepadHost.TryCreate() with
            | Ok host -> host
            | Error reason -> createUnsupported reason
        else
            createUnsupported "No gamepad backend is available for this platform yet."
