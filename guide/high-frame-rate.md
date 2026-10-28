# High frame rates

[← Back to the README](../README.md)

The Xbox 360 runs Forza Motorsport 4's races at 60 fps and its showroom at 30. This port runs
races, menus and the showroom at one frame rate from 60 up to your display's refresh rate (at
most 240), with the cars, the camera and the menus still moving at their real speed. This page
says how to pick a rate, and what the port changes in the game to get there.

## Picking a rate

**FRAME RATE** on the GRAPHICS page (**BACK + START** or **F6**) or in the launcher's SETTINGS;
`--fm4_frame_rate=60..240` as a launch option. 120 by default, lowered to the refresh rate on a
slower display. It applies as soon as you SAVE & APPLY, mid-race too.

| Your display | FRAME RATE | VSYNC |
|---|---|---|
| G-Sync or FreeSync | Any rate your PC holds, up to the refresh rate | Off |
| Fixed refresh rate | The refresh rate, or a whole fraction of it (144 Hz: 144 or 72) | On, if you see tearing |

Pick a rate your PC holds in a full race, not on an empty track: a rate it cannot hold
stutters (see [Limits](#limits)). [Benchmarks](performance.md#benchmarks) show what one PC
holds at each resolution scale.

## How the console paces the game

The game's main loop waits for the GPU's **vertical blank** (vblank), 60 times a second on the
console, once per tick: it reads the controller, steps the race and draws one frame. Each
finished frame is shown on a vblank too. Races take every vblank (60 fps); the showroom, the
pre-race screen and Autovista show a frame on every second one (30 fps).

## What the port changes

**The vblank runs at FRAME RATE.** Races and menus tick and show a frame on every vblank, as
on the console, but the vblank comes 60 to 240 times a second. A change applies from the next
vblank. The rate is the vblank itself, not every second vblank of
a faster one: a frame is shown on the first vblank after it is finished, so frames finished near
the vblank in between would alternate between half and one and a half frame periods.

**Half-rate scenes at full rate.** The showroom, the pre-race screen and Autovista ask for a
frame on every second vblank; the port has them draw on every one
(`--fm4_full_rate_scenes=false` gives back the game's half rate).

**Real-time animation.** The race steps by the time a tick actually took, measured by the game
itself, so a car covers the same distance in a second at any frame rate. The showroom camera and
the menus' animations instead assume one fixed 1/60 s step per update, and would run twice as
fast at 120 fps. The port feeds them the real time since their last update, capped at 0.1 s so
the showroom camera does not jump after time spent elsewhere. `--fm4_ui_speed` scales the menus'
animations (1 = the console's speed).

**Car physics.** Each tick, the game splits a car's step into physics substeps at 359 per
second. More frames mean fewer substeps per frame (6 at 60 fps, 2 at 240) but the same number
per second, so the physics run at the same rate at any frame rate.

**Faster race loads.** A race load advances partly once per main loop tick, so on the loading
screen the vblank runs at 480 Hz until the cars start to move. In one measured load that took
3.5 s instead of 5.8 s at 120 Hz.

## Limits

- **The CPU sets the ceiling at low resolution scales.** The game's main thread runs on one
  core. At 1x and 2x the [benchmark race](performance.md#benchmarks) averages 170-210 fps with
  1 % lows of 105-125; a bunched pack of AI cars can drop frames at 160.
- **A rate the PC cannot hold stutters.** The main loop keeps ticking at the vblank rate while
  fewer frames are drawn, so a shown frame jumps by one or two ticks. Lower FRAME RATE or the
  resolution scale until it holds.
- **AI cars at high frame rates** are being looked into. Some cars' physics take 239 substeps a
  second instead of 359, which at 240 fps is one substep a frame; whether the AI drives
  differently with so few is not settled yet.
- **Frame generation** tools (Lossless Scaling, NVIDIA Smooth Motion) need an
  [opt-in](launch-options.md#frame-generation).

## Diagnostics

`--fm4_console_rate=true` plays with the console's pacing (races 60, the showroom 30), to
compare. `--fm4_physics_log=<file>` writes every main loop time step and each car's physics
substeps to a CSV file. More in [DEBUG_OPTIONS.md](../DEBUG_OPTIONS.md#the-game).
