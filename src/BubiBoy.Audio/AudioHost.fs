namespace BubiBoy.Audio

module AudioHost =
    type AudioFormat =
        { SampleRate: int
          Channels: int }

    type AudioDevice =
        abstract Start: unit -> unit
        abstract Stop: unit -> unit

    let defaultFormat =
        { SampleRate = 48_000
          Channels = 2 }

