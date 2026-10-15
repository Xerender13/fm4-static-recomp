"""Writes config/supported-discs.json from a known-good Disc 1 extraction and Disc 2 packages."""
import argparse
import hashlib
import json
import pathlib


def digest(path: pathlib.Path) -> str:
    with path.open("rb") as handle:
        return hashlib.file_digest(handle, "sha256").hexdigest().upper()


def entry(root: pathlib.Path, path: pathlib.Path) -> dict:
    return {"path": path.relative_to(root).as_posix(), "size": path.stat().st_size, "sha256": digest(path)}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("disc1", type=pathlib.Path)
    parser.add_argument("disc2", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()
    files = sorted((p for p in args.disc1.rglob("*") if p.is_file()), key=lambda p: p.relative_to(args.disc1).as_posix())
    entries = [entry(args.disc1, p) for p in files]
    required = next(e for e in entries if e["path"] == "default.xex")
    packages = sorted(p for p in args.disc2.iterdir() if p.is_file())
    manifest = {
        "schema_version": 1,
        "title_id": "4D530910",
        "disc1": {"media_id": "61D62D9F", "required": required, "files": entries},
        "disc2": {"packages": [{"name": p.name, "size": p.stat().st_size, "sha256": digest(p)} for p in packages]},
    }
    args.output.write_text(json.dumps(manifest, indent=1) + "\n", encoding="utf-8", newline="\n")
    print(f"{len(entries)} disc 1 files, {len(packages)} disc 2 packages")


if __name__ == "__main__":
    main()
