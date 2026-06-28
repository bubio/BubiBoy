namespace BubiBoy.RetroAchievements

open System
open System.Runtime.InteropServices
open System.Text

module RaCredentialStore =
    [<Literal>]
    let private CredentialUnavailable = -1

    [<Literal>]
    let private CredentialBackendMissing = -2

    [<Literal>]
    let private CredentialBackendError = -3

    type Store =
        { SaveToken: string -> string -> Result<unit, string>
          TryLoadToken: string -> string option
          DeleteToken: string -> unit }

    [<Literal>]
    let private Service = "org.bubiboy.RetroAchievements"

    let private isMacOs = RuntimeInformation.IsOSPlatform OSPlatform.OSX
    let private isLinux = RuntimeInformation.IsOSPlatform OSPlatform.Linux
    let private isWindows = RuntimeInformation.IsOSPlatform OSPlatform.Windows
    let private supportsSecureStorage = isMacOs || isLinux || isWindows

    let private saveFailureMessage (result: int) =
        if isMacOs then
            $"macOS Keychain returned status {result}."
        elif isLinux then
            match result with
            | CredentialBackendMissing -> "Linux Secret Service (libsecret) support is not available in this build."
            | CredentialBackendError -> "Linux Secret Service returned an error while storing the token."
            | _ -> $"Linux credential storage returned status {result}."
        elif isWindows then
            $"Windows Credential Manager returned status {result} while storing the token."
        else
            "Secure RetroAchievements credential storage is not available on this platform yet."

    let saveToken username token =
        if String.IsNullOrWhiteSpace username || String.IsNullOrWhiteSpace token then
            Error "RetroAchievements username and token must not be empty."
        elif not supportsSecureStorage then
            Error(saveFailureMessage CredentialUnavailable)
        else
            let result =
                NativeInterop.Native.bubi_ra_credential_store (Service, username, token)

            if result = 0 then
                Ok()
            else
                Error(saveFailureMessage result)

    let tryLoadToken username =
        if String.IsNullOrWhiteSpace username || not supportsSecureStorage then
            None
        else
            let buffer = StringBuilder(1024)

            let result =
                NativeInterop.Native.bubi_ra_credential_load (Service, username, buffer, unativeint buffer.Capacity)

            if result = 0 then
                Some(buffer.ToString())
            elif isLinux && (result = CredentialBackendMissing || result = CredentialUnavailable) then
                None
            else
                None

    let deleteToken username =
        if String.IsNullOrWhiteSpace username || not supportsSecureStorage then
            ()
        else
            NativeInterop.Native.bubi_ra_credential_delete (Service, username) |> ignore

    let store =
        { SaveToken = saveToken
          TryLoadToken = tryLoadToken
          DeleteToken = deleteToken }
