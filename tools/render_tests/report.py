"""The run's verdict: a console line per check, summary.json, the exit code."""
import json
from dataclasses import asdict
from pathlib import Path

EXIT_PASS = 0
EXIT_FAIL = 1
EXIT_BROKEN = 2


def write(run_dir, results, broken=None, executable=None):
    width = max((len(result.name) for result in results), default=0)
    for result in results:
        status = "PASS" if result.passed else "FAIL"
        detail = f"  {result.detail}" if result.detail and not result.passed else ""
        print(f"{status} {result.name:<{width}}  {result.value} (limit {result.limit}){detail}")
    failed = sum(1 for result in results if not result.passed)
    if broken:
        code = EXIT_BROKEN
        print(f"BROKEN at step {broken.step}: {broken.reason}")
    else:
        code = EXIT_FAIL if failed else EXIT_PASS
    if executable:
        print(f"tested {executable.get('path', 'fm4_static_recomp.exe')}, built "
              f"{executable['modified']}, sha256 {executable['sha256'][:12]}")
    print(f"{len(results) - failed} passed, {failed} failed" + (", run broken" if broken else ""))
    summary = {
        "exit_code": code,
        "checks": [asdict(result) for result in results],
        "broken": {"step": broken.step, "reason": broken.reason} if broken else None,
        "executable": executable,
    }
    Path(run_dir).mkdir(parents=True, exist_ok=True)
    (Path(run_dir) / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    return code
