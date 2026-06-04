# miniaudio Integration

BubiBoy uses a small native wrapper named `bubi_miniaudio` instead of binding directly to the whole
miniaudio API. The managed `BubiBoy.Audio` layer only depends on these wrapper functions:

- `bubi_audio_create`
- `bubi_audio_destroy`
- `bubi_audio_start`
- `bubi_audio_stop`
- `bubi_audio_enqueue_pcm16`

This keeps the F# side narrow and makes it possible to fall back to an in-memory buffered device when the
native library is not present.

## Build The Native Library

`native/bubi_miniaudio.c` expects the vendored `native/miniaudio.h` to be available next to the wrapper.
The `BubiBoy.Audio` project builds the wrapper automatically for the current host RID when
`native/build/runtimes/<rid>/native` does not already contain the expected library. Set
`BubiBoyBuildNativeMiniaudio=false` to skip this step.

```sh
dotnet build src/BubiBoy.Audio/BubiBoy.Audio.fsproj
```

Cross-RID publishing still needs a native library built for the target RID. CI builds the wrapper on
macOS, Linux, and Windows before running managed tests. The workflow copies the result into
`native/build/runtimes/<rid>/native`, then sets `BUBIBOY_EXPECT_NATIVE_AUDIO=1` so the audio tests assert
that the managed loader can find the native library without requiring a real playback device.

Then place the built library where .NET can load it:

- macOS: `libbubi_miniaudio.dylib`
- Linux: `libbubi_miniaudio.so`
- Windows: `bubi_miniaudio.dll`

For local development, the automatic build places the library under `native/build/runtimes/<rid>/native`
and copies it through project references into app and test output. Putting the library next to
`BubiBoy.App.dll` or on the platform library search path is also sufficient.

When `native/build` contains one of the legacy top-level expected library names, `BubiBoy.Audio.fsproj`
still copies it to its own output directory. App/test output may still need a rebuild after the native
build so the project reference can copy the asset forward.

RID-specific output is also supported. Place native libraries under the standard .NET layout and they will
be copied to build and publish output:

```text
native/build/runtimes/osx-arm64/native/libbubi_miniaudio.dylib
native/build/runtimes/osx-x64/native/libbubi_miniaudio.dylib
native/build/runtimes/linux-arm64/native/libbubi_miniaudio.so
native/build/runtimes/linux-x64/native/libbubi_miniaudio.so
native/build/runtimes/win-arm64/native/bubi_miniaudio.dll
native/build/runtimes/win-x64/native/bubi_miniaudio.dll
```

The managed loader probes both the application directory and `runtimes/<rid>/native` for the current
runtime identifier before falling back to the platform library search path.

To verify the native output path without loading a ROM:

```sh
dotnet run --project tools/BubiBoy.AudioProbe/BubiBoy.AudioProbe.fsproj
```

The probe plays a short 440 Hz tone through the miniaudio backend and exits.

## License

miniaudio is available under permissive terms. Before vendoring any copy of `miniaudio.h`, update
`docs/reference-provenance.md` with the exact source URL, version or commit, license, and redistribution
decision.
