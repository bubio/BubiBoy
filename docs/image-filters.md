# Image Filters

BubiBoy exposes three display filters under `View > Image Filter`:

- `Off` uses nearest-neighbor sampling and is the default.
- `Smooth` uses Avalonia's high-quality bitmap interpolation. With the current Avalonia Skia backend,
  enlarged images use Mitchell cubic resampling.
- `LCD` generates reusable 2x and 3x bitmaps with a light pixel grid and small gamma and contrast
  adjustments. At 3x and above it also applies a restrained RGB subpixel mask.

At 2x, LCD uses the native 2x bitmap without an RGB mask so every emulated pixel maps to an exact 2x2
cell. The LCD effect is fully disabled at 1x to avoid color moire. It does not add bloom, screen
curvature, persistence, or vignette effects.

The emulator core is not involved in filtering. The Avalonia app keeps reusable normal and LCD
`WriteableBitmap` instances and BGRA transfer buffers; no filtered framebuffer is added to
`BubiBoy.Core`. Avalonia still renders the resulting bitmap through its normal Skia backend.

The LCD pixel transformation was written specifically for BubiBoy from the desired visual behavior.
No MAME HLSL source or shader code was copied or translated.
