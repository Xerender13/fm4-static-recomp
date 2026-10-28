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

A scripted race at Bernese Alps against 11 AI cars, about 12 seconds per row, with the frame
rate limit raised to 240 so the PC sets the rate. The 1 % low is the frame rate over the
slowest 1 % of frames.

### Console preset

| Scale | Render resolution | Average fps | 1 % low | GPU use | VRAM (game) |
|---|---|--:|--:|--:|--:|
| 1x | 1280x720 | 207 | 106 | 54 % | 1.2 GB |
| 2x | 2560x1440 | 194 | 124 | 80 % | 1.6 GB |
| 3x | 3840x2160 | 148 | 76 | 99 % | 2.3 GB |
| 4x | 5120x2880 | 92 | 47 | 99 % | 3.1 GB |

### Ultra preset

| Scale | Render resolution | Average fps | 1 % low | GPU use | VRAM (game) |
|---|---|--:|--:|--:|--:|
| 1x | 1280x720 | 183 | 123 | 54 % | 1.2 GB |
| 2x | 2560x1440 | 173 | 119 | 81 % | 1.6 GB |
| 3x | 3840x2160 | 100 | 54 | 99 % | 2.5 GB |
| 4x | 5120x2880 | 68 | 46 | 99 % | 3.3 GB |

At 1x and 2x the CPU is the limit (one game thread runs a full core); at 3x and 4x, the GPU.

## Compared with Xenia

The same race on the same PC and disc image in Xenia Canary
[`dac2794ca`](https://github.com/xenia-canary/xenia-canary/commit/dac2794ca72e20b2cb5e6ca9e765da8811a19d6d)
(16 October 2026): Direct3D 12, vsync off, no frame rate limit, `readback_resolve = "fast"`
and `occlusion_query = "strict"` (without these two the game renders incorrectly). Xenia's
figures are the median of three runs; this recomp's are the Console preset rows above.

| Scale | Average fps, Xenia / recomp | 1 % low, Xenia / recomp | GPU use, Xenia / recomp | VRAM, Xenia / recomp |
|---|---|---|---|---|
| 1x | 102 / 207 | 54 / 106 | 38 % / 54 % | 1.5 / 1.2 GB |
| 2x | 88 / 194 | 55 / 124 | 56 % / 80 % | 4.2 / 1.6 GB |
| 3x | 68 / 148 | 45 / 76 | 81 % / 99 % | 7.8 / 2.3 GB |
| 4x | 45 / 92 | 35 / 47 | 90 % / 99 % | 13.0 / 3.1 GB |

This recomp runs about twice as fast at every scale and needs a quarter to two fifths of
Xenia's video memory from 2x up.
