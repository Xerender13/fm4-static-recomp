"""Garage thumbnails, decoded from the save.

The game writes a bought car's thumbnail to
<state>\\user\\<profile>\\4D530910\\00000001\\ForzaProfile\\Thumbnails\\Thumbnail_<n>.xdc:
"cxds", then big-endian u32s: version (2), unpacked size, packed size, unpacked size again;
from 0x14 raw deflate. Unpacked: a 52-byte texture fetch constant (format 0x86, 768x288),
then 768x288 texels of 4 bytes, Xenos 2D-tiled, each X R G B (big-endian ARGB, X = 0).
Confirmed on Thumbnail_1.xdc of the test save (a red Ford Ka, upright, natural colours).
"""
import re
import struct
import zlib
from pathlib import Path

from .images import Image

WIDTH = 768
HEIGHT = 288
HEADER_BYTES = 0x14
FETCH_BYTES = 52
UNPACKED_BYTES = FETCH_BYTES + WIDTH * HEIGHT * 4
_NAME = re.compile(r"^Thumbnail_(\d+)\.xdc$", re.IGNORECASE)


def tiled_offset_2d(x, y, width, bytes_per_texel):
    """Byte offset of texel (x, y): the SDK's TiledOffset2DRow + TiledOffset2DColumn
    (src/graphics/pipeline/texture/conversion.cpp)."""
    log2_bpp = bytes_per_texel.bit_length() - 1
    macro = ((y // 32) * (width // 32)) << (log2_bpp + 7)
    micro = ((y & 6) << 2) << log2_bpp
    row = (macro + ((micro & ~0xF) << 1) + (micro & 0xF) + ((y & 8) << (3 + log2_bpp))
           + ((y & 1) << 4))
    macro = (x // 32) << (log2_bpp + 7)
    micro = (x & 7) << log2_bpp
    offset = row + macro + ((micro & ~0xF) << 1) + (micro & 0xF)
    return (((offset & ~0x1FF) << 3) + ((offset & 0x1C0) << 2) + (offset & 0x3F)
            + ((y & 16) << 7) + (((((y & 8) >> 2) + (x >> 3)) & 3) << 6))


def read_thumbnail(path):
    data = Path(path).read_bytes()
    if data[:4] != b"cxds":
        raise ValueError(f"{path}: not a thumbnail (magic {data[:4]!r}, expected cxds)")
    unpacked_size, = struct.unpack_from(">I", data, 8)
    if unpacked_size != UNPACKED_BYTES:
        raise ValueError(f"{path}: unpacked size {unpacked_size}, expected {UNPACKED_BYTES}")
    unpacked = zlib.decompress(data[HEADER_BYTES:], -15)
    if len(unpacked) != UNPACKED_BYTES:
        raise ValueError(f"{path}: {len(unpacked)} bytes unpacked, expected {UNPACKED_BYTES}")
    texels = memoryview(unpacked)[FETCH_BYTES:]
    rgb = bytearray(WIDTH * HEIGHT * 3)
    target = 0
    for y in range(HEIGHT):
        for x in range(WIDTH):
            offset = tiled_offset_2d(x, y, WIDTH, 4)
            rgb[target:target + 3] = texels[offset + 1:offset + 4]
            target += 3
    return Image(WIDTH, HEIGHT, bytes(rgb))


def thumbnail_paths(state_dir):
    """Thumbnail slot number -> file, for every profile in the state's save."""
    found = {}
    for path in Path(state_dir).glob("user/*/4D530910/*/ForzaProfile/Thumbnails/*.xdc"):
        match = _NAME.match(path.name)
        if match:
            found[int(match.group(1))] = path
    return found
