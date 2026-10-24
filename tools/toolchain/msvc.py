"""MSVC's C runtime and standard library from Microsoft's Visual Studio channel, without its installer."""
import json
import re
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import NamedTuple

from toolchain import archive, fetch

_MANIFEST_ITEM = "Microsoft.VisualStudio.Manifests.VisualStudio"
_BUILD_TOOLS_ITEM = "Microsoft.VisualStudio.Product.BuildTools"
_WANTED = ("CRT.Headers", "CRT.x64.Desktop", "CRT.x64.Store", "CRT.Redist.X64")
RECORD = "toolset.json"


class FeedError(Exception):
    pass


class Package(NamedTuple):
    id: str
    version: str
    url: str
    sha256: str
    size: int


class Resolved(NamedTuple):
    line: str
    licence_url: str
    packages: list


@dataclass(frozen=True)
class Installed:
    folder: Path
    toolset_version: str
    include: Path
    lib: Path
    redist: Path


def read_channel(channel_url):
    return json.loads(fetch.read(channel_url).decode("utf-8-sig"))


def licence_url(channel):
    for item in channel.get("channelItems", []):
        if item.get("id") == _BUILD_TOOLS_ITEM:
            resources = item.get("localizedResources") or []
            english = [r for r in resources if r.get("language", "").lower() == "en-us"]
            for resource in english + resources:
                if resource.get("license"):
                    return resource["license"]
    raise FeedError(f"the channel names no {_BUILD_TOOLS_ITEM} licence")


def resolve(channel_url, line, channel=None):
    channel = channel or read_channel(channel_url)
    items = [i for i in channel.get("channelItems", []) if i.get("id") == _MANIFEST_ITEM]
    if not items or not items[0].get("payloads"):
        raise FeedError(f"the channel has no {_MANIFEST_ITEM}")
    item = items[0]
    # The channel's SHA-256 for the manifest is never that of the minified file Microsoft's CDN
    # serves, so the manifest is matched to the channel by its build version instead.
    manifest = json.loads(fetch.read(item["payloads"][0]["url"]).decode("utf-8-sig"))
    build = manifest.get("info", {}).get("buildVersion")
    if build != item.get("version"):
        raise FeedError(f"the manifest is of build {build}, the channel of {item.get('version')}")
    pattern = re.compile(rf"^Microsoft\.VC\.{re.escape(line)}\.\d+\.\d+\."
                         rf"({'|'.join(re.escape(w) for w in _WANTED)})\.base$", re.IGNORECASE)
    found = {}
    for entry in manifest.get("packages", []):
        match = pattern.match(entry.get("id", ""))
        if not match:
            continue
        kind = match.group(1).lower()
        if kind in found:
            raise FeedError(f"the manifest holds {entry['id']} twice")
        payload = entry["payloads"][0]
        found[kind] = Package(entry["id"], entry["version"], payload["url"], payload["sha256"].upper(),
                              payload.get("size", 0))
    missing = [w for w in _WANTED if w.lower() not in found]
    if missing:
        raise FeedError(f"the feed serves no {', '.join(missing)} package of toolset {line}")
    return Resolved(line, licence_url(channel), [found[w.lower()] for w in _WANTED])


def install(resolved, downloads, root, progress=None):
    downloads, root = Path(downloads), Path(root)
    record_packages = [{"id": p.id, "version": p.version, "sha256": p.sha256} for p in resolved.packages]
    current = find_installed(root, resolved.line)
    if current and _record(current.folder)["packages"] == record_packages:
        return current
    files = _download(resolved.packages, downloads, progress)
    staging = archive.new_staging(root)
    try:
        toolset = _single_folder(files[0], "Contents/VC/Tools/MSVC/")
        redist = _single_folder(files[-1], "Contents/VC/Redist/MSVC/")
        for file in files:
            archive.extract(file, staging, _below_contents)
        # Packages of one line can carry different patch folders; a toolset missing these would
        # fail to link, and a rerun would take it as installed.
        tools = staging / "VC" / "Tools" / "MSVC" / toolset
        for required in (tools / "include" / "vcruntime.h", tools / "lib" / "x64" / "msvcprt.lib",
                         staging / "VC" / "Redist" / "MSVC" / redist / "x64" / "Microsoft.VC143.CRT" /
                         "vcruntime140.dll"):
            if not required.is_file():
                raise FeedError(f"the {resolved.line} packages leave out {required.relative_to(staging)}")
        (staging / RECORD).write_text(json.dumps({"line": resolved.line, "toolset_version": toolset,
                                                  "redist_version": redist, "packages": record_packages},
                                                 indent=2), encoding="utf-8")
        folder = root / toolset
        archive.replace_folder(staging, folder)
    except archive.ArchiveError as error:
        shutil.rmtree(staging, ignore_errors=True)
        raise FeedError(str(error)) from error
    except BaseException:
        shutil.rmtree(staging, ignore_errors=True)
        raise
    for other in root.iterdir():
        if other != folder and (other / RECORD).is_file() and _record(other).get("line") == resolved.line:
            shutil.rmtree(other, ignore_errors=True)
    for file in files:
        file.unlink(missing_ok=True)
    return _installed(folder)


def find_installed(root, line):
    root = Path(root)
    if not root.is_dir():
        return None
    found = []
    for folder in root.iterdir():
        if folder.name.startswith(".") or not (folder / RECORD).is_file():
            continue
        record = _record(folder)
        if record.get("line") == line:
            found.append((_version_key(record["toolset_version"]), folder))
    return _installed(max(found)[1]) if found else None


def _installed(folder):
    record = _record(folder)
    tools = folder / "VC" / "Tools" / "MSVC" / record["toolset_version"]
    return Installed(folder, record["toolset_version"], tools / "include", tools / "lib" / "x64",
                     folder / "VC" / "Redist" / "MSVC" / record["redist_version"] / "x64" / "Microsoft.VC143.CRT")


def _record(folder):
    return json.loads((folder / RECORD).read_text(encoding="utf-8-sig"))


def _version_key(version):
    return tuple(int(part) for part in version.split(".") if part.isdigit())


def _download(packages, downloads, progress):
    total = sum(p.size for p in packages)
    done_before = 0
    files = []
    for package in packages:
        path = downloads / f"{package.id}.vsix"
        report = (lambda done, _total, base=done_before: progress(base + done, total)) if progress else None
        fetch.download([package.url], path, package.sha256, progress=report)
        done_before += package.size
        files.append(path)
    return files


def _single_folder(file, prefix):
    folders = {name[len(prefix):].split("/", 1)[0] for name in archive.names(file)
               if name.lower().startswith(prefix.lower()) and "/" in name[len(prefix):]}
    if len(folders) != 1:
        raise FeedError(f"{file.name}: expected one folder under {prefix}, found {sorted(folders)}")
    return folders.pop()


def _below_contents(name):
    return name[len("Contents/"):] if name.startswith("Contents/") else None
