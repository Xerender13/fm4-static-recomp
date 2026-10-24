"""Serves fake GitHub releases for the launcher's update tests: GET /repos/<owner>/<repo>/releases
lists one release per subfolder of the root (v0.1.1, ...; a file named "prerelease" in one marks
it), each with that subfolder's files as assets, served with HTTP Range. Point the launcher at it
with FM4_STATIC_RECOMP_UPDATE_API=http://127.0.0.1:<port>. Usage:
  python fake-release-server.py <root> [port=8765]"""
import http.server
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(sys.argv[1]).resolve()
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 8765
RELEASES = re.compile(r"^/repos/[^/]+/[^/]+/releases(\?.*)?$")


def releases():
    result = []
    for folder in sorted(p for p in ROOT.iterdir() if p.is_dir()):
        assets = [f for f in sorted(folder.iterdir()) if f.is_file() and f.name != "prerelease"]
        result.append({
            "tag_name": folder.name,
            "name": folder.name,
            "draft": False,
            "prerelease": (folder / "prerelease").exists(),
            "body": f"Test release {folder.name}.\nSecond line.",
            "assets": [{"name": f.name, "size": f.stat().st_size,
                        "browser_download_url": f"http://127.0.0.1:{PORT}/assets/{folder.name}/{f.name}"} for f in assets],
        })
    return result


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if RELEASES.match(self.path):
            self.send_body(200, json.dumps(releases()).encode(), "application/json")
            return
        parts = self.path.split("?")[0].split("/")
        if len(parts) != 4 or parts[1] != "assets" or "/" in parts[2] or parts[3] in ("", ".", ".."):
            self.send_error(404)
            return
        file = ROOT / parts[2] / parts[3]
        if not file.is_file() or file.resolve().parent.parent != ROOT:
            self.send_error(404)
            return
        data = file.read_bytes()
        start = 0
        header = self.headers.get("Range")
        if header and header.startswith("bytes="):
            start = int(header[6:].split("-")[0] or 0)
        if start >= len(data) > 0:
            self.send_response(416)
            self.send_header("Content-Range", f"bytes */{len(data)}")
            self.end_headers()
            return
        self.send_response(206 if start else 200)
        if start:
            self.send_header("Content-Range", f"bytes {start}-{len(data) - 1}/{len(data)}")
        self.send_header("Content-Length", str(len(data) - start))
        self.end_headers()
        self.wfile.write(data[start:])

    def send_body(self, code, body, content_type):
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
