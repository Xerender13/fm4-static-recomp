"""showroom_cuts.py <seconds>: sample the test session's picture and report camera cuts (fades to
black). Run it with a session.ps1 game sitting in the showroom.

The showroom camera queue cuts at fixed times (Media/Camera/HomeSpaceCams.xml, StartCut), each
through a fade, so the spacing of the brightness dips against the file gives the speed of the
camera clock relative to real time."""
import os
import re
import struct
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATE = os.path.join(ROOT, ".local", "state-test")
SCRIPT = os.path.join(STATE, "live.input")
BMP = os.path.join(STATE, "shots", "sample.bmp")
CAMS = os.path.join(ROOT, "..", "extracted", "fm4", "Media", "Camera", "HomeSpaceCams.xml")


def brightness():
    if os.path.exists(BMP):
        os.remove(BMP)
    with open(SCRIPT + ".shot.tmp", "w") as f:
        f.write(BMP)
    os.replace(SCRIPT + ".shot.tmp", SCRIPT + ".shot")
    deadline = time.time() + 5
    while not os.path.exists(BMP):
        if time.time() > deadline:
            return None
        time.sleep(0.01)
    stamp = time.time()
    time.sleep(0.03)
    data = open(BMP, "rb").read()
    offset, = struct.unpack_from("<I", data, 10)
    pixels = data[offset::97]
    return stamp, sum(pixels) / max(1, len(pixels))


os.makedirs(os.path.dirname(BMP), exist_ok=True)
samples = []
end = time.time() + float(sys.argv[1])
while time.time() < end:
    s = brightness()
    if s:
        samples.append(s)
t0 = samples[0][0]
level = sorted(b for _, b in samples)[len(samples) // 2]
dips, inside = [], None
for t, b in samples:
    if b < level * 0.35:
        if inside is None or b < inside[1]:
            inside = (t, b)
    elif inside:
        dips.append(inside[0] - t0)
        inside = None
print(f"{len(samples)} samples over {samples[-1][0] - t0:.1f}s, median brightness {level:.1f}")
print("dips at s:", " ".join(f"{d:.2f}" for d in dips))
print("dip spacing s:", " ".join(f"{b - a:.2f}" for a, b in zip(dips, dips[1:])))
cuts = [float(x) for x in re.findall(r'StartCut="([0-9.]+)"', open(CAMS).read())]
print("file cut spacing s:", " ".join(f"{b - a:.2f}" for a, b in zip(cuts, cuts[1:])))
