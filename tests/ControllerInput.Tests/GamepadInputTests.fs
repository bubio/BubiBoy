module ControllerInput.Tests.GamepadInputTests

open System
open BubiBoy.App
open BubiBoy.Core
open BubiBoy.IO
open ControllerInput
open Xunit

[<Fact>]
let ``GamepadId trims non-empty values`` () =
    let id = GamepadId.create "  controller-1  "

    Assert.Equal("controller-1", GamepadId.value id)

[<Fact>]
let ``GamepadId rejects empty values`` () =
    Assert.Throws<ArgumentException>(fun () -> GamepadId.create "  " |> ignore)

[<Fact>]
let ``GamepadControl parses stable storage names`` () =
    Assert.Equal(Some GamepadControl.DPadUp, GamepadControl.tryParse "DPadUp")
    Assert.Equal(Some GamepadControl.South, GamepadControl.tryParse "south")
    Assert.Equal(None, GamepadControl.tryParse "NotAControl")

[<Fact>]
let ``GamepadSnapshot creates defensive pressed set copy`` () =
    let pressed = ResizeArray([ GamepadControl.South ])
    let snapshot = GamepadSnapshot.create (GamepadId.create "one") "  Test Pad  " pressed
    pressed.Add GamepadControl.East

    Assert.Equal("Test Pad", snapshot.Name)
    Assert.Contains(GamepadControl.South, snapshot.Pressed)
    Assert.DoesNotContain(GamepadControl.East, snapshot.Pressed)

[<Fact>]
let ``UnsupportedGamepadHost polls no controllers`` () =
    use host = GamepadHosts.createUnsupported "not available"

    Assert.Empty(host.Poll())

[<Fact>]
let ``Default GamepadHost can be created and polled`` () =
    use host = GamepadHosts.createDefault ()

    Assert.NotNull(host.Poll())

[<Fact>]
let ``ControllerInputAdapter applies custom controller mapping`` () =
    let mapping =
        AppSettings.defaultControllerMapping
        |> Map.add "A" "West"
        |> Map.add "B" "North"

    let snapshot =
        GamepadSnapshot.create
            (GamepadId.create "one")
            "Test Pad"
            [ GamepadControl.West
              GamepadControl.North
              GamepadControl.South ]

    let buttons = ControllerInputAdapter.joypadButtonsForSnapshot mapping snapshot

    Assert.Contains(Joypad.A, buttons)
    Assert.Contains(Joypad.B, buttons)
    Assert.DoesNotContain(Joypad.Select, buttons)

[<Fact>]
let ``Linux evdev key codes map to standard gamepad controls`` () =
    Assert.Equal(Some GamepadControl.South, LinuxEvdev.controlForKey LinuxEvdev.BTN_SOUTH)
    Assert.Equal(Some GamepadControl.East, LinuxEvdev.controlForKey LinuxEvdev.BTN_EAST)
    Assert.Equal(Some GamepadControl.West, LinuxEvdev.controlForKey LinuxEvdev.BTN_WEST)
    Assert.Equal(Some GamepadControl.North, LinuxEvdev.controlForKey LinuxEvdev.BTN_NORTH)
    Assert.Equal(Some GamepadControl.Start, LinuxEvdev.controlForKey LinuxEvdev.BTN_START)
    Assert.Equal(Some GamepadControl.Select, LinuxEvdev.controlForKey LinuxEvdev.BTN_SELECT)
    Assert.Equal(Some GamepadControl.DPadUp, LinuxEvdev.controlForKey LinuxEvdev.BTN_DPAD_UP)
    Assert.Equal(Some GamepadControl.DPadDown, LinuxEvdev.controlForKey LinuxEvdev.BTN_DPAD_DOWN)
    Assert.Equal(Some GamepadControl.DPadLeft, LinuxEvdev.controlForKey LinuxEvdev.BTN_DPAD_LEFT)
    Assert.Equal(Some GamepadControl.DPadRight, LinuxEvdev.controlForKey LinuxEvdev.BTN_DPAD_RIGHT)

[<Fact>]
let ``Linux evdev hat axes map negative and positive directions`` () =
    Assert.Equal<GamepadControl list>([ GamepadControl.DPadLeft ], LinuxEvdev.controlsForHatAxis LinuxEvdev.ABS_HAT0X -1)
    Assert.Equal<GamepadControl list>([ GamepadControl.DPadRight ], LinuxEvdev.controlsForHatAxis LinuxEvdev.ABS_HAT0X 1)
    Assert.Equal<GamepadControl list>([ GamepadControl.DPadUp ], LinuxEvdev.controlsForHatAxis LinuxEvdev.ABS_HAT0Y -1)
    Assert.Equal<GamepadControl list>([ GamepadControl.DPadDown ], LinuxEvdev.controlsForHatAxis LinuxEvdev.ABS_HAT0Y 1)
    Assert.Empty(LinuxEvdev.controlsForHatAxis LinuxEvdev.ABS_HAT0X 0)

[<Fact>]
let ``Linux evdev left stick axes use deadzone before digital directions`` () =
    let centered = LinuxEvdev.AbsInfo(0, -32768, 32767, 0, 0, 0)
    let left = LinuxEvdev.AbsInfo(-32768, -32768, 32767, 0, 0, 0)
    let right = LinuxEvdev.AbsInfo(32767, -32768, 32767, 0, 0, 0)
    let up = LinuxEvdev.AbsInfo(-32768, -32768, 32767, 0, 0, 0)
    let down = LinuxEvdev.AbsInfo(32767, -32768, 32767, 0, 0, 0)

    Assert.Empty(LinuxEvdev.controlsForStickAxis LinuxEvdev.ABS_X centered)
    Assert.Equal<GamepadControl list>([ GamepadControl.LeftStickLeft ], LinuxEvdev.controlsForStickAxis LinuxEvdev.ABS_X left)
    Assert.Equal<GamepadControl list>([ GamepadControl.LeftStickRight ], LinuxEvdev.controlsForStickAxis LinuxEvdev.ABS_X right)
    Assert.Equal<GamepadControl list>([ GamepadControl.LeftStickUp ], LinuxEvdev.controlsForStickAxis LinuxEvdev.ABS_Y up)
    Assert.Equal<GamepadControl list>([ GamepadControl.LeftStickDown ], LinuxEvdev.controlsForStickAxis LinuxEvdev.ABS_Y down)

[<Fact>]
let ``Linux evdev trigger axes map above midpoint`` () =
    let released = LinuxEvdev.AbsInfo(0, 0, 255, 0, 0, 0)
    let pressed = LinuxEvdev.AbsInfo(255, 0, 255, 0, 0, 0)

    Assert.Empty(LinuxEvdev.controlsForTriggerAxis LinuxEvdev.ABS_Z released)
    Assert.Equal<GamepadControl list>([ GamepadControl.LeftTrigger ], LinuxEvdev.controlsForTriggerAxis LinuxEvdev.ABS_Z pressed)
    Assert.Equal<GamepadControl list>([ GamepadControl.RightTrigger ], LinuxEvdev.controlsForTriggerAxis LinuxEvdev.ABS_RZ pressed)
