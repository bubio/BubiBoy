namespace ControllerInput

open System
open System.Collections.Generic
open System.Runtime.InteropServices

module private XInput =
    [<Literal>]
    let ERROR_SUCCESS = 0u

    [<Literal>]
    let ERROR_DEVICE_NOT_CONNECTED = 1167u

    [<Literal>]
    let XINPUT_GAMEPAD_DPAD_UP = 0x0001us

    [<Literal>]
    let XINPUT_GAMEPAD_DPAD_DOWN = 0x0002us

    [<Literal>]
    let XINPUT_GAMEPAD_DPAD_LEFT = 0x0004us

    [<Literal>]
    let XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008us

    [<Literal>]
    let XINPUT_GAMEPAD_START = 0x0010us

    [<Literal>]
    let XINPUT_GAMEPAD_BACK = 0x0020us

    [<Literal>]
    let XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100us

    [<Literal>]
    let XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200us

    [<Literal>]
    let XINPUT_GAMEPAD_A = 0x1000us

    [<Literal>]
    let XINPUT_GAMEPAD_B = 0x2000us

    [<Literal>]
    let XINPUT_GAMEPAD_X = 0x4000us

    [<Literal>]
    let XINPUT_GAMEPAD_Y = 0x8000us

    [<Literal>]
    let triggerThreshold = 30uy

    [<Literal>]
    let leftThumbDirectionThreshold = 16000s

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type XInputGamepad =
        val mutable wButtons: uint16
        val mutable bLeftTrigger: byte
        val mutable bRightTrigger: byte
        val mutable sThumbLX: int16
        val mutable sThumbLY: int16
        val mutable sThumbRX: int16
        val mutable sThumbRY: int16

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type XInputState =
        val mutable dwPacketNumber: uint32
        val mutable Gamepad: XInputGamepad

    [<UnmanagedFunctionPointer(CallingConvention.Winapi)>]
    type XInputGetState = delegate of uint32 * byref<XInputState> -> uint32

    let private hasFlag (buttons: uint16) flag = (buttons &&& flag) <> 0us

    let controlsForState (state: XInputState) =
        let pressed = HashSet<GamepadControl>()
        let buttons = state.Gamepad.wButtons

        let addButton flag control =
            if hasFlag buttons flag then
                pressed.Add control |> ignore

        addButton XINPUT_GAMEPAD_DPAD_UP GamepadControl.DPadUp
        addButton XINPUT_GAMEPAD_DPAD_DOWN GamepadControl.DPadDown
        addButton XINPUT_GAMEPAD_DPAD_LEFT GamepadControl.DPadLeft
        addButton XINPUT_GAMEPAD_DPAD_RIGHT GamepadControl.DPadRight
        addButton XINPUT_GAMEPAD_A GamepadControl.South
        addButton XINPUT_GAMEPAD_B GamepadControl.East
        addButton XINPUT_GAMEPAD_X GamepadControl.West
        addButton XINPUT_GAMEPAD_Y GamepadControl.North
        addButton XINPUT_GAMEPAD_START GamepadControl.Start
        addButton XINPUT_GAMEPAD_BACK GamepadControl.Select
        addButton XINPUT_GAMEPAD_LEFT_SHOULDER GamepadControl.LeftShoulder
        addButton XINPUT_GAMEPAD_RIGHT_SHOULDER GamepadControl.RightShoulder

        if state.Gamepad.bLeftTrigger >= triggerThreshold then
            pressed.Add GamepadControl.LeftTrigger |> ignore

        if state.Gamepad.bRightTrigger >= triggerThreshold then
            pressed.Add GamepadControl.RightTrigger |> ignore

        if state.Gamepad.sThumbLY >= leftThumbDirectionThreshold then
            pressed.Add GamepadControl.LeftStickUp |> ignore
        elif state.Gamepad.sThumbLY <= -leftThumbDirectionThreshold then
            pressed.Add GamepadControl.LeftStickDown |> ignore

        if state.Gamepad.sThumbLX >= leftThumbDirectionThreshold then
            pressed.Add GamepadControl.LeftStickRight |> ignore
        elif state.Gamepad.sThumbLX <= -leftThumbDirectionThreshold then
            pressed.Add GamepadControl.LeftStickLeft |> ignore

        pressed

type WindowsXInputGamepadHost private (libraryHandle: IntPtr, getState: XInput.XInputGetState) =
    [<Literal>]
    static let maxUserCount = 4u

    static let libraryNames =
        [| "xinput1_4.dll"
           "xinput9_1_0.dll"
           "xinput1_3.dll" |]

    let mutable disposed = false

    let readController userIndex =
        let mutable state = Unchecked.defaultof<XInput.XInputState>
        let result = getState.Invoke(userIndex, &state)

        if result = XInput.ERROR_SUCCESS then
            let id = GamepadId.create $"xinput:{userIndex}"
            let name = $"XInput Controller {userIndex + 1u}"
            let pressed = XInput.controlsForState state
            Some(GamepadSnapshot.create id name pressed)
        elif result = XInput.ERROR_DEVICE_NOT_CONNECTED then
            None
        else
            None

    member _.PollControllers() =
        if disposed then
            Array.Empty<GamepadSnapshot>() :> IReadOnlyList<GamepadSnapshot>
        else
            let snapshots = ResizeArray<GamepadSnapshot>()

            for userIndex in 0u .. (maxUserCount - 1u) do
                match readController userIndex with
                | Some snapshot -> snapshots.Add snapshot
                | None -> ()

            snapshots :> IReadOnlyList<GamepadSnapshot>

    static member TryCreate() =
        if not (RuntimeInformation.IsOSPlatform OSPlatform.Windows) then
            Error "XInput is available only on Windows."
        else
            let mutable libraryHandle = IntPtr.Zero
            let mutable loadedName = null

            libraryNames
            |> Array.exists (fun libraryName ->
                if NativeLibrary.TryLoad(libraryName, &libraryHandle) then
                    loadedName <- libraryName
                    true
                else
                    false)
            |> ignore

            if libraryHandle = IntPtr.Zero then
                Error "XInput library is not available."
            else
                let mutable getStateExport = IntPtr.Zero

                if NativeLibrary.TryGetExport(libraryHandle, "XInputGetState", &getStateExport) then
                    let getState =
                        Marshal.GetDelegateForFunctionPointer<XInput.XInputGetState> getStateExport

                    Ok(new WindowsXInputGamepadHost(libraryHandle, getState) :> GamepadHost)
                else
                    NativeLibrary.Free libraryHandle
                    Error $"{loadedName} does not export XInputGetState."

    interface GamepadHost with
        member this.Poll() = this.PollControllers()

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                if libraryHandle <> IntPtr.Zero then
                    NativeLibrary.Free libraryHandle
