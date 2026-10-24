import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path

from render_tests import report
from render_tests.checks import CheckResult
from render_tests.game import RunBroken


class ReportTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.run = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def write(self, results, broken=None):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = report.write(self.run, results, broken)
        return code, out.getvalue()

    def test_report_exit_codes(self):
        passing = CheckResult("resolves", True, "29.0", "29.0 +-2%")
        failing = CheckResult("car chroma", False, "mean 45.0", "<= 10")
        self.assertEqual(self.write([passing])[0], 0)
        code, console = self.write([passing, failing])
        self.assertEqual(code, 1)
        self.assertIn("FAIL car chroma", console)
        self.assertIn("PASS resolves", console)
        code, console = self.write([passing], RunBroken("quick race: track", "the game exited"))
        self.assertEqual(code, 2)
        self.assertIn("quick race: track", console)

    def test_summary_names_the_executable_tested(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            report.write(self.run, [], None, {"sha256": "ab12", "modified": "2026-10-24T05:00:00"})
        summary = json.loads((self.run / "summary.json").read_text(encoding="utf-8"))
        self.assertEqual(summary["executable"]["sha256"], "ab12")
        self.assertIn("ab12", out.getvalue())

    def test_summary_json(self):
        self.write([CheckResult("resolves", True, "29.0", "29.0 +-2%", "detail")])
        summary = json.loads((self.run / "summary.json").read_text(encoding="utf-8"))
        self.assertEqual(summary["exit_code"], 0)
        self.assertEqual(summary["checks"][0]["name"], "resolves")
        self.assertIsNone(summary["broken"])


if __name__ == "__main__":
    unittest.main()
