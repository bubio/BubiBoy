namespace BubiBoy.IO

open System
open System.IO
open BubiBoy.Core

module RomFile =
    type LoadedRom =
        { Path: string
          Bytes: byte[]
          Header: Cartridge.CartridgeHeader }

    let load path =
        if String.IsNullOrWhiteSpace path then
            Error "ROM path is empty."
        elif not (File.Exists path) then
            Error $"ROM file does not exist: {path}"
        else
            let bytes = File.ReadAllBytes path

            Cartridge.parseHeader bytes
            |> Result.map (fun header ->
                { Path = path
                  Bytes = bytes
                  Header = header })

