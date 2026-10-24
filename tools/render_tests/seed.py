"""Seeds: a pinned copy of the player's save with a shader cache built from the disc, and the
per-run copies the game plays on.

The content packages (Disc 2, DLC: gigabytes) are read-only in the seed and hard-linked into
each run, so a run costs no copy of them and a game that tried to write one would fail rather
than change the seed. Everything else is copied.
"""
import hashlib
import json
import os
import shutil
import stat
import subprocess
from datetime import datetime
from pathlib import Path

from . import game

PROJECT = Path(__file__).resolve().parents[2]
LOCAL = PROJECT / ".local"
SEEDS = LOCAL / "render-tests" / "seeds"
BUILD = PROJECT / "out" / "build" / "win-amd64-release"
GAME_ROOT = PROJECT.parent / "extracted" / "fm4"
CONTENT = Path("user") / "0000000000000000"
PIPELINE_LIST = PROJECT / "config" / "shaders" / "4D530910.fbo.vk.xpso"


def sdk_root():
    """As Resolve-Fm4SdkRoot (tools/release-common.ps1), without its payload fallback."""
    if os.environ.get("FM4_SDK_ROOT"):
        return Path(os.environ["FM4_SDK_ROOT"]).resolve()
    submodule = PROJECT / "thirdparty" / "shiftglue-sdk"
    if (submodule / "CMakeLists.txt").exists():
        return submodule
    return (PROJECT.parent / "pinyon-shift" / "thirdparty" / "shiftglue-sdk").resolve()


def _is_content(relative):
    return relative.parts[:2] == CONTENT.parts


def _read_only(path):
    os.chmod(path, stat.S_IREAD)


def _writable(path):
    os.chmod(path, stat.S_IREAD | stat.S_IWRITE)


def copy_player_state(player_state, seed_dir):
    """Copies the save (user) into <seed>\\state; no graphics.toml, so runs play at the built-in
    defaults the reference counters were measured with. Returns the seed's state folder."""
    seed_dir = Path(seed_dir)
    if seed_dir.exists():
        raise FileExistsError(f"seed {seed_dir} exists; seeds are never overwritten")
    state = seed_dir / "state"
    shutil.copytree(Path(player_state) / "user", state / "user")
    (state / "config").mkdir()
    for path in (state / CONTENT).rglob("*"):
        if path.is_file():
            _read_only(path)
    return state


def prepare_run(seed_dir, run_dir):
    """Copies the seed's state into <run>\\state, content packages hard-linked."""
    source = Path(seed_dir) / "state"
    target = Path(run_dir) / "state"
    if target.exists():
        raise FileExistsError(f"run state {target} exists")
    for directory, _, files in os.walk(source):
        relative = Path(directory).relative_to(source)
        (target / relative).mkdir(parents=True, exist_ok=True)
        for name in files:
            if _is_content(relative / name):
                os.link(Path(directory) / name, target / relative / name)
            else:
                shutil.copy2(Path(directory) / name, target / relative / name)
    return target


def remove_run_state(run_dir, seed_dir):
    """Deletes <run>\\state. A hard link shares its file's read-only flag, so the content links
    are made writable to delete them and the seed's files read-only again after."""
    state = Path(run_dir) / "state"
    for path in (state / CONTENT).rglob("*"):
        if path.is_file():
            _writable(path)
            path.unlink()
    shutil.rmtree(state)
    for path in (Path(seed_dir) / "state" / CONTENT).rglob("*"):
        if path.is_file():
            _read_only(path)


def build_shader_cache(state, log_path):
    """setup.ps1's shader step (release-common.ps1 Write-Fm4ShaderCache) on the seed's state."""
    def quote(value):
        return "'" + str(value).replace("'", "''") + "'"
    command = (f". {quote(PROJECT / 'tools' / 'release-common.ps1')}; Write-Fm4ShaderCache "
               f"-ExecutableDirectory {quote(BUILD)} -GameRoot {quote(GAME_ROOT)} "
               f"-StateRoot {quote(state)} -LogPath {quote(log_path)}")
    result = subprocess.run(["pwsh", "-NoProfile", "-Command", command], capture_output=True,
                            text=True)
    if result.returncode:
        raise RuntimeError(f"shader cache build failed: {result.stderr.strip() or result.stdout}")
    return result.stdout.strip().splitlines()[-1]


def make_seed(name, player_state=LOCAL / "state", seeds_root=SEEDS):
    """Reads the player's state (never writes it) into a new seed and builds its shader cache."""
    player_state = Path(player_state).resolve()
    marker = (str(player_state) + "\\").lower()
    if any(line and marker in line.lower() for _, line in game._game_processes()):
        raise RuntimeError(f"the player's game is running on {player_state}; close it first")
    seed_dir = Path(seeds_root) / name
    state = copy_player_state(player_state, seed_dir)
    cache = build_shader_cache(state, seed_dir / "shader-cache.log")
    executable = BUILD / "fm4_static_recomp.exe"
    (seed_dir / "seed.json").write_text(json.dumps({
        "name": name,
        "created": datetime.now().isoformat(timespec="seconds"),
        "player_state": str(player_state),
        "executable": str(executable),
        "executable_sha256": hashlib.sha256(executable.read_bytes()).hexdigest(),
        "shader_cache": cache,
    }, indent=2), encoding="utf-8")
    return seed_dir


SHAREABLE = Path("cache") / "shaders" / "shareable"


def warm_from(seed_dir, run_dir):
    """Takes a run's shader cache into the seed: the disc-built one lacks the pipelines of
    tracks the save never raced, which the game adds as it makes them."""
    seed_dir, run_dir = Path(seed_dir), Path(run_dir)
    target = seed_dir / "state" / SHAREABLE
    target.mkdir(parents=True, exist_ok=True)
    copied = []
    for path in sorted((run_dir / "state" / SHAREABLE).iterdir()):
        shutil.copy2(path, target / path.name)
        copied.append(f"{path.name} {path.stat().st_size}")
    record = seed_dir / "seed.json"
    info = json.loads(record.read_text(encoding="utf-8"))
    info.setdefault("warmed_from", []).append({"run": run_dir.name, "files": copied})
    record.write_text(json.dumps(info, indent=2), encoding="utf-8")
