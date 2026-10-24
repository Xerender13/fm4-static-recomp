import unittest
from datetime import datetime

from render_tests.counters import window
from render_tests.logtail import parse


def stats(time, frame, resolves, transfers, passes, one_off):
    return parse(f"[2026-10-24 {time}] [info] [gpu] [t9] FH1 native executor (Vulkan) frame={frame} "
                 f"draws={frame * 900} resolves={resolves} surfaces=7 surface_mb=38 skips={{}} "
                 f"stats={{clear_color=5,resolve_readback_one_off={one_off},transfer={transfers},"
                 f"transfer_tile_passes={passes}}}")


class CountersTest(unittest.TestCase):
    def test_counters_per_frame_over_window(self):
        lines = [stats("01:00:00.000", 6000, 100000, 500000, 5000000, 40),
                 stats("01:00:05.000", 6600, 117400, 587480, 5867840, 40),
                 stats("01:00:10.000", 7200, 134800, 674960, 6735680, 40),
                 stats("01:00:15.000", 7800, 999999, 999999, 9999999, 99)]
        counters = window(lines, datetime(2026, 10, 24, 1, 0, 4), datetime(2026, 10, 24, 1, 0, 11))
        self.assertEqual(counters.frames, 600)
        self.assertAlmostEqual(counters.resolves, 29.0)
        self.assertAlmostEqual(counters.transfers, 145.8)
        self.assertAlmostEqual(counters.transfer_tile_passes, 1446.4)
        self.assertEqual(counters.one_off_readbacks, 0)

    def test_one_off_readbacks_zero_in_race_window(self):
        lines = [stats("01:00:05.000", 6600, 0, 0, 0, 40), stats("01:00:10.000", 7200, 0, 0, 0, 46)]
        counters = window(lines, datetime(2026, 10, 24, 1, 0, 0), datetime(2026, 10, 24, 1, 1, 0))
        self.assertAlmostEqual(counters.one_off_readbacks, 0.01)

    def test_window_with_one_stats_line_is_none(self):
        lines = [stats("01:00:05.000", 6600, 0, 0, 0, 0)]
        self.assertIsNone(window(lines, datetime(2026, 10, 24, 1, 0, 0), datetime(2026, 10, 24, 1, 1, 0)))


if __name__ == "__main__":
    unittest.main()
