namespace BubiBoy.App

open BubiBoy.Core

/// Owns the synchronized output gain used by the emulation thread.
type OutputVolumeController(initialPercent: int) =
    let gate = obj ()
    let mutable gain = VolumeControl.gainFromPercent initialPercent

    /// Updates output gain from a normalized percentage.
    member _.SetPercent(percent: int) =
        lock gate (fun () -> gain <- VolumeControl.gainFromPercent percent)

    /// Applies output gain to a freshly drained sample buffer in place.
    member _.Apply(samples: Apu.Sample[]) =
        let currentGain = lock gate (fun () -> gain)

        if currentGain <> 1.0f then
            for index in 0 .. samples.Length - 1 do
                let sample = samples[index]
                samples[index] <-
                    { Left = sample.Left * currentGain
                      Right = sample.Right * currentGain }

        samples
