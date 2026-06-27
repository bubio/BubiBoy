namespace BubiBoy.App

open System

/// Presents transient application messages and retains the latest status.
type AppNotificationCenter(toast: AppChrome.Toast, isFloating: unit -> bool) =
    let mutable lastStatus: string option = None
    let defaultDuration = TimeSpan.FromSeconds 3.0

    do
        toast.Timer.Tick.Add(fun _ ->
            toast.Timer.Stop()
            toast.Host.IsVisible <- false)

    /// Gets the latest status used when composing ROM load messages.
    member _.LastStatus = lastStatus

    /// Replaces the retained status without presenting a toast.
    member _.SetLastStatus(status: string option) = lastStatus <- status

    /// Presents a transient message when window chrome is visible.
    member _.Show(message: string, duration: TimeSpan) =
        if isFloating () then
            lastStatus <- Some message
        else
            toast.Text.Text <- message
            toast.Host.IsVisible <- true
            toast.Timer.Stop()
            toast.Timer.Interval <- duration
            toast.Timer.Start()

    /// Presents a transient message using the standard application duration.
    member this.Show(message: string) = this.Show(message, defaultDuration)
