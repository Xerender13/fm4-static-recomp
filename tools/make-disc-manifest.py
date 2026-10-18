"""Writes config/supported-discs.json from known-good Disc 1 extractions and Disc 2 packages.

    make-disc-manifest.py --release "1.0 PAL (Europe, Australia)=D:/disc1-pal" \
        --release "1.0 USA=D:/disc1-usa" D:/disc2 config/supported-discs.json

Files identical on every release go to disc1.files; the rest go to each release's own list.
"""
import argparse
import hashlib
import json
import pathlib
import struct

HDR_EXECUTION_INFO = 0x00040006
# The dashboard update the console installs from the disc; the game never reads it, and some
# dumps leave it out.
OPTIONAL_PREFIX = "$SystemUpdate/"


def digest(path: pathlib.Path) -> str:
    with path.open("rb") as handle:
        return hashlib.file_digest(handle, "sha256").hexdigest().upper()


def media_id(xex: pathlib.Path) -> str:
    raw = xex.read_bytes()
    count = struct.unpack(">I", raw[20:24])[0]
    headers = dict(struct.unpack(">II", raw[24 + i * 8:32 + i * 8]) for i in range(count))
    return raw[headers[HDR_EXECUTION_INFO]:headers[HDR_EXECUTION_INFO] + 4].hex().upper()


def release(spec: str) -> tuple[str, pathlib.Path]:
    name, sep, root = spec.partition("=")
    if not sep:
        raise argparse.ArgumentTypeError("expected NAME=DIR")
    return name, pathlib.Path(root)


def scan(root: pathlib.Path) -> dict:
    return {p.relative_to(root).as_posix(): {"size": p.stat().st_size, "sha256": digest(p)}
            for p in root.rglob("*") if p.is_file()}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--release", type=release, action="append", required=True)
    parser.add_argument("disc2", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    scans = [scan(root) for _, root in args.release]
    paths = sorted(set().union(*scans))
    for (name, _), files in zip(args.release, scans):
        absent = [p for p in paths if p not in files and not p.startswith(OPTIONAL_PREFIX)]
        if absent:
            raise SystemExit(f"{name} lacks {len(absent)} files, first {absent[0]}")
    shared, own = [], {name: [] for name, _ in args.release}
    for path in paths:
        found = [files[path] for files in scans if path in files]
        if len(found) == len(scans) and all(f == found[0] for f in found):
            entry = {"path": path, **found[0]}
            if path.startswith(OPTIONAL_PREFIX):
                entry["optional"] = True
            shared.append(entry)
        else:
            for (name, _), files in zip(args.release, scans):
                if path in files:
                    own[name].append({"path": path, **files[path]})
    if any(e["path"] == "default.xex" for e in shared):
        raise SystemExit("every release has the same default.xex: one release is enough")
    packages = sorted(p for p in args.disc2.iterdir() if p.is_file())
    manifest = {
        "schema_version": 2,
        "title_id": "4D530910",
        "disc1": {
            "releases": [{"name": name, "media_id": media_id(root / "default.xex"), "files": own[name]}
                         for name, root in args.release],
            "files": shared,
        },
        "disc2": {"packages": [{"name": p.name, "size": p.stat().st_size, "sha256": digest(p)} for p in packages]},
    }
    args.output.write_text(json.dumps(manifest, indent=1) + "\n", encoding="utf-8", newline="\n")
    counts = ", ".join(f"{name} {len(files)}" for name, files in own.items())
    print(f"{len(shared)} shared disc 1 files, per release: {counts}; {len(packages)} disc 2 packages")


if __name__ == "__main__":
    main()
