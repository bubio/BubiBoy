# Reference Provenance

Record every external emulator, document, test ROM, or asset consulted for BubiBoy.

| Reference | Source | License | Redistribution | Usage |
| --- | --- | --- | --- | --- |
| Fame Boy | `/Users/seiji/dev/_Emu/Original/fame-boy` | MIT | Do not redistribute its bundled ROMs or assets from this repository. | Consulted for F# emulator structure, post-boot IO register defaults, LCD STAT edge behavior, and OAM DMA behavior. Implementation in BubiBoy remains independently written. |
| Mooneye Test Suite | https://github.com/Gekkio/mooneye-test-suite and https://gekkio.fi/files/mooneye-test-suite/mts-20240926-1737-443f6e1/ | MIT | Redistributed subset is allowed under MIT. Vendored files are under `tests/BubiBoy.TestRoms/roms/mooneye/` with the upstream `LICENSE`. | Used as executable acceptance ROMs for Phase 2 CPU validation. Current vendored subset: `acceptance/instr/daa.gb` and `acceptance/bits/reg_f.gb`. |
| miniaudio 0.11.25 | https://github.com/mackron/miniaudio/tree/0.11.25 and https://miniaud.io/ | Public domain or MIT-0 | Vendored header and license are under `native/miniaudio.h` and `native/miniaudio-LICENSE`. | Used as the native audio output backend through a narrow C wrapper and F# P/Invoke layer. |

## Rules

- Prefer permissive, public-domain, or documentation-only references.
- Do not copy code from GPL, LGPL, AGPL, proprietary, or unclear-license emulators.
- Do not commit ROMs, BIOS files, screenshots, fonts, or other assets without an explicit redistribution license.
- When behavior is learned from a reference, document the reference here and implement the behavior independently.
