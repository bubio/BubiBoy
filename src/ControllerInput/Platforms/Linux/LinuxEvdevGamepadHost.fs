namespace ControllerInput

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices

module internal LinuxEvdev =
    [<Literal>]
    let EV_SYN = 0us

    [<Literal>]
    let EV_KEY = 0x01us

    [<Literal>]
    let EV_ABS = 0x03us

    [<Literal>]
    let SYN_REPORT = 0us

    [<Literal>]
    let BTN_GAMEPAD = 0x130us

    [<Literal>]
    let BTN_SOUTH = 0x130us

    [<Literal>]
    let BTN_EAST = 0x131us

    [<Literal>]
    let BTN_NORTH = 0x133us

    [<Literal>]
    let BTN_WEST = 0x134us

    [<Literal>]
    let BTN_TL = 0x136us

    [<Literal>]
    let BTN_TR = 0x137us

    [<Literal>]
    let BTN_TL2 = 0x138us

    [<Literal>]
    let BTN_TR2 = 0x139us

    [<Literal>]
    let BTN_SELECT = 0x13aus

    [<Literal>]
    let BTN_START = 0x13bus

    [<Literal>]
    let BTN_DPAD_UP = 0x220us

    [<Literal>]
    let BTN_DPAD_DOWN = 0x221us

    [<Literal>]
    let BTN_DPAD_LEFT = 0x222us

    [<Literal>]
    let BTN_DPAD_RIGHT = 0x223us

    [<Literal>]
    let ABS_X = 0x00us

    [<Literal>]
    let ABS_Y = 0x01us

    [<Literal>]
    let ABS_Z = 0x02us

    [<Literal>]
    let ABS_RX = 0x03us

    [<Literal>]
    let ABS_RY = 0x04us

    [<Literal>]
    let ABS_RZ = 0x05us

    [<Literal>]
    let ABS_HAT0X = 0x10us

    [<Literal>]
    let ABS_HAT0Y = 0x11us

    [<Literal>]
    let ABS_HAT2X = 0x14us

    [<Literal>]
    let ABS_HAT2Y = 0x15us

    [<Struct>]
    type AbsInfo =
        val Value: int
        val Minimum: int
        val Maximum: int
        val Fuzz: int
        val Flat: int
        val Resolution: int

        new(value, minimum, maximum, fuzz, flat, resolution) =
            { Value = value
              Minimum = minimum
              Maximum = maximum
              Fuzz = fuzz
              Flat = flat
              Resolution = resolution }

    let controlForKey code =
        match code with
        | BTN_SOUTH -> Some GamepadControl.South
        | BTN_EAST -> Some GamepadControl.East
        | BTN_WEST -> Some GamepadControl.West
        | BTN_NORTH -> Some GamepadControl.North
        | BTN_START -> Some GamepadControl.Start
        | BTN_SELECT -> Some GamepadControl.Select
        | BTN_TL -> Some GamepadControl.LeftShoulder
        | BTN_TR -> Some GamepadControl.RightShoulder
        | BTN_TL2 -> Some GamepadControl.LeftTrigger
        | BTN_TR2 -> Some GamepadControl.RightTrigger
        | BTN_DPAD_UP -> Some GamepadControl.DPadUp
        | BTN_DPAD_DOWN -> Some GamepadControl.DPadDown
        | BTN_DPAD_LEFT -> Some GamepadControl.DPadLeft
        | BTN_DPAD_RIGHT -> Some GamepadControl.DPadRight
        | _ -> None

    let controlsForHatAxis code value =
        match code, value with
        | ABS_HAT0X, v when v < 0 -> [ GamepadControl.DPadLeft ]
        | ABS_HAT0X, v when v > 0 -> [ GamepadControl.DPadRight ]
        | ABS_HAT0Y, v when v < 0 -> [ GamepadControl.DPadUp ]
        | ABS_HAT0Y, v when v > 0 -> [ GamepadControl.DPadDown ]
        | _ -> []

    let private thresholdForAxis (info: AbsInfo) =
        let range = max 1 (info.Maximum - info.Minimum)
        let center = info.Minimum + (range / 2)
        let calculated = max info.Flat (range / 4)
        center, calculated

    let controlsForStickAxis code (info: AbsInfo) =
        let center, threshold = thresholdForAxis info
        let delta = info.Value - center

        match code, delta with
        | ABS_X, v when v <= -threshold -> [ GamepadControl.LeftStickLeft ]
        | ABS_X, v when v >= threshold -> [ GamepadControl.LeftStickRight ]
        | ABS_Y, v when v <= -threshold -> [ GamepadControl.LeftStickUp ]
        | ABS_Y, v when v >= threshold -> [ GamepadControl.LeftStickDown ]
        | _ -> []

    let controlsForTriggerAxis code (info: AbsInfo) =
        let range = max 1 (info.Maximum - info.Minimum)
        let threshold = info.Minimum + (range / 2)

        match code, info.Value with
        | ABS_Z, v when v >= threshold -> [ GamepadControl.LeftTrigger ]
        | ABS_RZ, v when v >= threshold -> [ GamepadControl.RightTrigger ]
        | ABS_HAT2Y, v when v >= threshold -> [ GamepadControl.LeftTrigger ]
        | ABS_HAT2X, v when v >= threshold -> [ GamepadControl.RightTrigger ]
        | _ -> []

module private LinuxNative =
    [<Literal>]
    let O_RDONLY = 0

    [<Literal>]
    let O_NONBLOCK = 0x800

    [<Literal>]
    let O_CLOEXEC = 0x80000

    [<Literal>]
    let EAGAIN = 11

    [<Literal>]
    let ENODEV = 19

    [<Literal>]
    let inputEventCodeMax = 0x2ff

    [<Literal>]
    let absCodeMax = 0x3f

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type InputAbsInfo =
        val mutable value: int
        val mutable minimum: int
        val mutable maximum: int
        val mutable fuzz: int
        val mutable flat: int
        val mutable resolution: int

    [<DllImport("libc", SetLastError = true, EntryPoint = "open")>]
    extern int openPath(string pathname, int flags)

    [<DllImport("libc", SetLastError = true)>]
    extern nativeint read(int fd, [<Out>] byte[] buffer, unativeint count)

    [<DllImport("libc", SetLastError = true)>]
    extern int close(int fd)

    [<DllImport("libc", SetLastError = true, EntryPoint = "ioctl")>]
    extern int ioctlBytes(int fd, unativeint request, [<Out>] byte[] data)

    [<DllImport("libc", SetLastError = true, EntryPoint = "ioctl")>]
    extern int ioctlAbsInfo(int fd, unativeint request, InputAbsInfo& data)

    let private iocRead = 2UL
    let private iocNrBits = 8
    let private iocTypeBits = 8
    let private iocSizeBits = 14
    let private iocNrShift = 0
    let private iocTypeShift = iocNrShift + iocNrBits
    let private iocSizeShift = iocTypeShift + iocTypeBits
    let private iocDirShift = iocSizeShift + iocSizeBits
    let private eventIoctlType = uint64 (int 'E')

    let private ioctlRequest dir number size =
        let value =
            (dir <<< iocDirShift)
            ||| (eventIoctlType <<< iocTypeShift)
            ||| (uint64 number <<< iocNrShift)
            ||| (uint64 size <<< iocSizeShift)

        unativeint value

    let eviocgBit eventType length =
        ioctlRequest iocRead (0x20 + int eventType) length

    let eviocgKey length =
        ioctlRequest iocRead 0x18 length

    let eviocgName length =
        ioctlRequest iocRead 0x06 length

    let eviocgAbs absCode =
        ioctlRequest iocRead (0x40 + int absCode) (Marshal.SizeOf<InputAbsInfo>())

module private LinuxBitSet =
    let private byteIndex bit = int bit / 8
    let private bitMask bit = 1uy <<< (int bit % 8)

    let contains bit (data: byte[]) =
        let index = byteIndex bit
        index < data.Length && ((data[index] &&& bitMask bit) <> 0uy)

    let set bit (data: byte[]) =
        let index = byteIndex bit

        if index < data.Length then
            data[index] <- data[index] ||| bitMask bit

type private LinuxEvdevDevice(fd: int, path: string, idPath: string, name: string, supportedKeys: byte[], supportedAbs: byte[]) =
    let keyState = Array.zeroCreate<byte> supportedKeys.Length
    let absState = Dictionary<uint16, LinuxEvdev.AbsInfo>()
    let eventSize = (IntPtr.Size * 2) + 8
    let eventBuffer = Array.zeroCreate<byte> eventSize
    let mutable disposed = false
    let mutable usable = true

    let readAbsInfo code =
        let mutable info = Unchecked.defaultof<LinuxNative.InputAbsInfo>

        if LinuxNative.ioctlAbsInfo(fd, LinuxNative.eviocgAbs code, &info) = 0 then
            Some(LinuxEvdev.AbsInfo(info.value, info.minimum, info.maximum, info.fuzz, info.flat, info.resolution))
        else
            None

    let readInitialState () =
        LinuxNative.ioctlBytes(fd, LinuxNative.eviocgKey keyState.Length, keyState) |> ignore

        for code in 0us .. uint16 LinuxNative.absCodeMax do
            if LinuxBitSet.contains code supportedAbs then
                match readAbsInfo code with
                | Some info -> absState[code] <- info
                | None -> ()

    let updateEvent eventType code value =
        if eventType = LinuxEvdev.EV_KEY then
            if LinuxBitSet.contains code supportedKeys then
                if value <> 0 then
                    LinuxBitSet.set code keyState
                else
                    let index = int code / 8
                    let mask = 1uy <<< (int code % 8)
                    keyState[index] <- keyState[index] &&& ~~~mask
        elif eventType = LinuxEvdev.EV_ABS then
            match readAbsInfo code with
            | Some info -> absState[code] <- info
            | None ->
                let current =
                    match absState.TryGetValue code with
                    | true, info -> info
                    | false, _ -> LinuxEvdev.AbsInfo(value, -32768, 32767, 0, 0, 0)

                absState[code] <- LinuxEvdev.AbsInfo(value, current.Minimum, current.Maximum, current.Fuzz, current.Flat, current.Resolution)

    do readInitialState ()

    member _.Path = path
    member _.IdPath = idPath
    member _.Name = name
    member _.Usable = usable && not disposed

    member _.DrainEvents() =
        if usable && not disposed then
            let mutable keepReading = true

            while keepReading do
                let count = LinuxNative.read(fd, eventBuffer, unativeint eventBuffer.Length)

                if count = nativeint eventBuffer.Length then
                    let eventType = BitConverter.ToUInt16(eventBuffer, IntPtr.Size * 2)
                    let code = BitConverter.ToUInt16(eventBuffer, (IntPtr.Size * 2) + 2)
                    let value = BitConverter.ToInt32(eventBuffer, (IntPtr.Size * 2) + 4)

                    if eventType <> LinuxEvdev.EV_SYN || code <> LinuxEvdev.SYN_REPORT then
                        updateEvent eventType code value
                elif count = 0n then
                    keepReading <- false
                else
                    let errno = Marshal.GetLastPInvokeError()

                    if errno = LinuxNative.EAGAIN then
                        keepReading <- false
                    elif errno = LinuxNative.ENODEV then
                        usable <- false
                        keepReading <- false
                    else
                        keepReading <- false

    member _.Snapshot() =
        let pressed = HashSet<GamepadControl>()

        for code in 0us .. uint16 LinuxNative.inputEventCodeMax do
            if LinuxBitSet.contains code keyState then
                match LinuxEvdev.controlForKey code with
                | Some control -> pressed.Add control |> ignore
                | None -> ()

        for KeyValue(code, info) in absState do
            for control in LinuxEvdev.controlsForHatAxis code info.Value do
                pressed.Add control |> ignore

            for control in LinuxEvdev.controlsForStickAxis code info do
                pressed.Add control |> ignore

            for control in LinuxEvdev.controlsForTriggerAxis code info do
                pressed.Add control |> ignore

        GamepadSnapshot.create (GamepadId.create idPath) name pressed

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                if fd >= 0 then
                    LinuxNative.close fd |> ignore

type LinuxEvdevGamepadHost private (initialDevices: LinuxEvdevDevice list) =
    let mutable disposed = false
    let devices = ResizeArray<LinuxEvdevDevice>(initialDevices)
    let knownDeviceIds = HashSet<string>(initialDevices |> Seq.map (fun device -> device.IdPath))
    let mutable pollsSinceScan = 0

    static let readName fd (fallbackName: string) =
        let buffer = Array.zeroCreate<byte> 256

        if LinuxNative.ioctlBytes(fd, LinuxNative.eviocgName buffer.Length, buffer) >= 0 then
            let length =
                buffer
                |> Array.tryFindIndex ((=) 0uy)
                |> Option.defaultValue buffer.Length

            let name = Text.Encoding.UTF8.GetString(buffer, 0, length)

            if String.IsNullOrWhiteSpace name then
                fallbackName
            else
                name.Trim()
        else
            fallbackName

    static let readCapabilities fd eventType maxCode =
        let length = (maxCode + 8) / 8
        let data = Array.zeroCreate<byte> length

        if LinuxNative.ioctlBytes(fd, LinuxNative.eviocgBit eventType data.Length, data) >= 0 then
            Some data
        else
            None

    static let hasAnyRelevantControl keys abs =
        [ LinuxEvdev.BTN_SOUTH
          LinuxEvdev.BTN_EAST
          LinuxEvdev.BTN_WEST
          LinuxEvdev.BTN_NORTH
          LinuxEvdev.BTN_START
          LinuxEvdev.BTN_SELECT
          LinuxEvdev.BTN_DPAD_UP
          LinuxEvdev.BTN_DPAD_DOWN
          LinuxEvdev.BTN_DPAD_LEFT
          LinuxEvdev.BTN_DPAD_RIGHT ]
        |> List.exists (fun code -> LinuxBitSet.contains code keys)
        || LinuxBitSet.contains LinuxEvdev.ABS_HAT0X abs
        || LinuxBitSet.contains LinuxEvdev.ABS_HAT0Y abs

    static let tryOpenDevice (path: string) (idPath: string) =
        let fd = LinuxNative.openPath(path, LinuxNative.O_RDONLY ||| LinuxNative.O_NONBLOCK ||| LinuxNative.O_CLOEXEC)

        if fd < 0 then
            None
        else
            match readCapabilities fd LinuxEvdev.EV_KEY LinuxNative.inputEventCodeMax,
                  readCapabilities fd LinuxEvdev.EV_ABS LinuxNative.absCodeMax with
            | Some keys, Some abs when LinuxBitSet.contains LinuxEvdev.BTN_GAMEPAD keys || hasAnyRelevantControl keys abs ->
                let fallbackName = Path.GetFileName path
                let name = readName fd fallbackName
                Some(new LinuxEvdevDevice(fd, path, idPath, name, keys, abs))
            | _ ->
                LinuxNative.close fd |> ignore
                None

    static let sortedFiles pattern directory =
        if Directory.Exists directory then
            Directory.GetFiles(directory, pattern) |> Array.sort
        else
            Array.empty

    static let candidatePaths () =
        let byId = sortedFiles "*-event-joystick" "/dev/input/by-id"

        if byId.Length > 0 then
            byId
        else
            sortedFiles "event*" "/dev/input"

    static member TryCreate() =
        if not (RuntimeInformation.IsOSPlatform OSPlatform.Linux) then
            Error "evdev gamepad input is available only on Linux."
        else
            let devices =
                candidatePaths ()
                |> Array.choose (fun path -> tryOpenDevice path path)
                |> Array.toList

            Ok(new LinuxEvdevGamepadHost(devices) :> GamepadHost)

    member private _.RescanDevices() =
        for path in candidatePaths () do
            if knownDeviceIds.Add path then
                match tryOpenDevice path path with
                | Some device -> devices.Add device
                | None -> ()

    member this.PollControllers() =
        if disposed then
            Array.Empty<GamepadSnapshot>() :> IReadOnlyList<GamepadSnapshot>
        else
            pollsSinceScan <- pollsSinceScan + 1

            if devices.Count = 0 || pollsSinceScan >= 60 then
                pollsSinceScan <- 0
                this.RescanDevices()

            let snapshots = ResizeArray<GamepadSnapshot>()
            let mutable index = 0

            while index < devices.Count do
                let device = devices[index]
                device.DrainEvents()

                if device.Usable then
                    snapshots.Add(device.Snapshot())
                    index <- index + 1
                else
                    (device :> IDisposable).Dispose()
                    knownDeviceIds.Remove device.IdPath |> ignore
                    devices.RemoveAt index

            snapshots :> IReadOnlyList<GamepadSnapshot>

    interface GamepadHost with
        member this.Poll() = this.PollControllers()

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                for device in devices do
                    (device :> IDisposable).Dispose()

                devices.Clear()
