import tempfile
import unittest
from pathlib import Path

from render_tests import routes
from render_tests.game import Session, String

from render_tests.tests.test_game import FakeHost


class ScriptedHost(FakeHost):
    """Answers each press with the next log text scripted for its control (None: dropped)."""

    def __init__(self, log_path, answers):
        super().__init__(log_path)
        self.answers = {control: list(texts) for control, texts in answers.items()}
        self.pressed = []

    def append_input(self, line):
        super().append_input(line)
        control = line.split()[-1]
        self.pressed.append(control)
        queue = self.answers.get(control)
        text = queue.pop(0) if queue else None
        if text:
            self.log(text)

    def log(self, text):
        with open(self.log_path, "a", encoding="utf-8") as log:
            log.write(f"[2026-10-24 01:00:00.000] [info] [core] [t1] {text}\n")


class EnterTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.run = Path(self.temp.name) / "run"
        self.log = self.run / "state" / "logs" / "runtime.log"

    def tearDown(self):
        self.temp.cleanup()

    def enter(self, answers, on_screen=("String: AUTOVISTA", "String: FREE PLAY")):
        host = ScriptedHost(self.log, answers)
        for text in on_screen:
            host.log(text)
        with Session(self.run, [], host=host) as session:
            session.poll()
            routes.enter(session, [], String("QUICK RACE"), routes.MAIN_MENU, "free play")
        return host.pressed

    def test_a_dropped_press_is_pressed_again_without_backing_out(self):
        pressed = self.enter({"A": [None, "String: QUICK RACE"]})
        self.assertEqual(pressed, ["A", "A"])

    def test_a_wrong_screen_is_backed_out_of_then_tried_again(self):
        pressed = self.enter({"A": ["String: UPGRADE", "String: QUICK RACE"],
                              "B": ["String: AUTOVISTA"]})
        self.assertEqual(pressed, ["A", "B", "A"])

    def test_the_screens_own_strings_again_are_not_a_new_screen(self):
        pressed = self.enter({"A": ["String: FREE PLAY", "String: QUICK RACE"]})
        self.assertEqual(pressed, ["A", "A"])


if __name__ == "__main__":
    unittest.main()
