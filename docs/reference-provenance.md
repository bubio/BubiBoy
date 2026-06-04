# Reference Provenance

Record every external emulator, document, test ROM, or asset consulted for BubiBoy.

| Reference | Source | License | Redistribution | Usage |
| --- | --- | --- | --- | --- |
| Fame Boy | `~/dev/_Emu/Original/fame-boy` | MIT | Do not redistribute its bundled ROMs or assets from this repository. | Consulted for F# emulator structure, post-boot IO register defaults, LCD STAT edge behavior, OAM DMA behavior, emulation loop structure, and audio-paced execution. Implementation in BubiBoy remains independently written. |
| Mooneye Test Suite | https://github.com/Gekkio/mooneye-test-suite and https://gekkio.fi/files/mooneye-test-suite/mts-20240926-1737-443f6e1/ | MIT | Redistributed subset is allowed under MIT. Vendored files are under `tests/BubiBoy.TestRoms/roms/mooneye/` with the upstream `LICENSE`. | Used as executable acceptance ROMs for Phase 2 CPU validation. Current vendored subset: `acceptance/instr/daa.gb` and `acceptance/bits/reg_f.gb`. |
| miniaudio 0.11.25 | https://github.com/mackron/miniaudio/tree/0.11.25 and https://miniaud.io/ | Public domain or MIT-0 | Vendored header and license are under `native/miniaudio.h` and `native/miniaudio-LICENSE`. | Used as the native audio output backend through a narrow C wrapper and F# P/Invoke layer. |
| Pan Docs audio chapters | https://gbdev.io/pandocs/Audio.html | Documentation license; verify before copying text. | Do not redistribute copied text beyond short citations. | Consulted for APU channel structure, triggering, envelope, length timer, and clocking concepts. |
| Pan Docs CGB registers and rendering notes | https://gbdev.io/pandocs/CGB_Registers.html and https://gbdev.io/pandocs/Rendering.html | Documentation license; verify before copying text. | Do not redistribute copied text beyond short citations. | Consulted for CGB-only registers, VRAM/WRAM bank behavior, palette ports, HDMA/GDMA shape, KEY1 speed switch behavior, and CGB object priority mode. |
| c-sp/game-boy-test-roms | https://github.com/c-sp/game-boy-test-roms | Repository is MIT, but bundled third-party ROMs retain their own provenance. | Do not vendor bundled ROMs without checking each upstream suite's license. | Consulted for test-suite inventory and external-ROM validation planning, especially Blargg `dmg_sound` location and result handling notes. |
| SameSuite | https://github.com/LIJI32/SameSuite | MIT | Do not vendor built ROMs until each artifact's provenance is recorded. | Consulted for APU DIV trigger test intent and expected pass/fail protocol. |
| Apple GameController documentation | https://developer.apple.com/documentation/gamecontroller/gccontroller and https://developer.apple.com/documentation/gamecontroller/gcextendedgamepad | Apple developer documentation; API reference only. | Do not copy documentation text into this repository beyond short citations. | Consulted for macOS controller discovery shape, `GCController.controllers`, `GCExtendedGamepad`, direction pad, face button, shoulder, and trigger property names. The backend is independently written through a narrow Objective-C runtime interop layer. |
| Microsoft XInput documentation | https://learn.microsoft.com/windows/win32/xinput/getting-started-with-xinput and https://learn.microsoft.com/windows/win32/api/xinput/nf-xinput-xinputgetstate | Microsoft documentation; API reference only. | Do not copy documentation text into this repository beyond short citations. | Consulted for the XInput controller count, `XInputGetState` state shape, button flags, and trigger threshold. The Windows backend is independently written through a narrow P/Invoke interop layer. |

## Rules

- Prefer permissive, public-domain, or documentation-only references.
- Do not copy code from GPL, LGPL, AGPL, proprietary, or unclear-license emulators.
- Do not commit ROMs, BIOS files, screenshots, fonts, or other assets without an explicit redistribution license.
- When behavior is learned from a reference, document the reference here and implement the behavior independently.
