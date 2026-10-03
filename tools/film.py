"""Greyscale films of a scripted session (src/scripted_input.cpp, a ".gray" film request).

    film.py info  <film> [anchor_time]
    film.py cuts  <tag>...                       picture cuts after the anchor, per film
    film.py lag   <ref tag> <tag> <t0> <t1> <step> [x0 y0 x1 y1]
    film.py sheet <out.png> <t0> <t1> <step> <tag>...

A tag names .local/perf/av-<tag>.gray and its log .local/perf/av-<tag>.log (written by
autovista-replay.ps1); times are seconds after the log's "autovista_shown" event. lag prints,
per sample time, the offset of the tag's picture that best matches the reference's: a
picture running faster drifts away from 0 within a shot. A region limits it to that part of
the 192x108 picture.
"""
import bisect
import re
import struct
import sys
import zlib
from pathlib import Path

W, H = 192, 108
RECORD = 8 + W * H
PERF = Path(__file__).resolve().parent.parent / '.local' / 'perf'


def frames(path):
    data = Path(path).read_bytes()
    for i in range(len(data) // RECORD):
        t, = struct.unpack_from('<d', data, i * RECORD)
        yield t, data[i * RECORD + 8:(i + 1) * RECORD]


def load(tag, step=1, box=None):
    log = (PERF / f'av-{tag}.log').read_text(encoding='utf-8', errors='replace')
    anchor = float(re.search(r'autovista_shown at ([\d.]+)', log).group(1))
    times, pictures = [], []
    for t, f in frames(PERF / f'av-{tag}.gray'):
        if box:
            x0, y0, x1, y1 = box
            f = b''.join(f[y * W + x0:y * W + x1] for y in range(y0, y1))
        times.append(t - anchor)
        pictures.append(f[::step])
    return times, pictures


def at(times, pictures, t):
    return pictures[max(bisect.bisect_right(times, t) - 1, 0)]


def difference(a, b):
    return sum(abs(x - y) for x, y in zip(a, b)) / len(a)


def samples(t0, t1, step):
    count = int(round((t1 - t0) / step))
    return [t0 + i * step for i in range(count + 1)]


def write_png(path, width, height, rows):
    raw = b''.join(b'\0' + bytes(r) for r in rows)

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))

    Path(path).write_bytes(b'\x89PNG\r\n\x1a\n'
                           + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 0, 0, 0, 0))
                           + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))


def main(argv):
    command = argv[0]
    if command == 'info':
        times = [t for t, _ in frames(argv[1])]
        anchor = float(argv[2]) if len(argv) > 2 else times[0]
        gaps = sorted(b - a for a, b in zip(times, times[1:]))
        print(f'{len(times)} pictures, {times[0] - anchor:.2f} to {times[-1] - anchor:.2f} s, '
              f'median gap {gaps[len(gaps) // 2] * 1000:.1f} ms')
    elif command == 'cuts':
        for tag in argv[1:]:
            times, pictures = load(tag, step=7)
            cuts = [f'{t:.2f}' for t, a, b in zip(times[1:], pictures, pictures[1:])
                    if t > 0 and difference(a, b) > 30]
            print(f'{tag}: {len(times)} pictures; cuts at', ' '.join(cuts))
    elif command == 'lag':
        ref, tag = argv[1], argv[2]
        t0, t1, step = map(float, argv[3:6])
        box = tuple(map(int, argv[6:10])) if len(argv) > 6 else None
        reference = load(ref, 1 if box else 5, box)
        other = load(tag, 1 if box else 5, box)
        for t in samples(t0, t1, step):
            a = at(*reference, t)
            best = min((difference(a, at(*other, t + k / 120)), k / 120) for k in range(-40, 41))
            print(f'{t:6.2f} s  lag {best[1] * 1000:+5.0f} ms  difference {best[0]:5.1f}')
    elif command == 'sheet':
        out, (t0, t1, step), tags = argv[1], map(float, argv[2:5]), argv[5:]
        films = [load(tag) for tag in tags]
        times = samples(t0, t1, step)
        width, height = len(tags) * (W + 2), len(times) * (H + 2)
        canvas = [bytearray(width) for _ in range(height)]
        for row, t in enumerate(times):
            for column, film in enumerate(films):
                picture = at(*film, t)
                for y in range(H):
                    canvas[row * (H + 2) + y][column * (W + 2):column * (W + 2) + W] = \
                        picture[y * W:(y + 1) * W]
        write_png(out, width, height, canvas)
    else:
        sys.exit(__doc__)


if __name__ == '__main__':
    main(sys.argv[1:])
