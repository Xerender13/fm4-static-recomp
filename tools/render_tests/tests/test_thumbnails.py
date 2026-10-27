import struct
import tempfile
import unittest
import zlib
from pathlib import Path

from render_tests import thumbnails
from render_tests.thumbnails import (HEIGHT, WIDTH, read_thumbnail, read_thumbnail_alpha,
                                     shadow_pixels, thumbnail_paths, tiled_offset_2d)

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

    def test_alpha_plane_from_the_first_byte(self):
        texels = bytearray(WIDTH * HEIGHT * 4)
        for x, y, value in ((0, 0, 7), (767, 287, 255), (400, 100, 128)):
            offset = tiled_offset_2d(x, y, WIDTH, 4)
            texels[offset:offset + 4] = bytes((value, 1, 2, 3))
        path = self.dir / "Thumbnail_4.xdc"
        path.write_bytes(pack(bytes(texels)))
        alpha = read_thumbnail_alpha(path)
        self.assertEqual(len(alpha), WIDTH * HEIGHT)
        for x, y, value in ((0, 0, 7), (767, 287, 255), (400, 100, 128), (5, 5, 0)):
            self.assertEqual(alpha[y * WIDTH + x], value, (x, y))

    def test_shadow_pixels_count_the_soft_band_under_the_car(self):
        def car(shadow):
            alpha = bytearray(WIDTH * HEIGHT)
            for y in range(100, 240):
                alpha[y * WIDTH + 100:y * WIDTH + 500] = bytes([255]) * 400
            # One fringe pixel under the body, as an antialiased edge leaves.
            alpha[240 * WIDTH + 100:240 * WIDTH + 500] = bytes([60]) * 400
            if shadow:
                for y in range(241, 270):
                    alpha[y * WIDTH + 80:y * WIDTH + 520] = bytes([200 - (y - 241) * 6]) * 440
            return bytes(alpha)

        self.assertEqual(shadow_pixels(car(False)), 400)
        self.assertGreater(shadow_pixels(car(True)), 10000)

    def test_shadow_pixels_ignore_the_small_side_view(self):
        alpha = bytearray(WIDTH * HEIGHT)
        for y in range(50, 200):
            alpha[y * WIDTH + 700:y * WIDTH + 760] = bytes([255]) * 60
        for y in range(200, 280):
            alpha[y * WIDTH + 700:y * WIDTH + 760] = bytes([100]) * 60
        self.assertEqual(shadow_pixels(bytes(alpha)), 0)

    def test_thumbnail_paths_by_slot(self):
        folder = self.dir / "user" / "B13EBABEBABEBABE" / "4D530910" / "00000001" / "ForzaProfile" / "Thumbnails"
        folder.mkdir(parents=True)
        for name in ("Thumbnail_1.xdc", "Thumbnail_12.xdc", "other.xdc"):
            (folder / name).write_bytes(b"")
        self.assertEqual(sorted(thumbnail_paths(self.dir)), [1, 12])
        self.assertEqual(thumbnail_paths(self.dir)[12].name, "Thumbnail_12.xdc")


if __name__ == "__main__":
    unittest.main()
