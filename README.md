# FM4 Static Recomp

An unofficial static recompilation of the Xbox 360 release of *Forza Motorsport 4* for
Windows, built on your own PC from your own disc.

The game's PowerPC code is translated ahead of time into C++ by the
[ShiftGlue SDK](https://github.com/arcanite24/shiftglue-sdk) and compiled for x86-64. The GPU
commands the game builds are executed by ShiftGlue's native Vulkan renderer, written for
[Pinyon Shift](https://github.com/arcanite24/pinyon-shift) (*Forza Horizon*) and extended
here for FM4.

> **Experimental source release.** The offline game modes play without known problems
> (see [What has been tested](#what-has-been-tested)), so far on one PC. There is no
> launcher yet: setup is the manual guide below. A launcher that installs both discs and
> DLC, manages profiles and updates is planned (see [Roadmap](#roadmap)).

This is an unofficial fan project. It is not affiliated with, endorsed, sponsored or
approved by Microsoft Corporation, Xbox Game Studios, Turn 10 Studios, or any other company
or person that made, published or licensed *Forza Motorsport 4*, including the car
manufacturers, track owners and other licensors whose names and brands appear in the game.
*Forza*, *Forza Motorsport*, *Xbox* and *Xbox 360* are trademarks of Microsoft Corporation;
every other trademark belongs to its owner. This repository contains **no** game code, game
assets, disc images, generated translation or prebuilt game executable: you need your own
legally obtained copy of the game, and the executable is built on your computer.

## Contents

- [Showcase](#showcase)
- [Supported game](#supported-game)
- [Features](#features)
- [What you need](#what-you-need)
- [Install](#install)
- [Graphics settings](#graphics-settings)
- [Launch options](#launch-options)
- [What has been tested](#what-has-been-tested)
- [What has not been tested](#what-has-not-been-tested)
- [Benchmarks](#benchmarks)
- [Known issues](#known-issues)
- [Reporting issues](#reporting-issues)
- [Roadmap](#roadmap)
- [Repository layout](#repository-layout)
- [AI use](#ai-use)
- [Credits](#credits)
- [License](#license)

## Showcase

| 3840x2160 (3x), 120 fps | 2560x1440 (2x), 160 fps |
|---|---|
| [![Nürburgring GP, BMW M3 E92, 2160p at 120 fps](https://img.youtube.com/vi/Kf01UV4NT_Y/maxresdefault.jpg)](https://www.youtube.com/watch?v=Kf01UV4NT_Y) | [![Hockenheimring Short, BMW M5, 1440p at 160 fps](https://img.youtube.com/vi/nxM1L6pbW0U/maxresdefault.jpg)](https://www.youtube.com/watch?v=nxM1L6pbW0U) |
| Nürburgring GP, BMW M3 E92 | Hockenheimring Short, BMW M5 |

YouTube plays video at up to 60 fps; the frame rates are the game's during the recording.

## Supported game

| | |
|---|---|
| Game | Forza Motorsport 4, Xbox 360, retail |
| Title ID | `4D530910` |
| Disc 1, 1.0 PAL | Forza Motorsport 4 (Europe, Australia) (Disc 1) (Play Disc), media ID `61D62D9F`; `default.xex` 15,155,200 bytes, SHA-256 `6A7E9AE561CCCA134916F27870A73299408F4AD14F1499063C0F6F6414C2A7E1` |
| Disc 1, 1.0 USA | Forza Motorsport 4 (USA) (Disc 1) (Play Disc), media ID `26FF1101`; `default.xex` 15,155,200 bytes, SHA-256 `3618E85AF83E972DDCC1B0D0DD808E78E29AB4AB3890A15B31C210A6A3E93E09` |
| Disc 2 | The four content packages of the install disc (optional, more cars and tracks) |
| DLC | Marketplace packages for title `4D530910` (optional) |

The two Disc 1 releases differ only in their three executables (`default.xex`,
`XMediaFacade_default.xex`, `SpeechFacade_default.xex`), and those only in their headers
(region, media ID, signature): the code inside is byte for byte the same, and so is the
translation. All other files are identical. Other versions of `default.xex` will not work:
the translation is made for this exact code. `config/supported-discs.json` lists every Disc 1
file with its size and hash; the `$SystemUpdate` folder is optional, the game never reads it.

The project was developed on a modified copy of the same release with the unicorn cars
unlocked; only `Media/db/gamedb.slt` differs, and it works the same.

**The 1.1 re-release (Rev 1) of Disc 1 is not supported.** It is a later pressing with a
newer `default.xex` (media ID `42C67824`, 15,187,968 bytes, SHA-256
`B34DFB88978819CCCDA6DAFD755E7139970A58FBF6707DFBCBA06846857F52BD`) whose code sits at
other addresses than the translation expects. Disc 2 of the Rev 1 release works: its four content packages are identical.

Any other discs weren't yet tested and cannot be guaranteed to work.

## Features

Compared with the original game on an Xbox 360, and with what the stock ShiftGlue SDK gives
FM4 (the same translation built against upstream ShiftGlue, without this project's game
hooks and renderer changes):

| | Xbox 360 | Stock ShiftGlue SDK | FM4 Static Recomp |
|---|---|---|---|
| Resolution | 1280x720 | 1x to 4x of 720p, but the road and FM4's integer-format targets (car shadows) render wrong | 1x to 4x of 720p (up to 5120x2880), switchable live in the game |
| Race frame rate | 60 fps | The console's pacing | 60 fps up to the display's refresh rate (`--fm4_frame_rate`, the GRAPHICS page) |
| Menus and showroom | 60 / 30 fps | The console's pacing | At the vblank rate; menu animations and the showroom camera run at real time |
| Race loading | Console speed | Console speed | Faster (the load runs on a fast guest vblank) |
| Sun and lights | Correct | Screen-filling glare (occlusion queries miscounted) | Correct |
| Anti-aliasing | The game's 2x/4x MSAA | The game's MSAA | Game MSAA on or off, 4x race MSAA, FXAA, FXAA Extreme, resolution scale |
| Graphics settings | Fixed | Fixed | Level of detail, reflection updates, full-rate mirror and bloom, scenery shadows, soft particles, crowds in every race, motion blur and depth of field switches |
| Settings menu | - | Launch options only | In-game GRAPHICS page in My Profile → Options (also F6), saved between sessions; see [Graphics settings](#graphics-settings) |
| VRAM at 3x | - | About 8 GB for the game's share | About 2.5 GB |
| Renderer threads | - | One thread parses and records | Parsing and Vulkan recording on separate threads (about +10 % frame rate at 2x) |
| Disc 2 and DLC | Installed from the disc and Marketplace | Not wired up | Installed at launch from folders you name |
| Frame generation tools | - | Not visible to them | Opt-in DXGI presentation for Lossless Scaling and NVIDIA Smooth Motion |
| Xbox LIVE (online, Storefront, Auction House) | Yes (servers closed) | No | No |
| Kinect | Yes | No | No |
| Input | Controller | XInput gamepad | XInput gamepad (no keyboard play yet) |

## What you need

- Windows 10 version 2004 (build 19041) or newer, 64-bit. Linux is not supported yet (see
  [Roadmap](#roadmap)).
- A GPU with a current Vulkan driver. The renderer is Vulkan only.
- An XInput gamepad (an Xbox controller, or another pad through a mapper).
- Your own Forza Motorsport 4 Disc 1, dumped from the disc or from your console as an ISO
  or a Games on Demand (GOD) copy. Disc 2 and DLC are optional.
- An internet connection for the first setup, and administrator permission once if the
  Microsoft C++ Build Tools are not installed yet.
- Disk space, all on one drive or spread out:
  - the extracted Disc 1: about 7.6 GB;
  - this repository with the downloaded SDK and the build: about 3.1 GB;
  - the build tools setup downloads (CMake, LLVM, Git, Python, xz): about 4.1 GB;
  - Disc 2 and DLC, once installed into your save folder: about 2.8 GB and 2.2 GB.
- About 40 minutes for the first setup on a fast PC with a fast connection, plus about
  10 for the shader warm-up; a rebuild after an update takes minutes.

## Install

Every command below is for PowerShell (Windows PowerShell 5.1, built into Windows, or
PowerShell 7). Use folder paths **without non-English letters and without `[` or `]`**;
paths with spaces work but are untested in every step, so `D:\FM4` style paths are the
safe choice.

### 1. Extract Disc 1

Extract the disc's file system (XDVDFS) into an empty folder, for example `D:\FM4\disc1`:

- from an ISO, with an XDVDFS extractor such as
  [extract-xiso](https://github.com/XboxDev/extract-xiso):
  `extract-xiso -x -d D:\FM4\disc1 FM4-Disc1.iso` (the options go before the ISO);
- from a GOD copy, convert it to an ISO first (for example with God2Iso), then extract
  that.

The folder must hold `default.xex` directly (not in a subfolder). Check that it is the
supported version:

```powershell
Get-FileHash D:\FM4\disc1\default.xex -Algorithm SHA256
```

The hash must be one of:

- `6A7E9AE561CCCA134916F27870A73299408F4AD14F1499063C0F6F6414C2A7E1` (1.0 PAL)
- `3618E85AF83E972DDCC1B0D0DD808E78E29AB4AB3890A15B31C210A6A3E93E09` (1.0 USA)

### 2. Get the source

```powershell
git clone https://github.com/i2kiselev/fm4-static-recomp D:\FM4\fm4-static-recomp
```

Without Git, download the repository as a ZIP from GitHub and extract it to
`D:\FM4\fm4-static-recomp` instead. No submodules are needed: setup downloads the pinned SDK.

### 3. Build

```powershell
cd D:\FM4\fm4-static-recomp
powershell -NoProfile -ExecutionPolicy Bypass -File tools\setup.ps1 -GameRoot D:\FM4\disc1
```

Give `-GameRoot` without a trailing backslash. Setup:

1. installs the Microsoft C++ Build Tools if they are missing (Windows asks for
   administrator permission once; choose Yes). They are about 2 GB, so the installer can take
   10 minutes or more; keep its windows open until they close by themselves;
2. downloads pinned, hash-checked copies of CMake, LLVM/clang, MinGit, xz and an embedded
   Python into `.local\toolchain`;
3. downloads the pinned ShiftGlue SDK revision into `.local\shiftglue-sdk` and builds its
   recompiler;
4. translates `default.xex` into C++ (several minutes);
5. compiles `out\build\win-amd64-release\fm4_static_recomp.exe`;
6. warms the shader cache: the game runs hidden in the background (nothing on screen, no
   sound) through the menus and a short race on five tracks, so that your first races do
   not stall while shaders compile (about 10 minutes; after 25 minutes it skips the
   tracks it has not reached, so a slow PC may take a few minutes longer). It runs only
   when the cache is missing or out of date, so a rebuild after an update usually skips
   it. Add `-SkipWarmUp` to leave it out; the game then compiles shaders while you play,
   with short stalls the first time each effect appears. Pass the same `-StateRoot` you
   play with (see below) so the cache lands in that state folder.

Everything stays inside the repository folder; nothing else on the system changes apart
from the Build Tools. If a step fails, the last lines name it, and
`.local\logs\setup-error.json` and the logs next to it hold the details. Run the same
command again after fixing the cause: finished downloads are not repeated.

### 4. Play

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\run.ps1 -GameRoot D:\FM4\disc1
```

Your save, logs and settings go to `.local\state` inside the repository (`user` is the
save, `config\graphics.toml` the GRAPHICS page's settings). `-StateRoot <folder>` puts them
elsewhere; each state folder is a separate save. Back up `.local\state\user` before you
update or experiment.

Launch options go after the script's own parameters, for example
`... tools\run.ps1 -GameRoot D:\FM4\disc1 --resolution_scale=2`.

### 5. Disc 2 and DLC (optional)

Disc 2 adds cars and tracks; the game asks for it when you pick one of them. Put its four
content packages in a folder (in a dump they sit in a folder named `00000002`), and your
DLC packages in another (subfolders are searched), then launch once with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\run.ps1 -GameRoot D:\FM4\disc1 `
    --fm4_disc2_packages=D:\FM4\disc2 --fm4_dlc_packages=D:\FM4\dlc
```

The packages are extracted into your save folder at launch (a few seconds); packages that
are already installed are skipped, so passing the options every time does no harm. The
log (`.local\state\logs\runtime.log`) ends the install with a `Content: ...` summary.

### Updating

**Close the game first**: Windows locks game files while it runs.

```powershell
git pull
powershell -NoProfile -ExecutionPolicy Bypass -File tools\setup.ps1 -GameRoot D:\FM4\disc1
```


## Graphics settings

In-game **Play Career → My Profile → Options → GRAPHICS**, or **F6** anywhere, opens a settings page drawn in the
game's own style. Each row describes itself on the page, applies at once or from the next
race or screen, and is saved to `config\graphics.toml` in the state folder. A launch option
for one of these settings sets its starting value for that session only; the row says so.

### Presets

GRAPHICS PRESET sets every row below it at once; changing one of those rows makes the
preset CUSTOM. FRAME RATE and RESOLUTION SCALE are not part of a preset.

| Setting | Console | High | Ultra | Experimental |
|---|---|---|---|---|
| Game MSAA | On | Off | Off | Off |
| Race MSAA | 2x | 2x | 2x | 4x |
| Anisotropic filtering | Game | 16x | 16x | 16x |
| Level of detail | Console | High | Ultra | Ultra |
| Scenery shadows | Off | On | On | On |
| Reflection updates | Console | Console | Every race | Double |
| Mirror | Console | Console | Full | Full |
| Soft particles | Off | Off | On | On |
| Crowds | Console | Console | Console | Every race |
| Post AA | Off | FXAA | FXAA Extreme | FXAA Extreme |
| Bloom | Console | Full rate | Full rate | Full rate |
| Motion blur | On | On | On | On |
| Depth of field | On | On | On | On |

### What each setting does

**Frame rate and resolution**

- **FRAME RATE**: the race frame rate, from the console's 60 fps up to your display's
  refresh rate (at most 240). Menus run at 120 fps max. Pick a rate your PC holds (see
  [Benchmarks](#benchmarks)). Without G-Sync or FreeSync, pick your display's refresh rate
  or a whole fraction of it (60 on a 120 Hz display): other rates show some frames for one
  refresh and others for two.
- **VSYNC**: off by default. Turn it on if you see tearing, which happens on a display
  without G-Sync or FreeSync. With either, leave it off: on an Nvidia GPU, vsync shows the
  frames through the desktop, without variable refresh and at a slightly lower frame rate.
- **RESOLUTION SCALE**: renders the game at 1x to 4x of 1280x720 on each axis, up to
  5120x2880. Sharper edges and textures, more GPU load.

**Anti-aliasing and filtering**

- **GAME MSAA**: the game's own multisampling of its scenes (2x in races, 4x in the
  showroom and pre-race screen). Off is faster and leaves edge smoothing to the resolution
  scale; at 1x the game's MSAA always stays on.
- **RACE MSAA**: 4x draws races with twice the console's samples, in three tiles instead of
  two. Heavy on the GPU.
- **POST AA**: FXAA or FXAA Extreme over the finished picture. Extreme smooths more and
  softens more.
- **ANISOTROPIC FILTERING**: keeps textures sharp at grazing angles, such as the road ahead.
  Game leaves each texture the filtering the game asks for; 2x to 16x force it.

**Cars**

- **LEVEL OF DETAIL**: how far away, and for how many cars, the detailed car models stay in
  use. Console keeps the console's switch distances and lets only the two nearest other cars
  use their best model. High moves the switch distances out by about 1.4 times and allows
  the three nearest cars. Ultra moves them out about twice as far, allows the four nearest
  cars, and keeps your own car always on its best model with the best wheels, at more guest
  memory per car. Applies from the next race load.
- **REFLECTION UPDATES**: how often the reflections in car paint, glass and chrome are
  redrawn. The console freezes them in 16-car races and split screen; Every race redraws
  them there too; Double redraws twice as many faces each frame.

**Track and effects**

- **MIRROR**: Full draws the rear-view mirror every frame with up to 16 cars and the cones
  and barriers, where the console halves its rate and shows 6 cars in 12- and 16-car races.
- **SCENERY SHADOWS**: shadows of cones and barriers in races, which the game otherwise
  draws only in replays and photo mode.
- **SOFT PARTICLES**: smoke and dust fade where they meet the ground and cars instead of
  cutting into them, as the game does in replays and photo mode.
- **CROWDS**: Every race adds the crowds the console leaves out of 1- and 16-car races. Uses
  more guest memory.
- **BLOOM**: Full rate updates the glow every frame, where the console skips every second
  one in 16-car races.
- **MOTION BLUR**: the speed blur on the road surface. Off keeps the asphalt as sharp as
  when standing still.
- **DEPTH OF FIELD**: the focus blur of the showroom, pre-race and replay cameras and photo
  mode.

## Launch options


| Option | Meaning |
|---|---|
| `--resolution_scale=1..4` | Internal resolution, a multiple of 1280x720 |
| `--fm4_frame_rate=60..240` | Race frame rate |
| `--vulkan_present_mode=immediate\|fifo` | VSYNC off or on |
| `--swap_post_effect=none\|fxaa\|fxaa_extreme` | Post-process anti-aliasing |
| `--fm4_disc2_packages=<folder>` | Install the Disc 2 packages from this folder |
| `--fm4_dlc_packages=<folder>` | Install every DLC package under this folder |
| `--fh1_async_readbacks=false` | Turn off asynchronous readbacks (see Known issues) |
| `--vulkan_present_via_dxgi=true` | Present through DXGI, so frame generation tools see the frames |
| `--fm4_config_root=<folder>` | Where `graphics.toml` lives (default: the state folder's `config`) |

`tools\run.ps1 -Compat` launches with every rendering shortcut of this project turned off
and the console's graphics settings. If something looks wrong, compare it with a `-Compat`
launch: if it looks the same there, it is most likely the game's own look; if not, please
report it.

### Frame generation

Lossless Scaling and NVIDIA Smooth Motion do not see frames presented through Vulkan's
swapchain here. Launch with `--vulkan_present_via_dxgi=true` to present through DXGI
instead; both tools then work. It is off by default because Smooth Motion's frame pacing
does not look right with it yet.

## What has been tested

- Offline game modes: World Tour (the career), Event List, Free Play (Quick
  Race, Hot Lap), Test Drive and Autovista, plus the garage (upgrades,
  tuning presets, paint and decals), replays and saving.
- World Tour played up to the Expert Division.
- About 30 Event List races completed without an issue.
- Autovista works as intended.
- Photo mode works.

## What has not been tested

- Any mode that uses Xbox Live functions
- Split-screen mode
- Wheel support

## Benchmarks
**Test bench**:

| | |
|---|---|
| CPU | AMD Ryzen 7 5700X3D |
| RAM | 32 GB DDR4-3600 |
| GPU | NVIDIA GeForce RTX 4070 Ti SUPER, 16 GB (driver 610.88) |
| OS | Windows 10 Pro 22H2 |
| Display | 170 Hz |

A scripted race at Bernese Alps against 11 AI cars, one run of about 12 seconds per row.
The race frame rate limit was raised to 240 so that the PC, not the limit, sets the rate;
every setting outside the preset is at its default. Average fps and the 1 % low (the frame
rate over the slowest 1 % of frames) come from the renderer's per-frame timestamps.

**Console preset**

| Resolution scale | Render resolution | Average fps | 1 % low | GPU use | VRAM (game) |
|---|---|---|---|---|---|
| 1x | 1280x720 | 207 | 106 | 54 % | 1.2 GB |
| 2x | 2560x1440 | 194 | 124 | 80 % | 1.6 GB |
| 3x | 3840x2160 | 148 | 76 | 99 % | 2.3 GB |
| 4x | 5120x2880 | 92 | 47 | 99 % | 3.1 GB |

**Ultra preset**

| Resolution scale | Render resolution | Average fps | 1 % low | GPU use | VRAM (game) |
|---|---|---|---|---|---|
| 1x | 1280x720 | 183 | 123 | 54 % | 1.2 GB |
| 2x | 2560x1440 | 173 | 119 | 81 % | 1.6 GB |
| 3x | 3840x2160 | 100 | 54 | 99 % | 2.5 GB |
| 4x | 5120x2880 | 68 | 46 | 99 % | 3.3 GB |

At 1x and 2x the CPU is the limit (one game thread runs a full core); at 3x and 4x the GPU
is the limit.

### Compared with Xenia

The same race on the same PC and disc image in Xenia Canary, build
[`dac2794ca`](https://github.com/xenia-canary/xenia-canary/commit/dac2794ca72e20b2cb5e6ca9e765da8811a19d6d)
(`canary_experimental`, 16 October 2026). Xenia test ran using following settings: Direct3D 12 backend, vsync off, no frame rate limit, `readback_resolve = "fast"`,
`occlusion_query = "strict"` (without these two the game renders incorrectly);
only `draw_resolution_scale` changed between rows. Xenia's figures are the median of three
runs per resolution; this recomp's measurements are of the Console preset rows above.

**Frame rate**

| Scale | Average fps, Xenia | Average fps, this recomp | 1 % low, Xenia | 1 % low, this recomp |
|---|---|---|---|---|
| 1x | 102 | 207 | 54 | 106 |
| 2x | 88 | 194 | 55 | 124 |
| 3x | 68 | 148 | 45 | 76 |
| 4x | 45 | 92 | 35 | 47 |

**GPU use and video memory**

| Scale | GPU use, Xenia | GPU use, this recomp | VRAM (game), Xenia | VRAM (game), this recomp |
|---|---|---|---|---|
| 1x | 38 % | 54 % | 1.5 GB | 1.2 GB |
| 2x | 56 % | 80 % | 4.2 GB | 1.6 GB |
| 3x | 81 % | 99 % | 7.8 GB | 2.3 GB |
| 4x | 90 % | 99 % | 13.0 GB | 3.1 GB |

This recomp runs about twice as fast at every scale and needs a quarter to two fifths of Xenia's
video memory from 2x up. Xenia is CPU-bound up to 2x (its GPU use stays under 60 %).

## Known issues

- **Damage rendering** sometimes causes a texture seam to appear on the hood and front
  bumper of a car. It comes from the game's own damage model, and it is the one visual
  problem known today. Under investigation.
- **Asynchronous readbacks** are on by default and considered stable: no corrupted texture
  showed in the last 10 hours of play, where earlier versions of this project showed
  one within minutes. They keep races smooth by not stalling the GPU; but in theory a texture
  could still be read before its copy lands. Should you ever see a corrupted texture,
  launch with `--fh1_async_readbacks=false` and please report it.
- **Shader stutter:** the first time an effect appears on a PC, the game stops for a
  moment to compile its shaders. Setup's warm-up covers the menus and five tracks; other
  tracks compile as you meet them, once. After changing the graphics card, GAME MSAA or
  the resolution scale, or whenever the stalls come back, run the warm-up again (add
  `-StateRoot` if you play with one):
  `powershell -NoProfile -ExecutionPolicy Bypass -File tools\warm-up.ps1 -GameRoot D:\FM4\disc1`.
  A GPU driver update needs nothing: the next launch rebuilds the cache, with one longer
  loading screen.
- **Frame pacing:** a rare, minor frame-time hitch in races, usually visible only in
  monitoring tools and not in play. A frame rate the PC cannot hold is less smooth, so
  pick one a little below your average.
- **AMD GPUs** require explicit use of `--vulkan_async_submission=false` launch parameter. If not used GPU driver crashes and requires a reboot.
  More info in corresponding issue - https://github.com/i2kiselev/fm4-static-recomp/issues/2 

Limitations rather than bugs:

- **Keyboard play** is not supported yet; a gamepad is needed.
- **Only Vulkan.** ShiftGlue's Direct3D 12 renderer does not run FM4.
- **No launcher yet**, so no profile switching, disc verification or in-app updates.
- Install folders with non-English letters (for example a Cyrillic Windows user name in
  the path) or `[`/`]` are untested or known to break the scripts.
- Only basic tests on AMD GPUs currently 

## Reporting issues

Before you open an issue:

- check [Known issues](#known-issues) and the open issues; add to an existing one rather
  than opening a duplicate;
- check that `default.xex` has a supported hash (see [Install](#1-extract-disc-1));
- update to the latest version (see [Updating](#updating)) and check that the problem is
  still there;
- for anything that looks wrong on screen, try a `-Compat` launch too (see
  [Launch options](#launch-options)) and note whether it looks the same there.

**Title:** `<Area>: <what happens> (<where>)`, for example
`Graphics: car shadows flicker at 3x (Bernese Alps, cockpit view)`. Area is one of
`Crash`, `Graphics`, `Performance`, `Audio`, `Input`, `Setup`, `Disc 2/DLC`, `Gameplay`,
`Other`.

**Body:** copy this template and fill in every line; write "unknown" rather than leaving
one out.

```markdown
### What happens
<!-- What you see, and what you expected instead. -->

### Steps to reproduce
1. <!-- Mode (World Tour, Free Play, ...), track, car, camera, number of AI cars. -->
2.

### How often
<!-- Always / sometimes (about how often) / once. -->

### Version
<!-- Output of `git rev-parse --short HEAD` in the repository folder,
     or the date you downloaded the ZIP. -->

### System
- Windows:
- CPU:
- GPU and driver version:
- RAM:
- Display resolution and refresh rate:

### Settings
- GRAPHICS preset (or attach `config\graphics.toml` from the state folder):
- Launch options:
- Same with `-Compat`? <!-- yes / no / not tried; graphics issues only -->
- Disc 2 installed / DLC installed:

### Attachments
<!-- runtime.log from the state folder's logs (.local\state\logs\runtime.log by default).
     Setup problems: .local\logs\setup-error.json and the logs next to it.
     Graphics problems: a screenshot or a short video. -->
```

Logs can contain folder paths with your Windows user name; edit them out if you prefer.
Never attach game files, disc images or a built executable.

## Roadmap

1. **A launcher for installation and play**: installs Disc 1, Disc 2 and DLC from your ISO
   or Games on Demand copies and checks them, builds the game, keeps separate profiles
   (saves), holds the GRAPHICS settings and updates to new versions while the previous one
   stays playable.
2. **Keyboard support**, with the game's controller button icons replaced by keyboard keys.
3. **Project Forza Plus**: explore support for the mod.
4. **Racing wheels**, starting with MOZA wheels.
5. **AMD GPU** testing 
6. **Linux**: a native Linux build of the game and the launcher. Windows is the only
   supported system today.
7. Test other disc releases for compatibility (e.g. Forza Motorsport 4: Essentials Edition)

## Repository layout

| Path | What it is |
|---|---|
| `src/` | The host app: game hooks, built-in defaults, Disc 2 and DLC install, the GRAPHICS page and its UI |
| `config/rexglue/` | The recompiler's input: function analysis, hooks, the manifest |
| `config/*.json` | The pinned toolchain and SDK, the supported discs' file lists |
| `tools/setup.ps1`, `build.ps1`, `codegen.ps1`, `run.ps1` | Setup, build and launch |
| `tools/warm-up.ps1`, `config/warm-up/` | The shader warm-up and the profile it plays with |
| `tools/` (the rest) | Development tools: scripted races and measurements, dumps, disassembly helpers. Several assume the author's folder layout |

## AI use

Code, scripts, documentation and reverse engineering of the game binary are AI-assisted.

## Credits

- [Pinyon Shift](https://github.com/arcanite24/pinyon-shift) by arcanite24: the
  *Forza Horizon* recompilation whose native renderer, host UI and build scripts this
  project builds on (see `THIRD_PARTY_NOTICES.md`).
- [ShiftGlue SDK](https://github.com/arcanite24/shiftglue-sdk), Pinyon Shift's fork of the
  [ReXGlue SDK](https://github.com/rexglue/rexglue-sdk): the recompiler and runtime. This
  project uses [a fork of it](https://github.com/i2kiselev/shiftglue-sdk) (branch
  `fm4-static-recomp`) with FM4's renderer fixes.
- [Xenia](https://xenia.jp), from which much of the runtime and GPU emulation in ReXGlue
  derives.
- [fm4-recomp](https://github.com/GabrielCRadu/fm4-recomp), an earlier FM4 recompilation
  on ReXGlue, which inspired this project.

## License

The source in this repository is under the BSD 3-Clause License (`LICENSE`). Parts adapted
from Pinyon Shift keep their own BSD 3-Clause notice (`THIRD_PARTY_NOTICES.md`). The license
covers this project's code only: the game, its assets and any executable built from its
translation are not covered, and must not be redistributed.
