module BubiBoy.RetroAchievements.Tests.RaStateCodecTests

open BubiBoy.RetroAchievements
open Xunit

[<Fact>]
let ``RA state round trips metadata core and progress`` () =
    let core = [| 1uy; 2uy; 3uy |]
    let progress = [| 4uy; 5uy |]

    match RaStateCodec.encode 42u "0123456789abcdef" 12003000u core progress with
    | Error message -> Assert.Fail message
    | Ok encoded ->
        match RaStateCodec.decode encoded with
        | Error message -> Assert.Fail message
        | Ok decoded ->
            Assert.Equal(42u, decoded.GameId)
            Assert.Equal("0123456789abcdef", decoded.RomHash)
            Assert.Equal(12003000u, decoded.RcheevosVersion)
            Assert.Equal<byte[]>(core, decoded.CoreState)
            Assert.Equal<byte[]>(progress, decoded.Progress)

[<Fact>]
let ``RA state rejects checksum corruption`` () =
    let encoded =
        RaStateCodec.encode 1u "hash" 12003000u [| 1uy |] [| 2uy |]
        |> Result.defaultWith failwith

    encoded[encoded.Length / 2] <- encoded[encoded.Length / 2] ^^^ 0xFFuy

    match RaStateCodec.decode encoded with
    | Ok _ -> Assert.Fail "Corrupt state was accepted."
    | Error message -> Assert.Contains("checksum", message)

[<Fact>]
let ``RA state rejects oversized progress`` () =
    let progress = Array.zeroCreate<byte> (RaStateCodec.MaxProgressSize + 1)

    match RaStateCodec.encode 1u "hash" 12003000u Array.empty progress with
    | Ok _ -> Assert.Fail "Oversized progress was accepted."
    | Error message -> Assert.Contains("exceeds", message)
