namespace BubiBoy.App

open BubiBoy.Core
open BubiBoy.IO

module RomSession =
    type Creation =
        { Session: Emulator.Session
          BootRomStatus: string
          BootRomWarning: string option }

    let private createSession (rom: RomFile.LoadedRom) =
        match rom.Header.CgbSupport with
        | Cartridge.DmgOnly ->
            match BootRomFile.loadDmg () with
            | Ok bootRom ->
                Emulator.createSessionWithDmgBootRom bootRom.Bytes rom.Bytes
                |> Result.map (fun session ->
                    { Session = session
                      BootRomStatus = $"DMG boot ROM loaded: {bootRom.Path} ({bootRom.Sha256})"
                      BootRomWarning = None })
            | Error message ->
                Emulator.createSession rom.Bytes
                |> Result.map (fun session ->
                    let warning =
                        $"DMG boot ROM unavailable; using post-boot initialization. Expected: {BootRomFile.dmgPath ()}"

                    { Session = session
                      BootRomStatus = $"{warning}\n{message}"
                      BootRomWarning = Some warning })
        | Cartridge.CgbEnhanced
        | Cartridge.CgbOnly ->
            Emulator.createSession rom.Bytes
            |> Result.map (fun session ->
                { Session = session
                  BootRomStatus = "CGB boot ROM support is not implemented; using post-boot initialization."
                  BootRomWarning = None })

    let createForRom (rom: RomFile.LoadedRom) =
        createSession rom
        |> Result.bind (fun creation ->
            SaveRam.loadForRom rom.Path (Bus.cartridge creation.Session.Bus)
            |> Result.map (fun cartridge ->
                { creation with
                    Session =
                        { creation.Session with
                            Bus = Bus.withCartridge cartridge creation.Session.Bus } }))
