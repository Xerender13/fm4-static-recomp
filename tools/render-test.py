"""Render regression tests: python -I tools\\render-test.py --tier short|long|full (see --help)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from render_tests import cli  # noqa: E402

if __name__ == "__main__":
    sys.exit(cli.main())
