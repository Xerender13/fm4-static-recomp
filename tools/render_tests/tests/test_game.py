import tempfile
import unittest
from pathlib import Path

from render_tests.game import FileOpened, Log, RunBroken, Session, String, owned
from render_tests.logtail import parse


class FakeHost:
    """A game that is never started: a fake clock, presses recorded, a log the test writes."""

    def __init__(self, log_path, dies_at=None, signal_after_presses=None, signal_line=""):
        self.log_path = log_path
        self.clock = 0.0
        self.dies_at = dies_at
        self.signal_after_presses = signal_after_presses
        self.signal_line = signal_line
        self.inputs = []
        self.killed = False
        log_path.parent.mkdir(parents=True, exist_ok=True)
        log_path.write_text("", encoding="utf-8")

    def start(self, run_dir, game_args):
        pass

    def alive(self):
        return not self.killed and (self.dies_at is None or self.clock < self.dies_at)

    def kill(self):
        self.killed = True

    def now(self):
        return self.clock

    def game_time(self):
        return self.clock

    def sleep(self, seconds):
        self.clock += seconds

    def append_input(self, line):
        self.inputs.append(line)
        presses = [entry for entry in self.inputs if not entry.startswith("#")]
        if self.signal_after_presses and len(presses) == self.signal_after_presses:
            with open(self.log_path, "a", encoding="utf-8") as log:
                log.write(f"[2026-10-24 01:00:00.000] [info] [core] [t1] {self.signal_line}\n")


class SessionTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.run = Path(self.temp.name) / "run"
        self.log = self.run / "state" / "logs" / "runtime.log"

    def tearDown(self):
        self.temp.cleanup()

    def test_press_until_repeats_until_signal(self):
        host = FakeHost(self.log, signal_after_presses=3,
                        signal_line="File open: game:\\media\\tracks\\homespace07\\bin.zip")
        with Session(self.run, [], host=host) as session:
            session.press_until("START", FileOpened("homespace07\\bin.zip"), timeout=30, every=1.5)
            presses = [line for line in host.inputs if line.endswith(" START")]
        self.assertEqual(len(presses), 3)
        self.assertTrue(host.killed)

    def test_press_until_times_out_naming_the_step(self):
        host = FakeHost(self.log)
        with Session(self.run, [], host=host) as session:
            with self.assertRaises(RunBroken) as caught:
                session.press_until("A", Log("never"), timeout=10, step="leave the title")
        self.assertIn("leave the title", str(caught.exception))
        self.assertLessEqual(host.clock, 12)

    def test_wait_fails_fast_when_process_exits(self):
        host = FakeHost(self.log, dies_at=2.0)
        with Session(self.run, [], host=host) as session:
            with self.assertRaises(RunBroken) as caught:
                session.wait(Log("never"), timeout=30, step="race start")
        self.assertIn("exited", str(caught.exception))
        self.assertIn("race start", str(caught.exception))
        self.assertLess(host.clock, 2.5)

    def test_press_lines_use_game_time_and_dot_decimals(self):
        host = FakeHost(self.log)
        host.clock = 12.35
        with Session(self.run, [], host=host) as session:
            session.press("A", hold=0.1)
        self.assertIn("12.65 0.10 A", host.inputs)

    def test_string_signal_matches_whole_text(self):
        def line(text):
            return parse(f"[2026-10-24 01:00:00.000] [info] [core] [t1] {text}")
        self.assertTrue(String("CAR SELECT").matches(line("String: CAR SELECT")))
        self.assertFalse(String("CAR SELECT").matches(line("String: CAR SELECT HELP")))
        self.assertFalse(String("CAR SELECT").matches(line("Prompt text 0001: CAR SELECT")))

    def test_owned_process_filter_ignores_other_state_roots(self):
        root = Path(r"C:\x\fm4\.local")
        run = root / "render-tests" / "runs" / "20261024-1-short"
        command_lines = [
            (10, rf'fm4_static_recomp.exe --user_data_root={root}\state\user'),
            (11, rf'fm4_static_recomp.exe --user_data_root={root}\render-tests\runs\20261024-10-short\state\user'),
            (12, rf'fm4_static_recomp.exe --user_data_root={str(run).upper()}\STATE\user'),
            (13, None),
        ]
        self.assertEqual(owned(command_lines, run), [12])
        self.assertEqual(owned(command_lines[:2], run), [])


if __name__ == "__main__":
    unittest.main()
