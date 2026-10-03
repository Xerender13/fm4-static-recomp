"""Decode resolve dumps (tiled 32bpp guest textures) into PNGs and print channel statistics.

usage: undump.py <dump dir> <out dir> <index:width:kind[:gain]>...
kind: t7 (2_10_10_10, red/blue swapped), f32 (32_FLOAT), t6 (8_8_8_8)
"""
import struct
import sys
import zlib
from pathlib import Path


def tiled_offset(x, y, pitch, bpp_log2=2):
    pitch = (pitch + 31) & ~31
    macro = ((x >> 5) + (y >> 5) * (pitch >> 5)) << (bpp_log2 + 7)
    micro = ((x & 7) + ((y & 0xE) << 2)) << bpp_log2
    offset = macro + ((micro & ~0xF) << 1) + (micro & 0xF) + ((y & 1) << 4)
    return (((offset & ~0x1FF) << 3) + ((y & 16) << 7) + ((offset & 0x1C0) << 2)
            + (((((y & 8) >> 2) + (x >> 3)) & 3) << 6) + (offset & 0x3F))


def png(path, width, height, rows):
    def chunk(tag, data):
        body = tag + data
        return struct.pack('>I', len(data)) + body + struct.pack('>I', zlib.crc32(body))
    raw = b''.join(b'\x00' + bytes(row) for row in rows)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 2, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(raw, 6)) + chunk(b'IEND', b''))


def main():
    dumps, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    for spec in sys.argv[3:]:
        parts = spec.split(':')
        index, width, kind = int(parts[0]), int(parts[1]), parts[2]
        gain = float(parts[3]) if len(parts) > 3 else 1.0
        file = next(dumps.glob(f'{index:05d}_*.bin'))
        data = file.read_bytes()
        height = len(data) // 4 // width
        rows, sums, peak, bright = [], [0.0, 0.0, 0.0], [0.0, 0.0, 0.0], 0
        for y in range(height):
            row = bytearray()
            for x in range(width):
                offset = tiled_offset(x, y, width)
                if offset + 4 > len(data):
                    row += b'\xff\x00\xff'
                    continue
                if kind == 'f32':
                    value = struct.unpack_from('>f', data, offset)[0]
                    rgb = (value, value, value)
                else:
                    word = struct.unpack_from('>I', data, offset)[0]
                    if kind == 't7':
                        rgb = (((word >> 20) & 0x3FF) / 1023, ((word >> 10) & 0x3FF) / 1023, (word & 0x3FF) / 1023)
                    else:
                        rgb = (((word >> 16) & 0xFF) / 255, ((word >> 8) & 0xFF) / 255, (word & 0xFF) / 255)
                for c in range(3):
                    sums[c] += rgb[c]
                    peak[c] = max(peak[c], rgb[c])
                bright += rgb[0] >= 0.999
                row += bytes(min(255, max(0, int(v * gain * 255 + 0.5))) for v in rgb)
            rows.append(row)
        count = width * height
        name = f'{index:02d}_{file.stem.split("_")[1]}_{width}x{height}'
        png(out / f'{name}.png', width, height, rows)
        print(f'{name}: mean ' + ' '.join(f'{s / count:.4f}' for s in sums) + ' peak '
              + ' '.join(f'{p:.4f}' for p in peak) + f' red>=1: {bright / count:.2%}')


if __name__ == '__main__':
    main()
