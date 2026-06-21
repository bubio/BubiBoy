namespace BubiBoy.App

open System.IO
open BubiBoy.IO

/// Owns the mutable application settings used by the Avalonia shell.
type AppSettingsStore(settingsPath: string, initialSettings: AppSettings.Settings) =
    let mutable current = AppSettings.normalize initialSettings

    /// Gets the path used for settings persistence.
    member _.Path = settingsPath

    /// Gets the current normalized settings snapshot.
    member _.Current = current

    member private _.Replace(next: AppSettings.Settings) =
        current <- AppSettings.normalize next
        current

    /// Saves the current settings snapshot.
    member _.Save() =
        AppSettings.saveToPath settingsPath current

    /// Stores a recently loaded ROM path.
    member this.RememberRom(path: string) =
        this.Replace(AppSettings.rememberRom path current)

    /// Clears the recent ROM list.
    member this.ClearRecentRoms() =
        this.Replace({ current with RecentRoms = [] })

    /// Updates the configured application scale and returns the normalized value.
    member this.SetScale(scale: int) =
        let next = this.Replace(AppSettings.withScale scale current)
        next.Scale

    /// Enables or disables informational side panels in full-screen mode.
    member this.SetShowFullScreenInfo(enabled: bool) =
        let next = this.Replace(AppSettings.withShowFullScreenInfo enabled current)
        next.ShowFullScreenInfo

    /// Updates the image filter used by the game viewport.
    member this.SetVideoFilter(filter: AppSettings.VideoFilter) =
        let next = this.Replace(AppSettings.withVideoFilter filter current)
        next.VideoFilter

    /// Updates the configured output volume and returns the normalized value.
    member this.SetVolumePercent(percent: int) =
        let next = this.Replace(AppSettings.withVolumePercent percent current)
        next.VolumePercent

    /// Updates the boot ROM selection used for future ROM loads and resets.
    member this.SetBootRomSelection(selection: AppSettings.BootRomSelection) =
        let next = this.Replace(AppSettings.withBootRomSelection selection current)
        next.BootRomSelection

    /// Updates the opt-in state and last successful RetroAchievements username.
    member this.SetRetroAchievements(enabled: bool, username: string) =
        this.Replace(AppSettings.withRetroAchievements enabled username current)

    /// Updates keyboard and controller mappings atomically.
    member this.SetInputMappings(keyboardMapping: Map<string, string>, controllerMapping: Map<string, string>) =
        this.Replace(
            current
            |> AppSettings.withKeyboardMapping keyboardMapping
            |> AppSettings.withControllerMapping controllerMapping
        )

module AppSettingsStore =
    /// Result of loading settings, including a recoverable load error for the UI.
    type LoadResult =
        { Store: AppSettingsStore
          LoadError: string option }

    /// Loads settings from the default path, falling back to defaults on recoverable errors.
    let loadDefault () =
        let settingsPath = AppSettings.defaultPath ()

        let settings, loadError =
            if File.Exists settingsPath then
                match AppSettings.loadFromPath settingsPath with
                | Ok settings -> settings, None
                | Error message -> AppSettings.defaults, Some message
            else
                match AppSettings.legacyDefaultPath () with
                | Some legacyPath when File.Exists legacyPath ->
                    match AppSettings.loadFromPath legacyPath with
                    | Error message -> AppSettings.defaults, Some message
                    | Ok settings ->
                        match AppSettings.saveToPath settingsPath settings with
                        | Ok() -> settings, None
                        | Error message -> settings, Some message
                | _ -> AppSettings.defaults, None

        { Store = AppSettingsStore(settingsPath, settings)
          LoadError = loadError }
