# Steam Deck and Linux

[← Back to the README](../README.md)

A native Linux build, not Proton: the game is translated and compiled on the Deck from your own
disc, as on Windows, and runs on SteamOS's own Vulkan driver (RADV). The approach and toolchain
follow [Pinyon Shift's Linux build](https://github.com/arcanite24/pinyon-shift/blob/dev/site/pages/linux.md).
The Windows launcher does not run under Proton: its build step needs PowerShell and
Microsoft's compiler runtime.

Everything happens in Desktop Mode, in a terminal (Konsole), from a clone of this repository.

## 1. Set up and build

```bash
git clone https://github.com/i2kiselev/fm4-static-recomp ~/FM4
cd ~/FM4
python3 tools/fm4_linux.py setup --iso ~/Downloads/FM4_disc1.iso --disc2 ~/Downloads/FM4_disc2.iso
```

| Option | What it does |
|---|---|
| `--iso` | Disc 1 image. It is checked against `config/supported-discs.json` (1.0 PAL or 1.0 USA) and extracted to `.local/game/disc1`. |
| `--game` | An already extracted Disc 1 folder instead of `--iso`. |
| `--disc2` | Optional: a Disc 2 image or folder. Its four packages are checked and copied to `.local/game/disc2`, and installed into the save at the next start. |
| `--dlc` | Optional: a folder of DLC packages, installed at each start. |
| `--container NAME` | Build in a distrobox instead of downloading the pinned toolchain (see below). Remembered. |
| `--build-only` | Rebuild after a `git pull` without checking the disc again. |

Setup downloads the pinned toolchain (`config/linux-toolchain.json`: CMake, Ninja, LLVM 20.1.8
and the Steam Runtime 3 "sniper" SDK, about 3.2 GB), fetches the pinned ShiftGlue SDK, translates
the game and compiles it into `out/build/linux-amd64-release`, then builds the shader cache. No
root, developer mode or system compiler is needed.

### On a slow connection: build in a distrobox

Instead of the 3.2 GB toolchain, an Ubuntu 24.04 distrobox with clang 20 builds the game. Only
the build runs there; the game runs on SteamOS itself.

```bash
distrobox create --name fm4-build --image ubuntu:24.04
distrobox enter fm4-build -- sh -c 'curl -fsSL https://apt.llvm.org/llvm.sh | sudo bash -s 20 &&
  sudo apt-get install -y lld-20 cmake ninja-build git python3 pkg-config libx11-dev libxext-dev \
    libxrandr-dev libxcursor-dev libxi-dev libxfixes-dev libxss-dev libxtst-dev libxkbcommon-dev \
    libwayland-dev wayland-protocols libdecor-0-dev libegl-dev libasound2-dev libpulse-dev \
    libpipewire-0.3-dev libdbus-1-dev libudev-dev &&
  sudo ln -sf clang-20 /usr/bin/clang && sudo ln -sf clang++-20 /usr/bin/clang++'
python3 tools/fm4_linux.py setup --container fm4-build --iso ~/Downloads/FM4_disc1.iso
```

`--pinned-toolchain` switches back.

### Memory

Each compile job takes up to about 2 GB. Setup picks the number of jobs from the memory free
when it starts, so close Firefox and other large programs first; `FM4_BUILD_JOBS=2` sets it
by hand. A compiler killed for lack of memory shows up as a failed build: run the same command
again and it resumes.

## 2. Play

```bash
python3 tools/fm4_linux.py launch
```

[Launch options](launch-options.md) go after `--`, for example
`python3 tools/fm4_linux.py launch -- --resolution_scale=2`. The save, settings, logs and
shader cache live in `.local/state` (`--state-root` picks another folder); the log is
`.local/state/logs/runtime.log`.

The first start compiles the shader cache's pipelines for the Deck's GPU before the title screen.

## 3. Add it to Steam (Game Mode)

```bash
python3 tools/fm4_linux.py shortcuts add
```

This writes `.local/fm4-static-recomp.sh`, adds it to Steam as a non-Steam game named
*Forza Motorsport 4 (Linux)*, and adds a desktop menu entry. Other shortcuts, such as one to the
Windows launcher, are left alone; each Steam user's `shortcuts.vdf` is backed up once, beside
it, as `shortcuts.vdf.fm4-backup`. Steam is closed while its shortcut file is
written and started again afterwards. Do **not** choose a compatibility tool (Proton) for the
entry: the game is a Linux program. In Game Mode, Steam Input presents the Deck's controls as a
gamepad. `shortcuts remove` takes the entries out again.

## Graphics on the Deck

The defaults are the PC's. On the Deck, open the GRAPHICS page (**BACK + START**, that is
**View + Menu**) and start from the **Console** preset at resolution scale **1x**. Pick a
FRAME RATE the Deck holds in races. VSYNC on avoids tearing. **FSR 2** at 1x works as
anti-aliasing. Leave *Manual GPU Clock* off in the Deck's Performance settings: Pinyon Shift
found that it can pin the GPU at its lowest clock.

## Tested

On a Steam Deck with SteamOS (glibc 2.43, Mesa RADV VANGOGH), built in an Ubuntu 24.04 distrobox
with clang 20.1.8: the build, the shader cache (4819 of 4819 pipelines), Disc 2's and 16 DLC packages' install,
and play with in-game sound. Races have not been timed yet.

## Sound on Linux

PipeWire asks for audio in 1024-sample chunks, four of the game's 256-sample frames at once,
faster than the game's audio mixer refills its output; every chunk then repeated the previous
21 ms and the game's sound (not the videos') played slowed and broken up. The game asks SDL for
one frame per request instead (`SDL_AUDIO_DEVICE_SAMPLE_FRAMES=256`, which the environment can
override).

## Threads on the Deck

The SDK's `linux_thread_cores=auto` pins threads to cores by their entry points, with a layout
measured for Forza Horizon (Pinyon Shift). FM4's threads have other entry points, so that
layout would put all of them on two SMT siblings. FM4 sets it to `off`, so the scheduler places
the threads. A layout of
your own can still be passed as a launch option, for example
`-- --linux_thread_cores=*=0-7`.

## Not on Linux

- The Windows launcher (profiles, updates, crash reports): use the commands above.
  Rebuild after an update with `git pull` and `setup --build-only`.
- Input recording for scripted replays (`FM4_STATIC_RECOMP_INPUT_RECORD`) reads XInput and
  is Windows only; replaying a script works.
- DLSS needs an NVIDIA GPU; the Deck has FSR 2 and FSR 3.
