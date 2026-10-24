import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

from toolchain import msvc
from toolchain.tests import packages, server

ENTRY = Path(__file__).resolve().parents[2] / "toolchain.py"


def run_cli(*args):
    return subprocess.run([sys.executable, "-I", str(ENTRY), *map(str, args)],
                          capture_output=True, text=True, encoding="utf-8")


class MsvcTest(unittest.TestCase):
    def setUp(self):
        self.files = {}
        serving = server.serve(self.files)
        self.base = serving.__enter__()
        self.addCleanup(serving.__exit__, None, None, None)
        self.files.update(packages.crt_feed(self.base))
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.downloads = Path(temp.name) / "downloads"
        self.root = Path(temp.name) / "msvc-14.44"

    def test_resolve_picks_the_four_crt_packages_of_the_line(self):
        resolved = msvc.resolve(self.base + "/channel", "14.44")
        self.assertEqual(sorted(".".join(p.id.split(".")[6:]) for p in resolved.packages),
                         ["CRT.Headers.base", "CRT.Redist.X64.base", "CRT.x64.Desktop.base", "CRT.x64.Store.base"])
        self.assertEqual(resolved.licence_url, packages.LICENCE)

    def test_resolve_refuses_a_manifest_of_another_build_than_the_channel(self):
        self.files.update(packages.crt_feed(self.base, manifest_build="17.14.0.9"))
        with self.assertRaises(msvc.FeedError):
            msvc.resolve(self.base + "/channel", "14.44")

    def test_resolve_does_not_need_the_manifest_hash_the_channel_names(self):
        # Microsoft's CDN serves a minified manifest whose hash never matches the channel's.
        self.files.update(packages.crt_feed(self.base, manifest_sha="ab" * 32))
        self.assertEqual(len(msvc.resolve(self.base + "/channel", "14.44").packages), 4)

    def test_resolve_refuses_a_line_the_feed_does_not_serve(self):
        with self.assertRaises(msvc.FeedError):
            msvc.resolve(self.base + "/channel", "14.99")

    def test_install_unpacks_contents_and_reports_the_paths(self):
        installed = msvc.install(msvc.resolve(self.base + "/channel", "14.44"), self.downloads, self.root)
        self.assertEqual(installed.toolset_version, "14.44.35226")
        self.assertTrue((installed.include / "vcruntime.h").is_file())
        self.assertTrue((installed.lib / "msvcprt.lib").is_file())
        self.assertTrue((installed.lib / "oldnames.lib").is_file())
        self.assertTrue((installed.lib / "libcpmt.lib").is_file())
        self.assertTrue((installed.redist / "vcruntime140.dll").is_file())
        self.assertFalse((installed.folder / "manifest.json").exists())
        self.assertFalse((installed.folder / "Contents").exists())
        self.assertEqual(msvc.find_installed(self.root, "14.44"), installed)

    def test_the_packages_are_deleted_once_unpacked(self):
        msvc.install(msvc.resolve(self.base + "/channel", "14.44"), self.downloads, self.root)
        self.assertEqual(list(self.downloads.iterdir()), [])

    def test_a_new_patch_gets_its_own_folder_and_the_old_one_goes(self):
        self.files.update(packages.crt_feed(self.base, prefix="/a", toolset="14.44.35207"))
        self.files.update(packages.crt_feed(self.base, prefix="/b", toolset="14.44.35226"))
        first = msvc.install(msvc.resolve(self.base + "/a/channel", "14.44"), self.downloads, self.root)
        second = msvc.install(msvc.resolve(self.base + "/b/channel", "14.44"), self.downloads, self.root)
        self.assertNotEqual(first.folder, second.folder)
        self.assertFalse(first.folder.exists())
        self.assertEqual(msvc.find_installed(self.root, "14.44").toolset_version, "14.44.35226")
        self.assertEqual(sorted(p.name for p in second.include.iterdir()), ["vcruntime.h", "vector"])

    def test_a_republished_package_of_the_same_patch_replaces_the_folder(self):
        first = msvc.install(msvc.resolve(self.base + "/channel", "14.44"), self.downloads, self.root)
        self.files.update(packages.crt_feed(self.base, redist="14.44.35299"))
        second = msvc.install(msvc.resolve(self.base + "/channel", "14.44"), self.downloads, self.root)
        self.assertEqual(first.folder, second.folder)
        self.assertTrue((second.redist / "vcruntime140.dll").is_file())
        self.assertFalse(first.redist.exists())
        self.assertEqual(msvc.find_installed(self.root, "14.44"), second)

    def test_an_interrupted_unpack_is_not_found(self):
        resolved = msvc.resolve(self.base + "/channel", "14.44")
        real_open = zipfile.ZipFile.open
        calls = []

        def failing_open(zip_file, *args, **kwargs):
            calls.append(1)
            if len(calls) == 3:
                raise OSError("disk full")
            return real_open(zip_file, *args, **kwargs)

        with mock.patch.object(zipfile.ZipFile, "open", failing_open):
            with self.assertRaises(OSError):
                msvc.install(resolved, self.downloads, self.root)
        self.assertIsNone(msvc.find_installed(self.root, "14.44"))
        msvc.install(resolved, self.downloads, self.root)
        self.assertEqual([p.name for p in self.root.iterdir()], ["14.44.35226"])

    def test_a_member_leaving_the_folder_is_refused(self):
        self.files.update(packages.crt_feed(self.base, extra_headers={"../../evil.h": b""}))
        with self.assertRaises(msvc.FeedError):
            msvc.install(msvc.resolve(self.base + "/channel", "14.44"), self.downloads, self.root)
        self.assertFalse((self.root.parent / "evil.h").exists())
        self.assertIsNone(msvc.find_installed(self.root, "14.44"))

    def test_libraries_under_another_patch_folder_are_not_recorded(self):
        self.files.update(packages.crt_feed(self.base, lib_toolset="14.44.39999"))
        with self.assertRaises(msvc.FeedError):
            msvc.install(msvc.resolve(self.base + "/channel", "14.44"), self.downloads, self.root)
        self.assertIsNone(msvc.find_installed(self.root, "14.44"))

    def test_cli_refuses_without_the_licence_and_fetches_nothing(self):
        out = run_cli("msvc", "--channel", self.base + "/channel", "--line", "14.44",
                      "--root", self.root, "--downloads", self.downloads)
        self.assertEqual(out.returncode, 3)
        result = json.loads(out.stdout.splitlines()[-1])
        self.assertEqual(result, {"error": "licence", "build_tools": packages.LICENCE})
        self.assertEqual(server.hits(self.base, ".vsix"), 0)

    def test_cli_installs_with_the_licence_and_finds_it(self):
        out = run_cli("msvc", "--channel", self.base + "/channel", "--line", "14.44",
                      "--root", self.root, "--downloads", self.downloads, "--accept-license", "--json-events")
        self.assertEqual(out.returncode, 0, out.stderr)
        lines = out.stdout.splitlines()
        self.assertTrue(lines[0].startswith("::fm4::"))
        result = json.loads(lines[-1])
        self.assertEqual(result["toolset_version"], "14.44.35226")
        self.assertTrue(Path(result["redist"], "msvcp140.dll").is_file())

    def test_cli_reports_a_feed_error_with_its_step(self):
        out = run_cli("msvc", "--channel", self.base + "/missing", "--line", "14.44",
                      "--root", self.root, "--downloads", self.downloads, "--accept-license")
        self.assertEqual(out.returncode, 2)
        self.assertEqual(json.loads(out.stdout.splitlines()[-1])["error"], "msvc")
