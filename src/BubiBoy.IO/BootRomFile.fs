namespace BubiBoy.IO

open System
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography

module BootRomFile =
    [<Literal>]
    let DmgFileName = "dmg_boot.bin"

    [<Literal>]
    let DmgSize = 256

    type LoadedBootRom =
        { Path: string
          Bytes: byte[]
          Sha256: string }

    let private appDirectory root = Path.Combine(root, "BubiBoy")

    let dataDirectory () =
        if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            |> appDirectory
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            |> appDirectory
        else
            match Environment.GetEnvironmentVariable("XDG_DATA_HOME") with
            | value when not (String.IsNullOrWhiteSpace value) -> appDirectory value
            | _ ->
                let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                Path.Combine(home, ".local", "share", "BubiBoy")

    let dmgPath () =
        Path.Combine(dataDirectory (), DmgFileName)

    let loadDmgFromPath path =
        if String.IsNullOrWhiteSpace path then
            Error "DMG boot ROM path is empty."
        elif not (File.Exists path) then
            Error $"DMG boot ROM does not exist: {path}"
        else
            try
                let bytes = File.ReadAllBytes path

                if bytes.Length <> DmgSize then
                    Error $"DMG boot ROM size mismatch: expected {DmgSize} bytes, got {bytes.Length} bytes: {path}"
                else
                    Ok
                        { Path = path
                          Bytes = bytes
                          Sha256 = SHA256.HashData bytes |> Convert.ToHexString }
            with
            | :? IOException as ex -> Error $"Could not read DMG boot ROM: {ex.Message}"
            | :? UnauthorizedAccessException as ex -> Error $"Could not read DMG boot ROM: {ex.Message}"

    let loadDmg () = dmgPath () |> loadDmgFromPath
