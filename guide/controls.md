# Keyboard

[← Back to the README](../README.md)

The keyboard plays beside a gamepad, with a layout after Forza Horizon 6's. While the
keyboard is the last device you used, the game's button prompts show keycaps of the bound
keys, and English prompt sentences ("PRESS THE BACK BUTTON...") name them too; touching the
pad brings the controller buttons back.

## Driving

| Action | Keys |
|---|---|
| Throttle / brake | W / S |
| Steer | A / D |
| Handbrake | Space |
| Gear up / down | E / Q |
| Clutch | Shift |
| Camera | Tab |
| Rewind | R |
| Look around (back: Down) | Arrow keys |
| Look straight back | C |
| Telemetry | T, G |
| Pause | Esc |

Driving keys apply while the car is on track and the game is not paused. Steering, pedals
and looking around ramp up while a key is held and ease back on release.

## Menus

The pre-race screen, pause menu, replays and results use these keys too.

| Button | Keys |
|---|---|
| Move | W A S D, arrow keys |
| Select (A) | Enter, Space |
| Back (B) | Esc, Backspace |
| X / Y | X / Y |
| LB / RB | Q / E |
| LT / RT | Z / C |
| Right stick | I J K L |
| BACK / START | Tab / P |

The mouse is not used.

## Rebinding keys

Create `config\controls.toml` in the state folder and add `menu_<button>` or
`driving_<button>` lines. A value lists keys separated by commas:

```toml
driving_x = "Q,Numpad1"
```

Changes apply at the next launch.

**Buttons:** `a`, `b`, `x`, `y`, `left_shoulder`, `right_shoulder`, `left_trigger`,
`right_trigger`, `lstick_up`, `lstick_down`, `lstick_left`, `lstick_right`, `lstick_press`,
the same five for `rstick_`, `dpad_up`, `dpad_down`, `dpad_left`, `dpad_right`, `back`,
`start`.

**Key names:** `A`..`Z`, `0`..`9`, `F1`..`F24`, `Up`, `Down`, `Left`, `Right`, `Space`,
`Return`, `Escape`, `Backspace`, `Tab`, `Shift`, `Control`, `Alt`, `Numpad0`..`Numpad9` and
the like.

**Ramps**, in seconds from rest to full and back:

| Key | Default |
|---|---|
| `ramp_steer_seconds` | 0.17 |
| `ramp_steer_return_seconds` | 0.10 |
| `ramp_pedal_seconds` | 0.10 |
| `ramp_pedal_return_seconds` | 0.08 |
| `ramp_look_seconds` | 0.15 |
