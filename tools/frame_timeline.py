"""The last dumped frame of a runtime log (race-trace.ps1, run.ps1 -Dumps): clears, resolves and
target changes in order, with the surface transfers and texture loads each one caused.

Usage: frame_timeline.py <log> [first_draw last_draw]

A surface is base tile / pitch in tiles / MSAA / c<color format> or d<depth format>; a transfer
copies tiles from the surface that owned them, "xN" passes each (9 when stencil is carried)."""
import collections
import re
import sys

draw = re.compile(r"FH1 debug draw (\d+) (.*)")
transfer = re.compile(r"FH1 debug transfer (\S+)->(\S+) tiles=(\d+) passes=(\d+)")
load = re.compile(r"FH1 debug texture load (\S+) (\S+) format=(\d+) scaled=(\w+) resolve_sourced=(\w+) "
                  r"base=(\w+) mips=(\w+)")
CLEAR_VS = "1E6883FC"  # the title's Clear

events = []
last_draw = -1
for line in open(sys.argv[1], errors="replace"):
    m = draw.search(line)
    if m:
        index = int(m.group(1))
        if index < last_draw:
            events = []
        last_draw = index
        fields = dict(p.split("=", 1) for p in m.group(2).split(" ") if "=" in p and not p.startswith("t"))
        events.append(("d", index, fields))
        continue
    m = transfer.search(line)
    if m:
        events.append(("t", m.group(1), m.group(2), int(m.group(3)), int(m.group(4))))
        continue
    m = load.search(line)
    if m:
        events.append(("l", m.group(1), m.group(2), int(m.group(3)), m.group(6) == "true", m.group(7) == "true"))

lo, hi = (int(sys.argv[2]), int(sys.argv[3])) if len(sys.argv) > 3 else (0, 10**9)
previous = None
run = 0
index = -1
merged = {}
passes_total = 0
loads = collections.Counter()


def flush():
    for (source, dest, passes), (count, tiles) in merged.items():
        print(f"        transfer {source} -> {dest} x{passes}: {tiles} tiles in {count}")
    merged.clear()


for event in events:
    if event[0] == "t":
        passes_total += event[3] * event[4]
        if lo <= index <= hi:
            entry = merged.setdefault((event[1], event[2], event[4]), [0, 0])
            entry[0] += 1
            entry[1] += event[3]
        continue
    if event[0] == "l":
        loads[(event[1], event[2], event[3], "base" if event[4] else "mips")] += 1
        if lo <= index <= hi:
            flush()
            print(f"        load {event[1]} {event[2]} format {event[3]} {'base' if event[4] else 'mips'}")
        continue
    flush()
    index, f = event[1], event[2]
    if not lo <= index <= hi:
        continue
    surface = int(f["surface"], 16)
    color = int(f["color0"], 16)
    depth = int(f.get("depth_info", "0"), 16)
    target = (surface & 0x3FFF, (surface >> 16) & 3, color & 0xFFF, (color >> 16) & 0xF, depth & 0xFFF,
              f["window_scissor"])
    is_clear = f["vs"].startswith(CLEAR_VS)
    if is_clear or f["mode"] != "4" or target != previous:
        if run:
            print(f"        ... {run} more draws")
        run = 0
        kind = "CLEAR" if is_clear else ("mode" + f["mode"] if f["mode"] != "4" else "draw ")
        print(f"#{index:<5}{kind} pitch={target[0]} msaa={1 << target[1]}x color={target[2]}/f{target[3]} "
              f"depth={target[4]} mask={f['mask']} zctl={f['depth']} scissor={f['window_scissor']} "
              f"copy={f['copy_ctrl']} dest={f['copy_dest']} prim={f['prim']} n={f['indices']}")
    else:
        run += 1
    previous = target
flush()
if run:
    print(f"        ... {run} more draws")
print(f"\n{passes_total} transfer tile passes in the frame; texture loads:")
for (address, size, fmt, part), count in loads.most_common():
    print(f"  {address} {size} format {fmt} {part} x{count}")
