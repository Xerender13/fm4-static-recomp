import ctypes
import json
import os
import shutil
import subprocess
import tarfile
import tempfile
import unittest
import zipfile
from pathlib import Path

from toolchain import windows_sdk
from toolchain.tests import packages, server

TOOLS = Path(__file__).resolve().parents[2]


def ps(expression, script=TOOLS / "release-common.ps1", env=None):
    # UTF-8 both ways, whatever the console's code page.
    command = (f"[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); . '{script}'; "
               f"{expression} | ConvertTo-Json -Compress")
    out = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
                         capture_output=True, text=True, encoding="utf-8", env={**os.environ, **(env or {})})
    if out.returncode != 0:
        raise AssertionError(out.stderr)
    return json.loads(out.stdout)


def command_line_to_argv(arguments):
    """How Windows splits a command line into the game's argv (without the program name)."""
    shell32, kernel32 = ctypes.windll.shell32, ctypes.windll.kernel32
    shell32.CommandLineToArgvW.restype = ctypes.POINTER(ctypes.c_wchar_p)
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    count = ctypes.c_int()
    parsed = shell32.CommandLineToArgvW("game.exe " + arguments, ctypes.byref(count))
    try:
        return [parsed[n] for n in range(1, count.value)]
    finally:
        kernel32.LocalFree(parsed)


class EnvironmentTest(unittest.TestCase):
    def test_a_game_argument_ending_in_a_backslash_reaches_the_game_whole(self):
        values = ["--cache_root=C:\\Тест 1\\cache\\", "--cache_root=D:\\", "--log_file=C:\\a b\\x.log"]
        quoted = ps("@(" + ", ".join(f"'{v}'" for v in values) + ") | ForEach-Object { ConvertTo-Fm4QuotedArgument $_ }")
        self.assertEqual(command_line_to_argv(" ".join(quoted)), values)

    def test_an_unpacked_zip_is_deleted(self):
        with tempfile.TemporaryDirectory() as temp:
            temp = Path(temp).resolve()
            with zipfile.ZipFile(temp / "tool.zip", "w") as archive:
                archive.writestr("bin/tool.exe", b"MZ")
            ps(f"Expand-Fm4Zip -Archive '{temp / 'tool.zip'}' -Destination '{temp / 'tool'}'; 'done'")
            self.assertTrue((temp / "tool" / "bin" / "tool.exe").is_file())
            self.assertFalse((temp / "tool.zip").exists())

    def test_a_zip_that_fails_to_unpack_is_kept(self):
        with tempfile.TemporaryDirectory() as temp:
            temp = Path(temp).resolve()
            with zipfile.ZipFile(temp / "tool.zip", "w") as archive:
                archive.writestr("../outside.exe", b"MZ")
            with self.assertRaises(AssertionError):
                ps(f"Expand-Fm4Zip -Archive '{temp / 'tool.zip'}' -Destination '{temp / 'tool'}'; 'done'")
            self.assertTrue((temp / "tool.zip").is_file())

    def test_an_unpacked_tar_xz_is_deleted(self):
        xz = shutil.which("xz")
        if xz is None:
            self.skipTest("no xz on PATH")
        with tempfile.TemporaryDirectory() as temp:
            temp = Path(temp).resolve()
            (temp / "src" / "llvm").mkdir(parents=True)
            (temp / "src" / "llvm" / "clang.exe").write_bytes(b"MZ")
            with tarfile.open(temp / "llvm.tar.xz", "w:xz") as archive:
                archive.add(temp / "src" / "llvm", arcname="llvm")
            (temp / "out").mkdir()
            ps(f"Expand-Fm4TarXz -Archive '{temp / 'llvm.tar.xz'}' -Destination '{temp / 'out'}' -XzPath '{xz}'; 'done'")
            self.assertTrue((temp / "out" / "llvm" / "clang.exe").is_file())
            self.assertFalse((temp / "llvm.tar.xz").exists())

    def test_scripts_quote_game_arguments_through_the_helper(self):
        found = [f"{script.name}:{number}" for script in sorted(TOOLS.glob("*.ps1"))
                 for number, line in enumerate(script.read_text(encoding="utf-8-sig").splitlines(), 1)
                 if '"`"--' in line]
        self.assertEqual(found, [])

    def test_build_variables_replace_a_developer_prompts_values(self):
        env = ps("$env:INCLUDE='C:\\stale\\inc'; $env:LIB='C:\\stale\\lib'; $env:EXTERNAL_INCLUDE='C:\\stale\\ext'; "
                 "Get-Fm4BuildVariables -Toolset ([pscustomobject]@{MsvcInclude='M\\inc'; MsvcLib='M\\lib';"
                 " SdkInclude=@('S\\ucrt','S\\um'); SdkLib=@('S\\ucrt\\x64','S\\um\\x64'); Rc='S\\bin\\rc.exe'}) "
                 "-LlvmBin L -NinjaDir N -CMakeBin C -InheritedPath 'C:\\Windows'")
        self.assertEqual(env["INCLUDE"], "M\\inc;S\\ucrt;S\\um")
        self.assertEqual(env["LIB"], "M\\lib;S\\ucrt\\x64;S\\um\\x64")
        self.assertTrue(env["PATH"].startswith("L;N;C;S\\bin;"))
        self.assertTrue(env["PATH"].endswith(";C:\\Windows"))
        self.assertEqual(env["VCToolsInstallDir"], "M\\")
        self.assertEqual(env["EXTERNAL_INCLUDE"], "")
        self.assertNotIn("stale", json.dumps(env))

    def test_toolchain_paths_prefer_this_checkout_then_the_sibling(self):
        with tempfile.TemporaryDirectory() as temp:
            temp = Path(temp).resolve()
            (temp / "fm4" / "tools").mkdir(parents=True)
            script = temp / "fm4" / "tools" / "release-common.ps1"
            shutil.copy(TOOLS / "release-common.ps1", script)
            (temp / "fm4" / ".local" / "toolchain" / "llvm").mkdir(parents=True)
            for item in ("llvm", "cmake"):
                (temp / "pinyon-shift" / ".local" / "toolchain" / item).mkdir(parents=True)
            found = ps("@('llvm', 'cmake', 'msvc-14.44') | ForEach-Object { Resolve-Fm4ToolchainPath -RelativePath $_ }",
                       script, env={"FM4_TOOLCHAIN_ROOT": ""})
            self.assertEqual([Path(p).resolve() for p in found], [temp / "fm4" / ".local" / "toolchain" / "llvm",
                                                        temp / "pinyon-shift" / ".local" / "toolchain" / "cmake",
                                                        temp / "fm4" / ".local" / "toolchain" / "msvc-14.44"])
            pinned = ps("Resolve-Fm4ToolchainPath -RelativePath 'cmake'", script,
                        env={"FM4_TOOLCHAIN_ROOT": str(temp / "data" / "toolchain")})
            self.assertEqual(Path(pinned).resolve(), temp / "data" / "toolchain" / "cmake")

    def test_toolchain_python_results_keep_non_ascii_paths(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve() / "Тест [1]"
            msvc = root / "msvc-14.44" / "14.44.1"
            msvc.mkdir(parents=True)
            (msvc / "toolset.json").write_text(json.dumps(
                {"line": "14.44", "toolset_version": "14.44.1", "redist_version": "14.44.1", "packages": []}),
                encoding="utf-8")
            sdk = root / "windows-sdk-1.0"
            sdk.mkdir()
            (sdk / "windows-sdk.json").write_text(json.dumps({"sdk_version": "10.0.1", "tools": list(windows_sdk.TOOLS)}),
                                                  encoding="utf-8")
            expected = msvc / "VC" / "Tools" / "MSVC" / "14.44.1" / "include"
            same = ps(f"(Invoke-Fm4ToolchainPython -Arguments @('find', '--line', '14.44', '--msvc-root', "
                      f"'{root / 'msvc-14.44'}', '--sdk-version', '1.0', '--sdk-root', '{root}')).Result.msvc.include "
                      f"-eq '{expected}'")
            self.assertTrue(same)

    def test_zips_expand_into_an_existing_bracketed_folder(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve() / "Тест [1]"
            (root / "dir").mkdir(parents=True)
            (root / "dir" / "a.txt").write_text("old", encoding="utf-8")
            archive = Path(temp).resolve() / "tool.zip"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("a.txt", "new")
                zip_file.writestr("bin/tool.exe", "exe")
            same = ps(f"Expand-Fm4Zip -Archive '{archive}' -Destination '{root / 'dir'}'; "
                      f"(Get-Content -LiteralPath '{root / 'dir' / 'a.txt'}' -Raw) -eq 'new' -and "
                      f"(Test-Path -LiteralPath '{root / 'dir' / 'bin' / 'tool.exe'}')")
            self.assertTrue(same)

    def test_a_toolchain_folder_with_brackets_is_refused_with_the_reason(self):
        refused = ps("$(try { Assert-Fm4ToolchainPathSupported; 'accepted' } "
                     "catch { $_.Exception.Data['step'] + ': ' + $_.Exception.Message })",
                     env={"FM4_TOOLCHAIN_ROOT": "C:\\Games\\FM4 [v2]\\data\\toolchain"})
        self.assertTrue(refused.startswith("tools: "), refused)
        self.assertIn("C:\\Games\\FM4 [v2]\\data\\toolchain", refused)
        self.assertIn("CMake", refused)
        for root in ("C:\\Users\\Иван\\Downloads\\FM4 1\\data\\toolchain", "C:\\Games (2026)\\FM4\\data\\toolchain"):
            accepted = ps("$(try { Assert-Fm4ToolchainPathSupported; 'accepted' } catch { $_.Exception.Message })",
                          env={"FM4_TOOLCHAIN_ROOT": root})
            self.assertEqual(accepted, "accepted", root)

    def test_parentheses_with_non_ascii_letters_are_refused_with_the_reason(self):
        refused = ps("$(try { Assert-Fm4ToolchainPathSupported; 'accepted' } "
                     "catch { $_.Exception.Data['step'] + ': ' + $_.Exception.Message })",
                     env={"FM4_TOOLCHAIN_ROOT": "C:\\Игры (1)\\FM4\\data\\toolchain"})
        self.assertTrue(refused.startswith("tools: "), refused)
        self.assertIn("parentheses", refused)

    def test_log_lines_are_read_as_utf8(self):
        with tempfile.TemporaryDirectory() as temp:
            log = Path(temp).resolve() / "fonts.log"
            log.write_bytes("[info] UI fonts: exported 2 to C:\\Тест 1\\ui-fonts\n".encode("utf-8"))
            same = ps(f"@(Read-Fm4LogLines -LogPath '{log}')[0] -eq '[info] UI fonts: exported 2 to C:\\Тест 1\\ui-fonts'")
            self.assertTrue(same)

    def test_the_build_tools_licence_is_read_without_python(self):
        # The launcher shows the licence before setup has provisioned anything.
        files = {}
        with server.serve(files) as base:
            files.update(packages.crt_feed(base))
            licence = ps(f"Get-Fm4BuildToolsLicence -ChannelUrl '{base}/channel'")
        self.assertEqual(licence, packages.LICENCE)

    def test_a_failed_toolchain_run_is_described_with_or_without_its_result(self):
        # A crash or a kill leaves no JSON line; StrictMode must not turn that into a second error.
        messages = ps("Set-StrictMode -Version Latest; "
                      "@((Get-Fm4ToolchainRunMessage -Run ([pscustomobject]@{ ExitCode = 1; Result = $null })), "
                      "(Get-Fm4ToolchainRunMessage -Run ([pscustomobject]@{ ExitCode = 2; "
                      "Result = [pscustomobject]@{ error = 'sdk-downloads'; message = 'timed out' } })), "
                      "(Get-Fm4ToolchainRunMessage -Run ([pscustomobject]@{ ExitCode = 3; "
                      "Result = [pscustomobject]@{ error = 'licence' } })))")
        self.assertEqual(messages, ["exit code 1", "timed out", "exit code 3"])

    def test_cmake_arguments_name_lld_the_sdk_tools_and_the_toolset_version(self):
        args = ps("Get-Fm4CMakeToolArguments -Environment ([pscustomobject]@{Ninja='N\\ninja.exe';"
                  " Toolset=[pscustomobject]@{Rc='S\\rc.exe'; Mt='S\\mt.exe'; ToolsetVersion='14.44.35207'}})")
        self.assertEqual(args, ["-DCMAKE_MAKE_PROGRAM=N/ninja.exe", "-DCMAKE_LINKER_TYPE=LLD",
                                "-DCMAKE_RC_COMPILER=S/rc.exe", "-DCMAKE_MT=S/mt.exe",
                                "-DCMAKE_C_COMPILER_TARGET=x86_64-pc-windows-msvc19.44.35207",
                                "-DCMAKE_CXX_COMPILER_TARGET=x86_64-pc-windows-msvc19.44.35207"])
