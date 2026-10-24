"""Each check shown to go red on a damaged copy of a real reference (Task 8 of the plan). The
references are game-made and live under .local\\render-tests\\references (never committed), so
these tests skip without them."""
import random
import tempfile
import unittest
from datetime import datetime
from pathlib import Path

from render_tests import images, scenarios
from render_tests.checks import References, chroma_check, differ_check, max_check, ratio_check
from render_tests.counters import window
from render_tests.images import Image
from render_tests.logtail import parse

REFERENCES = Path(__file__).resolve().parents[3] / ".local" / "render-tests" / "references" / "2x"


def reference(name):
    path = REFERENCES / f"{name}.png"
    if not path.exists():
        raise unittest.SkipTest(f"no reference {path}")
    return images.read_png(path)


def tinted(image, rect, red, green, blue):
    """Scales the channels of the bright pixels inside rect, as the past tint bugs did."""
    rgb = bytearray(image.rgb)
    x0, y0, x1, y1 = rect.pixels(image.width, image.height)
    for y in range(y0, y1):
        for x in range(x0, x1):
            at = (y * image.width + x) * 3
            rgb[at] = min(255, int(rgb[at] * red))
            rgb[at + 1] = min(255, int(rgb[at + 1] * green))
            rgb[at + 2] = min(255, int(rgb[at + 2] * blue))
    return Image(image.width, image.height, bytes(rgb))


def chroma(name, image, rect):
    return chroma_check(name, image, rect, scenarios.CHROMA_MEAN, scenarios.CHROMA_P95)


# The twentieth session's purple/blue (green low, blue high) and the thirty-first's magenta
# (red = blue, green very low).
PURPLE = (1.0, 0.85, 1.04)
MAGENTA = (1.0, 0.55, 1.0)


class ProofsTest(unittest.TestCase):
    def test_chroma_fails_on_tinted_cars(self):
        for name, rect in (("showroom", scenarios.SHOWROOM_CAR), ("garage", scenarios.GARAGE_CAR),
                           ("dealer-showroom", scenarios.SHOWROOM_CAR),
                           ("thumbnail-abarth-500", scenarios.THUMBNAIL_CAR)):
            clean = reference(name)
            self.assertTrue(chroma(name, clean, rect).passed, name)
            for tint in (PURPLE, MAGENTA):
                self.assertFalse(chroma(name, tinted(clean, rect, *tint), rect).passed,
                                 (name, tint))

    def test_race_tint_fails_on_tinted_cars(self):
        from render_tests.checks import tint_check
        clean = reference("showroom")
        rect = scenarios.SHOWROOM_CAR
        self.assertTrue(tint_check("car", clean, rect, scenarios.RACE_TINT).passed)
        for tint in (PURPLE, MAGENTA):
            self.assertFalse(tint_check("car", tinted(clean, rect, *tint), rect,
                                        scenarios.RACE_TINT).passed, tint)

    def test_image_check_fails_on_a_block_of_noise(self):
        for name, limits in (("garage", scenarios.FIXED), ("main-menu", scenarios.FIXED),
                             ("thumbnail-abarth-500", scenarios.THUMBNAIL)):
            clean = reference(name)
            rgb = bytearray(clean.rgb)
            generator = random.Random(1)
            x0, y0 = clean.width // 3, clean.height // 3
            for y in range(y0, y0 + 64):
                for x in range(x0, x0 + 64):
                    at = (y * clean.width + x) * 3
                    rgb[at:at + 3] = bytes(generator.randrange(256) for _ in range(3))
            with tempfile.TemporaryDirectory() as temp:
                checks = References(REFERENCES.parent, 2, diff_dir=Path(temp))
                damaged = Image(clean.width, clean.height, bytes(rgb))
                self.assertTrue(checks.image_check(name, clean, limits).passed, name)
                self.assertFalse(checks.image_check(name, damaged, limits).passed, name)

    def test_dirt_differ_fails_without_dirt(self):
        clean = reference("garage")
        self.assertFalse(differ_check("garage dirt", clean, clean, scenarios.GARAGE_CAR,
                                      scenarios.DIRT_MIN_DIFFERENCE).passed)
        self.assertTrue(differ_check("garage dirt", reference("garage-full-dirt"), clean,
                                     scenarios.GARAGE_CAR, scenarios.DIRT_MIN_DIFFERENCE).passed)

    def test_seam_lines_down_the_nose_fail_the_nose_check(self):
        dirty = reference("garage-full-dirt")
        rgb = bytearray(dirty.rgb)
        # Three thin dark lines from the badge up the hood, as the sixty-third session's seam.
        for line in range(3):
            for step in range(60):
                x, y = 662 - line * 5 - step, 356 - step
                for dx in range(2):
                    at = (y * dirty.width + x + dx) * 3
                    rgb[at:at + 3] = bytes(max(0, value - 80) for value in rgb[at:at + 3])
        seam = Image(dirty.width, dirty.height, bytes(rgb))
        with tempfile.TemporaryDirectory() as temp:
            checks = References(REFERENCES.parent, 2, diff_dir=Path(temp))
            whole = checks.image_check("garage-full-dirt", seam, scenarios.FIXED)
            nose = checks.image_check("garage-full-dirt", seam, scenarios.FIXED,
                                      scenarios.GARAGE_NOSE, label="nose")
        self.assertTrue(whole.passed, "the whole view misses thin lines: why the nose check exists")
        self.assertFalse(nose.passed)

    def test_one_off_readbacks_fail_when_a_race_reads_back(self):
        def stats(time, frame, one_off):
            return parse(f"[2026-10-24 {time}] [info] [gpu] [t9] FH1 native executor (Vulkan) "
                         f"frame={frame} draws=0 resolves=0 surfaces=7 surface_mb=38 skips={{}} "
                         f"stats={{resolve_readback_one_off={one_off},transfer=0}}")
        lines = [stats("01:00:05.000", 600, 0), stats("01:00:10.000", 1200, 600)]
        counters = window(lines, datetime(2026, 10, 24, 1, 0), datetime(2026, 10, 24, 1, 1))
        self.assertFalse(max_check("one-off readbacks", counters.one_off_readbacks,
                                   scenarios.ONE_OFF_READBACKS).passed)

    def test_surfaceless_clears_ratio_fails_when_the_switch_does_nothing(self):
        self.assertFalse(ratio_check("tile passes off/on", 1554.9, 1554.9, 2.0).passed)


if __name__ == "__main__":
    unittest.main()
