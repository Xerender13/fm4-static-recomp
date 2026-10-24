"""The executor's per-frame counters over a time window, and the game's own VRAM."""
import re
import subprocess
from dataclasses import dataclass

_STATS = re.compile(r"^FH1 native executor \(Vulkan\) frame=(\d+) draws=\d+ resolves=(\d+) .*stats=\{([^}]*)\}")


@dataclass(frozen=True)
class Counters:
    frames: int
    resolves: float
    transfers: float
    transfer_tile_passes: float
    one_off_readbacks: float


def _parse(line):
    match = _STATS.match(line.text)
    if not match:
        return None
    stats = dict(entry.split("=", 1) for entry in match.group(3).split(",") if "=" in entry)
    return (int(match.group(1)), int(match.group(2)), int(stats.get("transfer", 0)),
            int(stats.get("transfer_tile_passes", 0)), int(stats.get("resolve_readback_one_off", 0)))


def window(lines, start, end):
    """Per frame between the first and the last stats line (every 600 frames) inside the
    window; None with fewer than two."""
    inside = [parsed for line in lines if line.time and start <= line.time <= end
              for parsed in [_parse(line)] if parsed]
    if len(inside) < 2:
        return None
    first, last = inside[0], inside[-1]
    frames = last[0] - first[0]
    if frames <= 0:
        return None
    per_frame = [(b - a) / frames for a, b in zip(first[1:], last[1:])]
    return Counters(frames, *per_frame)


def vram_mb(pid):
    """The process's dedicated GPU memory (the per-process counter race-perf.ps1 reads)."""
    script = (f"((Get-Counter '\\GPU Process Memory(pid_{pid}_*)\\Dedicated Usage').CounterSamples"
              " | Measure-Object CookedValue -Sum).Sum")
    output = subprocess.run(["pwsh", "-NoProfile", "-Command", script], capture_output=True,
                            text=True, check=True).stdout.strip()
    return float(output) / (1024 * 1024)
