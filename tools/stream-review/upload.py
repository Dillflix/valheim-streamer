"""Outbound-only mirror for the existing diagnostic writer. Requires Python 3.10+."""
import argparse
import json
import os
from pathlib import Path
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

NAME = re.compile(r"(?:scenic|footage)-[0-9]{8}T[0-9]{9}-[0-9a-f]{8}-[0-9]{5,}\.jsonl")
MIB = 1024 * 1024


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args):
        return None  # Never forward uploader credentials to another origin.


class Mirror:
    def __init__(self, root, url, token):
        parts = urllib.parse.urlsplit(url)
        if (parts.scheme != "https" and not (parts.scheme == "http" and parts.hostname in ("127.0.0.1", "localhost"))) or not parts.hostname or parts.username or parts.query or parts.fragment:
            raise ValueError("Use the HTTPS /review URL on your own stream host")
        self.root, self.url, self.token = root, url.rstrip("/") + "/ingest/", token
        self.open = urllib.request.build_opener(NoRedirect).open
        self.offsets, self.finished = {}, set()

    def call(self, path, data=None, content="application/octet-stream", mtime=None):
        req = urllib.request.Request(self.url+path, data=data, headers={
            "Authorization": "Bearer "+self.token, "Content-Type": content, "X-Capture-Mtime": str(mtime or 0)})
        with self.open(req, timeout=30) as response:
            return json.load(response)["offset"]

    def trace(self, path):
        if path not in self.offsets:
            self.offsets[path] = self.call(path.name)
        offset = self.offsets[path]
        with path.open("rb") as source:
            source.seek(offset)
            for _ in range(8):
                block = source.read(256*1024)
                n = block.rfind(b'\n') + 1
                if n == 0:
                    break
                try:
                    offset = self.call(f"{path.name}?offset={offset}", block[:n], mtime=path.stat().st_mtime)
                except urllib.error.HTTPError as error:
                    if error.code != 409:
                        raise
                    self.offsets.pop(path, None)
                    return
                self.offsets[path] = offset
                source.seek(offset)

    def segment(self, path):
        if path in self.finished:
            return
        with path.open("rb") as src:
            size = src.seek(0, 2)
            if size > 3*MIB:
                return
            src.seek(max(0, size-1024))
            lines = src.read().splitlines()
        if not lines or b'"video-end"' not in lines[-1] or not json.loads(lines[-1]).get("complete"):
            return
        if self.call(path.name) == 0:
            movie = path.with_suffix(".mp4")
            if movie.is_symlink() or movie.stat().st_size > 8*MIB:
                return
            boundary = uuid.uuid4().hex
            def field(key, file):
                return (f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"; filename="{file.name}"\r\n\r\n'.encode()
                        + file.read_bytes() + b'\r\n')
            body = field("mapping", path) + field("video", movie) + f"--{boundary}--\r\n".encode()
            self.call(path.name, body, "multipart/form-data; boundary="+boundary, path.stat().st_mtime)
        self.finished.add(path)

    def sync(self):
        if not self.root.is_dir():
            raise FileNotFoundError("Diagnostic folder unavailable: "+str(self.root))
        paths = {p for folder, pattern in [(self.root,"scenic-*.jsonl"), (self.root/"footage","footage-*.jsonl")]
                 for p in folder.glob(pattern) if NAME.fullmatch(p.name) and not p.is_symlink()}
        self.finished.intersection_update(paths)
        self.offsets = {p: n for p, n in self.offsets.items() if p in paths}
        for path in sorted(paths, reverse=True):
            try:
                if path.name.startswith("scenic-"):
                    self.trace(path)
                else:
                    self.segment(path)
            except FileNotFoundError:
                continue  # Local retention raced this poll.
            except (ValueError, urllib.error.HTTPError) as error:
                if isinstance(error, urllib.error.HTTPError) and error.code != 400:
                    raise
                print(f"Skipping {path.name}: {error}", file=sys.stderr)


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("directory", type=Path)
    p.add_argument("url")
    p.add_argument("--once", action="store_true")
    args = p.parse_args()
    mirror = Mirror(args.directory, args.url, os.environ["REVIEW_UPLOAD_TOKEN"])
    while True:
        try:
            mirror.sync()
        except OSError as exc:
            if args.once:
                raise
            print(f"Upload delayed: {exc}", file=sys.stderr, flush=True)
            time.sleep(10)
        if args.once:
            break
        time.sleep(3)
