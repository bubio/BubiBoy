namespace BubiBoy.RetroAchievements

open System
open System.Reflection
open System.Runtime.InteropServices
open System.Text

module internal NativeInterop =
    [<Literal>]
    let LibraryName = "bubi_rcheevos"

    [<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
    type ReadMemoryCallback = delegate of nativeint * uint32 * nativeint * uint32 -> uint32

    [<UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
    type ServerRequestCallback = delegate of nativeint * unativeint * string * string * string -> unit

    [<UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
    type EventCallback = delegate of nativeint * uint32 * uint32 * string * string * string * string * float32 -> unit

    [<UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
    type LogCallback = delegate of nativeint * int * string -> unit

    [<UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
    type OperationCallback = delegate of nativeint * int * string -> unit

    [<UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
    type AchievementCallback =
        delegate of
            nativeint *
            byte *
            string *
            uint32 *
            string *
            string *
            uint32 *
            string *
            float32 *
            float32 *
            byte *
            byte *
            string ->
                unit

    type UserData =
        { Username: string
          DisplayName: string
          Token: string
          Score: uint32
          SoftcoreScore: uint32 }

    type GameData =
        { Id: uint32
          Title: string
          Hash: string
          ImageUrl: string }

    type Api =
        { Create: ReadMemoryCallback * ServerRequestCallback * EventCallback * LogCallback * nativeint -> nativeint
          Destroy: nativeint -> unit
          Version: unit -> uint32
          UserAgent: nativeint * StringBuilder * unativeint -> unativeint
          CompleteServerRequest: nativeint * unativeint * int * byte[] * unativeint -> unit
          AbortServerRequests: nativeint -> unit
          CancelOperation: nativeint -> unit
          LoginPassword: nativeint * string * string * OperationCallback -> unit
          LoginToken: nativeint * string * string * OperationCallback -> unit
          Logout: nativeint -> unit
          GetUser: nativeint -> UserData option
          LoadGame: nativeint * uint32 * byte[] * unativeint * OperationCallback -> unit
          UnloadGame: nativeint -> unit
          GetGame: nativeint -> GameData option
          GetRichPresence: nativeint -> string option
          EnumerateAchievements: nativeint * AchievementCallback -> unit
          DoFrame: nativeint -> unit
          Idle: nativeint -> unit
          CanPause: nativeint -> bool * uint32
          Reset: nativeint -> unit
          ProgressSize: nativeint -> unativeint
          SerializeProgress: nativeint * byte[] * unativeint -> int
          DeserializeProgress: nativeint * byte[] * unativeint -> int }

    module Native =
        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern nativeint bubi_ra_create(
            ReadMemoryCallback readMemory,
            ServerRequestCallback serverRequest,
            EventCallback eventCallback,
            LogCallback logCallback,
            nativeint userdata
        )

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_destroy(nativeint client)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern uint32 bubi_ra_version()

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern nativeint bubi_ra_version_string()

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
        extern unativeint bubi_ra_user_agent(nativeint client, StringBuilder buffer, unativeint size)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_complete_server_request(
            nativeint client,
            unativeint requestId,
            int httpStatus,
            byte[] body,
            unativeint bodySize
        )

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_abort_server_requests(nativeint client)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_cancel_operation(nativeint client)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
        extern void bubi_ra_login_password(
            nativeint client,
            string username,
            string password,
            OperationCallback callback
        )

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
        extern void bubi_ra_login_token(nativeint client, string username, string token, OperationCallback callback)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_logout(nativeint client)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
        extern int bubi_ra_get_user(
            nativeint client,
            StringBuilder username,
            unativeint usernameSize,
            StringBuilder displayName,
            unativeint displayNameSize,
            StringBuilder token,
            unativeint tokenSize,
            uint32& score,
            uint32& softcoreScore
        )

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_load_game(
            nativeint client,
            uint32 consoleId,
            byte[] rom,
            unativeint romSize,
            OperationCallback callback
        )

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_unload_game(nativeint client)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
        extern int bubi_ra_get_game(
            nativeint client,
            uint32& gameId,
            StringBuilder title,
            unativeint titleSize,
            StringBuilder hash,
            unativeint hashSize,
            StringBuilder imageUrl,
            unativeint imageUrlSize
        )

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
        extern int bubi_ra_get_rich_presence(nativeint client, StringBuilder message, unativeint messageSize)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_enumerate_achievements(nativeint client, AchievementCallback callback)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_do_frame(nativeint client)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_idle(nativeint client)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern int bubi_ra_can_pause(nativeint client, uint32& framesRemaining)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_reset(nativeint client)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern unativeint bubi_ra_progress_size(nativeint client)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern int bubi_ra_serialize_progress(nativeint client, byte[] buffer, unativeint size)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern int bubi_ra_deserialize_progress(nativeint client, byte[] buffer, unativeint size)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
        extern int bubi_ra_keychain_store(string service, string account, string secret)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
        extern int bubi_ra_keychain_load(string service, string account, StringBuilder secret, unativeint secretSize)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)>]
        extern int bubi_ra_keychain_delete(string service, string account)

    let api =
        { Create =
            fun (readMemory, serverRequest, eventCallback, logCallback, userdata) ->
                Native.bubi_ra_create (readMemory, serverRequest, eventCallback, logCallback, userdata)
          Destroy = Native.bubi_ra_destroy
          Version = Native.bubi_ra_version
          UserAgent = fun (client, buffer, size) -> Native.bubi_ra_user_agent (client, buffer, size)
          CompleteServerRequest =
            fun (client, requestId, status, body, size) ->
                Native.bubi_ra_complete_server_request (client, requestId, status, body, size)
          AbortServerRequests = Native.bubi_ra_abort_server_requests
          CancelOperation = Native.bubi_ra_cancel_operation
          LoginPassword =
            fun (client, username, password, callback) ->
                Native.bubi_ra_login_password (client, username, password, callback)
          LoginToken =
            fun (client, username, token, callback) -> Native.bubi_ra_login_token (client, username, token, callback)
          Logout = Native.bubi_ra_logout
          GetUser =
            fun client ->
                let username = StringBuilder(256)
                let displayName = StringBuilder(256)
                let token = StringBuilder(512)
                let mutable score = 0u
                let mutable softcoreScore = 0u

                if
                    Native.bubi_ra_get_user (
                        client,
                        username,
                        unativeint username.Capacity,
                        displayName,
                        unativeint displayName.Capacity,
                        token,
                        unativeint token.Capacity,
                        &score,
                        &softcoreScore
                    )
                    <> 0
                then
                    Some
                        { Username = username.ToString()
                          DisplayName = displayName.ToString()
                          Token = token.ToString()
                          Score = score
                          SoftcoreScore = softcoreScore }
                else
                    None
          LoadGame =
            fun (client, consoleId, rom, romSize, callback) ->
                Native.bubi_ra_load_game (client, consoleId, rom, romSize, callback)
          UnloadGame = Native.bubi_ra_unload_game
          GetGame =
            fun client ->
                let mutable gameId = 0u
                let title = StringBuilder(512)
                let hash = StringBuilder(64)
                let imageUrl = StringBuilder(2048)

                if
                    Native.bubi_ra_get_game (
                        client,
                        &gameId,
                        title,
                        unativeint title.Capacity,
                        hash,
                        unativeint hash.Capacity,
                        imageUrl,
                        unativeint imageUrl.Capacity
                    )
                    <> 0
                then
                    Some
                        { Id = gameId
                          Title = title.ToString()
                          Hash = hash.ToString()
                          ImageUrl = imageUrl.ToString() }
                else
                    None
          GetRichPresence =
            fun client ->
                let message = StringBuilder(1024)

                if
                    Native.bubi_ra_get_rich_presence (client, message, unativeint message.Capacity)
                    <> 0
                then
                    Some(message.ToString())
                else
                    None
          EnumerateAchievements = fun (client, callback) -> Native.bubi_ra_enumerate_achievements (client, callback)
          DoFrame = Native.bubi_ra_do_frame
          Idle = Native.bubi_ra_idle
          CanPause =
            fun client ->
                let mutable framesRemaining = 0u
                let allowed = Native.bubi_ra_can_pause (client, &framesRemaining) <> 0
                allowed, framesRemaining
          Reset = Native.bubi_ra_reset
          ProgressSize = Native.bubi_ra_progress_size
          SerializeProgress = fun (client, buffer, size) -> Native.bubi_ra_serialize_progress (client, buffer, size)
          DeserializeProgress = fun (client, buffer, size) -> Native.bubi_ra_deserialize_progress (client, buffer, size) }

    let private tryLoad () =
        let assembly = Assembly.GetExecutingAssembly()

        let names =
            [| LibraryName
               "libbubi_rcheevos.dylib"
               "libbubi_rcheevos.so"
               "bubi_rcheevos.dll" |]

        let mutable handle = nativeint 0

        names
        |> Array.exists (fun name -> NativeLibrary.TryLoad(name, assembly, Nullable(), &handle))
        |> fun loaded -> if loaded then Some handle else None

    do
        NativeLibrary.SetDllImportResolver(
            Assembly.GetExecutingAssembly(),
            DllImportResolver(fun libraryName _ _ ->
                if libraryName = LibraryName then
                    match tryLoad () with
                    | Some handle -> handle
                    | None -> nativeint 0
                else
                    nativeint 0)
        )

    let isAvailable () =
        match tryLoad () with
        | Some handle ->
            NativeLibrary.Free handle
            true
        | None -> false
