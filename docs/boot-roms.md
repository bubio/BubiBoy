# Boot ROMs

BubiBoy can execute external DMG and CGB boot ROMs. Boot ROM files are copyrighted system software
and are not included in or redistributed with the repository.

The expected file names are:

- DMG: `dmg_boot.bin` (256 bytes)
- CGB: `cgb_boot.bin` (2304 bytes)

Place them in the platform data directory:

- macOS: `~/Library/Application Support/BubiBoy`
- Linux: `$XDG_DATA_HOME/BubiBoy`, or `~/.local/share/BubiBoy` when `XDG_DATA_HOME` is unset
- Windows: `%LOCALAPPDATA%\BubiBoy`

BubiBoy records the selected boot ROM's SHA-256 identity in save states but does not restrict accepted
hashes. If a file is missing, unreadable, or the wrong size, games for that hardware mode fall back to
the existing post-boot initialization and report the reason in the application status.

On Windows, `settings.json` is also stored in `%LOCALAPPDATA%\BubiBoy`. An existing settings file under
`%APPDATA%\BubiBoy` is migrated automatically when the Local settings file does not exist.
