"""The game's runtime.log, read as it grows."""
import re
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

_LINE = re.compile(r"^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\] \[(\w+)\] \[([^\]]*)\] \[(t\d+)\] (.*)$")
_STATS = re.compile(r"FH1 native executor \(Vulkan\) frame=(\d+) draws=(\d+) resolves=(\d+)")


@dataclass(frozen=True)
class LogLine:
    time: datetime | None  # None for a line not in the log's format (a continuation)
    level: str
    category: str
    thread: str
    text: str


def parse(raw):
    match = _LINE.match(raw)
    if not match:
        return LogLine(None, "", "", "", raw)
    time = datetime.strptime(match.group(1), "%Y-%m-%d %H:%M:%S.%f")
    return LogLine(time, match.group(2), match.group(3), match.group(4), match.group(5))


class LogTail:
    def __init__(self, path):
        self.path = Path(path)
        self._position = 0
        self._pending = b""

    def poll(self):
        """The complete lines written since the last poll; [] while the file does not exist."""
        try:
            with open(self.path, "rb") as log:
                log.seek(0, 2)
                size = log.tell()
                if size < self._position:
                    self._position = 0
                    self._pending = b""
                log.seek(self._position)
                data = log.read()
        except FileNotFoundError:
            return []
        self._position += len(data)
        data = self._pending + data
        complete, newline, self._pending = data.rpartition(b"\n")
        if not newline:
            return []
        return [parse(raw.rstrip(b"\r").decode("utf-8", "replace"))
                for raw in complete.split(b"\n")]


def stats_lines(lines):
    return [line for line in lines if _STATS.match(line.text)]


def draws_per_frame(lines):
    """Draws per frame between the last two executor stats lines (every 600 frames); 0 with
    fewer than two."""
    stats = stats_lines(lines)
    if len(stats) < 2:
        return 0
    first, last = (_STATS.match(line.text) for line in stats[-2:])
    frames = int(last.group(1)) - int(first.group(1))
    return (int(last.group(2)) - int(first.group(2))) / max(1, frames)
