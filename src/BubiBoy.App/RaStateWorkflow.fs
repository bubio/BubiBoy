namespace BubiBoy.App

open System
open System.IO
open BubiBoy.Core
open BubiBoy.RetroAchievements

module RaStateWorkflow =
    type LoadResult =
        { Session: Emulator.Session
          ProgressRestored: bool }

    let private statePath (settingsPath: string) (game: RaGame) =
        let root =
            Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath settingsPath),
                "retroachievements",
                "states",
                string game.Id
            )

        Path.Combine(root, $"{game.Hash}.state")

    let private writeAtomic (path: string) (bytes: byte[]) =
        let directory = Path.GetDirectoryName path
        Directory.CreateDirectory directory |> ignore
        let tempPath = $"{path}.tmp-{Guid.NewGuid():N}"
        File.WriteAllBytes(tempPath, bytes)

        try
            File.Move(tempPath, path, true)
        with ex ->
            if File.Exists tempPath then
                File.Delete tempPath

            raise ex

    let save settingsPath (client: RaClient) session =
        match client.Snapshot.Game, client.SerializeProgress() with
        | None, _ -> Error "RetroAchievements game information is unavailable."
        | _, Error message -> Error message
        | Some game, Ok progress ->
            let coreState = session |> SaveState.capture |> SaveState.encode

            RaStateCodec.encode game.Id game.Hash client.Version coreState progress
            |> Result.bind (fun bytes ->
                try
                    writeAtomic (statePath settingsPath game) bytes
                    Ok()
                with
                | :? IOException as ex -> Error $"Could not write RetroAchievements state: {ex.Message}"
                | :? UnauthorizedAccessException as ex ->
                    Error $"Could not write RetroAchievements state: {ex.Message}")

    let load settingsPath (client: RaClient) session =
        match client.Snapshot.Game with
        | None -> Error "RetroAchievements game information is unavailable."
        | Some game ->
            let path = statePath settingsPath game

            if not (File.Exists path) then
                Error $"RetroAchievements state file does not exist: {path}"
            else
                try
                    File.ReadAllBytes path
                    |> RaStateCodec.decode
                    |> Result.bind (fun decoded ->
                        if decoded.GameId <> game.Id then
                            Error "RetroAchievements state belongs to another game."
                        elif not (String.Equals(decoded.RomHash, game.Hash, StringComparison.OrdinalIgnoreCase)) then
                            Error "RetroAchievements state belongs to another ROM."
                        elif decoded.RcheevosVersion <> client.Version then
                            Error "RetroAchievements state was created by another rcheevos version."
                        else
                            SaveState.restoreBytes decoded.CoreState session
                            |> Result.map (fun restored ->
                                { Session = restored
                                  ProgressRestored = client.DeserializeProgress decoded.Progress }))
                with
                | :? IOException as ex -> Error $"Could not read RetroAchievements state: {ex.Message}"
                | :? UnauthorizedAccessException as ex -> Error $"Could not read RetroAchievements state: {ex.Message}"
