module ControllerInput.Tests.GamepadInputTests

open System
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
