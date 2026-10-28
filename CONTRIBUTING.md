# Contributing

Thanks for helping with FM4 Static Recomp. Bug reports, test results on hardware the project
has not seen yet, documentation fixes and code are all welcome.

## Ground rules

> [!CAUTION]
> Never commit, attach or link game code, game assets, disc images, the generated translation
> (codegen's output under `.local/generated/`) or a built `fm4_static_recomp.exe`. This includes strings,
> textures or fonts taken from the disc, in tests too: tests that need game files read them
> from the contributor's own extracted disc and skip when it is missing.

- Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md).
- Contributions are accepted under the project's [BSD 3-Clause License](LICENSE).
- Code adapted from another project keeps its notice; add it to `THIRD_PARTY_NOTICES.md`.
- The maintainer has no Kinect, so Kinect changes cannot be tested on their side: include a
  recording of it working.

## Reporting a bug

Follow [Reporting issues](guide/reporting-issues.md): its checklist, title format and template.
A report with `runtime.log`, the settings and a `-Compat` comparison is usually enough to act on.

Test results are useful even without a bug: a GPU, driver, disc release or game mode that works
is worth an issue too, so [Tested](guide/performance.md#tested) can list it.

## Before you write code

- **Small fixes** (a typo, a clear bug with an obvious fix): open a pull request directly.
- **Anything bigger** (a new setting, a hook, a renderer change, a [roadmap](README.md#roadmap)
  item): open an issue or comment on the existing one first, so the approach is agreed before
  you spend the time.

## Where a change belongs

| What you change | Where |
|---|---|
| Game hooks, defaults, the GRAPHICS page, input, Disc 2/DLC install | This repository, `src/` |
| Function analysis, hooks the recompiler needs | This repository, `config/rexglue/` |
| Setup, build and release scripts | This repository, `tools/` |
| The launcher | This repository, `launcher/` |
| Renderer, GPU emulation, runtime, recompiler | The SDK fork, [i2kiselev/shiftglue-sdk](https://github.com/i2kiselev/shiftglue-sdk), branch `fm4-static-recomp` |

The build fetches the SDK at the revision pinned in `config/release-toolchain.json`. To work on
the SDK, clone the fork, set `FM4_SDK_ROOT` to its folder, and the build uses it instead. An SDK
change lands as two pull requests: one to the fork, then one here that moves the pin.

Fixes that are not specific to FM4 are better sent upstream to
[arcanite24/shiftglue-sdk](https://github.com/arcanite24/shiftglue-sdk), so Pinyon Shift gets
them as well.

## Setting up

Follow the [manual install](guide/manual-install.md): `tools\setup.ps1` provisions the
toolchain, translates your Disc 1 and builds the game. After that, `tools\build.ps1` rebuilds
and `tools\run.ps1` starts the game; `tools\codegen.ps1` regenerates the translation after a
change under `config/rexglue/`.

Many scripts in `tools/` beyond these are the maintainer's development tools and assume their
folder layout; you do not need them to contribute.

## Testing

Run the tests for what you touched, and say in the pull request what you ran.

| Area | How |
|---|---|
| Host app (`src/`) | Configure with `-DFM4_BUILD_TESTS=ON` and build the `fm4_unit_tests` target. Tests that read disc files expect your extracted Disc 1 at `../extracted/fm4` and skip without it |
| Launcher | `dotnet test launcher\Fm4StaticRecomp.Launcher.sln` (needs the .NET 8 SDK) |
| Toolchain scripts | `python -m unittest discover -s tools/toolchain/tests -t tools` |
| Rendering | `python -I tools\render-test.py` runs against a pinned save that is not in the repository; the maintainer runs it on renderer pull requests |

Unit tests do not reach most of the game, so also play what you changed:

- **Graphics:** screenshots before and after, at the scale and settings you tested, and the
  same scene with [`-Compat`](guide/launch-options.md#-compat).
- **Performance:** frame rates before and after on the same track, car and camera, with your
  CPU and GPU.
- **Input:** both the keyboard and a controller.
- **Setup and launcher:** a fresh install into an empty folder, not only an update of yours.

## Pull requests

- One topic per pull request. Unrelated fixes go in separate ones.
- Commit subjects are one line, `<type>: <Sentence>`, with type `feat`, `fix`, `refactor` or
  `docs`, for example `fix: Stop Esc on the GRAPHICS page from reopening it`.
- Update the [guides](guide/) and the README's features table when what a player sees or sets
  changes. A new launch option goes in [Launch options](guide/launch-options.md).
- Match the surrounding code: its naming, formatting and idiom. Comment only where the code
  cannot say why, such as a hardware quirk or a constraint from the game.

**AI-assisted contributions** are fine; the project is largely AI-assisted itself. You are
still the author: build it, test it on your PC, and be ready to explain every line.
