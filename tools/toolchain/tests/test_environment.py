import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

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


class EnvironmentTest(unittest.TestCase):
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
