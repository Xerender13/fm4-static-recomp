import struct
import tempfile
import unittest
from pathlib import Path

from render_tests import images
from render_tests.images import Image, ImageLimits, Rect


def solid(width, height, rgb):
    return Image(width, height, bytes(rgb) * (width * height))


def bmp_bytes(width, height, rows, top_down):
    row_bytes = (width * 3 + 3) & ~3
    pixels = bytearray()
    for row in rows if top_down else reversed(rows):
        line = bytearray()
        for r, g, b in row:
            line += bytes((b, g, r))
        pixels += line + bytes(row_bytes - len(line))
    header = b"BM" + struct.pack("<IHHI", 54 + len(pixels), 0, 0, 54)
    info = struct.pack("<IiiHHIIiiII", 40, width, -height if top_down else height, 1, 24, 0,
                       len(pixels), 0, 0, 0, 0)
    return header + info + bytes(pixels)


class ImagesTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.dir = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def test_bmp_round_trip_top_down_and_bottom_up(self):
        rows = [[(1, 2, 3), (4, 5, 6), (7, 8, 9)], [(10, 11, 12), (13, 14, 15), (16, 17, 18)]]
        expected = bytes(value for row in rows for pixel in row for value in pixel)
        for top_down in (True, False):
            path = self.dir / f"{top_down}.bmp"
            path.write_bytes(bmp_bytes(3, 2, rows, top_down))
            image = images.read_bmp(path)
            self.assertEqual((image.width, image.height), (3, 2))
            self.assertEqual(image.rgb, expected)

    def test_png_round_trip(self):
        image = Image(3, 2, bytes(range(18)))
        path = self.dir / "a.png"
        images.write_png(path, image)
        back = images.read_png(path)
        self.assertEqual((back.width, back.height, back.rgb), (3, 2, image.rgb))

    def test_compare_identical_passes(self):
        image = solid(10, 10, (90, 100, 110))
        result = images.compare(image, image, ImageLimits())
        self.assertTrue(result.passed)
        self.assertEqual((result.mean_abs, result.rmse, result.changed_ratio), (0, 0, 0))

    def test_compare_one_bright_pixel_changed(self):
        reference = solid(100, 100, (128, 128, 128))
        actual = bytearray(reference.rgb)
        actual[0:3] = bytes((168, 168, 168))
        result = images.compare(Image(100, 100, bytes(actual)), reference, ImageLimits())
        self.assertAlmostEqual(result.changed_ratio, 0.0001)
        self.assertTrue(result.passed)

    def test_compare_size_mismatch_fails(self):
        result = images.compare(solid(1280, 720, (0, 0, 0)), solid(2560, 1440, (0, 0, 0)),
                                ImageLimits())
        self.assertIs(result.passed, False)
        self.assertIn("1280x720", result.detail)
        self.assertIn("2560x1440", result.detail)

    def test_compare_inside_mask_only(self):
        reference = solid(10, 10, (0, 0, 0))
        actual = bytearray(reference.rgb)
        for x in range(5):
            for y in range(10):
                actual[(y * 10 + x) * 3:(y * 10 + x) * 3 + 3] = bytes((255, 255, 255))
        result = images.compare(Image(10, 10, bytes(actual)), reference, ImageLimits(),
                                mask=Rect(0.5, 0, 1, 1))
        self.assertTrue(result.passed)
        self.assertEqual(result.mean_abs, 0)

    def test_chroma_white_is_neutral(self):
        result = images.chroma(solid(8, 8, (255, 255, 255)), Rect(0, 0, 1, 1))
        self.assertEqual(result.mean, 0)
        self.assertEqual(result.pixels, 64)

    def test_chroma_purple_tint_detected(self):
        result = images.chroma(solid(8, 8, (235, 200, 245)), Rect(0, 0, 1, 1))
        self.assertEqual(result.mean, 45)

    def test_chroma_ignores_dark_pixels(self):
        rgb = bytearray(solid(4, 4, (0, 0, 0)).rgb)
        for index in (0, 5, 10):
            rgb[index * 3:index * 3 + 3] = bytes((250, 250, 250))
        result = images.chroma(Image(4, 4, bytes(rgb)), Rect(0, 0, 1, 1))
        self.assertEqual(result.pixels, 3)
        self.assertEqual(result.mean, 0)

    def test_green_deficit_ignores_sky_shading_and_finds_purple(self):
        def deficit(rgb):
            return images.chroma(solid(4, 4, rgb), Rect(0, 0, 1, 1), min_luma=0).green_deficit
        self.assertEqual(deficit((200, 215, 240)), 0)
        self.assertEqual(deficit((245, 235, 200)), 0)
        self.assertEqual(deficit((235, 200, 245)), 35)
        self.assertEqual(deficit((240, 130, 240)), 110)

    def test_downscale_box_filter(self):
        image = Image(2, 2, bytes((0, 0, 0, 100, 100, 100, 200, 200, 200, 100, 100, 100)))
        small = images.downscale(image, 1, 1)
        self.assertEqual(small.rgb, bytes((100, 100, 100)))

    def test_difference_image_scaled_by_four(self):
        diff = images.difference_image(solid(1, 1, (10, 10, 100)), solid(1, 1, (0, 20, 0)))
        self.assertEqual(diff.rgb, bytes((40, 40, 255)))


if __name__ == "__main__":
    unittest.main()
