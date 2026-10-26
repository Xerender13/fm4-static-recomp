# FM4 Static Recomp

An unofficial static recompilation of the Xbox 360 release of *Forza Motorsport 4* for
Windows, built on your own PC from your own disc.

The game's PowerPC code is translated ahead of time into C++ by the
[ShiftGlue SDK](https://github.com/arcanite24/shiftglue-sdk) and compiled for x86-64. Its GPU
commands run on ShiftGlue's native Vulkan renderer, written for
[Pinyon Shift](https://github.com/arcanite24/pinyon-shift) (*Forza Horizon*) and extended
here for FM4.

> **Experimental release.** The offline game modes play without known blocking problems
> (see [Tested](#tested)). The [launcher](#launcher) sets everything up from your discs; the
> [manual guide](#install) does the same from the command line.

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
- [Features](#features)
- [Supported game](#supported-game)
- [What you need](#what-you-need)
- [Launcher](#launcher)
- [Install](#install)
- [Graphics settings](#graphics-settings)
- [Keyboard](#keyboard)
- [Launch options](#launch-options)
- [Tested](#tested)
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

## Features

Compared with the original console, and with the same translation built against the stock
ShiftGlue SDK (without this project's game hooks and renderer changes):

| | Xbox 360 | Stock ShiftGlue SDK | FM4 Static Recomp |
|---|---|---|---|
| Resolution | 1280x720 | 1x to 4x of 720p, but the road and car shadows render wrong | 1x to 4x of 720p (up to 5120x2880), switchable live |
| Race frame rate | 60 fps | The console's pacing | 60 fps up to the display's refresh rate (at most 240) |
| Menus and showroom | 60 / 30 fps | The console's pacing | The race frame rate; animations and the showroom camera run at real time |
| Race loading | Console speed | Console speed | Faster |
| Sun and lights | Correct | Screen-filling glare | Correct |
| Anti-aliasing | The game's 2x/4x MSAA | The game's MSAA | Showroom and race MSAA 4x, 2x or off each, FXAA, FXAA Extreme |
| Graphics settings | Fixed | Launch options only | In-game GRAPHICS page with presets and per-setting switches, saved between sessions |
| VRAM at 3x | - | About 8 GB | About 2.5 GB |
| Input | Controller | XInput gamepad | XInput gamepad and **keyboard**, with keycap button prompts |
| Disc 2 and DLC | From the disc and Marketplace | Not wired up | Installed at launch from folders you name |
| Frame generation tools | - | Not visible to them | Opt-in for Lossless Scaling and NVIDIA Smooth Motion |
| Xbox LIVE, Kinect | Yes (LIVE servers closed) | No | No |

## Supported game

| | |
|---|---|
| Game | Forza Motorsport 4, Xbox 360, retail, title ID `4D530910` |
| Disc 1, 1.0 PAL | Europe, Australia, media ID `61D62D9F` |
| Disc 1, 1.0 USA | USA, media ID `26FF1101` |
| Disc 2 | Optional: the install disc's four content packages (more cars and tracks) |
| DLC | Optional: Marketplace packages for title `4D530910` |

Both Disc 1 releases carry the same code; only their executables' headers differ. Any other
version of `default.xex` will not work, because the translation is made for this exact code.
**The 1.1 re-release (Rev 1) of Disc 1 is not supported** (media ID `42C67824`); its Disc 2
works. Other releases are untested. `config/supported-discs.json` lists every Disc 1 file
with its size and hash.

## What you need

- Windows 10 version 2004 (build 19041) or newer, 64-bit.
- A GPU with a current Vulkan driver.
- An XInput gamepad (an Xbox controller, or another pad through a mapper), or the keyboard.
- Your own Forza Motorsport 4 Disc 1, as an ISO or a Games on Demand (GOD) copy. Disc 2 and
  DLC are optional.
- An internet connection for the first setup. No administrator permission and no Visual
  Studio: setup downloads the parts of Microsoft's C++ runtime and Windows SDK the build
  needs (about 300 MB) once you accept their licences.
- About 17 GB of disk space (Disc 1 7.6 GB, the repository with the SDK and build 3.3 GB,
  the build tools with Microsoft's files and their downloads 6.3 GB), plus about 5 GB for
  Disc 2 and DLC.
- About 40 minutes for the first setup; a rebuild after an update takes minutes.

## Launcher

1. Download `FM4StaticRecomp-Launcher.zip` from the
   [releases](https://github.com/i2kiselev/fm4-static-recomp/releases) and extract it into a
   short folder you own, for example `D:\FM4`. Not into `Program Files`, and not by starting
   the launcher from inside the zip's window: Windows runs it from a temporary folder then.
2. Start `FM4StaticRecompLauncher.exe`. Its first screen has five steps:
   - **Disc 1**: drop or choose your ISO, GOD folder or extracted folder. It is verified and
     extracted.
   - **Disc 2** (optional): an ISO, a GOD folder or the folder with its four packages, or
     **Later** (CONTENT installs it any time). It is verified now and installed after the
     build, so keep it where it is until then.
   - **Microsoft licence**: the build downloads Microsoft's C++ runtime and Windows SDK files
     from Microsoft. Read and accept their licences, or decline and use Microsoft's installer
     instead, which asks for administrator permission once.
   - **Build**: the launcher downloads its build tools, translates and compiles the game.
   - **Your profile**: a name; the gamertag starts as it.
3. **PLAY**. PROFILE beside it adds, renames, imports and deletes profiles (separate saves);
   CONTENT installs Disc 2 and DLC; SETTINGS has the GRAPHICS page's settings, the game's
   language, launch options and advanced options.

Everything lives in `data\` beside the launcher: the extracted disc, the build tools, the
builds, your saves and settings. Move or back up the folder as a whole. A first setup with
Disc 2 took 19 GB there (Disc 1 7.6 GB, Disc 2 2.8 GB, the build tools, and the game's source and
build), and setup asks for a 2 GB margin on top while it works. An update builds the new
version beside the old one, about 3 GB more until the older one is removed.

The folder's limits come from the build tools: the path of `data\` may be at most 54
characters, may not hold `[` or `]`, and may not hold parentheses together with non-English
letters. The launcher checks this at start and names the folder if it cannot use it.

**Updates**: the launcher checks GitHub when it starts (and from CHECK FOR UPDATES).
UPDATE TO downloads the new release and restarts; the new version then builds beside the
current one, which you can keep playing, and GO BACK TO switches back. Saves, content and
settings are kept; each profile's save is zipped to `data\backups\` before a new version first
runs it.

## Install

The manual guide, for the command line. Every command is for PowerShell. Folder paths may hold
non-English letters and spaces, but **no `[` or `]`**, and no parentheses when the path has non-English letters: CMake, which the
build uses, cannot run from such folders, and setup stops and says so.

### 1. Extract Disc 1

Extract the disc's file system into an empty folder, for example `D:\FM4\disc1`:

- from an ISO, with [extract-xiso](https://github.com/XboxDev/extract-xiso):
  `extract-xiso -x -d D:\FM4\disc1 FM4-Disc1.iso` (the options go before the ISO);
- from a GOD copy, convert it to an ISO first (for example with God2Iso), then extract it.

`default.xex` must sit directly in that folder. Check its hash:

```powershell
Get-FileHash D:\FM4\disc1\default.xex -Algorithm SHA256
```

It must be one of:

- `6A7E9AE561CCCA134916F27870A73299408F4AD14F1499063C0F6F6414C2A7E1` (1.0 PAL)
- `3618E85AF83E972DDCC1B0D0DD808E78E29AB4AB3890A15B31C210A6A3E93E09` (1.0 USA)

### 2. Get the source

```powershell
git clone https://github.com/i2kiselev/fm4-static-recomp D:\FM4\fm4-static-recomp
```

Or download the repository as a ZIP and extract it there. No submodules are needed.

### 3. Build

```powershell
cd D:\FM4\fm4-static-recomp
powershell -NoProfile -ExecutionPolicy Bypass -File tools\setup.ps1 -GameRoot D:\FM4\disc1 -AcceptMicrosoftLicense
```

`-AcceptMicrosoftLicense` accepts the licences of the Microsoft files setup downloads: the
[Visual Studio Build Tools licence](https://go.microsoft.com/fwlink/?LinkId=2179911) (MSVC's C
and C++ runtime headers and libraries) and the
[Windows SDK licence](https://aka.ms/WinSDKLicenseURL). They come straight from Microsoft and
stay on your PC; `tools\microsoft-licences.ps1` prints the current links. Without the switch,
setup uses an installed Visual Studio or Build Tools that holds the 14.44 toolset, or stops and
says so. `-ToolchainSource VisualStudio` uses Microsoft's installer instead (it asks for
administrator permission once and can take 10 minutes or more).

Give `-GameRoot` without a trailing backslash. Setup:

1. downloads pinned, hash-checked copies of CMake, Ninja, LLVM/clang, MinGit, xz, Python and
   the ShiftGlue SDK into `.local`;
2. downloads Microsoft's C++ runtime (the MSVC 14.44 headers and libraries, from Microsoft's
   Visual Studio channel) and the Windows SDK (from Microsoft's NuGet packages) into
   `.local\toolchain`, each checked against Microsoft's published hash;
3. translates `default.xex` into C++ and compiles
   `out\build\win-amd64-release\fm4_static_recomp.exe`;
4. builds the shader cache from your Disc 1 (about a second), so your first races do not
   stall while shaders compile. It adds the shaders and pipelines listed in
   `config\shaders` to your cache and never removes anything; the repository ships only
   that list (hashes, render states, edit positions), no game data. The first launch then
   compiles the listed pipelines for your GPU before the title screen: about 20 seconds on
   the test bench below, longer on a processor with fewer threads. `-SkipShaderCache`
   leaves it out. Pass the same `-StateRoot` you play with so the cache lands there.

Nothing outside the repository folder changes. If a step fails,
the last lines name it and `.local\logs\setup-error.json` holds the details. Running the
same command again resumes: finished downloads are not repeated.

### 4. Play

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\run.ps1 -GameRoot D:\FM4\disc1
```

Your save, logs and settings go to `.local\state` in the repository (`user` is the save).
`-StateRoot <folder>` puts them elsewhere; each state folder is a separate save, and setup
takes the same `-StateRoot` so the shader cache lands there. Back up `.local\state\user`
before you update or experiment.

Launch options go after the script's own parameters, for example
`... tools\run.ps1 -GameRoot D:\FM4\disc1 --resolution_scale=2`.

### 5. Disc 2 and DLC (optional)

Put the four Disc 2 content packages in one folder (in a dump they sit in a folder named
`00000002`) and your DLC packages in another (subfolders are searched), then launch once
with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\run.ps1 -GameRoot D:\FM4\disc1 `
    --fm4_disc2_packages=D:\FM4\disc2 --fm4_dlc_packages=D:\FM4\dlc
```

The packages are installed into your save folder in a few seconds; installed ones are
skipped, so passing the options every time does no harm. `.local\state\logs\runtime.log`
ends the install with a `Content: ...` summary.

### Updating

**Close the game first**, then:

```powershell
git pull
powershell -NoProfile -ExecutionPolicy Bypass -File tools\setup.ps1 -GameRoot D:\FM4\disc1
```

Your save, settings, Disc 2 and DLC stay as they are.

## Graphics settings

**Play Career → My Profile → Options → GRAPHICS**, or **F6** anywhere, opens a settings page
in the game's own style. Each row explains itself, applies at once or from the next race or
screen, and is saved to `config\graphics.toml` in the state folder. A launch option for one
of these settings sets its starting value for that session only.

### Presets

GRAPHICS PRESET sets every row below it; changing one of them makes the preset CUSTOM.
FRAME RATE and RESOLUTION SCALE are not part of a preset.

| Setting | Console | High | Ultra | Experimental |
|---|---|---|---|---|
| Showroom MSAA | 4x | 2x | 2x | 4x |
| Race MSAA | 2x | 2x | 2x | 4x |
| Anisotropic filtering | Game | 16x | 16x | 16x |
| Level of detail | Console | High | Ultra | Ultra |
| Scenery shadows | Off | Off | Off | On |
| Reflection updates | Console | Console | Every race | Double |
| Mirror | Console | Console | Full | Full |
| Soft particles | Off | Off | On | On |
| Crowds | Console | Console | Console | Every race |
| Post AA | Off | FXAA | FXAA Extreme | FXAA Extreme |
| Bloom | Console | Full rate | Full rate | Full rate |
| Motion blur | On | On | On | On |
| Depth of field | On | On | On | On |
| Car dirt | On | On | On | On |

### What each setting does

| Setting | Effect |
|---|---|
| FRAME RATE | Frame rate of races, menus and the showroom, 60 fps up to your display's refresh rate (at most 240). Pick a rate your PC holds (see [Benchmarks](#benchmarks)). Without G-Sync or FreeSync, pick the refresh rate or a whole fraction of it. |
| VSYNC | Off by default, which suits G-Sync and FreeSync displays. Turn it on if you see tearing. |
| RESOLUTION SCALE | 1x to 4x of 1280x720 on each axis, up to 5120x2880. |
| SHOWROOM MSAA | The game's own multisampling in the showroom, menus, pre-race, replays and photo mode: 4x as on the console, 2x or off. Fewer samples are faster and leave edge smoothing to the resolution scale. From the next screen. |
| RACE MSAA | The same in races, the countdown and the results: 2x as on the console (4x in races alone on track), 4x (heavy on the GPU) or off. From the next race. |
| POST AA | FXAA or FXAA Extreme over the finished picture; Extreme smooths and softens more. |
| ANISOTROPIC FILTERING | Keeps textures sharp at grazing angles. Game keeps what the game asks for; 2x to 16x force it. |
| LEVEL OF DETAIL | How far away, and for how many cars, detailed models stay in use. High moves the switch distances out about 1.4 times, Ultra about twice and keeps your own car on its best model. From the next race load. |
| REFLECTION UPDATES | How often car paint, glass and chrome reflections are redrawn. The console freezes them in 16-car races and split screen; Every race redraws them there too; Double redraws twice as much each frame. |
| MIRROR | Full draws the rear-view mirror every frame with up to 16 cars, where the console halves its rate and shows 6 cars in large races. |
| SCENERY SHADOWS | Experimental, only in the Experimental preset. Shadows of cones, barriers and other scenery in races, otherwise only in replays and photo mode. Known bug: a shadow vanishes or flickers as its object nears the edge of the screen, most visibly on large objects ([#3](https://github.com/i2kiselev/fm4-static-recomp/issues/3)). |
| SOFT PARTICLES | Smoke and dust fade into the ground and cars instead of cutting into them. |
| CROWDS | Every race adds the crowds the console leaves out of 1- and 16-car races. |
| BLOOM | Full rate updates the glow every frame, where the console skips every second one in 16-car races. |
| MOTION BLUR | The speed blur on the road surface. |
| DEPTH OF FIELD | The focus blur of the showroom, pre-race and replay cameras and photo mode. |
| CAR DIRT | The dirt cars gather in a race. Off leaves cars clean. |

## Keyboard

The keyboard plays beside a gamepad, with a layout after Forza Horizon 6's. While the
keyboard is the last device you used, the game's button prompts show keycaps of the bound
keys, and English prompt sentences ("PRESS THE BACK BUTTON...") name them too; touching the
pad brings the controller buttons back.

| Driving | Keys | | Menus | Keys |
|---|---|---|---|---|
| Throttle / brake | W / S | | Move | W A S D, arrow keys |
| Steer | A / D | | Select (A) | Enter, Space |
| Handbrake | Space | | Back (B) | Esc, Backspace |
| Gear up / down | E / Q | | X / Y | X / Y |
| Clutch | Shift | | LB / RB | Q / E |
| Camera | Tab | | LT / RT | Z / C |
| Rewind | R | | Right stick | I J K L |
| Look around (back: Down) | arrow keys | | BACK / START | Tab / P |
| Look straight back | C | | | |
| Telemetry | T, G | | | |
| Pause | Esc | | | |

Driving keys apply while the car is on track and the game is not paused; the pre-race
screen, pause menu, replays and results use the menu keys. Steering, pedals and looking
around ramp up while a key is held and ease back on release. The mouse is not used.

<details>
<summary>Rebinding keys</summary>

Create `config\controls.toml` in the state folder and add `menu_<button>` or
`driving_<button>` lines. `<button>` is one of `a`, `b`, `x`, `y`, `left_shoulder`,
`right_shoulder`, `left_trigger`, `right_trigger`, `lstick_up`, `lstick_down`,
`lstick_left`, `lstick_right`, `lstick_press`, the same five for `rstick_`, `dpad_up`,
`dpad_down`, `dpad_left`, `dpad_right`, `back`, `start`. A value lists keys separated by
commas:

```toml
driving_x = "Q,Numpad1"
```

Key names are `A`..`Z`, `0`..`9`, `F1`..`F24`, `Up`, `Down`, `Left`, `Right`, `Space`,
`Return`, `Escape`, `Backspace`, `Tab`, `Shift`, `Control`, `Alt`, `Numpad0`..`Numpad9` and
the like. The ramps, in seconds from rest to full and back, are `ramp_steer_seconds` (0.17),
`ramp_steer_return_seconds` (0.10), `ramp_pedal_seconds` (0.10),
`ramp_pedal_return_seconds` (0.08) and `ramp_look_seconds` (0.15). Changes apply at the
next launch.

</details>

## Launch options

| Option | Meaning |
|---|---|
| `--resolution_scale=1..4` | Internal resolution, a multiple of 1280x720 |
| `--fm4_frame_rate=60..240` | Frame rate of races, menus and the showroom |
| `--vulkan_present_mode=immediate\|fifo` | VSYNC off or on |
| `--swap_post_effect=none\|fxaa\|fxaa_extreme` | Post-process anti-aliasing |
| `--fm4_disc2_packages=<folder>` | Install the Disc 2 packages from this folder |
| `--fm4_dlc_packages=<folder>` | Install every DLC package under this folder |
| `--fm4_skip_intro=true` | Skip the startup disclaimer and studio videos |
| `--fm4_language=<code>` | The game's language: `en-US` (default), `en-GB`, `fr`, `de`, `it`, `es`, `es-MX`, `pl`, `ru`, `ja`, `ko`, `zh-TW`, `pt-BR`, `cs`, `hu` or `nl` |
| `--fm4_config_root=<folder>` | Where `graphics.toml` and `controls.toml` live (default: the state folder's `config`) |
| `--fh1_async_readbacks=false` | Turn off asynchronous readbacks, if you ever see a corrupted texture |
| `--vulkan_present_via_dxgi=true` | Present through DXGI, for frame generation tools |

**Languages**: the game shows every language on Disc 1. Korean, Traditional Chinese,
Brazilian Portuguese, Czech, Hungarian and Dutch have no voice-over of their own and keep
the English one. With the keyboard, some prompts keep the controller's wording in languages
other than English.

**`-Compat`**: `tools\run.ps1 -Compat` turns off every rendering shortcut of this project
and uses the console's graphics settings. If something looks wrong, compare with a
`-Compat` launch: if it looks the same there, it is most likely the game's own look.

**Frame generation**: Lossless Scaling and NVIDIA Smooth Motion do not see frames presented
through the Vulkan swapchain. `--vulkan_present_via_dxgi=true` presents through DXGI
instead, and both tools then work. It is off by default because Smooth Motion's frame
pacing does not look right with it yet.

**Debug and A/B options**: switches of individual rendering fixes, debug views of the
upscaler's inputs and renderer diagnostics are listed in [DEBUG_OPTIONS.md](DEBUG_OPTIONS.md).

## Tested

- Offline modes: World Tour (played up to the Expert Division), Event List (about 30 races
  without an issue), Free Play (Quick Race, Hot Lap), Test Drive, Autovista and photo mode.
- The garage (upgrades, tuning presets, paint and decals), replays and saving.
- Not tested: Xbox LIVE modes, split screen, racing wheels, and most AMD and Intel GPUs.

## Benchmarks

| Test bench | |
|---|---|
| CPU | AMD Ryzen 7 5700X3D |
| RAM | 32 GB DDR4-3600 |
| GPU | NVIDIA GeForce RTX 4070 Ti SUPER, 16 GB (driver 610.88) |
| OS | Windows 10 Pro 22H2 |
| Display | 170 Hz |

A scripted race at Bernese Alps against 11 AI cars, about 12 seconds per row, with the
frame rate limit raised to 240 so the PC sets the rate. The 1 % low is the frame rate over
the slowest 1 % of frames.

**Console preset**

| Scale | Render resolution | Average fps | 1 % low | GPU use | VRAM (game) |
|---|---|---|---|---|---|
| 1x | 1280x720 | 207 | 106 | 54 % | 1.2 GB |
| 2x | 2560x1440 | 194 | 124 | 80 % | 1.6 GB |
| 3x | 3840x2160 | 148 | 76 | 99 % | 2.3 GB |
| 4x | 5120x2880 | 92 | 47 | 99 % | 3.1 GB |

**Ultra preset**

| Scale | Render resolution | Average fps | 1 % low | GPU use | VRAM (game) |
|---|---|---|---|---|---|
| 1x | 1280x720 | 183 | 123 | 54 % | 1.2 GB |
| 2x | 2560x1440 | 173 | 119 | 81 % | 1.6 GB |
| 3x | 3840x2160 | 100 | 54 | 99 % | 2.5 GB |
| 4x | 5120x2880 | 68 | 46 | 99 % | 3.3 GB |

At 1x and 2x the CPU is the limit (one game thread runs a full core); at 3x and 4x, the GPU.

### Compared with Xenia

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

## Known issues

Known problems are tracked as
[open issues](https://github.com/i2kiselev/fm4-static-recomp/issues).

## Reporting issues

Before you open an issue:

- check the [open issues](https://github.com/i2kiselev/fm4-static-recomp/issues) and add
  to an existing one rather than opening a duplicate;
- check that `default.xex` has a supported hash (see [Install](#1-extract-disc-1));
- update to the latest version (see [Updating](#updating)) and check the problem is still
  there;
- for anything that looks wrong on screen, try a `-Compat` launch and note whether it looks
  the same there.

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

1. **Project Forza Plus** support.
2. **Racing wheels**, starting with MOZA wheels.
3. **AMD GPU** testing.
4. **Linux**: a native build of the game and the launcher.
5. **Other disc releases**, such as the Essentials Edition.

## Repository layout

| Path | What it is |
|---|---|
| `src/` | The host app: game hooks, built-in defaults, Disc 2 and DLC install, the GRAPHICS page |
| `src/input/` | Keyboard play: bindings, driving smoothing, keycap prompts |
| `tests/` | Unit tests of the host app |
| `config/rexglue/` | The recompiler's input: function analysis, hooks, the manifest |
| `config/*.json` | The pinned toolchain and SDK, the supported discs' file lists |
| `tools/setup.ps1`, `build.ps1`, `codegen.ps1`, `run.ps1` | Setup, build and launch |
| `launcher/` | The launcher (Avalonia, .NET 8): its core library with tests, and the app |
| `tools/package-release.ps1` | Builds the release assets and the players' zip |
| `config/shaders/` | The pipeline list and vertex shader table setup builds the shader cache from |
| `tools/shader_list.py`, `tools/warm-up.ps1`, `config/warm-up/` | Development tools that make that list: a scripted warm-up, its profile, list merging and checks |
| `DEBUG_OPTIONS.md` | Debug views, A/B switches of rendering fixes and diagnostics |
| `tools/` (the rest) | Development tools; several assume the author's folder layout |

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
