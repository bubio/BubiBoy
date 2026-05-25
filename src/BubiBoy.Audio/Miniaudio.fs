namespace BubiBoy.Audio

open System
open System.IO
open System.Reflection
open System.Runtime.InteropServices
open BubiBoy.Core

module Miniaudio =
    [<Literal>]
    let private LibraryName = "bubi_miniaudio"

    module private Native =
        [<DllImport(LibraryName, EntryPoint = "bubi_audio_create", CallingConvention = CallingConvention.Cdecl)>]
        extern nativeint create(int sampleRate, int channels, int bufferFrames)

        [<DllImport(LibraryName, EntryPoint = "bubi_audio_destroy", CallingConvention = CallingConvention.Cdecl)>]
        extern void destroy(nativeint device)

        [<DllImport(LibraryName, EntryPoint = "bubi_audio_start", CallingConvention = CallingConvention.Cdecl)>]
        extern int start(nativeint device)

        [<DllImport(LibraryName, EntryPoint = "bubi_audio_stop", CallingConvention = CallingConvention.Cdecl)>]
        extern int stop(nativeint device)

        [<DllImport(LibraryName, EntryPoint = "bubi_audio_enqueue_pcm16", CallingConvention = CallingConvention.Cdecl)>]
        extern int enqueuePcm16(nativeint device, byte[] pcmBytes, int frames)

        [<DllImport(LibraryName, EntryPoint = "bubi_audio_buffered_frames", CallingConvention = CallingConvention.Cdecl)>]
        extern int bufferedFrames(nativeint device)

        [<DllImport(LibraryName, EntryPoint = "bubi_audio_underrun_frames", CallingConvention = CallingConvention.Cdecl)>]
        extern uint64 underrunFrames(nativeint device)

        [<DllImport(LibraryName, EntryPoint = "bubi_audio_dropped_frames", CallingConvention = CallingConvention.Cdecl)>]
        extern uint64 droppedFrames(nativeint device)

    let private nativeLibraryCandidates () =
        let baseDirectory = AppContext.BaseDirectory
        let currentDirectory = Environment.CurrentDirectory
        let names =
            [| LibraryName
               "libbubi_miniaudio.dylib"
               "libbubi_miniaudio.so"
               "bubi_miniaudio.dll" |]

        [| for name in names do
               yield Path.Combine(baseDirectory, name)
               yield Path.Combine(currentDirectory, name)
               yield name |]

    let private tryLoadNativeLibrary () =
        let candidates = nativeLibraryCandidates ()
        let mutable handle = nativeint 0
        let mutable found = false
        let mutable index = 0

        while not found && index < candidates.Length do
            if NativeLibrary.TryLoad(candidates[index], &handle) then
                found <- true
            else
                index <- index + 1

        if found then Some handle else None

    do
        NativeLibrary.SetDllImportResolver(
            Assembly.GetExecutingAssembly(),
            DllImportResolver(fun libraryName _ _ ->
                if libraryName = LibraryName then
                    match tryLoadNativeLibrary () with
                    | Some handle -> handle
                    | None -> nativeint 0
                else
                    nativeint 0)
        )

    let isNativeLibraryAvailable () =
        match tryLoadNativeLibrary () with
        | Some handle ->
            NativeLibrary.Free handle
            true
        | None -> false

    type Device private (handle: nativeint, format: AudioHost.AudioFormat) =
        let mutable disposed = false
        let mutable running = false

        member _.Format = format
        member _.IsRunning = running

        member private _.EnsureNotDisposed() =
            if disposed then
                invalidOp "The miniaudio device has already been disposed."

        interface AudioHost.AudioDevice with
            member this.Start() =
                this.EnsureNotDisposed()

                if Native.start handle <> 0 then
                    invalidOp "miniaudio failed to start the playback device."

                running <- true

            member this.Stop() =
                this.EnsureNotDisposed()

                if running then
                    Native.stop handle |> ignore
                    running <- false

            member this.Enqueue(samples: Apu.Sample[]) =
                this.EnsureNotDisposed()

                let pcm = AudioHost.toPcm16StereoBytes samples
                let accepted = Native.enqueuePcm16(handle, pcm, samples.Length)
                let accepted = max 0 (min samples.Length accepted)

                { AcceptedFrames = accepted
                  DroppedFrames = samples.Length - accepted }

            member this.Diagnostics() =
                this.EnsureNotDisposed()

                { BufferedFrames = Native.bufferedFrames handle
                  UnderrunFrames = int64 (Native.underrunFrames handle)
                  DroppedFrames = int64 (Native.droppedFrames handle)
                  IsRunning = running }

        interface IDisposable with
            member _.Dispose() =
                if not disposed then
                    if running then
                        Native.stop handle |> ignore
                        running <- false

                    Native.destroy handle
                    disposed <- true

        static member TryCreate(format: AudioHost.AudioFormat, bufferFrames: int) =
            if bufferFrames <= 0 then
                Error "miniaudio buffer frame count must be positive."
            elif not (isNativeLibraryAvailable ()) then
                Error "bubi_miniaudio native library was not found."
            else
                try
                    let handle = Native.create (format.SampleRate, format.Channels, bufferFrames)

                    if handle = nativeint 0 then
                        Error "miniaudio failed to create a playback device."
                    else
                        Ok(new Device(handle, format))
                with
                | :? DllNotFoundException as ex -> Error ex.Message
                | :? EntryPointNotFoundException as ex -> Error ex.Message

    let tryCreateDevice format bufferFrames =
        Device.TryCreate(format, bufferFrames)
