"""Private review receiver. Run behind TLS with the supplied Compose service."""
import base64
import hmac
import importlib.util
import io
import json
import math
import os
from pathlib import Path
import re
import sqlite3
import tempfile
import time
from contextlib import closing

from flask import Flask, Response, abort, jsonify, request, send_file, send_from_directory

HERE = Path(__file__).resolve().parent
exporter = HERE / "review-footage.py"
if not exporter.exists():
    exporter = HERE.parent / exporter.name
spec = importlib.util.spec_from_file_location("footage_review", exporter)
review = importlib.util.module_from_spec(spec)
spec.loader.exec_module(review)

SESSION = r"[0-9]{8}T[0-9]{9}-[0-9a-f]{8}"
NAME = re.compile(rf"(scenic|footage)-({SESSION})-([0-9]{{5,}})\.jsonl")
MIB = 1024 * 1024


def create_app():
    app = Flask(__name__, static_folder=None)
    token = os.environ["REVIEW_UPLOAD_TOKEN"]
    password = os.environ["REVIEW_PASSWORD"]
    if min(len(token), len(password)) < 16 or token == password:
        raise ValueError("Set distinct review and upload secrets of at least 16 characters")
    user = os.environ.get("REVIEW_USER", "reviewer")
    viewer = "Basic " + base64.b64encode(f"{user}:{password}".encode()).decode()
    uploader = "Bearer " + token
    budget = max(32, int(os.environ.get("REVIEW_MAX_MIB", 2048))) * MIB
    lifetime = max(1, int(os.environ.get("REVIEW_RETENTION_HOURS", 24))) * 3600
    folder = Path(os.environ.get("REVIEW_DATA", "data"))
    folder.mkdir(parents=True, exist_ok=True)
    app.config["MAX_CONTENT_LENGTH"] = 12 * MIB

    def db():
        conn = sqlite3.connect(folder / "review.db", timeout=20)
        conn.row_factory = sqlite3.Row
        return conn

    with closing(db()) as conn:
        conn.executescript("""
        CREATE TABLE IF NOT EXISTS items (
            name TEXT PRIMARY KEY, session TEXT NOT NULL, updated REAL, lo REAL, hi REAL,
            mapping BLOB NOT NULL, video BLOB);
        CREATE INDEX IF NOT EXISTS by_session ON items(session,lo,hi);
        CREATE INDEX IF NOT EXISTS by_age ON items(updated);
        """)

    @app.before_request
    def authenticate():
        auth = request.headers.get("Authorization", "").encode()
        ingest = request.path.startswith("/review/ingest/")
        if not hmac.compare_digest(auth, (uploader if ingest else viewer).encode()):
            return Response(status=401, headers={} if ingest else {"WWW-Authenticate": 'Basic realm="Broadcast review"'})

    @app.after_request
    def headers(response):
        response.headers.update({"Cache-Control": "private, no-store", "X-Content-Type-Options": "nosniff",
                                 "Referrer-Policy": "no-referrer", "X-Frame-Options": "DENY",
                                 "Content-Security-Policy": "default-src 'self'; media-src 'self' blob:; connect-src 'self'; worker-src 'self' blob:; frame-ancestors 'none'"})
        return response

    @app.get("/review/")
    def index():
        return send_from_directory(HERE / "static", "index.html")

    @app.get("/review/assets/<name>")
    def asset(name):
        return send_from_directory(HERE / "static", name)

    def prune(conn):
        conn.execute("DELETE FROM items WHERE updated < ?", (time.time() - lifetime,))
        used = conn.execute("SELECT COALESCE(sum(length(mapping)+coalesce(length(video),0)),0) FROM items").fetchone()[0]
        while used > budget:
            row = conn.execute("SELECT name,length(mapping)+coalesce(length(video),0) FROM items ORDER BY updated LIMIT 1").fetchone()
            conn.execute("DELETE FROM items WHERE name=?", (row[0],))
            used -= row[1]

    @app.route("/review/ingest/<name>", methods=["GET", "POST"])
    def ingest(name):
        match = NAME.fullmatch(name)
        if not match:
            abort(400)
        with closing(db()) as conn, conn:
            if request.method == "GET":
                row = conn.execute("SELECT length(mapping) FROM items WHERE name=?", (name,)).fetchone()
                return jsonify(offset=row[0] if row else 0)
            modified = request.headers.get("X-Capture-Mtime", type=float)
            touched = min(time.time(), modified) if modified and math.isfinite(modified) else time.time()
            conn.execute("BEGIN IMMEDIATE")
            if match[1] == "scenic":
                offset = request.args.get("offset", type=int)
                current = conn.execute("SELECT length(mapping) FROM items WHERE name=?", (name,)).fetchone()
                size = current[0] if current else 0
                if offset != size:
                    return jsonify(offset=size), 409
                data = request.get_data()
                if not data.endswith(b'\n') or len(data) > MIB or size + len(data) > 5*MIB:
                    abort(400)
                try:
                    times = [row["time"] for line in data.splitlines() if line.strip()
                             for row in [json.loads(line)] if "seq" in row]
                    if any(not math.isfinite(t) or t < 0 for t in times):
                        abort(400)
                except (ValueError, TypeError, KeyError):
                    abort(400)
                lo, hi = (min(times), max(times)) if times else (None, None)
                conn.execute("""INSERT INTO items VALUES(?,?,?,?,?,?,NULL)
                    ON CONFLICT(name) DO UPDATE SET mapping=CAST(mapping || excluded.mapping AS BLOB),
                    updated=excluded.updated, lo=coalesce(min(lo,excluded.lo),lo,excluded.lo),
                    hi=coalesce(max(hi,excluded.hi),hi,excluded.hi)""",
                             (name, match[2], touched, lo, hi, data))
                size += len(data)
            else:
                raw, video = request.files.get("mapping"), request.files.get("video")
                if not raw or not video:
                    abort(400)
                mapping, movie = raw.read(), video.read()
                if len(mapping) > 3*MIB or len(movie) > 8*MIB or movie[4:8] != b'ftyp':
                    abort(400)
                with tempfile.TemporaryDirectory() as temp:
                    path = Path(temp) / name
                    path.write_bytes(mapping)
                    try:
                        seg = review.read_segment(path)
                        frames = seg["frames"]
                        sessions = {f["traceSession"] for f in frames}
                        times = [f["time"] for f in frames]
                        if (not seg["complete"] or not times or not all(math.isfinite(t) and t >= 0 for t in times)
                                or len(sessions) != 1 or not re.fullmatch(SESSION, next(iter(sessions)))
                                or seg["header"]["video"] != path.with_suffix(".mp4").name or seg["header"]["fps"] != 12):
                            abort(400)
                    except (KeyError, TypeError, ValueError, AttributeError):
                        abort(400)
                conn.execute("INSERT OR IGNORE INTO items VALUES(?,?,?,?,?,?,?)", (
                    name, sessions.pop(), touched, min(times), max(times), mapping, movie))
                size = len(mapping)
            prune(conn)
            return jsonify(offset=size)

    @app.get("/review/api/segments")
    def segments():
        with closing(db()) as conn:
            rows = conn.execute("""SELECT name,session,lo,hi FROM items WHERE video IS NOT NULL AND updated>=?
                                    ORDER BY updated DESC LIMIT 100""", (time.time()-lifetime,))
            return jsonify([dict(row) for row in rows])

    @app.get("/review/api/media/<name>/<part>")
    def media(name, part):
        if not NAME.fullmatch(name) or part not in ("video", "mapping"):
            abort(404)
        with closing(db()) as conn:
            row = conn.execute(f"SELECT {part} FROM items WHERE name=? AND video IS NOT NULL AND updated>=?",
                               (name, time.time()-lifetime)).fetchone()
        if not row:
            abort(404)
        if part == "video":
            return send_file(io.BytesIO(row[0]), mimetype="video/mp4", conditional=True)
        frames = [r for line in row[0].splitlines() for r in [json.loads(line)] if r.get("kind") == "video-sample"]
        return jsonify([{"session": f["traceSession"], "time": f["time"]} for f in frames])

    @app.get("/review/api/report")
    def report():
        session = request.args.get("session", "")
        at = request.args.get("time", type=float)
        if not re.fullmatch(SESSION, session) or at is None or not math.isfinite(at) or not 0 <= at <= 281474976710:
            abort(400)
        start, end = max(0, at-15), at+15
        with closing(db()) as conn, tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            footage = root / "footage"
            footage.mkdir()
            rows = conn.execute("""SELECT * FROM items WHERE session=? AND hi>=? AND lo<=? AND updated>=?
                                    ORDER BY name""", (session, start, end, time.time()-lifetime))
            total = 0
            for row in rows:
                video = row["video"]
                total += len(row["mapping"]) + (len(video) if video else 0)
                if total > 96*MIB:
                    abort(413, "This interval exceeds the export size limit.")
                path = (footage if video else root) / row["name"]
                path.write_bytes(row["mapping"])
                if video:
                    path.with_suffix(".mp4").write_bytes(video)
            result = root / "review.zip"
            try:
                events, count = review.export_window(footage, result, session, start, end)
            except ValueError:
                return jsonify(error="No retained evidence for this moment yet. Retry after the capture segment closes."), 404
            response = send_file(io.BytesIO(result.read_bytes()), mimetype="application/zip", as_attachment=True,
                                 download_name=f"review-{session}-{at:.3f}s.zip")
            response.headers["X-Trace-Records"] = str(events)
            response.headers["X-Video-Segments"] = str(count)
            return response
    return app
