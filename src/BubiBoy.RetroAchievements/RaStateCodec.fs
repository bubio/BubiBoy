namespace BubiBoy.RetroAchievements

open System
open System.IO
open System.Text

module RaStateCodec =
    [<Literal>]
    let MaxProgressSize = 16 * 1024 * 1024

    let private magic = Encoding.ASCII.GetBytes "BBRA"
    let private version = 1

    type Decoded =
        { GameId: uint32
          RomHash: string
          RcheevosVersion: uint32
          CoreState: byte[]
          Progress: byte[] }

    let private crc32 (data: byte[]) offset count =
        let mutable crc = 0xFFFFFFFFu

        for index = offset to offset + count - 1 do
            crc <- crc ^^^ uint32 data[index]

            for _ = 0 to 7 do
                crc <-
                    if crc &&& 1u <> 0u then
                        (crc >>> 1) ^^^ 0xEDB88320u
                    else
                        crc >>> 1

        ~~~crc

    let encode (gameId: uint32) (romHash: string) (rcheevosVersion: uint32) (coreState: byte[]) (progress: byte[]) =
        if isNull coreState || isNull progress then
            Error "RetroAchievements state data is null."
        elif progress.Length > MaxProgressSize then
            Error $"RetroAchievements progress exceeds {MaxProgressSize} bytes."
        elif String.IsNullOrWhiteSpace romHash then
            Error "RetroAchievements ROM hash is empty."
        else
            use stream = new MemoryStream()
            use writer = new BinaryWriter(stream, Encoding.UTF8, true)
            writer.Write magic
            writer.Write version
            writer.Write gameId
            writer.Write rcheevosVersion
            writer.Write romHash
            writer.Write coreState.Length
            writer.Write progress.Length
            writer.Write coreState
            writer.Write progress
            writer.Flush()
            let withoutCrc = stream.ToArray()
            writer.Write(crc32 withoutCrc 0 withoutCrc.Length)
            writer.Flush()
            Ok(stream.ToArray())

    let decode (bytes: byte[]) =
        try
            if isNull bytes || bytes.Length < 32 then
                Error "RetroAchievements state is truncated."
            else
                let storedCrc = BitConverter.ToUInt32(bytes, bytes.Length - 4)
                let computedCrc = crc32 bytes 0 (bytes.Length - 4)

                if storedCrc <> computedCrc then
                    Error "RetroAchievements state checksum mismatch."
                else
                    use stream = new MemoryStream(bytes, 0, bytes.Length - 4, false)
                    use reader = new BinaryReader(stream, Encoding.UTF8, true)
                    let fileMagic = reader.ReadBytes magic.Length

                    if fileMagic <> magic then
                        Error "File is not a BubiBoy RetroAchievements state."
                    elif reader.ReadInt32() <> version then
                        Error "Unsupported RetroAchievements state version."
                    else
                        let gameId = reader.ReadUInt32()
                        let rcheevosVersion = reader.ReadUInt32()
                        let romHash = reader.ReadString()
                        let coreSize = reader.ReadInt32()
                        let progressSize = reader.ReadInt32()
                        let remaining = stream.Length - stream.Position

                        if coreSize < 0 || progressSize < 0 || progressSize > MaxProgressSize then
                            Error "RetroAchievements state contains invalid sizes."
                        elif int64 coreSize + int64 progressSize <> remaining then
                            Error "RetroAchievements state payload size mismatch."
                        else
                            Ok
                                { GameId = gameId
                                  RomHash = romHash
                                  RcheevosVersion = rcheevosVersion
                                  CoreState = reader.ReadBytes coreSize
                                  Progress = reader.ReadBytes progressSize }
        with
        | :? IOException as ex -> Error $"Could not read RetroAchievements state: {ex.Message}"
        | :? ArgumentException as ex -> Error $"Invalid RetroAchievements state: {ex.Message}"
