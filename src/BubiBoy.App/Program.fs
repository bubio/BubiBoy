namespace BubiBoy.App

open System
open Avalonia

module Program =
    [<EntryPoint>]
    [<STAThread>]
    let main argv =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(argv)
