import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("journey_recording", Path(__file__).resolve().parents[2] / "tools" / "journey-recording.py")
review = importlib.util.module_from_spec(spec)
spec.loader.exec_module(review)


class JourneyEvidenceTests(unittest.TestCase):
    def test_completion_counter_without_footage_cannot_qualify(self):
        with tempfile.TemporaryDirectory() as work:
            folder = Path(work)
            (folder / "footage").mkdir()
            base = dict(kind="journey", journey=3, destination="same", targetMetres=2000,
                        measuredMetres=2000, arrivalShownSeconds=3, departureState="Travelling",
                        missionCause="None", outcome="Completed")
            rows = [dict(kind="journey-selection", seq=1, time=1, selected=True,
                         requestedMetres=2000, destination="same"),
                    dict(base, seq=2, time=2, phase="departure-start"),
                    dict(base, seq=3, time=1002, phase="finish")]
            (folder / "scenic-session-00001.jsonl").write_text(
                "\n".join(json.dumps(row) for row in rows), encoding="utf-8")
            result = review.summarize(folder)
            self.assertEqual("incomplete", result["result"])
            self.assertIn("Video coverage gap or missing footage", result["issues"])
            self.assertIn("Independent footage-camera distance differs from mission counter", result["issues"])


class ArchiveTests(unittest.TestCase):
    def test_rotated_file_survives_and_open_file_gets_its_final_contents(self):
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "diagnostics"
            archive = Path(temp) / "archive"
            source.mkdir()
            first = source / "scenic-s-00001.jsonl"
            second = source / "scenic-s-00002.jsonl"
            first.write_bytes(b"first")
            second.write_bytes(b"partial")

            def rotate(_):
                first.unlink()
                second.write_bytes(b"complete")
                raise KeyboardInterrupt()

            with patch.object(review.time, "sleep", side_effect=rotate):
                review.collect(source, archive, 1000)
            self.assertEqual(b"first", (archive / first.name).read_bytes())
            self.assertEqual(b"complete", (archive / second.name).read_bytes())
