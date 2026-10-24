"""Unpacking zip-based packages into a staging folder that a rerun never mistakes for a finished one."""
import os
import shutil
import uuid
import zipfile
from pathlib import Path, PurePosixPath


class ArchiveError(Exception):
    pass


def new_staging(root):
    """A fresh `root\\.staging-<random>`, after removing what an interrupted run left."""
    root.mkdir(parents=True, exist_ok=True)
    for stale in [*root.glob(".staging-*"), *root.glob(".old-*")]:
        shutil.rmtree(stale, ignore_errors=True)
    staging = root / f".staging-{uuid.uuid4().hex[:8]}"
    staging.mkdir()
    return staging


def extract(archive_path, staging, target_of):
    """Writes each member for which `target_of(name)` gives a relative POSIX path below `staging`."""
    staging_text = os.path.abspath(staging)
    with zipfile.ZipFile(archive_path) as zip_file:
        for member in zip_file.infolist():
            if member.is_dir():
                continue
            relative = target_of(member.filename.replace("\\", "/"))
            if relative is None:
                continue
            path = PurePosixPath(relative)
            target = os.path.abspath(os.path.join(staging_text, *path.parts))
            if path.is_absolute() or ".." in path.parts or ":" in relative or \
                    os.path.commonpath([staging_text, target]) != staging_text:
                raise ArchiveError(f"{archive_path.name}: member {member.filename!r} leaves the folder")
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with zip_file.open(member) as source, open(target, "wb") as out:
                shutil.copyfileobj(source, out)


def names(archive_path):
    with zipfile.ZipFile(archive_path) as zip_file:
        return [name.replace("\\", "/") for name in zip_file.namelist()]


def replace_folder(staging, folder):
    """Moves `staging` to `folder`; a folder already there is moved aside first and then deleted."""
    old = None
    if folder.exists():
        old = folder.with_name(f".old-{uuid.uuid4().hex[:8]}")
        folder.rename(old)
    staging.rename(folder)
    if old:
        shutil.rmtree(old, ignore_errors=True)
