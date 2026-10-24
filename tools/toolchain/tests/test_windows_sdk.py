import json
import tempfile
import unittest
from pathlib import Path

from toolchain import windows_sdk
from toolchain.tests import packages, server
from toolchain.tests.test_msvc import run_cli

VERSION = "10.0.26100.9169"


class WindowsSdkTest(unittest.TestCase):
    def setUp(self):
        files, self.pins = packages.sdk_packages(VERSION)
        serving = server.serve(files)
        self.base = serving.__enter__()
        self.addCleanup(serving.__exit__, None, None, None)
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.downloads = Path(temp.name) / "downloads"
        self.root = Path(temp.name) / "toolchain"

    def test_windows_sdk_takes_only_includes_libs_and_the_two_tools(self):
        installed = windows_sdk.install(VERSION, self.pins, self.downloads, self.root, base_url=self.base)
        self.assertEqual([p.name for p in installed.include], ["ucrt", "um", "shared", "winrt", "cppwinrt"])
        self.assertTrue((installed.lib[1] / "kernel32.lib").is_file())
        self.assertTrue(installed.rc.is_file() and installed.mt.is_file())
        # rcdll.dll loads ServicingCommon.dll, mt.exe loads midlrtmd.dll (0xC0000135 without them).
        for dll in ("rcdll.dll", "ServicingCommon.dll", "midlrtmd.dll"):
            self.assertTrue((installed.rc.parent / dll).is_file(), dll)
        self.assertFalse((installed.rc.parent / "signtool.exe").exists())
        self.assertFalse((installed.folder / "Source").exists())
        self.assertEqual(installed.folder, self.root / f"windows-sdk-{VERSION}")
        self.assertEqual(installed.sdk_version, packages.SDK_FOLDER)
        self.assertEqual(windows_sdk.find_installed(self.root, VERSION), installed)

    def test_progress_rises_across_both_packages(self):
        seen = []
        windows_sdk.install(VERSION, self.pins, self.downloads, self.root, base_url=self.base,
                            progress=lambda name, fraction: seen.append(fraction))
        self.assertEqual(seen, sorted(seen))
        self.assertEqual(seen[-1], 1.0)

    def test_the_packages_are_deleted_once_unpacked(self):
        windows_sdk.install(VERSION, self.pins, self.downloads, self.root, base_url=self.base)
        self.assertEqual(list(self.downloads.iterdir()), [])

    def test_an_install_with_fewer_tools_is_unpacked_again(self):
        installed = windows_sdk.install(VERSION, self.pins, self.downloads, self.root, base_url=self.base)
        record = installed.folder / windows_sdk.RECORD
        data = json.loads(record.read_text(encoding="utf-8"))
        data["tools"] = ["rc.exe", "rcdll.dll", "mt.exe"]
        record.write_text(json.dumps(data), encoding="utf-8")
        (installed.rc.parent / "ServicingCommon.dll").unlink()
        self.assertIsNone(windows_sdk.find_installed(self.root, VERSION))
        again = windows_sdk.install(VERSION, self.pins, self.downloads, self.root, base_url=self.base)
        self.assertTrue((again.rc.parent / "ServicingCommon.dll").is_file())

    def test_a_package_with_another_hash_is_refused(self):
        pins = [(self.pins[0][0], "00" * 32), self.pins[1]]
        with self.assertRaises(windows_sdk.fetch.DownloadError):
            windows_sdk.install(VERSION, pins, self.downloads, self.root, base_url=self.base)
        self.assertIsNone(windows_sdk.find_installed(self.root, VERSION))

    def test_cli_refuses_without_the_licence(self):
        out = run_cli("windows-sdk", "--version", VERSION, "--package", "=".join(self.pins[0]),
                      "--package", "=".join(self.pins[1]), "--root", self.root, "--downloads", self.downloads)
        self.assertEqual(out.returncode, 3)
        self.assertEqual(json.loads(out.stdout.splitlines()[-1])["error"], "licence")
        self.assertFalse(self.downloads.exists())

    def test_cli_find_reports_both_records_or_missing(self):
        out = run_cli("find", "--line", "14.44", "--msvc-root", self.root / "msvc-14.44",
                      "--sdk-version", VERSION, "--sdk-root", self.root)
        self.assertEqual(out.returncode, 4)
        self.assertEqual(json.loads(out.stdout.splitlines()[-1]), {"error": "missing"})
