import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

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

    def test_single_guide_identity_is_verified_through_arrival(self):
        with tempfile.TemporaryDirectory() as work, mock.patch.object(review.subprocess, "check_output") as probe:
            folder = Path(work)
            footage = folder / "footage"
            footage.mkdir()
            shot, reached, finish, fps = 91, 25, 28, 12
            base = dict(kind="journey", journey=3, journeyEngine="guide", destination="coast",
                        targetMetres=260, missionCause="None", departureState="Travelling",
                        outcome="Running", destinationX=260, destinationZ=0, arrivalRadius=4,
                        owned=True, journeyOwnsPicture=True, travelSection=True, section=shot)
            rows = [dict(kind="journey-selection", seq=1, time=0, selected=True,
                         requestedMetres=260, destination="coast"),
                    dict(base, seq=2, time=0, phase="departure-start", measuredMetres=0),
                    dict(base, seq=3, time=reached, phase="arrival-start", measuredMetres=260),
                    dict(base, seq=4, time=finish, phase="finish", outcome="Completed",
                         measuredMetres=260, arrivalShownSeconds=3)]
            trace = folder / "scenic-session-00001.jsonl"
            trace.write_text("\n".join(json.dumps(row) for row in rows))
            video = footage / "footage-01.mp4"
            video.touch()
            frames = [dict(kind="video-sample", traceSession="session", shot=shot,
                           videoFrame=i, time=i / fps, presentation="Live", usable=True,
                           cameraX=min(260, 260 * i / (reached * fps)), cameraY=6, cameraZ=0)
                      for i in range(finish * fps + 1)]
            probe.return_value = str(len(frames))

            def evaluate():
                mapping = [dict(video=video.name), *frames, dict(complete=True, frames=len(frames))]
                video.with_suffix(".jsonl").write_text("\n".join(json.dumps(row) for row in mapping))
                return review.summarize(folder, distance=260)

            self.assertEqual([], evaluate()["issues"])
            for frame in frames[50:60]:
                frame["shot"] = 42
            self.assertIn("Guide picture interrupted during travel", evaluate()["issues"])
            frames[-2]["cameraX"] -= 10
            self.assertIn("Arrival footage outside the original destination radius", evaluate()["issues"])
