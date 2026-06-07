namespace BubiBoy.App

open BubiBoy.Core
open BubiBoy.IO

module RomSession =
    let createForRom (rom: RomFile.LoadedRom) =
        Emulator.createSession rom.Bytes
        |> Result.bind (fun session ->
            SaveRam.loadForRom rom.Path (Bus.cartridge session.Bus)
            |> Result.map (fun cartridge ->
                { session with
                    Bus = Bus.withCartridge cartridge session.Bus }))
