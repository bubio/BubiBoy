namespace BubiBoy.App

open System
open Avalonia
open Avalonia.Threading

module Program =
    [<EntryPoint>]
    [<STAThread>]
    let main argv =
        Dispatcher.UIThread.UnhandledException.Add(fun args ->
            Console.Error.WriteLine $"Unhandled UI exception: {args.Exception}")

        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(argv)
