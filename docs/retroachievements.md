# RetroAchievements Integration

BubiBoy uses the `rc_client` API from rcheevos 12.3.0 for its initial
RetroAchievements integration. The feature is opt-in and currently targets macOS
Softcore sessions.

## Boundaries

- `BubiBoy.Core` only exposes a side-effect-free inspection memory map.
- `BubiBoy.RetroAchievements` owns `rc_client`, HTTP, credentials, event snapshots,
  and runtime progress serialization.
- `native/bubi_rcheevos` flattens the C API into an ownership-safe ABI for F#.
- Passwords are never persisted. The username is stored in settings and the token
  is stored in macOS Keychain under `org.bubiboy.RetroAchievements`.
- A failed login or game identification never prevents the ROM from running. That
  ROM remains an offline session until it is reloaded.

## Memory Map

The normal `0x0000-0xFFFF` Game Boy map is exposed without CPU access restrictions
or device synchronization. The RetroAchievements extensions are:

- `0x010000-0x015FFF`: CGB WRAM banks 2-7
- `0x016000-0x033FFF`: cartridge RAM banks 1-15

`0xD000-0xDFFF` always maps to CGB WRAM bank 1, matching the official rcheevos
console map. Cartridge RAM at `0xA000-0xBFFF` always maps to physical bank 0.

## State Files

Active RA sessions store state under the application data directory at
`retroachievements/states/<game-id>/<rom-hash>.state`. The envelope contains the
Game ID, ROM hash, rcheevos version, core state, sized `rc_client` progress, and a
CRC32. All metadata is validated before the core state is restored. Normal state
files remain unchanged.

## Dependency Update

The pinned version and commit are recorded in `ThirdParty/versions.json`. To update:

1. Review the upstream changelog and public `rc_client.h` changes.
2. Replace only the explicitly compiled source inventory in `native/bubi_rcheevos/CMakeLists.txt`.
3. Update the license and version manifest.
4. Run memory-map, state-codec, native build, and full solution tests.

