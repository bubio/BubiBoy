namespace ControllerInput

open System
open System.Collections.Generic
open System.Runtime.InteropServices

module private ObjC =
    [<Literal>]
    let private ObjCLibrary = "/usr/lib/libobjc.A.dylib"

    [<DllImport(ObjCLibrary, EntryPoint = "objc_getClass")>]
    extern IntPtr getClass(string name)

    [<DllImport(ObjCLibrary, EntryPoint = "sel_registerName")>]
    extern IntPtr sel(string name)

    [<DllImport(ObjCLibrary, EntryPoint = "objc_autoreleasePoolPush")>]
    extern IntPtr autoreleasePoolPush()

    [<DllImport(ObjCLibrary, EntryPoint = "objc_autoreleasePoolPop")>]
    extern void autoreleasePoolPop(IntPtr pool)

    [<DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")>]
    extern IntPtr sendIntPtr(IntPtr receiver, IntPtr selector)

    [<DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")>]
    extern IntPtr sendIntPtrWithUIntPtr(IntPtr receiver, IntPtr selector, UIntPtr value)

    [<DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")>]
    extern UIntPtr sendUIntPtr(IntPtr receiver, IntPtr selector)

    [<DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")>]
    extern byte sendBool(IntPtr receiver, IntPtr selector)

    [<DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")>]
    extern byte sendBoolWithIntPtr(IntPtr receiver, IntPtr selector, IntPtr value)

type MacOSGamepadHost private (frameworkHandle: IntPtr, controllerClass: IntPtr) =
    [<Literal>]
    static let gameControllerFramework =
        "/System/Library/Frameworks/GameController.framework/GameController"

    static let controllersSel = ObjC.sel "controllers"
    static let countSel = ObjC.sel "count"
    static let objectAtIndexSel = ObjC.sel "objectAtIndex:"
    static let hashSel = ObjC.sel "hash"
    static let vendorNameSel = ObjC.sel "vendorName"
    static let descriptionSel = ObjC.sel "description"
    static let respondsToSelectorSel = ObjC.sel "respondsToSelector:"
    static let utf8StringSel = ObjC.sel "UTF8String"
    static let extendedGamepadSel = ObjC.sel "extendedGamepad"
    static let gamepadSel = ObjC.sel "gamepad"
    static let dpadSel = ObjC.sel "dpad"
    static let leftThumbstickSel = ObjC.sel "leftThumbstick"
    static let upSel = ObjC.sel "up"
    static let downSel = ObjC.sel "down"
    static let leftSel = ObjC.sel "left"
    static let rightSel = ObjC.sel "right"
    static let buttonASel = ObjC.sel "buttonA"
    static let buttonBSel = ObjC.sel "buttonB"
    static let buttonXSel = ObjC.sel "buttonX"
    static let buttonYSel = ObjC.sel "buttonY"
    static let buttonMenuSel = ObjC.sel "buttonMenu"
    static let buttonOptionsSel = ObjC.sel "buttonOptions"
    static let leftShoulderSel = ObjC.sel "leftShoulder"
    static let rightShoulderSel = ObjC.sel "rightShoulder"
    static let leftTriggerSel = ObjC.sel "leftTrigger"
    static let rightTriggerSel = ObjC.sel "rightTrigger"
    static let isPressedSel = ObjC.sel "isPressed"

    let mutable disposed = false

    let sendObject receiver selector =
        if receiver = IntPtr.Zero then
            IntPtr.Zero
        elif ObjC.sendBoolWithIntPtr(receiver, respondsToSelectorSel, selector) = 0uy then
            IntPtr.Zero
        else
            ObjC.sendIntPtr(receiver, selector)

    let readString nsString =
        if nsString = IntPtr.Zero then
            None
        else
            let utf8 = ObjC.sendIntPtr(nsString, utf8StringSel)

            if utf8 = IntPtr.Zero then
                None
            else
                match Marshal.PtrToStringUTF8 utf8 with
                | value when String.IsNullOrWhiteSpace value -> None
                | value -> Some(value.Trim())

    let readControllerName index controller =
        readString (sendObject controller vendorNameSel)
        |> Option.orElseWith (fun () -> readString (sendObject controller descriptionSel))
        |> Option.defaultValue $"Controller {index + 1}"

    let isPressed button =
        button <> IntPtr.Zero && ObjC.sendBool(button, isPressedSel) <> 0uy

    let addButton (pressed: HashSet<GamepadControl>) control button =
        if isPressed button then
            pressed.Add control |> ignore

    let addDirection (pressed: HashSet<GamepadControl>) dpad up down left right =
        addButton pressed up (sendObject dpad upSel)
        addButton pressed down (sendObject dpad downSel)
        addButton pressed left (sendObject dpad leftSel)
        addButton pressed right (sendObject dpad rightSel)

    let readProfileControls profile =
        let pressed = HashSet<GamepadControl>()

        addDirection
            pressed
            (sendObject profile dpadSel)
            GamepadControl.DPadUp
            GamepadControl.DPadDown
            GamepadControl.DPadLeft
            GamepadControl.DPadRight

        addDirection
            pressed
            (sendObject profile leftThumbstickSel)
            GamepadControl.LeftStickUp
            GamepadControl.LeftStickDown
            GamepadControl.LeftStickLeft
            GamepadControl.LeftStickRight

        addButton pressed GamepadControl.South (sendObject profile buttonASel)
        addButton pressed GamepadControl.East (sendObject profile buttonBSel)
        addButton pressed GamepadControl.West (sendObject profile buttonXSel)
        addButton pressed GamepadControl.North (sendObject profile buttonYSel)
        addButton pressed GamepadControl.Start (sendObject profile buttonMenuSel)
        addButton pressed GamepadControl.Select (sendObject profile buttonOptionsSel)
        addButton pressed GamepadControl.LeftShoulder (sendObject profile leftShoulderSel)
        addButton pressed GamepadControl.RightShoulder (sendObject profile rightShoulderSel)
        addButton pressed GamepadControl.LeftTrigger (sendObject profile leftTriggerSel)
        addButton pressed GamepadControl.RightTrigger (sendObject profile rightTriggerSel)
        pressed

    let readController index controller =
        let profile =
            match sendObject controller extendedGamepadSel with
            | value when value <> IntPtr.Zero -> value
            | _ -> sendObject controller gamepadSel

        if profile = IntPtr.Zero then
            None
        else
            let hash = ObjC.sendUIntPtr(controller, hashSel).ToUInt64()
            let id = GamepadId.create $"macos:{hash:X}"
            let name = readControllerName index controller
            let pressed = readProfileControls profile
            Some(GamepadSnapshot.create id name pressed)

    member _.PollControllers() =
        if disposed then
            Array.Empty<GamepadSnapshot>() :> IReadOnlyList<GamepadSnapshot>
        else
            let pool = ObjC.autoreleasePoolPush()

            try
                let controllers = ObjC.sendIntPtr(controllerClass, controllersSel)

                if controllers = IntPtr.Zero then
                    Array.Empty<GamepadSnapshot>() :> IReadOnlyList<GamepadSnapshot>
                else
                    let count = int (ObjC.sendUIntPtr(controllers, countSel).ToUInt64())
                    let snapshots = ResizeArray<GamepadSnapshot>()

                    for index in 0 .. count - 1 do
                        let controller =
                            ObjC.sendIntPtrWithUIntPtr(controllers, objectAtIndexSel, UIntPtr(uint64 index))

                        match readController index controller with
                        | Some snapshot -> snapshots.Add snapshot
                        | None -> ()

                    snapshots :> IReadOnlyList<GamepadSnapshot>
            finally
                ObjC.autoreleasePoolPop pool

    static member TryCreate() =
        if not (RuntimeInformation.IsOSPlatform OSPlatform.OSX) then
            Error "GameController.framework is available only on macOS."
        else
            try
                let frameworkHandle = NativeLibrary.Load gameControllerFramework
                let controllerClass = ObjC.getClass "GCController"

                if controllerClass = IntPtr.Zero then
                    NativeLibrary.Free frameworkHandle
                    Error "GCController class is not available."
                else
                    Ok(new MacOSGamepadHost(frameworkHandle, controllerClass) :> GamepadHost)
            with ex ->
                Error ex.Message

    interface GamepadHost with
        member this.Poll() = this.PollControllers()

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                if frameworkHandle <> IntPtr.Zero then
                    NativeLibrary.Free frameworkHandle
