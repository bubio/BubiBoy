namespace BubiBoy.IO

open System
open System.IO
open BubiBoy.Core

module RomFile =
    type LoadedRom =
        { Path: string
          Bytes: byte[]
          Header: Cartridge.CartridgeHeader }

    let isAppleDoubleMetadataPath path =
        if String.IsNullOrWhiteSpace path then
            false
        else
            Path.GetFileName(path).StartsWith("._", StringComparison.Ordinal)

    let hasSupportedExtension path =
        if String.IsNullOrWhiteSpace path then
            false
        else
            let extension = Path.GetExtension(path)

            extension.Equals(".gb", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gbc", StringComparison.OrdinalIgnoreCase)

    let isCandidatePath path =
        hasSupportedExtension path && not (isAppleDoubleMetadataPath path)

    let load path =
        if String.IsNullOrWhiteSpace path then
            Error "ROM path is empty."
        elif isAppleDoubleMetadataPath path then
            Error "macOS AppleDouble metadata files are not Game Boy ROMs."
        elif not (hasSupportedExtension path) then
            Error $"Unsupported ROM file extension: {Path.GetExtension(path)}"
        elif not (File.Exists path) then
            Error $"ROM file does not exist: {path}"
        else
            let bytes = File.ReadAllBytes path

            Cartridge.parseHeader bytes
            |> Result.map (fun header ->
                { Path = path
                  Bytes = bytes
                  Header = header })
