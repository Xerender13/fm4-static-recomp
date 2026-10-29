# Testing and benchmarks

[← Back to the README](../README.md)

## Tested

- Offline modes: World Tour (played up to the Expert Division), Event List (about 30 races
  without an issue), Free Play (Quick Race, Hot Lap), Test Drive, Autovista and photo mode.
- The garage (upgrades, tuning presets, paint and decals), replays and saving.
- Intel GPUs: a player's test confirmed the flickering lights fixed in 0.3.0.
- Not tested: Xbox LIVE modes, split screen, racing wheels, and most AMD and Intel GPUs.

## Test bench

| Part | |
|---|---|
| CPU | AMD Ryzen 7 5700X3D |
| RAM | 32 GB DDR4-3600 |
| GPU | NVIDIA GeForce RTX 4070 Ti SUPER, 16 GB (driver 610.88) |
| OS | Windows 10 Pro 22H2 |
| Display | 170 Hz |

## Benchmarks

A scripted race at Bernese Alps against 11 AI cars in version 0.3.0, about 12 seconds per row,
with the frame rate limit raised to 240 so the PC sets the rate. The game runs in a 1280x720
window with the upscaler off. Frame times come from PresentMon, and each figure is the median of
three runs. The 1 % low is the frame rate over the slowest 1 % of frames.

### Console preset

| Scale | Render resolution | Average fps | 1 % low | GPU use | VRAM (game) |
|---|---|--:|--:|--:|--:|
| 1x | 1280x720 | 233 | 160 | 58 % | 0.8 GB |
| 2x | 2560x1440 | 232 | 155 | 87 % | 1.3 GB |
| 3x | 3840x2160 | 156 | 99 | 99 % | 2.0 GB |
| 4x | 5120x2880 | 102 | 73 | 99 % | 2.8 GB |

### Ultra preset

| Scale | Render resolution | Average fps | 1 % low | GPU use | VRAM (game) |
|---|---|--:|--:|--:|--:|
| 1x | 1280x720 | 206 | 143 | 58 % | 0.8 GB |
| 2x | 2560x1440 | 201 | 140 | 91 % | 1.3 GB |
| 3x | 3840x2160 | 139 | 86 | 99 % | 2.0 GB |
| 4x | 5120x2880 | 92 | 64 | 99 % | 2.9 GB |

With the Console preset, 1x and 2x run close to the 240 fps limit; at 3x and 4x the GPU is the
limit.

## Compared with Xenia

The same race on the same PC and disc image in Xenia Canary
[`dac2794ca`](https://github.com/xenia-canary/xenia-canary/commit/dac2794ca72e20b2cb5e6ca9e765da8811a19d6d)
(16 October 2026): Direct3D 12, vsync off, no frame rate limit, `readback_resolve = "fast"`
and `occlusion_query = "strict"` (without these two the game renders incorrectly). Xenia's
figures are the median of three runs; this recomp's are the Console preset rows above.

| Scale | Average fps, Xenia / recomp | 1 % low, Xenia / recomp | GPU use, Xenia / recomp | VRAM, Xenia / recomp |
|---|---|---|---|---|
| 1x | 102 / 233 | 54 / 160 | 38 % / 58 % | 1.5 / 0.8 GB |
| 2x | 88 / 232 | 55 / 155 | 56 % / 87 % | 4.2 / 1.3 GB |
| 3x | 68 / 156 | 45 / 99 | 81 % / 99 % | 7.8 / 2.0 GB |
| 4x | 45 / 102 | 35 / 73 | 90 % / 99 % | 13.0 / 2.8 GB |

This recomp runs 2.3 to 2.6 times as fast as Xenia and needs a fifth to a third of Xenia's
video memory from 2x up.

## Compared with hl360-forza4

The same race on the same PC in [hl360-forza4](https://github.com/0herculabs/hl360-forza4)
[v0.3.9](https://github.com/0herculabs/hl360-forza4/releases/tag/v0.3.9), another static
recompilation of FM4. It is built from the USA Rev 1 disc; it ran here from the European Rev 1
disc, whose game data matches. Since v0.3.9 it renders with the same ShiftGlue Vulkan renderer
as this recomp. It ran in its experimental 120 FPS mode, the fastest it offers (60 FPS is its
default), with the closest settings to the Console preset: the game's own anisotropic
filtering, no post-process anti-aliasing, bilinear output. Its figures are the median of three
runs; this recomp's are the Console preset rows above.

| Scale | Average fps, hl360 / recomp | 1 % low, hl360 / recomp | GPU use, hl360 / recomp | VRAM, hl360 / recomp |
|---|---|---|---|---|
| 1x | 118 / 233 | 78 / 160 | 76 % / 58 % | 0.8 / 0.8 GB |
| 2x | 118 / 232 | 63 / 155 | 69 % / 87 % | 1.6 / 1.3 GB |
| 3x | 91 / 156 | 56 / 99 | 99 % / 99 % | 2.7 / 2.0 GB |
| 4x | 51 / 102 | 40 / 73 | 100 % / 99 % | 4.3 / 2.8 GB |

At 1x and 2x hl360-forza4 holds its 120 fps limit, with a 1 % low under half this recomp's.
At 3x and 4x both are limited by the GPU, and this recomp runs 1.7 to 2 times as fast with
two thirds to three quarters of the video memory.

Also seen in hl360-forza4 during the runs:

- The seam along car panels from the dirt layer, which this recomp fixes, is there.
- Shaders compile in the background during play: in the first races, the road surface was drawn
  flat grey for several seconds until its shaders were ready. This recomp builds its shaders at
  setup and before the title screen.
