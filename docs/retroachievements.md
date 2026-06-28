# RetroAchievements Integration

BubiBoy uses the `rc_client` API from rcheevos 12.3.0 for its initial
RetroAchievements integration. The feature is opt-in and currently targets macOS
and Linux Softcore/Hardcore sessions.

## Boundaries

- `BubiBoy.Core` only exposes a side-effect-free inspection memory map.
- `BubiBoy.RetroAchievements` owns `rc_client`, HTTP, credentials, event snapshots,
  and runtime progress serialization.
- `native/bubi_rcheevos` flattens the C API into an ownership-safe ABI for F#.
- Passwords are never persisted. The username is stored in settings. On macOS, the
  token is stored in Keychain under `org.bubiboy.RetroAchievements`. On Linux, the
  token is stored via Secret Service (`libsecret`) when available in the runtime.
  If Secret Service is unavailable, the token is not persisted and the user must
  re-authenticate after restart. Windows currently keeps the existing non-persistent
  behavior.
- A failed login or game identification never prevents the ROM from running. That
  ROM remains an offline session until it is reloaded.
- Rich Presence is sampled from the active emulation session once per second and
  shown in the Achievements window. It is cleared immediately when the game or
  authenticated session ends.
- Leaderboard attempts are shown in the game viewport. Active tracker values are
  displayed at the upper left, and submitted rank, score, personal best, and the
  server-provided top entries are displayed at the lower left. The Achievements
  window also exposes the current leaderboard list in a separate tab and fetches
  each leaderboard's top entry for page-like context.

## Memory Map

The normal `0x0000-0xFFFF` Game Boy map is exposed without CPU access restrictions
or device synchronization. The RetroAchievements extensions are:

- `0x010000-0x015FFF`: CGB WRAM banks 2-7
- `0x016000-0x033FFF`: cartridge RAM banks 1-15

`0xD000-0xDFFF` always maps to CGB WRAM bank 1, matching the official rcheevos
console map. Cartridge RAM at `0xA000-0xBFFF` always maps to physical bank 0.

## State Files

Active Softcore RA sessions store state under the application data directory at
`retroachievements/states/<game-id>/<rom-hash>.state`. The envelope contains the
Game ID, ROM hash, rcheevos version, core state, sized `rc_client` progress, and a
CRC32. All metadata is validated before the core state is restored. Normal state
files remain unchanged. Save State creation remains available in Hardcore Mode,
but loading a state is unavailable. Battery-backed in-game saves use the normal
`.sav` file and are not separated by achievement mode.

## Controlled Operations

Pause, Save State, Load State, Reset, and game changes pass through the shared
RetroAchievements operation policy before mutating the emulator session. Softcore
permits state, reset, and game-change operations. Hardcore rejects Load State at
the shared operation boundary, and the corresponding menu item is disabled. Save
State creation is allowed. Pause attempts during an active RA session call
`rc_client_can_pause()` and report the remaining delay when denied. Enabling
Hardcore for a loaded game handles `RC_CLIENT_EVENT_RESET` by resetting the
emulator and acknowledging the reset with `rc_client_reset()`. The persisted
Hardcore preference defaults to enabled and is applied before loading a game.

The shared operation policy also reserves explicit operation types for Rewind,
Slow Motion, Frame Advance, Cheats, Input Playback, and Debugger access. These
features are not currently exposed by BubiBoy, but their Hardcore restrictions
are already tested. A future implementation must call the shared policy before
performing any of these operations instead of adding a feature-local Hardcore
check. Fast-forward is not classified as restricted.

## Manual Acceptance

Server-backed acceptance has been completed with one supported title and one
achievement unlock. Automated tests remain the primary coverage for login,
generation changes, overlays, state serialization, and failure paths.

## Dependency Update

The pinned version and commit are recorded in `ThirdParty/versions.json`. To update:

1. Review the upstream changelog and public `rc_client.h` changes.
2. Replace only the explicitly compiled source inventory in `native/bubi_rcheevos/CMakeLists.txt`.
3. Update the license and version manifest.
4. Run memory-map, state-codec, native build, and full solution tests.
