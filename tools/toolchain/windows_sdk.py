"""The Windows SDK's headers, import libraries, rc.exe and mt.exe from Microsoft's NuGet packages."""
import json
import re
import shutil
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import unquote

from toolchain import archive, fetch

NUGET = "https://api.nuget.org/v3-flatcontainer"
RECORD = "windows-sdk.json"
INCLUDE_PARTS = ("ucrt", "um", "shared", "winrt", "cppwinrt")
LIB_PARTS = ("ucrt", "um")
# rcdll.dll loads ServicingCommon.dll and mt.exe loads midlrtmd.dll.
TOOLS = ("rc.exe", "rcdll.dll", "ServicingCommon.dll", "mt.exe", "midlrtmd.dll")


@dataclass(frozen=True)
class Installed:
    folder: Path
    sdk_version: str
    include: list
    lib: list
    rc: Path
    mt: Path


def install(version, packages, downloads, root, base_url=NUGET, progress=None):
    """`progress(name, fraction)` over all packages."""
    downloads, root = Path(downloads), Path(root)
    current = find_installed(root, version)
    if current:
        return current
    files = []
    for index, (package_id, sha256) in enumerate(packages):
        lower = package_id.lower()
        path = downloads / f"{lower}.{version}.nupkg"
        report = None
        if progress:
            def report(done, total, index=index, name=package_id):
                progress(name, (index + (done / total if total else 0)) / len(packages))
        fetch.download([f"{base_url}/{lower}/{version}/{lower}.{version}.nupkg"], path, sha256, parts=16,
                       progress=report)
        files.append(path)
    staging = archive.new_staging(root)
    try:
        names = [unquote(n) for file in files for n in archive.names(file)]
        folders = {n.split("/")[2] for n in names if n.lower().startswith("c/include/") and n.count("/") > 2}
        if len(folders) != 1:
            raise archive.ArchiveError(f"expected one SDK folder under c/Include, found {sorted(folders)}")
        sdk_version = folders.pop()
        for file in files:
            archive.extract(file, staging, _target_mapper(sdk_version))
        (staging / RECORD).write_text(json.dumps({"version": version, "sdk_version": sdk_version,
                                                  "packages": [list(p) for p in packages],
                                                  "tools": list(TOOLS)}, indent=2),
                                      encoding="utf-8")
        folder = root / f"windows-sdk-{version}"
        archive.replace_folder(staging, folder)
    except BaseException:
        shutil.rmtree(staging, ignore_errors=True)
        raise
    for file in files:
        file.unlink(missing_ok=True)
    return _installed(folder)


def find_installed(root, version):
    """None also for a folder unpacked with fewer tools than this version takes."""
    folder = Path(root) / f"windows-sdk-{version}"
    if not (folder / RECORD).is_file():
        return None
    if json.loads((folder / RECORD).read_text(encoding="utf-8-sig")).get("tools") != list(TOOLS):
        return None
    return _installed(folder)


def _installed(folder):
    sdk_version = json.loads((folder / RECORD).read_text(encoding="utf-8-sig"))["sdk_version"]
    bin_x64 = folder / "bin" / sdk_version / "x64"
    return Installed(folder, sdk_version, [folder / "Include" / sdk_version / p for p in INCLUDE_PARTS],
                     [folder / "Lib" / sdk_version / p / "x64" for p in LIB_PARTS],
                     bin_x64 / "rc.exe", bin_x64 / "mt.exe")


def _target_mapper(sdk_version):
    folder = re.escape(sdk_version)
    include = re.compile(rf"^c/Include/{folder}/(.+)$", re.IGNORECASE)
    tool = re.compile(rf"^c/bin/{folder}/x64/({'|'.join(map(re.escape, TOOLS))})$", re.IGNORECASE)
    lib = re.compile(rf"^c/({'|'.join(LIB_PARTS)})/x64/(.+)$", re.IGNORECASE)

    def target_of(name):
        name = unquote(name)
        if match := include.match(name):
            return f"Include/{sdk_version}/{match.group(1)}"
        if match := tool.match(name):
            return f"bin/{sdk_version}/x64/{match.group(1)}"
        if match := lib.match(name):
            return f"Lib/{sdk_version}/{match.group(1).lower()}/x64/{match.group(2)}"
        return None

    return target_of
