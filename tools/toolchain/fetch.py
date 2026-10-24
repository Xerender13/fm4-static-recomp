"""Downloads checked against a pinned SHA-256, in parallel ranged parts when the server allows."""
import hashlib
import os
import re
import threading
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

USER_AGENT = "fm4-static-recomp-setup"
TIMEOUT = 30
_CHUNK = 1 << 20
_MIN_PART = 256 << 10
_CONTENT_RANGE = re.compile(r"bytes (\d+)-(\d+)/(\d+)$")


class DownloadError(Exception):
    pass


def sha256_file(path, text_lf=False):
    digest = hashlib.sha256()
    with open(path, "rb") as file:
        if text_lf:
            digest.update(file.read().replace(b"\r\n", b"\n"))
        else:
            for chunk in iter(lambda: file.read(_CHUNK), b""):
                digest.update(chunk)
    return digest.hexdigest().upper()


def download(urls, destination, sha256, parts=8, progress=None, text_lf=False):
    """`text_lf`: the pin is of the file with CRLF folded to LF; the file is kept as served."""
    destination = Path(destination)
    sha256 = sha256.upper()
    if destination.is_file() and sha256_file(destination, text_lf) == sha256:
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    partial = destination.with_name(destination.name + ".partial")
    errors = []
    for url in urls:
        try:
            _fetch(url, partial, parts, progress)
            actual = sha256_file(partial, text_lf)
            if actual != sha256:
                raise DownloadError(f"{url}: SHA-256 {actual}, expected {sha256}")
            os.replace(partial, destination)
            return
        except Exception as error:
            errors.append(f"{url}: {error}" if not isinstance(error, DownloadError) else str(error))
        finally:
            _remove_temporaries(partial, parts)
    raise DownloadError("; ".join(errors) or "no URL given")


def read(url):
    """A small file's bytes, unchecked (a manifest whose own content is checked by its reader)."""
    with _open(url) as response:
        return response.read()


def _open(url, headers=None):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, **(headers or {})})
    return urllib.request.urlopen(request, timeout=TIMEOUT)


def _fetch(url, partial, parts, progress):
    with _open(url, {"Range": "bytes=0-0"}) as probe:
        match = _CONTENT_RANGE.match(probe.headers.get("Content-Range", ""))
        if probe.status == 206 and match:
            total = int(match.group(3))
            probe.read()
        else:
            total = int(probe.headers.get("Content-Length") or 0)
            _stream(probe, partial, _Progress(total, progress))
            return
    report = _Progress(total, progress)
    bounds = _split(total, parts)
    files = [_part_name(partial, n) for n in range(len(bounds))]
    with ThreadPoolExecutor(len(bounds)) as pool:
        for future in [pool.submit(_fetch_part, url, start, end, file, report)
                       for (start, end), file in zip(bounds, files)]:
            future.result()
    with open(partial, "wb") as out:
        for file in files:
            with open(file, "rb") as part:
                for chunk in iter(lambda: part.read(_CHUNK), b""):
                    out.write(chunk)


def _split(total, parts):
    parts = max(1, min(parts, -(-total // _MIN_PART)))
    size = -(-total // parts)
    return [(start, min(start + size, total) - 1) for start in range(0, total, size)]


def _fetch_part(url, start, end, file, report):
    with _open(url, {"Range": f"bytes={start}-{end}"}) as response:
        match = _CONTENT_RANGE.match(response.headers.get("Content-Range", ""))
        if response.status != 206 or not match or int(match.group(1)) != start:
            raise DownloadError(f"{url}: the server stopped answering the range {start}-{end}")
        written = _stream(response, file, report)
    if written != end - start + 1:
        raise DownloadError(f"{url}: range {start}-{end} ended after {written} bytes")


def _stream(response, path, report):
    written = 0
    with open(path, "wb") as out:
        for chunk in iter(lambda: response.read(_CHUNK), b""):
            out.write(chunk)
            written += len(chunk)
            report.add(len(chunk))
    return written


def _part_name(partial, n):
    return partial.with_name(partial.name.removesuffix(".partial") + f".part{n}")


def _remove_temporaries(partial, parts):
    for path in [partial] + [_part_name(partial, n) for n in range(parts)]:
        try:
            path.unlink()
        except FileNotFoundError:
            pass


class _Progress:
    def __init__(self, total, callback):
        self._total = total
        self._done = 0
        self._callback = callback
        self._lock = threading.Lock()

    def add(self, count):
        if not self._callback:
            return
        with self._lock:
            self._done += count
            self._callback(self._done, max(self._total, self._done))
