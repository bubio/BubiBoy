# Boot ROMs

BubiBoy can execute external DMG and CGB boot ROMs. Boot ROM files are copyrighted system software
and are not included in or redistributed with the repository. Boot ROM use is disabled by default.

The expected file names are:

- DMG: `dmg_boot.bin` (256 bytes)
- CGB: `cgb_boot.bin` (2304 bytes)

Place them in the platform data directory:

- macOS: `~/Library/Application Support/BubiBoy`
- Linux: `$XDG_DATA_HOME/BubiBoy`, or `~/.local/share/BubiBoy` when `XDG_DATA_HOME` is unset
- Windows: `%LOCALAPPDATA%\BubiBoy`

The Emulation > Settings menu controls boot ROM selection:

- `使用しない`: skip boot ROMs and use post-boot initialization.
- `自動`: use the DMG boot ROM for DMG-only cartridges and the CGB boot ROM for CGB-capable cartridges.
- `CGB`: use the CGB boot ROM for every cartridge. DMG-only cartridges enter CGB compatibility mode.
- `DMG`: use the DMG boot ROM for DMG-only cartridges. CGB-capable cartridges fall back to CGB
  post-boot initialization because the DMG boot ROM cannot start them.

BubiBoy records the selected boot ROM's SHA-256 identity in save states but does not restrict accepted
hashes. If the selected file is missing, unreadable, or the wrong size, the game falls back to post-boot
initialization and reports the reason in the application status. It does not try a different boot ROM.

On Windows, `settings.json` is also stored in `%LOCALAPPDATA%\BubiBoy`. An existing settings file under
`%APPDATA%\BubiBoy` is migrated automatically when the Local settings file does not exist.
