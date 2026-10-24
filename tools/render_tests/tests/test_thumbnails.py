import struct
import tempfile
import unittest
import zlib
from pathlib import Path

from render_tests import thumbnails
from render_tests.thumbnails import HEIGHT, WIDTH, read_thumbnail, thumbnail_paths, tiled_offset_2d

# A texture fetch constant as the game writes it before the texels (Thumbnail_1.xdc).
FETCH = bytes.fromhex("0000000300000001000000000000000000000000ffff0000ffff0000860000020000"
                      "00860023e2ff00000c140000000000000a00")


def pack(texels, magic=b"cxds", size=None):
    raw = FETCH + texels
    compressor = zlib.compressobj(6, zlib.DEFLATED, -15)
    payload = compressor.compress(raw) + compressor.flush()
    size = len(raw) if size is None else size
    return magic + struct.pack(">IIII", 2, size, len(payload), size) + payload


def pattern(x, y):
    return (x * 255 // (WIDTH - 1), y * 255 // (HEIGHT - 1), (x ^ y) & 0xFF)


class ThumbnailsTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.dir = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_header_rejects_other_magic(self):
        path = self.dir / "Thumbnail_1.xdc"
        path.write_bytes(pack(bytes(WIDTH * HEIGHT * 4), magic=b"xxxx"))
        with self.assertRaisesRegex(ValueError, "cxds"):
            read_thumbnail(path)

    def test_unpacked_size_checked(self):
        path = self.dir / "Thumbnail_1.xdc"
        path.write_bytes(pack(bytes(WIDTH * HEIGHT * 4), size=1000))
        with self.assertRaisesRegex(ValueError, "884788"):
            read_thumbnail(path)

    def test_tiled_offsets_match_sdk(self):
        # From the SDK's TiledOffset2DRow/TiledOffset2DColumn (src/graphics/pipeline/texture/
        # conversion.cpp), compiled as they are and run at width 768, log2 bytes per texel 2.
        expected = {(0, 0): 0, (1, 0): 4, (8, 1): 80, (33, 17): 6164, (767, 287): 884604,
                    (100, 200): 603296}
        for (x, y), offset in expected.items():
            self.assertEqual(tiled_offset_2d(x, y, WIDTH, 4), offset, (x, y))

    def test_synthetic_round_trip(self):
        texels = bytearray(WIDTH * HEIGHT * 4)
        for y in range(HEIGHT):
            for x in range(WIDTH):
                offset = tiled_offset_2d(x, y, WIDTH, 4)
                texels[offset:offset + 4] = bytes((0, *pattern(x, y)))
        path = self.dir / "Thumbnail_3.xdc"
        path.write_bytes(pack(bytes(texels)))
        image = read_thumbnail(path)
        self.assertEqual((image.width, image.height), (WIDTH, HEIGHT))
        for x, y in ((0, 0), (5, 3), (767, 287), (400, 100)):
            start = (y * WIDTH + x) * 3
            self.assertEqual(tuple(image.rgb[start:start + 3]), pattern(x, y), (x, y))

    def test_thumbnail_paths_by_slot(self):
        folder = self.dir / "user" / "B13EBABEBABEBABE" / "4D530910" / "00000001" / "ForzaProfile" / "Thumbnails"
        folder.mkdir(parents=True)
        for name in ("Thumbnail_1.xdc", "Thumbnail_12.xdc", "other.xdc"):
            (folder / name).write_bytes(b"")
        self.assertEqual(sorted(thumbnail_paths(self.dir)), [1, 12])
        self.assertEqual(thumbnail_paths(self.dir)[12].name, "Thumbnail_12.xdc")


if __name__ == "__main__":
    unittest.main()
