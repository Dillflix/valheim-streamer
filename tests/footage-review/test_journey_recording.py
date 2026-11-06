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
