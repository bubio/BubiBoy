# Boot ROMs

BubiBoy can execute an external 256-byte DMG boot ROM for DMG-only cartridges. Boot ROM files are
copyrighted system software and are not included in or redistributed with the repository.

The expected file name is `dmg_boot.bin`. Place it in the platform data directory:

- macOS: `~/Library/Application Support/BubiBoy/dmg_boot.bin`
- Linux: `$XDG_DATA_HOME/BubiBoy/dmg_boot.bin`, or `~/.local/share/BubiBoy/dmg_boot.bin` when
  `XDG_DATA_HOME` is unset
- Windows: `%LOCALAPPDATA%\BubiBoy\dmg_boot.bin`

The file must be exactly 256 bytes. BubiBoy records its SHA-256 identity in save states but does not
restrict the accepted hash. If the file is missing, unreadable, or the wrong size, DMG games fall back
to the existing post-boot initialization and report the reason in the application status.

CGB boot ROM execution is not implemented yet. CGB-capable cartridges continue to use post-boot
initialization.
