import tempfile
import unittest
from datetime import datetime
from pathlib import Path

from render_tests.logtail import LogTail, draws_per_frame

STATS_600 = ("[2026-10-24 01:03:36.650] [info] [gpu] [t20888] FH1 native executor (Vulkan) frame=600 "
             "draws=51964 resolves=1984 surfaces=7 surface_mb=38 skips={} stats={transfer=204,"
             "transfer_tile_passes=13328}")
STATS_1200 = ("[2026-10-24 01:03:41.733] [info] [gpu] [t20888] FH1 native executor (Vulkan) "
              "frame=1200 draws=352939 resolves=14295 surfaces=9 surface_mb=50 skips={} "
              "stats={transfer=4930,transfer_tile_passes=133780}")


class LogTailTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name) / "runtime.log"

    def tearDown(self):
        self.temp.cleanup()

    def append(self, text):
        with open(self.path, "a", encoding="utf-8", newline="") as log:
            log.write(text)

    def test_logtail_reads_only_new_complete_lines(self):
        tail = LogTail(self.path)
        self.assertEqual(tail.poll(), [])
        self.append("[2026-10-24 01:00:00.000] [info] [core] [t1] one\n[2026-10-24 01:00")
        self.assertEqual([line.text for line in tail.poll()], ["one"])
        self.assertEqual(tail.poll(), [])
        self.append(":01.000] [info] [core] [t1] two\n")
        self.assertEqual([line.text for line in tail.poll()], ["two"])

    def test_logtail_parses_timestamp_and_text(self):
        self.append(STATS_600 + "\n")
        line, = LogTail(self.path).poll()
        self.assertEqual(line.time, datetime(2026, 10, 24, 1, 3, 36, 650000))
        self.assertEqual((line.level, line.category, line.thread), ("info", "gpu", "t20888"))
        self.assertTrue(line.text.startswith("FH1 native executor (Vulkan) frame=600 draws=51964"))

    def test_logtail_starts_over_when_the_log_is_replaced(self):
        self.append("[2026-10-24 01:00:00.000] [info] [core] [t1] a long first line\n")
        tail = LogTail(self.path)
        tail.poll()
        self.path.write_text("[2026-10-24 01:00:01.000] [info] [core] [t1] b\n", encoding="utf-8")
        self.assertEqual([line.text for line in tail.poll()], ["b"])

    def test_draws_at_least_uses_last_two_stats_lines(self):
        self.append(STATS_600 + "\n" + STATS_1200 + "\n")
        lines = LogTail(self.path).poll()
        self.assertAlmostEqual(draws_per_frame(lines), (352939 - 51964) / 600)
        self.assertEqual(draws_per_frame(lines[:1]), 0)


if __name__ == "__main__":
    unittest.main()
