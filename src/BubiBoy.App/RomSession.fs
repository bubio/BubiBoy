namespace BubiBoy.App

open BubiBoy.Core
open BubiBoy.IO

module RomSession =
    type Creation =
        { Session: Emulator.Session
          BootRomStatus: string
          BootRomWarning: string option }

    let private postBoot rom status warning =
        Emulator.createSession rom
        |> Result.map (fun session ->
            { Session = session
              BootRomStatus = status
              BootRomWarning = warning })

    let private createWithDmgBootRom (rom: RomFile.LoadedRom) =
        if rom.Header.CgbSupport <> Cartridge.DmgOnly then
            let warning =
                "DMG boot ROM is incompatible with CGB-capable cartridges; using post-boot initialization."

            postBoot rom.Bytes warning (Some warning)
        else
            match BootRomFile.loadDmg () with
            | Ok bootRom ->
                Emulator.createSessionWithDmgBootRom bootRom.Bytes rom.Bytes
                |> Result.map (fun session ->
                    { Session = session
                      BootRomStatus = $"DMG boot ROM loaded: {bootRom.Path} ({bootRom.Sha256})"
                      BootRomWarning = None })
            | Error message ->
                let warning =
                    $"DMG boot ROM unavailable; using post-boot initialization. Expected: {BootRomFile.dmgPath ()}"

                postBoot rom.Bytes $"{warning}\n{message}" (Some warning)

    let private createWithCgbBootRom (rom: RomFile.LoadedRom) =
        match BootRomFile.loadCgb () with
        | Ok bootRom ->
            Emulator.createSessionWithCgbBootRom bootRom.Bytes rom.Bytes
            |> Result.map (fun session ->
                { Session = session
                  BootRomStatus = $"CGB boot ROM loaded: {bootRom.Path} ({bootRom.Sha256})"
                  BootRomWarning = None })
        | Error message ->
            let warning =
                $"CGB boot ROM unavailable; using post-boot initialization. Expected: {BootRomFile.cgbPath ()}"

            postBoot rom.Bytes $"{warning}\n{message}" (Some warning)

    let private createSession selection (rom: RomFile.LoadedRom) =
        match selection with
        | AppSettings.Disabled -> postBoot rom.Bytes "Boot ROM disabled; using post-boot initialization." None
        | AppSettings.Automatic ->
            match rom.Header.CgbSupport with
            | Cartridge.DmgOnly -> createWithDmgBootRom rom
            | Cartridge.CgbEnhanced
            | Cartridge.CgbOnly -> createWithCgbBootRom rom
        | AppSettings.Cgb -> createWithCgbBootRom rom
        | AppSettings.Dmg -> createWithDmgBootRom rom

    let createForRom selection (rom: RomFile.LoadedRom) =
        createSession selection rom
        |> Result.bind (fun creation ->
            SaveRam.loadForRom rom.Path (Bus.cartridge creation.Session.Bus)
            |> Result.map (fun cartridge ->
                { creation with
                    Session =
                        { creation.Session with
                            Bus = Bus.withCartridge cartridge creation.Session.Bus } }))
