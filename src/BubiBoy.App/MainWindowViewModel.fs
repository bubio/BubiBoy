namespace BubiBoy.App

open System
open System.ComponentModel
open System.Windows.Input

type RelayCommand(execute: unit -> unit, canExecute: unit -> bool) =
    let canExecuteChanged = Event<EventHandler, EventArgs>()

    new(execute: unit -> unit) = RelayCommand(execute, fun () -> true)

    member _.RaiseCanExecuteChanged() =
        canExecuteChanged.Trigger(null, EventArgs.Empty)

    interface ICommand with
        member _.CanExecute(_parameter: obj) = canExecute ()

        member _.Execute(_parameter: obj) = execute ()

        [<CLIEvent>]
        member _.CanExecuteChanged = canExecuteChanged.Publish

type MainWindowViewModel
    (
        initialScale: int,
        initialFloating: bool,
        initialVolumePercent: int,
        openRom: unit -> unit,
        toggleRunPause: unit -> unit,
        reset: unit -> unit,
        clearRecent: unit -> unit
    ) =
    let propertyChanged = Event<PropertyChangedEventHandler, PropertyChangedEventArgs>()

    let mutable romDetails =
        "Choose a .gb or .gbc file to inspect its cartridge header."

    let mutable debugDetails = "CPU debug run is available after loading a ROM."
    let mutable isRunning = false
    let mutable hasSession = false
    let mutable hasLoadedRom = false
    let mutable romDisplayName: string option = None
    let mutable selectedScale = initialScale
    let mutable isFloating = initialFloating
    let mutable volumePercent = initialVolumePercent
    let openRomCommand = RelayCommand(openRom)
    let runPauseCommand = RelayCommand(toggleRunPause, fun () -> hasSession)
    let resetCommand = RelayCommand(reset, fun () -> hasLoadedRom)
    let clearRecentCommand = RelayCommand(clearRecent)

    let notify propertyName =
        propertyChanged.Trigger(null, PropertyChangedEventArgs(propertyName))

    let setValue (storage: byref<'T>) (value: 'T) propertyName =
        if not (Object.Equals(storage, value)) then
            storage <- value
            notify propertyName
            true
        else
            false

    member _.RomDetails
        with get () = romDetails
        and set value = setValue &romDetails value "RomDetails" |> ignore

    member _.DebugDetails
        with get () = debugDetails
        and set value = setValue &debugDetails value "DebugDetails" |> ignore

    member this.IsRunning
        with get () = isRunning
        and set value =
            if setValue &isRunning value "IsRunning" then
                notify "RunPauseHeader"

    member _.HasSession
        with get () = hasSession
        and set value =
            if setValue &hasSession value "HasSession" then
                runPauseCommand.RaiseCanExecuteChanged()

    member _.HasLoadedRom
        with get () = hasLoadedRom
        and set value =
            if setValue &hasLoadedRom value "HasLoadedRom" then
                resetCommand.RaiseCanExecuteChanged()

    member _.RomDisplayName
        with get () = romDisplayName
        and set value = setValue &romDisplayName value "RomDisplayName" |> ignore

    member _.SelectedScale
        with get () = selectedScale
        and set value = setValue &selectedScale value "SelectedScale" |> ignore

    member _.IsFloating
        with get () = isFloating
        and set value = setValue &isFloating value "IsFloating" |> ignore

    member _.VolumePercent
        with get () = volumePercent
        and set value = setValue &volumePercent value "VolumePercent" |> ignore

    member this.RunPauseHeader = if this.IsRunning then "Pause" else "Run"

    member _.OpenRomCommand = openRomCommand :> ICommand

    member _.RunPauseCommand = runPauseCommand :> ICommand

    member _.ResetCommand = resetCommand :> ICommand

    member _.ClearRecentCommand = clearRecentCommand :> ICommand

    member _.UpdateSessionState(nextHasSession: bool, nextHasLoadedRom: bool) =
        let changedSession =
            if setValue &hasSession nextHasSession "HasSession" then
                runPauseCommand.RaiseCanExecuteChanged()
                true
            else
                false

        let changedRom =
            if setValue &hasLoadedRom nextHasLoadedRom "HasLoadedRom" then
                resetCommand.RaiseCanExecuteChanged()
                true
            else
                false

        (changedSession || changedRom) |> ignore

    [<CLIEvent>]
    member _.PropertyChanged = propertyChanged.Publish

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member _.PropertyChanged = propertyChanged.Publish
