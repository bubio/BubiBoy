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

    [<Literal>]
    let CgbFileName = "cgb_boot.bin"

    [<Literal>]
    let CgbSize = 2304

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

    let cgbPath () =
        Path.Combine(dataDirectory (), CgbFileName)

    let private loadFromPath label expectedSize path =
        if String.IsNullOrWhiteSpace path then
            Error $"{label} boot ROM path is empty."
        elif not (File.Exists path) then
            Error $"{label} boot ROM does not exist: {path}"
        else
            try
                let bytes = File.ReadAllBytes path

                if bytes.Length <> expectedSize then
                    Error
                        $"{label} boot ROM size mismatch: expected {expectedSize} bytes, got {bytes.Length} bytes: {path}"
                else
                    Ok
                        { Path = path
                          Bytes = bytes
                          Sha256 = SHA256.HashData bytes |> Convert.ToHexString }
            with
            | :? IOException as ex -> Error $"Could not read {label} boot ROM: {ex.Message}"
            | :? UnauthorizedAccessException as ex -> Error $"Could not read {label} boot ROM: {ex.Message}"

    let loadDmgFromPath path = loadFromPath "DMG" DmgSize path
    let loadDmg () = dmgPath () |> loadDmgFromPath

    let loadCgbFromPath path = loadFromPath "CGB" CgbSize path
    let loadCgb () = cgbPath () |> loadCgbFromPath
