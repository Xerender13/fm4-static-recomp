# Reporting issues

[← Back to the README](../README.md)

## Before you open an issue

- [ ] Check the [open issues](https://github.com/i2kiselev/fm4-static-recomp/issues) and add
  to an existing one rather than opening a duplicate.
- [ ] Check that `default.xex` has a supported hash (see
  [Extract Disc 1](manual-install.md#1-extract-disc-1)).
- [ ] Update to the latest version (see [launcher updates](launcher.md#updates) or
  [manual updating](manual-install.md#updating)) and check the problem is still there.
- [ ] For anything that looks wrong on screen, try a [`-Compat`](launch-options.md#-compat)
  launch and note whether it looks the same there.

## Title

`<Area>: <what happens> (<where>)`, for example
`Graphics: car shadows flicker at 3x (Bernese Alps, cockpit view)`.

Area is one of `Crash`, `Graphics`, `Performance`, `Audio`, `Input`, `Setup`, `Disc 2/DLC`,
`Gameplay`, `Other`.

## Body

Copy this template and fill in every line; write "unknown" rather than leaving one out.

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
<!-- runtime.log: data\logs\runtime.log under the launcher, .local\state\logs\runtime.log
     in a manual install (see "Logs and crash reports" in guide/launch-options.md); a crash
     report's ZIP instead.
     Setup problems: .local\logs\setup-error.json and the logs next to it.
     Graphics problems: a screenshot or a short video. -->
```

> [!CAUTION]
> Logs can contain folder paths with your Windows user name; edit them out if you prefer.
> Never attach game files, disc images or a built executable.
