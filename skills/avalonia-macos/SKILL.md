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

`App.fs` owns `Application.Name`, theme setup, and the application menu:

```fsharp
type App() =
    inherit Application()

    override this.Initialize() =
        this.Name <- "My Application"
        this.Styles.Add(FluentTheme())

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
            let mainWindow = MainWindow()
            desktop.MainWindow <- mainWindow

            let appMenu = NativeMenu()
            let aboutItem = NativeMenuItem("About My Application...")
            aboutItem.Click.Add(fun _ -> mainWindow.ShowAbout())
            appMenu.Items.Add aboutItem |> ignore
            NativeMenu.SetMenu(this, appMenu)
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

`CFBundleName` is used for the menu bar and Quit item and should fit macOS's short-name expectations. Use `CFBundleDisplayName` for the Dock/Finder display name when the full name is longer.

For development app-bundle behavior, the docs recommend an output layout like:

```xml
<OutputPath>bin\$(Configuration)\$(Platform)\MyApp.app/Contents/MacOS</OutputPath>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
<UseAppHost>true</UseAppHost>
```

Then place `Info.plist` in the `.app/Contents` directory.

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
