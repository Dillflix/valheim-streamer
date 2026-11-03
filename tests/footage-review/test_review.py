import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("review", Path(__file__).resolve().parents[2] / "tools" / "review-footage.py")
review = importlib.util.module_from_spec(spec)
spec.loader.exec_module(review)


class ReviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.footage = self.root / "footage"
        self.footage.mkdir()
        self.header = {"kind": "video-header", "video": "footage-test-00001.mp4", "fps": 12}
        self.frame = {"kind": "video-sample", "videoFrame": 0, "videoSeconds": 0, "time": 1, "sourceTime": 1,
                      "seq": 10, "traceSession": "20260101T000000000-abcdef12", "diagnosticRepeat": False,
                      "presentation": "Live", "reason": "</script><script>bad()</script>"}
        self.end = {"kind": "video-end", "complete": True, "frames": 1}
        self.mapping = self.footage / "footage-test-00001.jsonl"
        self.write([self.header, self.frame, self.end])

    def tearDown(self):
        self.temp.cleanup()

    def write(self, rows):
        self.mapping.write_text("\n".join(json.dumps(row) for row in rows), encoding="utf-8")

    def test_complete_and_incomplete(self):
        self.assertTrue(review.read_segment(self.mapping)["complete"])
        self.write([self.header, self.frame])
        self.assertFalse(review.read_segment(self.mapping)["complete"])

    def test_mapping_gap_is_rejected(self):
        self.frame["videoFrame"] = 1
        self.write([self.header, self.frame, self.end])
        with self.assertRaises(ValueError):
            review.read_segment(self.mapping)

    def test_untrusted_filename_cannot_escape_directory(self):
        for value in ["../footage-evil.mp4", "..\\footage-evil.mp4", "https://example.test/footage-a.mp4"]:
            self.header["video"] = value
            self.write([self.header, self.frame, self.end])
            with self.assertRaises(ValueError):
                review.read_segment(self.mapping)

    def test_local_report_escapes_metadata(self):
        output = self.footage / "footage-review.html"
        review.make_report(self.footage, output)
        text = output.read_text(encoding="utf-8")
        self.assertNotIn("</script><script>bad()", text)
        self.assertIn("\\u003c/script", text)
        self.assertIn("diagnosticRepeat", text)
        self.assertIn("join('\\n\\n')", text)
        self.assertIn('"videoLink":"footage-test-00001.mp4"', text)

    def test_trace_join_is_session_and_sequence_specific(self):
        trace = self.root / "scenic-20260101T000000000-abcdef12-00001.jsonl"
        trace.write_text('\n'.join(json.dumps({"seq": seq}) for seq in [1, 9, 10, 11, 20]), encoding="utf-8")
        result = review.nearby_trace(self.root, [review.read_segment(self.mapping)])
        self.assertEqual([row["seq"] for row in next(iter(result.values()))], [9, 10, 11])


if __name__ == "__main__":
    unittest.main()
