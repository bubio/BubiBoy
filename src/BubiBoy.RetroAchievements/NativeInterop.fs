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
    type EventCallback = delegate of nativeint * uint32 * uint32 * string * string * string -> unit

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

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_enumerate_achievements(nativeint client, AchievementCallback callback)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_do_frame(nativeint client)

        [<DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)>]
        extern void bubi_ra_idle(nativeint client)

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
