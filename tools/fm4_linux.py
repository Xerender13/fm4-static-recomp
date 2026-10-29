#!/usr/bin/env python3
"""FM4 Static Recomp on Linux and the Steam Deck: a native build, not Proton.

  python3 tools/fm4_linux.py setup --iso DISC1.iso [--disc2 DISC2.iso] [--dlc DIR]
  python3 tools/fm4_linux.py setup --game DIR       an extracted Disc 1 instead
  python3 tools/fm4_linux.py setup --build-only     rebuild after a source or SDK change
  python3 tools/fm4_linux.py setup --container fm4-build ...  build in a distrobox instead
  python3 tools/fm4_linux.py launch [-- game options]
  python3 tools/fm4_linux.py shortcuts add|remove   Steam (Game Mode) and the desktop menu

The Linux counterpart of tools/setup.ps1, prepare-sdk.ps1, codegen.ps1, build.ps1 and
run.ps1. The toolchain is Pinyon Shift's for its Linux build (config/linux-toolchain.json):
the pinned LLVM targets the Steam Runtime 3 "sniper" SDK sysroot, so SteamOS needs no
system compiler, no root and no developer mode. Everything stays under .local/ in this
folder: the toolchain, the SDK, the extracted discs, the build's state and the save.
Adapted from Pinyon Shift's tools/pinyon_setup.py and pinyon_steam.py (BSD-3, see
THIRD_PARTY_NOTICES.md).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shlex
import shutil
import struct
import subprocess
import sys
import tarfile
import time
import urllib.request
import zipfile
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOCAL = ROOT / ".local"
LOGS = LOCAL / "logs"
DOWNLOADS = LOCAL / "downloads"
TOOLCHAIN = LOCAL / "toolchain" / "linux-x86_64"
DISC1 = LOCAL / "game" / "disc1"
DISC2 = LOCAL / "game" / "disc2"
STATE = LOCAL / "state"
PRESET = "linux-amd64-release"
BUILD = ROOT / "out" / "build" / PRESET
EXECUTABLE = "fm4_static_recomp"
# Distinct from a shortcut to the Windows launcher, which players often name after the game.
NAME = "Forza Motorsport 4 (Linux)"


class SetupError(RuntimeError):
    pass


def step(message: str) -> None:
    print(f"==> {message}", flush=True)


def read_json(relative: str) -> dict:
    return json.loads((ROOT / relative).read_text(encoding="utf-8"))


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(8 << 20), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def available_memory_gb() -> float:
    try:
        for line in Path("/proc/meminfo").read_text().splitlines():
            if line.startswith("MemAvailable:"):
                return int(line.split()[1]) / (1 << 20)
    except OSError:
        pass
    return 8.0


def job_count() -> int:
    """The translated game's files are large; each clang takes about 2 GB at its peak. The
    Deck's 16 GB are shared with the GPU and the desktop, so the jobs follow the memory
    available now, not the total. FM4_BUILD_JOBS overrides it."""
    chosen = os.environ.get("FM4_BUILD_JOBS")
    if chosen:
        return max(1, int(chosen))
    return max(1, min(os.cpu_count() or 1, int(available_memory_gb() / 2.2)))


# --- Toolchain ------------------------------------------------------------------

def download(item: dict, label: str) -> Path:
    DOWNLOADS.mkdir(parents=True, exist_ok=True)
    name = item["url"].rsplit("/", 1)[1].replace("%2B", "+").replace("%2C", ",")
    target = DOWNLOADS / name
    if target.is_file() and sha256_file(target) == item["sha256"].upper():
        return target
    partial = target.with_name(target.name + ".partial")
    request = urllib.request.Request(item["url"], headers={"User-Agent": "fm4-static-recomp-setup"})
    for attempt in range(3):
        try:
            with urllib.request.urlopen(request, timeout=60) as response, partial.open("wb") as out:
                total = int(response.headers.get("Content-Length") or 0)
                done, last = 0, 0.0
                while block := response.read(1 << 20):
                    out.write(block)
                    done += len(block)
                    if total and time.monotonic() - last > 5:
                        last = time.monotonic()
                        print(f"    {label}: {done >> 20} of {total >> 20} MB", flush=True)
            break
        except OSError as error:
            if attempt == 2:
                raise SetupError(f"could not download {label}: {error}")
            time.sleep(3 * (attempt + 1))
    actual = sha256_file(partial)
    if actual != item["sha256"].upper():
        partial.unlink()
        raise SetupError(f"{label} failed its SHA-256 check (got {actual})")
    partial.replace(target)
    return target


def unpack(archive: Path, install: Path, strip_top: bool = True) -> None:
    """Unpacks into install, dropping the archive's single top folder."""
    staging = install.with_name(install.name + ".staging")
    if staging.exists():
        shutil.rmtree(staging)
    staging.mkdir(parents=True)
    if archive.name.endswith(".zip"):
        with zipfile.ZipFile(archive) as zipped:
            for info in zipped.infolist():
                path = zipped.extract(info, staging)
                mode = (info.external_attr >> 16) & 0o777
                if mode:
                    os.chmod(path, mode)
    else:
        # The archives are pinned by SHA-256; the sniper sysroot holds absolute symlinks,
        # which make_links_relative() rewrites afterwards.
        with tarfile.open(archive) as tarred:
            if hasattr(tarfile, "fully_trusted_filter"):
                tarred.extractall(staging, filter="fully_trusted")
            else:
                tarred.extractall(staging)
    entries = list(staging.iterdir())
    source = entries[0] if strip_top and len(entries) == 1 and entries[0].is_dir() else staging
    if install.exists():
        shutil.rmtree(install)
    install.parent.mkdir(parents=True, exist_ok=True)
    source.rename(install)
    if staging.exists():
        shutil.rmtree(staging)


def make_links_relative(sysroot: Path) -> None:
    """Absolute symlinks in the sysroot point at the build host's /; point them inside it."""
    for directory, folders, files in os.walk(sysroot):
        for name in folders + files:
            path = Path(directory) / name
            if not path.is_symlink():
                continue
            target = os.readlink(path)
            if target.startswith("/"):
                path.unlink()
                os.symlink(os.path.relpath(str(sysroot) + target, directory), path)


# libxml2 2.14 renamed its library libxml2.so.16, so SteamOS no longer has the libxml2.so.2
# that the LLVM release's ld.lld and the sysroot's wayland-scanner link.
HOST_LIBRARY = re.compile(r"^lib(xml2\.so\.2|icuuc\.so\.\d+|icudata\.so\.\d+)$")


def prepare_toolchain_folder(tools: dict) -> None:
    """The layout cmake/toolchains/linux-x86_64-sniper.cmake reads: llvm/, sniper-sdk/,
    sniper-libgcc/ (GCC 10's libgcc_s.so and libgcc_eh.a) and bin/ wrappers."""
    TOOLCHAIN.mkdir(parents=True, exist_ok=True)
    for link, key in (("llvm", "llvm"), ("sniper-sdk", "sysroot")):
        path = TOOLCHAIN / link
        if path.is_symlink():
            path.unlink()
        elif path.exists():
            shutil.rmtree(path)
        os.symlink(ROOT / tools[key]["install_path"], path)
    sysroot = ROOT / tools["sysroot"]["install_path"]
    overlay = TOOLCHAIN / "sniper-libgcc"
    overlay.mkdir(exist_ok=True)
    for name in ("libgcc_s.so", "libgcc_eh.a"):
        shutil.copyfile(sysroot / "usr/lib/gcc/x86_64-linux-gnu/10" / name, overlay / name)
    libs = TOOLCHAIN / "host-libs"
    if libs.exists():
        shutil.rmtree(libs)
    libs.mkdir()
    for entry in sorted((sysroot / "usr/lib/x86_64-linux-gnu").iterdir()):
        if HOST_LIBRARY.match(entry.name):
            os.symlink(entry, libs / entry.name)
    wrappers = TOOLCHAIN / "bin"
    wrappers.mkdir(exist_ok=True)
    for name, target in (("ld.lld", ROOT / tools["llvm"]["install_path"] / "bin/ld.lld"),
                         ("wayland-scanner", sysroot / "usr/bin/wayland-scanner")):
        wrapper = wrappers / name
        wrapper.write_text(
            "#!/bin/sh\n"
            "# Written by tools/fm4_linux.py: runs the pinned program with the\n"
            "# libxml2.so.2 the host may lack.\n"
            'libs="$(dirname -- "$0")/../host-libs"\n'
            'LD_LIBRARY_PATH="$libs${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" '
            f'exec {shlex.quote(str(target))} "$@"\n', encoding="utf-8")
        wrapper.chmod(0o755)


def provision() -> dict:
    """Downloads and unpacks every pinned tool; returns name -> executable or folder."""
    tools = read_json("config/linux-toolchain.json")["tools"]
    found = {}
    for key in ("cmake", "ninja", "llvm", "sysroot"):
        item = tools[key]
        install = ROOT / item["install_path"]
        stamp = install / ".fm4-sha256"
        if not (stamp.is_file() and stamp.read_text().strip() == item["sha256"]):
            label = f"{item.get('label', key)} {item['version']}"
            step(f"Getting {label}")
            archive = download(item, label)
            unpack(archive, install, strip_top=item.get("strip_top", True))
            if key == "sysroot":
                make_links_relative(install)
            stamp.write_text(item["sha256"] + "\n")
            archive.unlink()  # LLVM alone is 2 GB; a new pin downloads again
        found[key] = install / item["executable"] if item["executable"] else install
    prepare_toolchain_folder(tools)
    return found


def build_environment(tools: dict) -> dict:
    environment = dict(os.environ)
    paths = [str(Path(tools[key]).parent) for key in ("cmake", "ninja") if "/" in str(tools[key])]
    environment["PATH"] = os.pathsep.join(paths + [environment.get("PATH", "")])
    environment["FM4_LINUX_TOOLCHAIN"] = "system" if container() else str(TOOLCHAIN)
    return environment


# --- Build container ------------------------------------------------------------------
#
# Instead of the pinned tools, the build can run in a distrobox (Ubuntu 24.04 with clang 20,
# lld, CMake 3.25+, Ninja and SDL3's X11, Wayland and audio headers). Only the build runs
# there; the game runs on SteamOS itself, whose glibc is newer than the container's.

CONTAINER_FILE = LOCAL / "build-container"
CONTAINER_PACKAGES = (
    "clang-20 lld-20 cmake ninja-build git python3 pkg-config libx11-dev libxext-dev "
    "libxrandr-dev libxcursor-dev libxi-dev libxfixes-dev libxss-dev libxtst-dev "
    "libxkbcommon-dev libwayland-dev wayland-protocols libdecor-0-dev libegl-dev "
    "libasound2-dev libpulse-dev libpipewire-0.3-dev libdbus-1-dev libudev-dev")


def container() -> str | None:
    return CONTAINER_FILE.read_text().strip() if CONTAINER_FILE.is_file() else None


def in_container(command: list, environment: dict, cwd: Path) -> list:
    """The command run inside the build container, in cwd, with the build's variables."""
    name = container()
    if not name:
        return command
    return (["distrobox", "enter", name, "--", "env",
             f"FM4_LINUX_TOOLCHAIN={environment['FM4_LINUX_TOOLCHAIN']}",
             "sh", "-c", 'cd "$1" && shift && exec "$@"', "sh", str(cwd)] + command)


# --- ShiftGlue SDK -----------------------------------------------------------------

def git(*arguments: str, cwd: Path = ROOT) -> None:
    command = ["git", "-c", "http.version=HTTP/1.1"] + list(arguments)
    for attempt in range(3):
        if subprocess.run(command, cwd=cwd).returncode == 0:
            return
        time.sleep(3 * (attempt + 1))
    raise SetupError(f"git {' '.join(arguments)} failed")


def git_output(*arguments: str, cwd: Path = ROOT) -> str:
    completed = subprocess.run(["git"] + list(arguments), cwd=cwd, capture_output=True, text=True)
    return completed.stdout.strip() if completed.returncode == 0 else ""


# Apple's Vulkan layer, Android's driver loader, and Tracy and Catch2, which the build turns off.
LINUX_UNUSED_SUBMODULES = {"thirdparty/moltenvk", "thirdparty/libadrenotools",
                           "thirdparty/tracy", "thirdparty/catch2"}


def patch_sdk(sdk: Path) -> None:
    """Keep local Linux runtime fixes across SDK setup and rebuilds."""
    for patch in sorted((ROOT / "tools/patches").glob("*.patch")):
        command = ["git", "apply"]
        if subprocess.run(command + ["--reverse", "--check", str(patch)], cwd=sdk,
                          capture_output=True).returncode == 0:
            continue
        checked = subprocess.run(command + ["--check", str(patch)], cwd=sdk,
                                 capture_output=True, text=True)
        if checked.returncode:
            raise SetupError(f"Cannot apply SDK fix {patch.name}: {checked.stderr}")
        subprocess.run(command + [str(patch)], cwd=sdk, check=True)


def prepare_sdk() -> Path:
    """The pinned SDK in .local/shiftglue-sdk (prepare-sdk.ps1), or FM4_SDK_ROOT."""
    override = os.environ.get("FM4_SDK_ROOT")
    if override:
        sdk = Path(override).resolve()
        patch_sdk(sdk)
        return sdk
    sdk_config = read_json("config/release-toolchain.json")["sdk"]
    sdk = ROOT / sdk_config["fallback_path"]
    revision = sdk_config["revision"]
    head = git_output("rev-parse", "HEAD", cwd=sdk) if (sdk / ".git").exists() else ""
    if head == revision and (sdk / ".git" / "fm4-prepared").is_file():
        patch_sdk(sdk)
        return sdk
    step("Downloading the pinned ShiftGlue SDK source")
    if not head:
        if sdk.exists():
            shutil.rmtree(sdk)
        sdk.mkdir(parents=True)
        git("init", "--quiet", cwd=sdk)
        git("remote", "add", "origin",
            os.environ.get("FM4_SDK_REPOSITORY", sdk_config["repository"]), cwd=sdk)
    if head != revision:
        git("fetch", "--depth", "1", "--no-tags", "origin", revision, cwd=sdk)
        git("checkout", "--quiet", "--detach", "FETCH_HEAD", cwd=sdk)
    git("submodule", "sync", "--recursive", cwd=sdk)
    # Shallow, and without what a Linux build never reads: whole histories (FFmpeg's above
    # all) and MoltenVK cost a slow connection hours.
    paths = [line.split()[-1] for line in git_output(
        "config", "--file", ".gitmodules", "--get-regexp", r"\.path$", cwd=sdk).splitlines()]
    paths = [path for path in paths if path not in LINUX_UNUSED_SUBMODULES]
    git("submodule", "update", "--init", "--recursive", "--force", "--depth", "1",
        "--jobs", "8", "--", *paths, cwd=sdk)
    if git_output("rev-parse", "HEAD", cwd=sdk) != revision:
        raise SetupError("The prepared SDK is not at the pinned revision.")
    (sdk / ".git" / "fm4-prepared").write_text(revision)
    patch_sdk(sdk)
    return sdk


# --- Disc images (XDVDFS) -------------------------------------------------------------

SECTOR = 2048
XDVDFS_MAGIC = b"MICROSOFT*XBOX*MEDIA"
# Where the game partition starts: rebuilt images, XGD3, XGD2, XGD1.
PARTITION_OFFSETS = (0, 0x2080000, 0xFD90000, 0x18300000)


class DiscImage:
    """Reads the Xbox file system on a disc image, read-only."""

    def __init__(self, path: Path):
        self.stream = path.open("rb")
        self.offset = None
        for offset in PARTITION_OFFSETS:
            self.stream.seek(offset + 32 * SECTOR)
            if self.stream.read(len(XDVDFS_MAGIC)) == XDVDFS_MAGIC:
                self.offset = offset
                break
        if self.offset is None:
            self.stream.close()
            raise SetupError(f"{path} is not an Xbox 360 disc image")
        self.stream.seek(self.offset + 32 * SECTOR + 20)
        self.root_sector, self.root_size = struct.unpack("<II", self.stream.read(8))

    def close(self) -> None:
        self.stream.close()

    def files(self) -> list[tuple[str, int, int]]:
        """(path, first sector, size) of every file, directories walked."""
        found = []
        pending = [("", self.root_sector, self.root_size)]
        while pending:
            prefix, sector, size = pending.pop()
            if size == 0:
                continue
            self.stream.seek(self.offset + sector * SECTOR)
            table = self.stream.read(size)
            nodes, seen = [0], set()
            while nodes:
                position = nodes.pop()
                if position in seen or position + 14 > len(table):
                    continue
                seen.add(position)
                left, right, start, length, attributes, name_length = struct.unpack_from(
                    "<HHIIBB", table, position)
                if left == 0xFFFF and right == 0xFFFF:
                    continue  # sector padding
                name = table[position + 14: position + 14 + name_length].decode("latin-1")
                if left:
                    nodes.append(left * 4)
                if right:
                    nodes.append(right * 4)
                path = f"{prefix}{name}"
                if attributes & 0x10:
                    pending.append((path + "/", start, length))
                else:
                    found.append((path, start, length))
        return found

    def copy(self, sector: int, size: int, destination: Path) -> str:
        """Copies one file out; returns its SHA-256."""
        digest = hashlib.sha256()
        self.stream.seek(self.offset + sector * SECTOR)
        remaining = size
        with destination.open("wb") as out:
            while remaining:
                block = self.stream.read(min(remaining, 8 << 20))
                if not block:
                    raise SetupError("the disc image ends early")
                out.write(block)
                digest.update(block)
                remaining -= len(block)
        return digest.hexdigest().upper()


def match_disc1_release(found: dict[str, tuple[int, str]]) -> dict | None:
    """The supported release whose three executables these are: lower-case path -> (size, sha)."""
    for release in read_json("config/supported-discs.json")["disc1"]["releases"]:
        if all(found.get(entry["path"].lower()) == (entry["size"], entry["sha256"].upper())
               for entry in release["files"]):
            return release
    return None


def extract_disc1(iso: Path) -> None:
    disc = DiscImage(iso)
    try:
        files = [entry for entry in disc.files()
                 if entry[0].split("/", 1)[0].lower() != "$systemupdate"]
        if not any(path.lower() == "default.xex" for path, _, _ in files):
            raise SetupError(f"{iso} has no default.xex: is it Disc 1?")
        staging = DISC1.with_name(DISC1.name + ".extracting")
        if staging.exists():
            shutil.rmtree(staging)
        staging.mkdir(parents=True)
        # The executables first: an unsupported disc fails in seconds.
        root_executables = [entry for entry in files
                            if "/" not in entry[0] and entry[0].lower().endswith(".xex")]
        executables = {path.lower(): (size, disc.copy(sector, size, staging / path))
                       for path, sector, size in root_executables}
        release = match_disc1_release(executables)
        if release is None:
            shutil.rmtree(staging)
            raise SetupError("This Disc 1 is not a supported release (1.0 PAL or 1.0 USA; "
                             "config/supported-discs.json). The 1.1 re-release is not supported.")
        print(f"    Disc 1: {release['name']}, media ID {release['media_id']}")
        files = sorted((entry for entry in files if entry not in root_executables),
                       key=lambda entry: entry[1])
        total = sum(size for _, _, size in files)
        done, last = 0, 0.0
        for path, sector, size in files:
            target = staging / path
            target.parent.mkdir(parents=True, exist_ok=True)
            disc.copy(sector, size, target)
            done += size
            if time.monotonic() - last > 5:
                last = time.monotonic()
                print(f"    copying the game files: {done / 1e9:.1f} of {total / 1e9:.1f} GB",
                      flush=True)
    finally:
        disc.close()
    if DISC1.exists():
        shutil.rmtree(DISC1)
    staging.rename(DISC1)


def verify_disc1_folder(folder: Path) -> None:
    found = {}
    for name in ("default.xex", "SpeechFacade_default.xex", "XMediaFacade_default.xex"):
        path = next((p for p in folder.iterdir() if p.name.lower() == name.lower()), None)
        if path is None:
            raise SetupError(f"{name} is missing in {folder}")
        found[name.lower()] = (path.stat().st_size, sha256_file(path))
    release = match_disc1_release(found)
    if release is None:
        raise SetupError(f"{folder} is not a supported Disc 1 release (1.0 PAL or 1.0 USA)")
    print(f"    Disc 1: {release['name']}, media ID {release['media_id']}")


def extract_disc2(source: Path) -> None:
    """The four content packages from a Disc 2 image or folder, checked by name, size and
    hash, into .local/game/disc2 (--fm4_disc2_packages)."""
    expected = {entry["name"].lower(): entry
                for entry in read_json("config/supported-discs.json")["disc2"]["packages"]}
    DISC2.mkdir(parents=True, exist_ok=True)
    copied = 0
    if source.is_dir():
        for path in source.rglob("*"):
            entry = expected.get(path.name.lower())
            if entry and path.is_file() and path.stat().st_size == entry["size"]:
                target = DISC2 / entry["name"]
                print(f"    {entry['name']}")
                shutil.copyfile(path, target)
                if sha256_file(target) != entry["sha256"].upper():
                    target.unlink()
                    raise SetupError(f"{path} differs from the supported Disc 2 package")
                copied += 1
    else:
        disc = DiscImage(source)
        try:
            for path, sector, size in disc.files():
                entry = expected.get(path.rsplit("/", 1)[-1].lower())
                if not entry or size != entry["size"]:
                    continue
                target = DISC2 / entry["name"]
                if target.is_file() and target.stat().st_size == size:
                    copied += 1
                    continue
                print(f"    {entry['name']} ({size / 1e9:.1f} GB)", flush=True)
                if disc.copy(sector, size, target) != entry["sha256"].upper():
                    target.unlink()
                    raise SetupError(f"{path} on {source} differs from the supported package")
                copied += 1
        finally:
            disc.close()
    if copied != len(expected):
        raise SetupError(f"found {copied} of the {len(expected)} Disc 2 packages in {source}")


# --- Build -----------------------------------------------------------------------------

def run_logged(command: list, log: Path, environment: dict, cwd: Path = ROOT) -> None:
    """Runs a build step into its log, echoing Ninja's progress now and then."""
    LOGS.mkdir(parents=True, exist_ok=True)
    last = 0.0
    with log.open("w", encoding="utf-8", errors="replace") as stream:
        command = in_container([str(part) for part in command], environment, cwd)
        process = subprocess.Popen(command, cwd=cwd, env=environment,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                                   errors="replace")
        assert process.stdout is not None
        for line in process.stdout:
            stream.write(line)
            if line.startswith("[") and "/" in line.split("]", 1)[0]:
                if time.monotonic() - last > 10:
                    last = time.monotonic()
                    print(f"    {line.split(']', 1)[0]}]", flush=True)
            elif line.startswith("FAILED:") or ": error:" in line:
                print("    " + line.rstrip()[:300], flush=True)
        if process.wait():
            raise SetupError(f"failed; see {log}")


def discard_unfinished_configure(directory: Path) -> None:
    """A configure that failed before writing build.ninja may have cached flags from an
    older toolchain; the retry starts it over."""
    if (directory / "CMakeCache.txt").is_file() and not (directory / "build.ninja").is_file():
        (directory / "CMakeCache.txt").unlink()
        shutil.rmtree(directory / "CMakeFiles", ignore_errors=True)


def build(tools: dict, sdk: Path, game: Path) -> Path:
    environment = build_environment(tools)
    cmake = str(tools["cmake"])
    jobs = job_count()
    toolchain = [f"-DCMAKE_TOOLCHAIN_FILE={ROOT / 'cmake/toolchains/linux-x86_64-sniper.cmake'}",
                 f"-DFM4_LINUX_TOOLCHAIN={environment['FM4_LINUX_TOOLCHAIN']}"]
    if not container():
        # ShiftGlue's preset names plain clang; the toolchain file's pinned paths must win or
        # every configure discards the cache.
        llvm = TOOLCHAIN / "llvm" / "bin"
        toolchain += [f"-DCMAKE_C_COMPILER={llvm / 'clang'}",
                      f"-DCMAKE_CXX_COMPILER={llvm / 'clang++'}"]

    step(f"Building the ShiftGlue code generator ({jobs} jobs)")
    discard_unfinished_configure(sdk / "out" / "build" / "linux-amd64")
    run_logged([cmake, "--preset", "linux-amd64", "-DREXGLUE_ENABLE_TRACY=OFF",
                "-DSDL_HIDAPI_LIBUSB=OFF"] + toolchain, LOGS / "rexglue-configure.log",
               environment, cwd=sdk)
    run_logged([cmake, "--build", "--preset", "linux-amd64-release", "--target", "rexglue",
                "--parallel", str(jobs)], LOGS / "rexglue-build.log", environment, cwd=sdk)
    generator = sdk / "out" / "linux-amd64" / "Release" / "rexglue"
    if not generator.is_file():
        raise SetupError(f"The ShiftGlue code generator was not produced: {generator}")

    # codegen.ps1: the tracked manifest names the disc as ../../../extracted/fm4.
    tracked = ROOT / "config/rexglue/fm4_manifest.toml"
    manifest = ROOT / "config/rexglue/fm4_manifest.local.toml"
    manifest.write_text(tracked.read_text(encoding="utf-8").replace(
        "../../../extracted/fm4", game.as_posix()), encoding="utf-8")
    step("Translating the game code. This takes several minutes.")
    run_logged([generator, "--log-level", "info", "--log-file", LOGS / "codegen.log",
                "codegen", manifest], LOGS / "codegen-console.log", environment)

    step(f"Compiling FM4 Static Recomp ({jobs} jobs). The first build takes a long while.")
    discard_unfinished_configure(BUILD)
    run_logged([cmake, "--preset", PRESET, f"-DREXSDK_DIR={sdk}",
                f"-DPYTHON_EXECUTABLE={sys.executable}",
                f"-DFM4_STATIC_RECOMP_REXGLUE_CODEGEN={generator}"],
               LOGS / "configure.log", environment)
    command = [cmake, "--build", "--preset", PRESET, "--parallel", str(jobs)]
    try:
        run_logged(command, LOGS / "build.log", environment)
    except SetupError:
        # The dependency-tracked translation can rewrite sources.cmake mid-build; one more
        # build reconfigures from the new lists. Any other failure stands.
        ninja = BUILD / "build.ninja"
        configured = ninja.stat().st_mtime if ninja.is_file() else 0.0
        lists = [LOCAL / "generated" / tree / "sources.cmake"
                 for tree in ("default", "speech", "xmedia")]
        if not any(p.is_file() and p.stat().st_mtime > configured for p in lists):
            raise
        run_logged(command, LOGS / "build-retry.log", environment)
    executable = BUILD / EXECUTABLE
    if not executable.is_file():
        raise SetupError(f"The build finished without {executable}")
    return executable


def write_shader_cache(game: Path) -> None:
    """setup.ps1's last step: the shipped pipeline list compiled into the player's cache, so
    races do not stall while shaders compile."""
    step("Building the shader cache from the disc")
    log = STATE / "logs" / "shaders.log"
    for folder in ("user", "cache", "logs"):
        (STATE / folder).mkdir(parents=True, exist_ok=True)
    if log.exists():
        log.unlink()
    # A tool mode (tool_mode.h): the game adds the list's pipelines to the cache, then quits.
    subprocess.run([str(BUILD / EXECUTABLE), f"--game_data_root={game}", f"--log_file={log}",
                    f"--fm4_build_shader_cache={STATE / 'cache/shaders/shareable'}",
                    f"--fm4_shader_list_dir={ROOT / 'config/shaders'}",
                    f"--user_data_root={STATE / 'user'}", f"--cache_root={STATE / 'cache'}"],
                   cwd=BUILD, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    lines = log.read_text(encoding="utf-8", errors="replace").splitlines() if log.is_file() else []
    result = [line.split("] ", 1)[-1] for line in lines if "] Shader cache: " in line]
    print("    " + (result[-1] if result else
                    f"The shader cache was not written ({log}); the game compiles shaders "
                    "while you play."))


def game_root(args: argparse.Namespace) -> Path:
    return (args.game or DISC1).resolve()


def setup(args: argparse.Namespace) -> int:
    if not shutil.which("git"):
        raise SetupError("git is needed to fetch the ShiftGlue SDK")
    if args.iso:
        step(f"Extracting Disc 1 from {args.iso}")
        extract_disc1(args.iso.resolve())
    game = game_root(args)
    if not args.build_only:
        if not (game / "default.xex").is_file():
            raise SetupError(f"No extracted Disc 1 at {game}: pass --iso or --game")
        verify_disc1_folder(game)
    if args.disc2:
        step(f"Copying the Disc 2 packages from {args.disc2}")
        extract_disc2(args.disc2.resolve())
    if args.dlc:
        (LOCAL / "dlc-folder").write_text(str(args.dlc.resolve()))
    if args.container:
        CONTAINER_FILE.parent.mkdir(parents=True, exist_ok=True)
        CONTAINER_FILE.write_text(args.container + "\n")
    elif args.pinned_toolchain:
        CONTAINER_FILE.unlink(missing_ok=True)
    if container():
        step(f"Building in the {container()} distrobox")
        if subprocess.run(["distrobox", "enter", container(), "--", "sh", "-c",
                           "command -v cmake && command -v ninja && echo 'int main(){}' | "
                           "clang++ -x c++ -fuse-ld=lld - -o /dev/null"],
                          capture_output=True).returncode:
            raise SetupError(
                f"The {container()} distrobox needs clang, lld, cmake and ninja. Create it with:\n"
                f"  distrobox create --name {container()} --image ubuntu:24.04\n"
                f"  distrobox enter {container()} -- sudo apt-get install -y {CONTAINER_PACKAGES}\n"
                "  (and link clang and clang++ to their -20 names)")
        tools = {"cmake": "cmake", "ninja": "ninja"}
    else:
        tools = provision()
    sdk = prepare_sdk()
    executable = build(tools, sdk, game)
    print(f"    built {executable}")
    write_shader_cache(game)
    step("Done. Start it with: python3 tools/fm4_linux.py launch "
         "(or add it to Steam: python3 tools/fm4_linux.py shortcuts add)")
    return 0


# --- Launch ------------------------------------------------------------------------

def launch(args: argparse.Namespace) -> int:
    """run.ps1: the game with its state under .local/state; Disc 2 and DLC installed at start."""
    game = game_root(args)
    state = (args.state_root or STATE).resolve()
    executable = BUILD / EXECUTABLE
    if not executable.is_file():
        raise SetupError(f"The game is not built at {BUILD}: run setup first")
    if not (game / "default.xex").is_file():
        raise SetupError(f"Game files are missing at {game}")
    for folder in ("user", "update", "cache", "logs"):
        (state / folder).mkdir(parents=True, exist_ok=True)
    log = state / "logs" / "runtime.log"
    if log.exists():
        log.unlink()
    arguments = [f"--game_data_root={game}", f"--user_data_root={state / 'user'}",
                 f"--update_data_root={state / 'update'}", f"--cache_root={state / 'cache'}",
                 f"--log_file={log}", "--gpu_backend=vulkan"]
    # Installed packages are skipped, so passing them every time costs nothing.
    if any(DISC2.glob("4d530910*")):
        arguments.append(f"--fm4_disc2_packages={DISC2}")
    dlc = LOCAL / "dlc-folder"
    if dlc.is_file():
        arguments.append(f"--fm4_dlc_packages={dlc.read_text().strip()}")
    arguments += [a for a in args.game_arguments if a != "--"]
    process = subprocess.Popen([str(executable)] + arguments, cwd=BUILD)
    try:
        return process.wait()
    except KeyboardInterrupt:
        process.terminate()
        return process.wait()


# --- Steam and desktop shortcuts --------------------------------------------------------

MAP, STRING, INT32, END = 0x00, 0x01, 0x02, 0x08
SCRIPT = LOCAL / "fm4-static-recomp.sh"


def _cstring(data: bytes, position: int) -> tuple[str, int]:
    end = data.index(b"\0", position)
    return data[position:end].decode("utf-8", errors="surrogateescape"), end + 1


def parse_vdf(data: bytes, position: int = 0) -> tuple[dict, int]:
    value: dict = {}
    while position < len(data):
        kind = data[position]
        position += 1
        if kind == END:
            return value, position
        key, position = _cstring(data, position)
        if kind == MAP:
            value[key], position = parse_vdf(data, position)
        elif kind == STRING:
            value[key], position = _cstring(data, position)
        elif kind == INT32:
            value[key] = struct.unpack_from("<i", data, position)[0]
            position += 4
        else:
            raise ValueError(f"unknown VDF type {kind} at {position - 1}")
    return value, position


def dump_vdf(value: dict) -> bytes:
    out = bytearray()
    for key, item in value.items():
        name = key.encode("utf-8", errors="surrogateescape") + b"\0"
        if isinstance(item, dict):
            out += bytes([MAP]) + name + dump_vdf(item)
        elif isinstance(item, int):
            out += bytes([INT32]) + name + struct.pack("<i", item)
        else:
            out += bytes([STRING]) + name + str(item).encode("utf-8", errors="surrogateescape") + b"\0"
    return bytes(out) + bytes([END])


def steam_users() -> list[Path]:
    users, seen = [], set()
    home = Path.home()
    for root in (home / ".local/share/Steam", home / ".steam/steam"):
        if not (root / "userdata").is_dir() or root.resolve() in seen:
            continue
        seen.add(root.resolve())
        users += [p for p in (root / "userdata").iterdir()
                  if p.is_dir() and p.name.isdigit() and p.name != "0"]
    return users


def steam_running() -> bool:
    return subprocess.run(["pgrep", "-x", "steam"], capture_output=True).returncode == 0


def stop_steam() -> bool:
    """Steam keeps shortcuts.vdf in memory and writes it back on exit: close it first."""
    if not steam_running():
        return False
    print("Closing Steam so it does not overwrite the new shortcut...")
    subprocess.run(["steam", "-shutdown"], capture_output=True)
    deadline = time.monotonic() + 60
    while steam_running():
        if time.monotonic() > deadline:
            raise SetupError("Steam did not close; quit it and run this again")
        time.sleep(1)
    return True


def write_shortcuts(add: bool) -> list[str]:
    exe = f'"{SCRIPT}"'
    app_id = zlib.crc32((exe + NAME).encode("utf-8")) | 0x80000000
    restart = stop_steam()
    changed = []
    try:
        for user in steam_users():
            path = user / "config" / "shortcuts.vdf"
            path.parent.mkdir(parents=True, exist_ok=True)
            shortcuts: dict = {}
            if path.is_file() and path.stat().st_size:
                shortcuts = parse_vdf(path.read_bytes())[0].get("shortcuts", {})
                backup = path.with_suffix(".vdf.fm4-backup")
                if not backup.exists():
                    shutil.copyfile(path, backup)
            # Only this script's own entry is replaced or removed, never another of the game's.
            entries = [e for e in shortcuts.values() if e.get("Exe", e.get("exe")) != exe]
            if add:
                entries.append({
                    "appid": struct.unpack("<i", struct.pack("<I", app_id))[0],
                    "AppName": NAME, "Exe": exe, "StartDir": f'"{ROOT}"',
                    "icon": "", "ShortcutPath": "", "LaunchOptions": "", "IsHidden": 0,
                    "AllowDesktopConfig": 1, "AllowOverlay": 1, "OpenVR": 0, "Devkit": 0,
                    "DevkitGameID": "", "DevkitOverrideAppID": 0, "LastPlayTime": 0,
                    "FlatpakAppID": "", "tags": {}})
            elif len(entries) == len(shortcuts):
                continue
            temporary = path.with_suffix(".vdf.tmp")
            temporary.write_bytes(dump_vdf({"shortcuts": {str(i): e for i, e in enumerate(entries)}}))
            temporary.replace(path)
            changed.append(user.name)
    finally:
        if restart and shutil.which("steam"):
            subprocess.Popen(["steam"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                             start_new_session=True)
    return changed


def shortcuts(args: argparse.Namespace) -> int:
    desktop = Path(os.environ.get("XDG_DATA_HOME") or Path.home() / ".local/share") / \
        "applications" / "fm4-static-recomp.desktop"
    if args.action == "remove":
        print(f"Removed from {len(write_shortcuts(False))} Steam user(s).")
        desktop.unlink(missing_ok=True)
        return 0
    if not (BUILD / EXECUTABLE).is_file():
        raise SetupError("Build the game first: python3 tools/fm4_linux.py setup")
    SCRIPT.parent.mkdir(parents=True, exist_ok=True)
    state = f' --state-root "{args.state_root.resolve()}"' if args.state_root else ""
    SCRIPT.write_text("#!/bin/sh\n"
                      "# Written by tools/fm4_linux.py shortcuts add.\n"
                      f'cd "{ROOT}" || exit 1\n'
                      f'exec "{sys.executable}" tools/fm4_linux.py launch{state} -- "$@"\n')
    SCRIPT.chmod(0o755)
    if not args.no_steam:
        users = write_shortcuts(True)
        print(f"Added to Steam for {len(users)} user(s): {NAME}, under Non-Steam in Game Mode."
              if users else "No Steam user found; add the script as a non-Steam game by hand: "
              f"{SCRIPT}")
    desktop.parent.mkdir(parents=True, exist_ok=True)
    desktop.write_text("[Desktop Entry]\nType=Application\n"
                       f"Name={NAME}\nComment=FM4 Static Recomp, built from your own disc\n"
                       f'Exec="{SCRIPT}"\nCategories=Game;\nTerminal=false\n')
    print(f"Desktop entry: {desktop}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    setup_parser = commands.add_parser("setup", help="extract the discs and build the game")
    setup_parser.add_argument("--iso", type=Path, help="Disc 1 image to extract")
    setup_parser.add_argument("--game", type=Path, help="an extracted Disc 1 folder instead")
    setup_parser.add_argument("--disc2", type=Path, help="Disc 2 image or folder (optional)")
    setup_parser.add_argument("--dlc", type=Path, help="folder of DLC packages (optional)")
    setup_parser.add_argument("--build-only", action="store_true",
                              help="skip the Disc 1 check and only rebuild")
    setup_parser.add_argument("--container", metavar="NAME",
                              help="build in this distrobox instead of downloading the pinned "
                                   "LLVM and Steam Runtime SDK (about 3 GB); remembered")
    setup_parser.add_argument("--pinned-toolchain", action="store_true",
                              help="go back to the pinned toolchain after --container")
    launch_parser = commands.add_parser("launch", help="start the game")
    launch_parser.add_argument("--game", type=Path)
    launch_parser.add_argument("--state-root", type=Path)
    launch_parser.add_argument("game_arguments", nargs=argparse.REMAINDER)
    shortcut_parser = commands.add_parser("shortcuts", help="add to Steam and the desktop menu")
    shortcut_parser.add_argument("action", choices=("add", "remove"))
    shortcut_parser.add_argument("--state-root", type=Path)
    shortcut_parser.add_argument("--no-steam", action="store_true")
    args = parser.parse_args()
    if sys.platform != "linux":
        print("error: this is the Linux setup; on Windows use tools\\setup.ps1", file=sys.stderr)
        return 2
    try:
        return {"setup": setup, "launch": launch, "shortcuts": shortcuts}[args.command](args)
    except SetupError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
