import os
import stat
import tempfile
import unittest
from pathlib import Path

from render_tests import seed

PROFILE = Path("user") / "B13EBABEBABEBABE" / "4D530910" / "00000001" / "ForzaProfile" / "ForzaProfile"
CONTENT = Path("user") / "0000000000000000" / "4D530910" / "00000002" / "pack" / "Media" / "a.zip"
CACHE = Path("cache") / "shaders" / "shareable" / "4D530910.xpso"


class SeedTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.player = self.root / "state"
        for relative, content in ((PROFILE, b"profile"), (CONTENT, b"content"),
                                  (Path("config") / "graphics.toml", b"resolution_scale = 3\n")):
            (self.player / relative).parent.mkdir(parents=True, exist_ok=True)
            (self.player / relative).write_bytes(content)
        self.seeds = self.root / "seeds"

    def tearDown(self):
        for path in self.root.rglob("*"):
            if path.is_file():
                os.chmod(path, stat.S_IWRITE | stat.S_IREAD)
        self.temp.cleanup()

    def copy_seed(self):
        return seed.copy_player_state(self.player, self.seeds / "s1")

    def test_make_seed_refuses_existing(self):
        (self.seeds / "s1").mkdir(parents=True)
        with self.assertRaisesRegex(FileExistsError, "s1"):
            self.copy_seed()

    def test_seed_takes_the_save_not_the_graphics_settings(self):
        state = self.copy_seed()
        self.assertEqual((state / PROFILE).read_bytes(), b"profile")
        self.assertEqual((state / CONTENT).read_bytes(), b"content")
        self.assertFalse((state / "config" / "graphics.toml").exists())

    def test_prepare_run_never_touches_seed_or_player_state(self):
        seed_state = self.copy_seed()
        (seed_state / CACHE).parent.mkdir(parents=True)
        (seed_state / CACHE).write_bytes(b"pipelines")
        run = self.root / "runs" / "r1"
        seed.prepare_run(seed_state.parent, run)
        (run / "state" / PROFILE).write_bytes(b"changed")
        (run / "state" / CACHE).write_bytes(b"changed")
        with self.assertRaises(PermissionError):
            (run / "state" / CONTENT).write_bytes(b"changed")
        self.assertEqual((seed_state / PROFILE).read_bytes(), b"profile")
        self.assertEqual((seed_state / CACHE).read_bytes(), b"pipelines")
        self.assertEqual((seed_state / CONTENT).read_bytes(), b"content")
        self.assertEqual((self.player / PROFILE).read_bytes(), b"profile")
        self.assertEqual((self.player / CONTENT).read_bytes(), b"content")

    def test_remove_run_state_keeps_the_seed(self):
        seed_state = self.copy_seed()
        run = self.root / "runs" / "r1"
        seed.prepare_run(seed_state.parent, run)
        seed.remove_run_state(run, seed_state.parent)
        self.assertFalse((run / "state").exists())
        self.assertEqual((seed_state / CONTENT).read_bytes(), b"content")
        self.assertFalse(os.access(seed_state / CONTENT, os.W_OK))

    def test_warm_from_copies_a_runs_grown_shader_cache(self):
        seed_state = self.copy_seed()
        (seed_state / CACHE).parent.mkdir(parents=True)
        (seed_state / CACHE).write_bytes(b"pipelines")
        (seed_state.parent / "seed.json").write_text("{}", encoding="utf-8")
        run = self.root / "runs" / "r1"
        seed.prepare_run(seed_state.parent, run)
        (run / "state" / CACHE).write_bytes(b"pipelines and more")
        (run / "state" / CACHE).with_name("4D530910.signs.xpso").write_bytes(b"s")
        seed.warm_from(seed_state.parent, run)
        self.assertEqual((seed_state / CACHE).read_bytes(), b"pipelines and more")
        self.assertTrue((seed_state / CACHE).with_name("4D530910.signs.xpso").exists())
        self.assertIn("r1", (seed_state.parent / "seed.json").read_text(encoding="utf-8"))

    def test_prepare_run_refuses_an_existing_run(self):
        seed_state = self.copy_seed()
        run = self.root / "runs" / "r1"
        (run / "state").mkdir(parents=True)
        with self.assertRaises(FileExistsError):
            seed.prepare_run(seed_state.parent, run)


if __name__ == "__main__":
    unittest.main()
