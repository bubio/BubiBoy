namespace BubiBoy.App

open System.Runtime.InteropServices
open Avalonia.Media

module AppFonts =
    let ui =
        if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
            FontFamily("SF Pro Text, Helvetica Neue, Helvetica, Arial, sans-serif")
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            FontFamily("Segoe UI, Noto Sans, Arial, sans-serif")
        else
            FontFamily("Noto Sans, DejaVu Sans, Ubuntu, Cantarell, Liberation Sans, Arial, sans-serif")

    let monospace =
        if RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
            FontFamily("Menlo, SF Mono, Consolas, monospace")
        elif RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
            FontFamily("Cascadia Mono, Consolas, Courier New, monospace")
        else
            FontFamily("JetBrains Mono, Cascadia Mono, DejaVu Sans Mono, Liberation Mono, Consolas, monospace")
