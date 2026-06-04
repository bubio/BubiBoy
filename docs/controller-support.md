# Controller Support Plan

## Current State

BubiBoy currently handles user input in the Avalonia app layer:

- `BubiBoy.Core.Joypad` already exposes the correct emulator-facing model: a set of pressed
  `Joypad.Button` values.
- `MainWindow` keeps keyboard and controller button sets as app-owned input intent and reconciles their
  union into the emulation session at frame boundaries.
- `InputMapping` and `InputMappingWindow` are keyboard-specific.
- `AppSettings` stores `KeyboardMapping` only.
- `ControllerInput` provides reusable gamepad types, an unsupported fallback host, and a macOS
  GameController.framework backend.

This means the core does not need controller-specific concepts. Controller support should be added through
a reusable host input library, then adapted in BubiBoy to produce the same `Set<Joypad.Button>` as keyboard
input.

## Recommendation

Avoid pulling in SDL3 only for controller input. Start with a small reusable .NET controller-input library
and implement platform-native backends incrementally.

Reasons:

- The emulator only needs eight digital Game Boy buttons, so a large media/game framework is out of scale
  for the initial requirement.
- Windows has XInput for a useful first-pass controller path.
- macOS has GameController.framework, which directly models connected controllers and their gamepad
  profiles.
- Linux exposes gamepads through the input subsystem, and the kernel documents standard gamepad button and
  axis codes.
- Keeping the backend behind a narrow interface lets BubiBoy add a broader library later without touching
  the core or the UI-facing input model.
- The same library can serve Avalonia apps that want controller input without adopting a game framework.

Avalonia should remain responsible for keyboard, menus, focus, and windowing. Its input model has a
`KeyDeviceType.Gamepad`, but Avalonia documentation also notes that broad cross-platform gamepad APIs are
not currently available out of the box. Treat Avalonia gamepad key events as an optional future shortcut,
not the primary controller backend.

## Reusable Library Shape

The reusable layer should not mention BubiBoy, Game Boy, Avalonia controls, or emulator concepts. A working
name could be `GamepadInput.Native`, `Avalonia.GamepadInput`, or `ControllerInput.Native`.

Prefer a small standalone project in this repository first, for example:

```text
src/ControllerInput/
  ControllerInput.fsproj
  GamepadTypes.fs
  GamepadHost.fs
  UnsupportedGamepadHost.fs
  Platforms/
    macOS/
    Windows/
    Linux/
tests/ControllerInput.Tests/
```

Once the API proves useful, it can be moved to its own repository or packed as a NuGet package. Keeping it
inside this repo initially avoids premature package maintenance while still forcing the API boundary to stay
clean.

The public API should be generic:

```fsharp
type GamepadId =
    new: value: string -> GamepadId
    member Value: string

type GamepadControl =
    | DPadUp = 0
    | DPadDown = 1
    | DPadLeft = 2
    | DPadRight = 3
    | South = 4
    | East = 5
    | West = 6
    | North = 7
    | Start = 8
    | Select = 9
    | LeftShoulder = 10
    | RightShoulder = 11
    | LeftTrigger = 12
    | RightTrigger = 13
    | LeftStickUp = 14
    | LeftStickDown = 15
    | LeftStickLeft = 16
    | LeftStickRight = 17

type GamepadSnapshot =
    { Id: GamepadId
      Name: string
      Pressed: IReadOnlySet<GamepadControl> }

type GamepadHost =
    inherit IDisposable
    abstract Poll: unit -> IReadOnlyList<GamepadSnapshot>
```

Use an enum for `GamepadControl` rather than an F# discriminated union so C# Avalonia apps can consume the
library without FSharp-specific pattern matching.

Optional but useful once the first backend works:

- `DeviceConnected` / `DeviceDisconnected` events, or a previous/current snapshot diff helper.
- `Capabilities` for controls that are not present on a device.
- `Deadzone` configuration for analog-stick digitalization.
- A C#-friendly API surface if the library is intended for broader Avalonia use.

Avoid requiring Avalonia references in the core input library. If Avalonia integration is useful, add a
separate thin package or module later, such as `Avalonia.GamepadInput`, that helps start/stop polling with
an Avalonia app lifetime and dispatches snapshots on the UI thread.

## BubiBoy Integration Shape

BubiBoy should adapt the generic snapshots to emulator buttons locally:

```fsharp
let mapControlToJoypad control =
    match control with
    | GamepadControl.DPadRight -> Some Joypad.Right
    | GamepadControl.DPadLeft -> Some Joypad.Left
    | GamepadControl.DPadUp -> Some Joypad.Up
    | GamepadControl.DPadDown -> Some Joypad.Down
    | GamepadControl.South -> Some Joypad.A
    | GamepadControl.East -> Some Joypad.B
    | GamepadControl.Select -> Some Joypad.Select
    | GamepadControl.Start -> Some Joypad.Start
    | _ -> None
```

The first version should be single-player:

- Pick the first connected controller automatically.
- If several controllers are connected, switch the active controller only when an explicit "assign" action
  is added later, or when no active controller exists and another controller sends input.
- Merge keyboard and controller state by unioning separate source states:
  `keyboardButtons + controllerButtons -> desiredButtons`.
- Do not let a keyboard release clear a controller-held button, or vice versa.

`MainWindow` tracks input sources separately:

```fsharp
let mutable desiredKeyboardButtons: Set<Joypad.Button> = Set.empty
let mutable desiredControllerButtons: Set<Joypad.Button> = Set.empty
```

Then `applyInput` can continue to reconcile only the union into the core session.

## Default Mapping

Start with a conventional Game Boy mapping:

| Game Boy | Controller |
| --- | --- |
| D-pad | D-pad |
| A | South face button |
| B | East or West face button, configurable |
| Start | Start/Menu |
| Select | Back/View |

The A/B default needs care because Nintendo-style and Xbox-style labels differ physically. Store mappings
by semantic control location, not by printed button label.

## Settings

Bump `AppSettings.CurrentVersion` when controller settings are persisted.

Suggested additions:

```fsharp
ControllerEnabled: bool
ControllerMapping: Map<string, string>
ControllerDeadzonePercent: int
PreferredControllerName: string option
```

For the first implementation, `ControllerEnabled = true`, default mapping, and a fixed deadzone are enough.
Expose remapping only after the polling backend is stable.

## Polling And Timing

Polling once per video frame is sufficient for a Game Boy emulator and keeps input deterministic at the
frame boundary already used by keyboard input. If a platform backend requires event pumping or notification
registration for device connection changes, do that inside the reusable host and expose only the resulting
button snapshots to BubiBoy.

Analog sticks can initially map to d-pad directions using a deadzone. Avoid diagonals policy changes in the
core; if both left/right or up/down are reported, normalize in the controller host before producing
`Joypad.Button` values.

The reusable library should not own app settings. It can expose stable string names for `GamepadControl`
values so each host app can persist mappings in its own settings format.

## Dependency Options

Preferred path:

- Add no third-party controller dependency for the first version.
- Keep platform calls in the reusable `ControllerInput` project, not in `BubiBoy.App`.
- Use `UnsupportedGamepadHost` on platforms/backends that are not implemented yet, so keyboard-only
  builds stay unchanged.
- Implement backends in this order:
  1. macOS GameController.framework, because it is the primary local development platform and supports
     common modern controllers through the OS.
  2. Windows XInput, because it is a small P/Invoke surface for Xbox-compatible controllers.
  3. Linux evdev, because it avoids SDL but needs more care around device discovery, permissions, and
     per-device quirks.

Alternative:

- SDL3 remains the most complete fallback if platform-specific support grows into a maintenance problem.
  Its Gamepad API has broad mappings and a permissive zlib license, but its runtime footprint and packaging
  cost are not justified for the first BubiBoy controller milestone.
- `Silk.NET.Input` is MIT-licensed and cross-platform, but it is also broader than the app needs and is
  oriented around Silk input/windowing backends. It is a secondary fallback, not the first choice.

Avoid:

- Windows-only XInput/GameInput as the primary path.
- HID-per-platform implementations as the initial path.
- Adding controller types to `BubiBoy.Core`.
- Making SDL3 part of the app just for eight digital buttons before trying the OS-native path.

## Implementation Milestones

1. [x] Add a reusable `ControllerInput` project with generic `GamepadControl`, `GamepadSnapshot`,
   `GamepadHost`, and `UnsupportedGamepadHost` types.
2. [x] Add a BubiBoy adapter from `GamepadControl` to `Joypad.Button`.
3. [x] Split `MainWindow` input state into keyboard and controller button sets, even before the real backend is
   added.
4. [x] Add the macOS GameController backend and poll it once per frame.
5. [x] Add a small status surface, such as menu text or toast, for connected/disconnected controller events.
6. [ ] Persist enable/disable and default mapping settings.
7. [x] Add Windows XInput and Linux evdev backends.
8. [ ] Add controller remapping UI after the default path is verified.
9. [ ] Consider extracting the reusable project to a separate repository/NuGet package once at least macOS and
   Windows backends are real and manually verified.

## Verification

Focused checks:

- The reusable project has no dependency on `BubiBoy.Core`, `BubiBoy.App`, or Avalonia.
- Keyboard input still works when no controller backend is available.
- Keyboard and controller can hold the same Game Boy button without one release clearing the other source.
- Hot-plugging a controller does not crash or stall the emulation loop.
- The app still builds and runs when a platform backend is unavailable, preferably with controller support
  disabled and a diagnostic message.

Broader checks:

- [x] `dotnet test`
- [x] Manual smoke on macOS with a physical controller.
- CI packaging checks for any native shims introduced by the platform backends. Linux evdev uses libc P/Invoke only and does not add a native shim.

## References Checked

- Avalonia input documentation: pointer/key input is built into Avalonia, but broad controller APIs are not
  enough for this use case.
- Apple GameController documentation: `GCController` represents connected controllers and exposes gamepad
  profiles that can be polled or handled by callbacks.
- Microsoft XInput documentation: `XInputGetState` exposes Xbox-compatible controller state through a small
  Win32 API surface.
- Linux kernel gamepad documentation: the input subsystem defines standard gamepad geometry and event codes.
- SDL3 Gamepad documentation: SDL exposes a standard gamepad layer over lower-level joystick inputs, but is
  kept as a fallback rather than the first implementation path.
- Silk.NET.Input NuGet metadata: MIT-licensed cross-platform input library, possible fallback.
