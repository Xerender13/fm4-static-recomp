# Launch options

[← Back to the README](../README.md)

Pass these after `tools\run.ps1`'s own parameters (see the [manual install](manual-install.md#4-play)),
or in the launcher's SETTINGS.

| Option | Meaning |
|---|---|
| `--resolution_scale=1..4` | Internal resolution, a multiple of 1280x720 |
| `--fm4_frame_rate=60..240` | Frame rate of races, menus and the showroom |
| `--vulkan_present_mode=immediate\|fifo` | VSYNC off or on |
| `--swap_post_effect=none\|fxaa\|fxaa_extreme` | Post-process anti-aliasing |
| `--fm4_disc2_packages=<folder>` | Install the Disc 2 packages from this folder |
| `--fm4_dlc_packages=<folder>` | Install every DLC package under this folder |
| `--fm4_skip_intro=true` | Skip the startup disclaimer and studio videos |
| `--fm4_language=<code>` | The game's language (see [Languages](#languages)) |
| `--fm4_config_root=<folder>` | Where `graphics.toml` and `controls.toml` live (default: the state folder's `config`) |
| `--fh1_async_readbacks=false` | Turn off asynchronous readbacks, if you ever see a corrupted texture |
| `--vulkan_present_via_dxgi=true` | Present through DXGI, for [frame generation tools](#frame-generation) |

## Languages

`--fm4_language` takes `en-US` (default), `en-GB`, `fr`, `de`, `it`, `es`, `es-MX`, `pl`,
`ru`, `ja`, `ko`, `zh-TW`, `pt-BR`, `cs`, `hu` or `nl`.

The game shows every language on Disc 1. Korean, Traditional Chinese, Brazilian Portuguese,
Czech, Hungarian and Dutch have no voice-over of their own and keep the English one. With the
keyboard, some prompts keep the controller's wording in languages other than English.

## `-Compat`

`tools\run.ps1 -Compat` turns off every rendering shortcut of this project and uses the
console's graphics settings. If something looks wrong, compare with a `-Compat` launch: if it
looks the same there, it is most likely the game's own look.

## Frame generation

Lossless Scaling and NVIDIA Smooth Motion do not see frames presented through the Vulkan
swapchain. `--vulkan_present_via_dxgi=true` presents through DXGI instead, and both tools then
work. It is off by default because Smooth Motion's frame pacing does not look right with it
yet.
