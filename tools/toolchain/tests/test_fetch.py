import hashlib
import tempfile
import unittest
from pathlib import Path

from toolchain import fetch
from toolchain.tests import server


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


class FetchTest(unittest.TestCase):
    DATA = bytes(range(256)) * 4000  # 1,024,000 bytes

    def test_download_splits_into_ranged_parts_and_checks_the_hash(self):
        with server.serve({"/f.bin": self.DATA}) as base, tempfile.TemporaryDirectory() as d:
            seen = []
            fetch.download([base + "/f.bin"], Path(d) / "f.bin", sha(self.DATA), parts=4,
                           progress=lambda done, total: seen.append((done, total)))
            self.assertEqual((Path(d) / "f.bin").read_bytes(), self.DATA)
            self.assertEqual(seen[-1], (len(self.DATA), len(self.DATA)))
            self.assertEqual(list(Path(d).iterdir()), [Path(d) / "f.bin"])

    def test_server_that_ignores_range_still_gives_the_file(self):
        with server.serve({"/f.bin": self.DATA}, honour_range=False) as base, tempfile.TemporaryDirectory() as d:
            fetch.download([base + "/f.bin"], Path(d) / "f.bin", sha(self.DATA), parts=4)
            self.assertEqual((Path(d) / "f.bin").read_bytes(), self.DATA)

    def test_wrong_hash_raises_and_leaves_nothing(self):
        with server.serve({"/f.bin": self.DATA}) as base, tempfile.TemporaryDirectory() as d:
            with self.assertRaises(fetch.DownloadError):
                fetch.download([base + "/f.bin"], Path(d) / "f.bin", "00" * 32)
            self.assertEqual(list(Path(d).iterdir()), [])

    def test_mirror_is_used_after_the_first_url_fails(self):
        with server.serve({"/m.bin": self.DATA}, fail={"/f.bin"}) as base, tempfile.TemporaryDirectory() as d:
            fetch.download([base + "/f.bin", base + "/m.bin"], Path(d) / "f.bin", sha(self.DATA))
            self.assertEqual((Path(d) / "f.bin").read_bytes(), self.DATA)

    def test_present_file_with_the_right_hash_is_not_fetched(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "f.bin").write_bytes(self.DATA)
            fetch.download(["http://127.0.0.1:9/never"], Path(d) / "f.bin", sha(self.DATA))

    def test_text_hash_folds_crlf(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d) / "a.h").write_bytes(b"x\r\ny\r\n")
            self.assertEqual(fetch.sha256_file(Path(d) / "a.h", text_lf=True), sha(b"x\ny\n"))
