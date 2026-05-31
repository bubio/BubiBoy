---
name: avalonia-macos
description: Use when implementing, reviewing, or fixing Avalonia desktop behavior specific to macOS, including application identity, Info.plist, native menu bar, About menu item, dock menu, keyboard gestures, app bundle layout, and keeping Application/Window/menu code separated in a maintainable F# Avalonia app.
---

# Avalonia macOS Integration

Use this skill for Avalonia desktop macOS issues before editing app startup, menus, About windows, app naming, bundle metadata, or macOS-only shortcuts.

Primary source: https://docs.avaloniaui.net/docs/platform-specific-guides/macos

## Required Checks

1. Verify the app is using Avalonia's default desktop backend (`netX.0`), not a macOS-specific TFM, unless the app needs full macOS APIs.
2. Check both development identity and bundled identity:
   - Development/unbundled: `Application.Name`.
   - Bundled `.app`: `Info.plist` values, especially `CFBundleName` and `CFBundleDisplayName`.
3. Do not add the app-name menu as a window menu on macOS.
4. Define the macOS application menu on the `Application`, and define File/Edit/View/etc. menus on the `Window`.
5. Do not add Quit manually for the normal case. Avalonia appends standard application-menu items, including Quit, after the custom application menu items.
6. The About item is not magic. Create a `NativeMenuItem` yourself, place it first in the application menu, and wire its `Click` event to show the app's About UI.
7. Window menu items must have a `Click` handler or a `Command`; otherwise they may appear disabled.
8. Use `Meta` for Command-key gestures (`Meta+O`, `Meta+Comma`, etc.).
9. If the first item still says `About Avalonia`, the app is still using Avalonia's default application menu. Define a `NativeMenu` on the `Application` early enough to replace the default; do not try to fix this from the window menu.
10. Remember that `CFBundleName` is the short app name macOS uses for the bold app menu and generated Quit item when bundled. Keep it at or under 15 characters; use `CFBundleDisplayName` for longer Finder/Dock names.
11. A framework-dependent `.app` launched from Finder may not inherit shell `DOTNET_ROOT`, even when `dotnet` works in Terminal. For double-clickable release bundles, publish self-contained for the target RID.
12. After self-contained publish, ad-hoc sign the `.app` (`codesign --force --deep --sign - MyApp.app`) for local LaunchServices execution. Validate with `open -W MyApp.app`; directly running `Contents/MacOS/MyApp` can take a different AppKit registration path.

## Code-First F# Pattern

Keep the entry point, application startup, windows, and dialogs in separate files. A useful compile order is:

```xml
<Compile Include="AboutWindow.fs" />
<Compile Include="MainWindow.fs" />
<Compile Include="App.fs" />
<Compile Include="Program.fs" />
```

`Program.fs` should stay small:

```fsharp
namespace MyApp

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
```

`App.fs` owns `Application.Name`, theme setup, and the application menu. In code-first apps, attach the application `NativeMenu` during `Initialize` so Avalonia does not keep the default menu with `About Avalonia`:

```fsharp
type App() =
    inherit Application()

    override this.Initialize() =
        this.Name <- "My Application"
        this.Styles.Add(FluentTheme())

        let appMenu = NativeMenu()
        let aboutItem = NativeMenuItem("About My Application...")
        aboutItem.Click.Add(fun _ ->
            match this.ApplicationLifetime with
            | :? IClassicDesktopStyleApplicationLifetime as desktop ->
                match desktop.MainWindow with
                | :? MainWindow as mainWindow -> mainWindow.ShowAbout()
                | _ -> ()
            | _ -> ())
        appMenu.Items.Add aboutItem |> ignore
        NativeMenu.SetMenu(this, appMenu)

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let mainWindow = MainWindow()
            desktop.MainWindow <- mainWindow
        | _ -> ()

        base.OnFrameworkInitializationCompleted()
```

`MainWindow.fs` owns window menus:

```fsharp
let nativeMenu = NativeMenu()
let fileMenu = NativeMenuItem("File")
let fileSubmenu = NativeMenu()
fileSubmenu.Items.Add(openItem) |> ignore
fileMenu.Menu <- fileSubmenu
nativeMenu.Items.Add fileMenu |> ignore
NativeMenu.SetMenu(this, nativeMenu)
```

## Info.plist Guidance

If running as a bundled `.app`, create a valid `Info.plist` in `Contents`. Keep bundle names consistent with `Application.Name`.

Minimum identity keys to check:

```xml
<key>CFBundleName</key>
<string>My App</string>
<key>CFBundleDisplayName</key>
<string>My Application</string>
<key>CFBundleIdentifier</key>
<string>com.example.myapp</string>
```

`CFBundleName` is used for the menu bar and Quit item and should fit macOS's 15-character short-name expectation. Use `CFBundleDisplayName` for the Dock/Finder display name when the full name is longer. `Window.Title` is independent and does not fix the application menu name.

For development app-bundle behavior, the docs recommend an output layout like:

```xml
<OutputPath>bin\$(Configuration)\$(Platform)\MyApp.app/Contents/MacOS</OutputPath>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
<UseAppHost>true</UseAppHost>
```

Then place `Info.plist` in the `.app/Contents` directory.

Avalonia's default desktop backend does not require `netX.0-macos` for normal windowing, menus, file dialogs, dock menus, clipboard, drag-and-drop, rendering, or accessibility. Use a macOS-specific TFM only when the app needs the full Apple API surface or native `NSView` hosting code; doing so requires building on macOS.

For a double-clickable local release bundle, prefer self-contained publish and then sign:

```bash
dotnet publish src/MyApp/MyApp.fsproj -c Release -r osx-arm64 --self-contained true /p:PublishDir=bin/Release/osx-arm64/MyApp.app/Contents/MacOS/
codesign --force --deep --sign - bin/Release/osx-arm64/MyApp.app
open -W bin/Release/osx-arm64/MyApp.app
```

## MVVM Direction

Do not leave all app logic in `Program.fs`. For F# code-first Avalonia, the first maintainability step is file-level responsibility separation:

- `Program.fs`: entry point only.
- `App.fs`: application lifetime, theme, Application-level native menu, dock menu.
- `MainWindow.fs`: window UI, window-level menus, input wiring.
- `AboutWindow.fs`: About dialog UI.
- Later: move state and commands into view-model records/classes/modules when bindings become useful.

When introducing MVVM, keep emulator core state and deterministic stepping in core/domain modules. ViewModels should expose UI state and commands; they should not own emulator hardware behavior.

## Validation

Run a focused app build after menu/startup changes:

```bash
dotnet build src/BubiBoy.App/BubiBoy.App.fsproj --no-restore --disable-build-servers -v:minimal /p:UseSharedCompilation=false /p:BuildInParallel=false
```

If Avalonia BuildServices fails by writing outside the sandbox, rerun only for local validation with:

```bash
dotnet build src/BubiBoy.App/BubiBoy.App.fsproj --no-restore --disable-build-servers -v:minimal /p:UseSharedCompilation=false /p:BuildInParallel=false /p:UsedAvaloniaProducts=
```

Do not treat the second command as a production build configuration change; it is a local validation workaround.
