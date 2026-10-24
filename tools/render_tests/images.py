"""Images as plain RGB bytes: the capture watcher's BMPs, our own PNGs, comparisons, chroma.

Stdlib only. The per-pixel work runs through map() and slicing over bytes: a 2560x1440
comparison takes 0.85 s, a downscale to 1280x720 0.5 s (measured 2026-10-24). Speed needs no
downscale; references are kept at 1280x720 only to keep them small.
"""
import math
import operator
import struct
import zlib
from dataclasses import dataclass
from pathlib import Path

CHANGED_CHANNEL_DIFFERENCE = 16


@dataclass(frozen=True)
class Image:
    width: int
    height: int
    rgb: bytes  # row-major, top-down, 3 bytes a pixel

    @property
    def size(self):
        return f"{self.width}x{self.height}"


@dataclass(frozen=True)
class Rect:
    """A region in fractions 0..1 of the image."""
    x0: float
    y0: float
    x1: float
    y1: float

    def pixels(self, width, height):
        return (round(self.x0 * width), round(self.y0 * height),
                round(self.x1 * width), round(self.y1 * height))


@dataclass(frozen=True)
class ImageLimits:
    """After Pinyon's DEFAULT_IMAGE_LIMITS: mean absolute and root mean square error over every
    channel (0-255), and the share of pixels with a channel changed by more than 16."""
    mean_abs: float = 12.0
    rmse: float = 30.0
    changed_ratio: float = 0.60


@dataclass(frozen=True)
class Comparison:
    passed: bool
    mean_abs: float
    rmse: float
    changed_ratio: float
    detail: str = ""


@dataclass(frozen=True)
class Chroma:
    mean: float
    p95: int
    pixels: int
    # max(0, min(r, b) - g): purple and magenta paint raise it; sky-blue shade and warm sun on
    # white paint do not.
    green_deficit: float = 0.0


def read_bmp(path):
    data = Path(path).read_bytes()
    if data[:2] != b"BM":
        raise ValueError(f"{path}: not a BMP")
    offset, = struct.unpack_from("<I", data, 10)
    width, height, _, bits = struct.unpack_from("<iiHH", data, 18)
    if bits != 24:
        raise ValueError(f"{path}: {bits}-bit BMP, expected 24")
    top_down = height < 0
    height = abs(height)
    row_bytes = (width * 3 + 3) & ~3
    rows = []
    for y in range(height):
        start = offset + row_bytes * (y if top_down else height - 1 - y)
        row = data[start:start + width * 3]
        swapped = bytearray(len(row))
        swapped[0::3] = row[2::3]
        swapped[1::3] = row[1::3]
        swapped[2::3] = row[0::3]
        rows.append(bytes(swapped))
    return Image(width, height, b"".join(rows))


def _chunk(kind, payload):
    return (struct.pack(">I", len(payload)) + kind + payload
            + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF))


def write_png(path, image):
    stride = image.width * 3
    raw = b"".join(b"\x00" + image.rgb[y * stride:(y + 1) * stride] for y in range(image.height))
    header = struct.pack(">IIBBBBB", image.width, image.height, 8, 2, 0, 0, 0)
    Path(path).write_bytes(b"\x89PNG\r\n\x1a\n" + _chunk(b"IHDR", header)
                           + _chunk(b"IDAT", zlib.compress(raw, 6)) + _chunk(b"IEND", b""))


def read_png(path):
    data = Path(path).read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path}: not a PNG")
    position = 8
    idat = bytearray()
    width = height = 0
    while position < len(data):
        length, = struct.unpack_from(">I", data, position)
        kind = data[position + 4:position + 8]
        payload = data[position + 8:position + 8 + length]
        position += 12 + length
        if kind == b"IHDR":
            width, height, depth, colour, _, _, interlace = struct.unpack(">IIBBBBB", payload)
            if (depth, colour, interlace) != (8, 2, 0):
                raise ValueError(f"{path}: only 8-bit RGB, not interlaced")
        elif kind == b"IDAT":
            idat += payload
        elif kind == b"IEND":
            break
    raw = zlib.decompress(bytes(idat))
    stride = width * 3
    previous = bytearray(stride)
    rows = []
    for y in range(height):
        start = y * (stride + 1)
        kind = raw[start]
        row = bytearray(raw[start + 1:start + 1 + stride])
        if kind == 1:
            for i in range(3, stride):
                row[i] = (row[i] + row[i - 3]) & 0xFF
        elif kind == 2:
            row = bytearray(map(lambda a, b: (a + b) & 0xFF, row, previous))
        elif kind == 3:
            for i in range(stride):
                left = row[i - 3] if i >= 3 else 0
                row[i] = (row[i] + ((left + previous[i]) >> 1)) & 0xFF
        elif kind == 4:
            for i in range(stride):
                a = row[i - 3] if i >= 3 else 0
                b = previous[i]
                c = previous[i - 3] if i >= 3 else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                predictor = a if pa <= pb and pa <= pc else b if pb <= pc else c
                row[i] = (row[i] + predictor) & 0xFF
        elif kind != 0:
            raise ValueError(f"{path}: unknown PNG filter {kind}")
        rows.append(bytes(row))
        previous = row
    return Image(width, height, b"".join(rows))


def downscale(image, width, height):
    """Box filter by a whole factor (2560x1440 -> 1280x720 is a factor of 2)."""
    factor = image.width // width
    if factor * width != image.width or factor * height != image.height:
        raise ValueError(f"cannot box-filter {image.size} to {width}x{height}")
    if factor == 1:
        return image
    area = factor * factor
    divide = [(total + area // 2) // area for total in range(255 * area + 1)]
    stride = image.width * 3
    out = bytearray()
    for y in range(height):
        sums = [0] * (width * 3)
        for dy in range(factor):
            row = image.rgb[(y * factor + dy) * stride:(y * factor + dy + 1) * stride]
            for channel in range(3):
                for dx in range(factor):
                    column = row[dx * 3 + channel::factor * 3]
                    sums[channel::3] = map(operator.add, sums[channel::3], column)
        out += bytes(map(divide.__getitem__, sums))
    return Image(width, height, bytes(out))


def _region(image, rect):
    if rect is None:
        return image.rgb
    x0, y0, x1, y1 = rect.pixels(image.width, image.height)
    stride = image.width * 3
    return b"".join(image.rgb[y * stride + x0 * 3:y * stride + x1 * 3] for y in range(y0, y1))


def _absolute_differences(first, second):
    return bytes(map(abs, map(operator.sub, first, second)))


_SQUARES = [value * value for value in range(256)]
_CHANGED = bytes(1 if value > CHANGED_CHANNEL_DIFFERENCE else 0 for value in range(256))


def compare(actual, reference, limits, mask=None):
    if (actual.width, actual.height) != (reference.width, reference.height):
        return Comparison(False, math.inf, math.inf, 1.0,
                          f"size {actual.size} against reference {reference.size}")
    differences = _absolute_differences(_region(actual, mask), _region(reference, mask))
    if not differences:
        return Comparison(True, 0.0, 0.0, 0.0, "empty region")
    channels = len(differences)
    mean_abs = sum(differences) / channels
    rmse = math.sqrt(sum(map(_SQUARES.__getitem__, differences)) / channels)
    changed = differences.translate(_CHANGED)
    any_changed = bytes(map(operator.or_, map(operator.or_, changed[0::3], changed[1::3]),
                            changed[2::3]))
    changed_ratio = (len(any_changed) - any_changed.count(0)) / len(any_changed)
    failed = [name for name, value, limit in (("mean_abs", mean_abs, limits.mean_abs),
                                              ("rmse", rmse, limits.rmse),
                                              ("changed_ratio", changed_ratio,
                                               limits.changed_ratio)) if value > limit]
    detail = f"over the limit: {', '.join(failed)}" if failed else ""
    return Comparison(not failed, mean_abs, rmse, changed_ratio, detail)


_TIMES_FOUR = bytes(min(255, value * 4) for value in range(256))


def difference_image(actual, reference):
    return Image(actual.width, actual.height,
                 _absolute_differences(actual.rgb, reference.rgb).translate(_TIMES_FOUR))


_LUMA_R = [299 * value for value in range(256)]
_LUMA_G = [587 * value for value in range(256)]
_LUMA_B = [114 * value for value in range(256)]


def chroma(image, rect, min_luma=150):
    """Chroma, max(r,g,b) - min(r,g,b), over the pixels of rect at least min_luma bright
    (Rec. 601). The white Abarth's paint is the bright pixels; a tint raises it."""
    region = _region(image, rect)
    r, g, b = region[0::3], region[1::3], region[2::3]
    luma = map(operator.add, map(operator.add, map(_LUMA_R.__getitem__, r),
                                 map(_LUMA_G.__getitem__, g)), map(_LUMA_B.__getitem__, b))
    threshold = min_luma * 1000
    bright = [index for index, value in enumerate(luma) if value >= threshold]
    values = bytes(max(r[i], g[i], b[i]) - min(r[i], g[i], b[i]) for i in bright)
    if not values:
        return Chroma(0.0, 0, 0)
    deficit = sum(max(0, min(r[i], b[i]) - g[i]) for i in bright) / len(bright)
    histogram = [values.count(value) for value in range(256)]
    target = math.ceil(0.95 * len(values))
    running = 0
    p95 = 0
    for value, count in enumerate(histogram):
        running += count
        if running >= target:
            p95 = value
            break
    return Chroma(sum(values) / len(values), p95, len(values), deficit)
