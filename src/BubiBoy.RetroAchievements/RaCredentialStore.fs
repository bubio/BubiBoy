namespace BubiBoy.RetroAchievements

open System
open System.Runtime.InteropServices
open System.Text

module RaCredentialStore =
    [<Literal>]
    let private Service = "org.bubiboy.RetroAchievements"

    let saveToken username token =
        if String.IsNullOrWhiteSpace username || String.IsNullOrWhiteSpace token then
            Error "RetroAchievements username and token must not be empty."
        elif not (RuntimeInformation.IsOSPlatform OSPlatform.OSX) then
            Error "Secure RetroAchievements credential storage is not available on this platform yet."
        else
            let result = NativeInterop.Native.bubi_ra_keychain_store (Service, username, token)

            if result = 0 then
                Ok()
            else
                Error $"macOS Keychain returned status {result}."

    let tryLoadToken username =
        if
            String.IsNullOrWhiteSpace username
            || not (RuntimeInformation.IsOSPlatform OSPlatform.OSX)
        then
            None
        else
            let buffer = StringBuilder(1024)

            let result =
                NativeInterop.Native.bubi_ra_keychain_load (Service, username, buffer, unativeint buffer.Capacity)

            if result = 0 then Some(buffer.ToString()) else None

    let deleteToken username =
        if
            String.IsNullOrWhiteSpace username
            || not (RuntimeInformation.IsOSPlatform OSPlatform.OSX)
        then
            ()
        else
            NativeInterop.Native.bubi_ra_keychain_delete (Service, username) |> ignore
