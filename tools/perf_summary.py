"""Summarises one race-perf.ps1 capture: frame rate and the executor's per-frame counters over
the sampled window, its GPU time per phase (with --fh1_native_gpu_profile=true), GPU
utilisation and power, and the busiest threads.

Usage: perf_summary.py <perf dir> <tag>"""
import collections
import re
import sys
from datetime import datetime

folder, tag = sys.argv[1], sys.argv[2]
base = f"{folder}/{tag}"

lines = open(f"{base}-threads.txt").read().split("\n")
window = re.search(r"window (\S+) to (\S+)", "\n".join(lines))
start, end = (datetime.strptime(t, "%H:%M:%S.%f") for t in window.groups())

stamp = re.compile(r"^\[\d+-\d+-\d+ (\d+:\d+:\d+\.\d+)\] \[\w+\] \[(\w+)\] \[t(\d+)\] (.*)")
roles = collections.defaultdict(collections.Counter)
frames = []
extra = []
for line in open(f"{base}-runtime.log", errors="replace"):
    m = stamp.match(line)
    if not m:
        continue
    time = datetime.strptime(m.group(1), "%H:%M:%S.%f")
    roles[int(m.group(3))][m.group(2)] += 1
    text = m.group(4)
    f = re.search(r"FH1 native executor \(Vulkan\) frame=(\d+) draws=(\d+) resolves=(\d+)", text)
    if f:
        stats = dict(kv.split("=") for kv in re.search(r"stats=\{(.*)\}", text).group(1).split(","))
        frames.append((time, int(f.group(1)), int(f.group(2)), int(f.group(3)), stats))
    elif start <= time <= end and re.search(r"gpu ms/frame|transfer tile-passes|per frame over", text):
        extra.append(text[text.find("(Vulkan)") + 9:][:330])

print(f"== {tag}: window {start.time()} to {end.time()}")
inside = [f for f in frames if start <= f[0] <= end]
before = [f for f in frames if f[0] < start]
span = ([before[-1]] if before else []) + inside
if len(span) >= 2:
    a, b = span[0], span[-1]
    seconds = (b[0] - a[0]).total_seconds()
    n = b[1] - a[1]
    print(f"fps {n / seconds:.1f} over {seconds:.1f} s; per frame: draws {(b[2] - a[2]) / n:.0f}, resolves {(b[3] - a[3]) / n:.1f}")
    keys = ["transfer", "transfer_tile_passes", "transfer_batch", "rendering_new_targets", "rendering_interrupted",
            "end_executor", "end_texture_load", "end_memory_upload", "end_image_barrier", "resolve_readback_one_off",
            "clear_color", "clear_depth", "depth_overwrite_draw"]
    print("per frame: " + ", ".join(
        f"{k} {(int(b[4].get(k, 0)) - int(a[4].get(k, 0))) / n:.1f}" for k in keys))
for text in extra[-4:]:
    print("  " + text)

rows = [r.split(",") for r in open(f"{base}-gpu.csv").read().split("\n") if r.strip()]
util = [float(r[0]) for r in rows]
power = [float(r[2]) for r in rows]
memory = [float(r[1]) for r in rows]
print(f"gpu: util avg {sum(util) / len(util):.0f}% (min {min(util):.0f}, max {max(util):.0f}), "
      f"power avg {sum(power) / len(power):.0f} W (max {max(power):.0f}), vram max {max(memory):.0f} MiB, {len(rows)} samples")
print("threads (% of one core):")
for line in lines:
    m = re.match(r"\s*(\d+)\s+([\d.,]+)", line)
    if m and float(m.group(2).replace(",", ".")) >= 3:
        role = ",".join(f"{k}:{v}" for k, v in roles[int(m.group(1))].most_common(3)) or "-"
        print(f"  t{m.group(1):>6} {m.group(2):>6}  {role}")
