namespace BubiBoy.App

open Avalonia.Input
open BubiBoy.Core

type InputStateController() =
    let gate = obj ()
    let mutable desiredKeyboardButtons: Set<Joypad.Button> = Set.empty
    let mutable desiredControllerButtons: Set<Joypad.Button> = Set.empty
    let mutable activeControllerId: ControllerInput.GamepadId option = None

    let hasPressedInput (controller: ControllerInput.GamepadSnapshot) =
        controller.Pressed.Count > 0

    let chooseController (controllers: ControllerInput.GamepadSnapshot list) activeId =
        let current =
            activeId
            |> Option.bind (fun id -> controllers |> List.tryFind (fun controller -> controller.Id = id))

        match current with
        | Some controller when hasPressedInput controller -> Some controller
        | Some controller ->
            controllers
            |> List.tryFind (fun candidate -> candidate.Id <> controller.Id && hasPressedInput candidate)
            |> Option.orElse (Some controller)
        | None ->
            controllers
            |> List.tryFind hasPressedInput
            |> Option.orElseWith (fun () -> controllers |> List.tryHead)

    member _.ResetKeyboard() =
        lock gate (fun () -> desiredKeyboardButtons <- Set.empty)

    member _.DisableController() =
        lock gate (fun () ->
            desiredControllerButtons <- Set.empty
            activeControllerId <- None)

    member _.UpdateKeyboardKey(mapping, key: Key, pressed) =
        match InputMapping.mapKey mapping key with
        | Some button ->
            // Only record intent here; the emulation thread reconciles it into the
            // session via ApplyInput. Recording the latest state (rather than queuing
            // edits) means a press immediately followed by a release can never be lost.
            lock gate (fun () ->
                desiredKeyboardButtons <-
                    if pressed then
                        desiredKeyboardButtons.Add button
                    else
                        desiredKeyboardButtons.Remove button)

            true
        | None -> false

    member _.PollController(host: ControllerInput.GamepadHost, mapping) =
        let controllers = host.Poll() |> Seq.toList
        let activeController = lock gate (fun () -> chooseController controllers activeControllerId)

        let controllerButtons =
            activeController
            |> Option.map (ControllerInputAdapter.joypadButtonsForSnapshot mapping)
            |> Option.defaultValue Set.empty

        lock gate (fun () ->
            desiredControllerButtons <- controllerButtons

            match activeControllerId, activeController with
            | None, Some controller ->
                activeControllerId <- Some controller.Id
                Some $"Controller connected: {controller.Name}"
            | Some _, None ->
                activeControllerId <- None
                Some "Controller disconnected."
            | Some previous, Some controller when previous <> controller.Id ->
                activeControllerId <- Some controller.Id
                Some $"Controller connected: {controller.Name}"
            | _ -> None)

    member _.ApplyInput(session: Emulator.Session) =
        // The authoritative set of currently-held buttons is tracked per input source.
        // The emulation thread reconciles the union into the live session at frame
        // boundaries, so one source releasing a button cannot clear another source's hold.
        let desired =
            lock gate (fun () -> Set.union desiredKeyboardButtons desiredControllerButtons)

        if desired = (Bus.joypad session.Bus).Pressed then
            session
        else
            // Bus.setButton only raises the joypad interrupt on a fresh press, so
            // re-applying an unchanged set is a no-op and held buttons never re-trigger.
            let bus =
                InputMapping.allJoypadButtons
                |> List.fold
                    (fun bus button ->
                        let want = Set.contains button desired
                        let have = Set.contains button (Bus.joypad bus).Pressed
                        if want = have then bus else Bus.setButton button want bus)
                    session.Bus

            { session with Bus = bus }
