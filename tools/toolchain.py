"""Private copies of Microsoft's C++ runtime and Windows SDK: python -I tools\toolchain.py --help."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from toolchain import cli  # noqa: E402

if __name__ == "__main__":
    sys.exit(cli.main())
