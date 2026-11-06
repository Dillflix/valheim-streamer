#!/usr/bin/env python3
"""Review evidence recorded by the in-game broadcaster."""
import argparse
import itertools
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def records(path):
    with path.open(encoding="utf-8-sig") as source:
        for number, line in enumerate(source, 1):
            try:
                yield json.loads(line)
            except ValueError as exc:
                raise ValueError(f"{path.name}:{number}: incomplete or invalid JSON") from exc


def scan(directory):
    missions = {}
    sessions = {}
    for path in sorted(directory.glob("scenic-*.jsonl")):
        session = path.stem[7:].rsplit("-", 1)[0]
        info = sessions.setdefault(session, {"travel": [], "selection": [], "gaps": []})
        for row in records(path):
            kind = row.get("kind")
            seq = row.get("seq")
            if isinstance(seq, int):
                previous = info.get("seq")
                if previous is not None and seq != previous + 1:
                    info["gaps"].append((previous, seq))
                info["seq"] = seq
            if kind == "journey" and row.get("journey", 0) > 0:
                missions.setdefault((session, row["journey"]), []).append(row)
            elif kind in ("travel", "journey-selection"):
                info["travel" if kind == "travel" else "selection"].append(row)
            elif kind == "lost":
                info["gaps"].append((info.get("seq", 0), info.get("seq", 0) + 1))
    return missions, sessions


def summarize(directory, session=None, mission=None, export=None, probe="ffprobe", encoder="ffmpeg", distance=2000):
    missions, sessions = scan(directory)
    keys = [k for k in missions if (session is None or session == k[0]) and (mission is None or mission == k[1])]
    if len(keys) != 1:
        raise ValueError(f"Choose one --session and --mission from {list(missions)}")
    session, mission = keys[0]
    rows = missions[keys[0]]
    first, last = rows[0], rows[-1]
    start, end = first["time"], last["time"]
    issues = []
    if first["phase"] != "departure-start":
        issues.append("Departure trace missing")
    if last["phase"] != "finish" or last["outcome"] != "Completed" or last.get("arrivalShownSeconds", 0) < 3:
        issues.append("Completed arrival footage not established")
    destination = first["destination"]
    if any(r["destination"] != destination for r in rows):
        issues.append("Destination changed")
    info = sessions[session]
    selection = next((r for r in reversed(info["selection"]) if r["seq"] < first["seq"] and r.get("selected")), {})
    if selection.get("destination") != destination or selection.get("requestedMetres") != distance:
        issues.append(f"Selection for a {distance:,} m request missing")
    if last["measuredMetres"] < distance:
        issues.append(f"Less than {distance:,} m of measured camera travel")
    if any(a < last["seq"] and b > first["seq"] for a, b in info["gaps"]):
        issues.append("Missing trace records in mission interval")
    if any(start <= r["time"] <= end for r in info["travel"]):
        issues.append("Observer relocation recorded during mission")
    route_ids = {r["section"] for r in rows if r.get("owned") and r.get("journeyOwnsPicture") and r.get("travelSection", False) and r.get("section", 0) > 0}
    if len(route_ids) < 2:
        issues.append("Fewer than two owned travel sections recorded")

    terrain = {r.get("biomeAtRenderedPosition", "unknown") for r in rows} - {"unknown"}
    surfaces = {r.get("surfaceAtRenderedPosition", "unknown") for r in rows} - {"unknown"}
    files, samples = [], []
    for path in sorted((directory / "footage").glob("footage-*.jsonl")):
        mapping = list(records(path))
        frames = [r for r in mapping if r.get("kind") == "video-sample"]
        relevant = [r for r in frames if r.get("traceSession") == session and start-.1 <= r["time"] <= end+.1]
        if not relevant:
            continue
        video = path.with_suffix(".mp4")
        if (not mapping or mapping[0].get("video") != video.name or not video.is_file()
                or not frames or mapping[-1].get("complete") is not True or mapping[-1].get("frames") != len(frames)
                or any(r["videoFrame"] != i for i, r in enumerate(frames))):
            issues.append(f"Incomplete segment: {path.name}")
        else:
            count = subprocess.check_output([probe, "-v", "error", "-count_frames", "-select_streams", "v:0",
                "-show_entries", "stream=nb_read_frames", "-of", "default=nw=1:nk=1", str(video)],
                text=True, timeout=120).strip()
            if count != str(len(frames)):
                issues.append(f"Decoded frame count mismatch: {video.name}")
        files.append((relevant[0]["time"], video))
        samples.extend(relevant)
    samples.sort(key=lambda r: r["time"])
    clock = [start] + [r["time"] for r in samples if start <= r["time"] <= end] + [end]
    maximum_gap = max((b-a for a, b in zip(clock, clock[1:])), default=0)
    if not samples or maximum_gap > .25:
        issues.append("Video coverage gap or missing footage")
    # Keep stationary live frames visible in the report. Presentation=Live
    # alone does not establish that the camera moved.
    last_pose = None
    previous_live = None
    sampled_metres = 0
    stationary_anchor = None
    longest_stationary = 0
    stops = []
    jumps = []
    for frame in samples:
        if frame.get("shot") not in route_ids or frame.get("presentation") != "Live" or not frame.get("usable"):
            previous_live = stationary_anchor = None
            continue
        pos = tuple(frame["camera" + axis] for axis in "XYZ")
        at = frame["time"]
        if previous_live and previous_live[2] == frame["shot"] and at-previous_live[0] <= .25:
            sampled_metres += math.dist(pos, previous_live[1])
        previous_live = at, pos, frame["shot"]
        if stationary_anchor is None or stationary_anchor[2] != frame["shot"] or math.dist(pos, stationary_anchor[1]) > .25:
            stationary_anchor = previous_live
        longest_stationary = max(longest_stationary, at-stationary_anchor[0])
        if last_pose:
            old_at, old_pos, old_id = last_pose
            distance = math.dist(pos, old_pos)
            if distance > (2 if old_id != frame["shot"] else 2 + 12 * (at-old_at)):
                jumps.append({"at": at, "metres": round(distance, 2)})
        last_pose = at, pos, frame["shot"]
    for r in rows:
        if r.get("noProgressSeconds", 0) > 0:
            stops.append(r["noProgressSeconds"])
    if abs(sampled_metres - last["measuredMetres"]) > max(2, last["measuredMetres"] * .02):
        issues.append("Independent footage-camera distance differs from mission counter")
    live_ids = {r["shot"] for r in samples if r["shot"] in route_ids and r["presentation"] == "Live" and r["usable"]}
    if len(live_ids) < 2:
        issues.append("Footage does not show two live travel sections")
    arrival_ids = {r.get("arrivalEnding") for r in rows if r.get("arrivalEnding", 0) > 0}
    arrival_frames = sum(r["shot"] in arrival_ids and r["presentation"] == "Live" and r["usable"] for r in samples)
    if first.get("arrivalRadius", 0) <= 0 or "destinationX" not in first or "destinationZ" not in first:
        issues.append("Original arrival coordinates missing")
    else:
        destination_xz = (first["destinationX"], first["destinationZ"])
        if any(math.dist((r["cameraX"], r["cameraZ"]), destination_xz) > first["arrivalRadius"]
               for r in samples if r.get("shot") in arrival_ids and r["presentation"] == "Live" and r["usable"]):
            issues.append("Arrival footage outside the original destination radius")
    if arrival_frames < 34:
        issues.append("Missing mapped footage of the arrival ending")
    if jumps:
        issues.append("Discontinuous travel poses require review")
    if export:
        if issues:
            raise ValueError("Cannot export milestone evidence: " + ", ".join(issues))
        # Include the first and last segments in full for lead-in and aftermath.
        # File names originate from the fixed capture format, never from shell input.
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=directory / "footage",
                                         suffix=".ffconcat", delete=False) as tmp:
            for _, video in sorted(files):
                if not all(ch.isalnum() or ch in "-." for ch in video.name):
                    raise ValueError("Unexpected capture filename")
                tmp.write("file '" + video.name + "'\n")
        try:
            subprocess.run([encoder, "-nostdin", "-n", "-v", "error", "-f", "concat", "-safe", "1",
                "-i", tmp.name, "-c", "copy", str(export)], check=True)
        finally:
            Path(tmp.name).unlink(missing_ok=True)

    cuts = []
    for (shot, presentation), group in itertools.groupby(samples, lambda r: (r["shot"], r["presentation"])):
        chunk = list(group)
        movement = sum(math.dist(tuple(a["camera" + ax] for ax in "XYZ"), tuple(b["camera" + ax] for ax in "XYZ"))
                       for a, b in zip(chunk, chunk[1:]))
        cuts.append({"at": round(chunk[0]["time"]-start, 2), "shot": shot, "presentation": presentation,
                     "duration": round(chunk[-1]["time"]-chunk[0]["time"], 2), "cameraMetres": round(movement, 2)})
    prepared = {}
    handoffs = []
    for r in rows:
        if r["phase"] == "section-prepared":
            prepared[r["preparedSection"]] = r
        if r["phase"] == "section-admitted":
            before = prepared.get(r.get("section"))
            handoffs.append({"at": round(r["time"]-start, 2), "section": r.get("section"),
                             "secondsBetweenPreparationAndAdmission": round(r["time"]-before["time"], 2) if before else None,
                             "totalVerifiedMetresAtPreparation": before["freshRunwayMetres"] if before else None})
    timeline = []
    prior = None
    for row in rows:
        dt = row["time"]-prior["time"] if prior else 0
        if prior and dt < 1 and row["phase"] not in ("finish", "section-admitted", "section-prepared"):
            continue
        timeline.append({"at": round(row["time"]-start, 2), "metres": round(row["measuredMetres"], 2),
                         "measuredSpeed": round((row["measuredMetres"]-prior["measuredMetres"])/dt, 2) if dt > 0 else None,
                         "commandedSpeed": row.get("effectiveCruise"), "section": row.get("section"),
                         "verifiedMetresAhead": row.get("freshRunwayMetres"), "successorReady": row.get("successorReady"),
                         "state": row["departureState"], "cause": row["missionCause"]})
        prior = row
    transitions = [{"at": round(group[0]["time"] - start, 1), "state": key[0], "cause": key[1],
                    "seconds": round(group[-1]["time"] - group[0]["time"], 1)}
                   for key, items in itertools.groupby(rows, lambda r: (r["departureState"], r["missionCause"]))
                   if (group := list(items))]
    return {"session": session, "mission": mission, "destination": destination, "targetMetres": last["targetMetres"],
            "measuredMetres": last["measuredMetres"], "durationSeconds": end-start, "arrivalSeconds": last["arrivalShownSeconds"],
            "travelSections": sorted(route_ids), "footageCameraMetres": round(sampled_metres, 2),
            "longestNearlyStationaryTravelShotSeconds": round(longest_stationary, 2), "videoSegments": len(files), "videoSamples": len(samples),
            "longestVideoGap": maximum_gap, "longestNoProgress": max(stops, default=0),
            "biomes": sorted(terrain), "surfaces": sorted(surfaces), "travelPoseJumps": jumps,
            "progressTimeline": timeline, "stateIntervals": transitions, "footageCuts": cuts, "handoffs": handoffs, "issues": issues,
            "result": "incomplete" if issues else "ready for footage review",
            "review": "Inspect the actual terrain changes, controls, camera continuity, cuts, speed and original-destination arrival."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    check = sub.add_parser("review")
    check.add_argument("archive", type=Path)
    check.add_argument("--distance", type=int, default=2000)
    check.add_argument("--session")
    check.add_argument("--mission", type=int)
    check.add_argument("--export", type=Path)
    check.add_argument("--ffprobe", default="ffprobe")
    check.add_argument("--ffmpeg", default="ffmpeg")
    args = parser.parse_args()
    try:
        if not 256 <= args.distance <= 5000:
            raise ValueError("Expected request distance must be between 256 and 5,000 metres")
        result = summarize(args.archive, args.session, args.mission, args.export, args.ffprobe, args.ffmpeg, args.distance)
        print(json.dumps(result, indent=2))
        return int(bool(result["issues"]))
    except (OSError, ValueError, subprocess.SubprocessError) as exc:
        parser.exit(2, f"{exc}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
