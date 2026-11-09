import base64
from contextlib import contextmanager
from io import BytesIO
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import unittest
import zipfile
from unittest.mock import patch

from werkzeug.serving import make_server

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "stream-review"))
from service import create_app
from upload import Mirror

SESSION = "20261108T193012345-abcd1234"
TOKEN = "upload-test-secret-1234"
AUTH = {"Authorization": "Basic " + base64.b64encode(b"reviewer:viewer-test-secret-1234").decode()}


class EndToEnd(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        (self.root/"footage").mkdir()
        with patch.dict(os.environ, REVIEW_DATA=str(self.root/"db"), REVIEW_PASSWORD="viewer-test-secret-1234",
                        REVIEW_UPLOAD_TOKEN=TOKEN):
            self.app = create_app()
        self.client = self.app.test_client()
        self.host = make_server("127.0.0.1", 0, self.app)
        thread = threading.Thread(target=self.host.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(self.host.server_close)
        self.addCleanup(self.host.shutdown)
        self.url = f"http://127.0.0.1:{self.host.server_port}/review"
        self.trace = self.root / f"scenic-{SESSION}-00001.jsonl"
        self.trace.write_text(json.dumps({"seq": 1, "time": 388, "reason": "doorway"})+"\n")
        self.map = self.root/"footage"/f"footage-{SESSION}-00001.jsonl"
        video = self.map.with_suffix(".mp4")
        video.write_bytes(b'\x00\x00\x00\x18ftypisom' + bytes(30))
        self.rows = [{"kind":"video-header","video":video.name,"fps":12},
                     {"kind":"video-sample","videoFrame":0,"traceSession":SESSION,"time":387.125,"seq":1},
                     {"kind":"video-end","complete":True,"frames":1}]
        self.map.write_text("\n".join(map(json.dumps,self.rows))+"\n")

    def test_private_and_separate_roles(self):
        for path in ["/review/", "/review/assets/player.js", "/review/api/segments", "/review/api/report"]:
            self.assertEqual(401,self.client.get(path).status_code)
            self.assertEqual(401,self.client.get(path,headers={"Authorization":"Bearer "+TOKEN}).status_code)
        self.assertEqual(401,self.client.get("/review/ingest/"+self.trace.name,headers=AUTH).status_code)
        with self.client.get("/review/",headers=AUTH) as page:
            self.assertIn(b"Report this moment",page.data)

    def test_upload_retry_trace_only_and_segment(self):
        mirror = Mirror(self.root,self.url,TOKEN)
        self.map.write_text("\n".join(map(json.dumps,self.rows[:-1]))+"\n")
        mirror.sync()
        def report(t=387.125):
            return self.client.get("/review/api/report",query_string={"session":SESSION,"time":t},headers=AUTH)
        self.assertEqual("0",report().headers["X-Video-Segments"])
        with self.trace.open("a") as f: f.write('{"seq":2,"time":404')
        mirror.sync()
        with self.trace.open("a") as f: f.write('}\n')
        self.map.write_text("\n".join(map(json.dumps,self.rows))+"\n")
        Mirror(self.root,self.url,TOKEN).sync()
        Mirror(self.root,self.url,TOKEN).sync()
        r = report()
        self.assertEqual(("1","1"),(r.headers["X-Trace-Records"], r.headers["X-Video-Segments"]))
        with zipfile.ZipFile(BytesIO(r.data)) as z:
            self.assertIn("footage-review.html", z.namelist())
            self.assertEqual(388,json.loads(z.read(f"scenic-{SESSION}-window.jsonl"))["time"])
        self.assertEqual(404,report(3000).status_code)
        self.assertEqual(1,len(self.client.get("/review/api/segments",headers=AUTH).json))
        media = "/review/api/media/" + self.map.name
        self.assertEqual(387.125,self.client.get(media+"/mapping",headers=AUTH).json[0]["time"])
        self.assertEqual(206,self.client.get(media+"/video",headers=dict(AUTH,Range="bytes=0-3")).status_code)


if __name__ == "__main__":
    unittest.main()
