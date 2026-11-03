#!/usr/bin/env python3
"""Build a local synchronized footage/diagnostic viewer; never uploads anything.

Usage: python tools/review-footage.py PATH/diagnostics/ValheimBroadcaster/footage
Add --verify to check each video frame count using ffprobe. Browser playback needs
H.264 support. The JSONL remains authoritative for source/capture/sample timing.
"""
import argparse
import json
from pathlib import Path
import subprocess


def read_segment(path, verify=False, ffprobe="ffprobe"):
    if path.stat().st_size > 3 * 1024 * 1024:
        raise ValueError(f"Oversize diagnostic mapping: {path.name}")
    records = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]
    if not records or records[0].get("kind") != "video-header":
        raise ValueError(f"Missing video header: {path.name}")
    header = records[0]
    video = header.get("video", "")
    if "/" in video or "\\" in video or Path(video).name != video or not video.startswith("footage-") or not video.endswith(".mp4"):
        raise ValueError("Video filename must be a local owned MP4")
    frames = [row for row in records if row.get("kind") == "video-sample"]
    if any(row.get("videoFrame") != index for index, row in enumerate(frames)):
        raise ValueError(f"Non-contiguous encoded frame mapping: {path.name}")
    end = records[-1] if records[-1].get("kind") == "video-end" else {}
    complete = end.get("complete") is True and end.get("frames") == len(frames)
    decoded = None
    if verify:
        result = subprocess.run([ffprobe, "-v", "error", "-count_frames", "-select_streams", "v:0", "-show_entries",
                                 "stream=nb_read_frames", "-of", "default=nw=1:nk=1", str(path.parent / video)],
                                check=True, capture_output=True, text=True, timeout=20)
        decoded = int(result.stdout.strip())
        if decoded != len(frames):
            complete = False
    return {"header": header, "frames": frames, "complete": complete, "decoded": decoded, "map": path.name}


def nearby_trace(directory, segments):
    """Keep bounded diagnostic neighbours of frame samples, not arbitrary logs."""
    wanted = {}
    for segment in segments:
        for frame in segment["frames"]:
            session, seq = frame.get("traceSession"), frame.get("seq")
            if isinstance(session, str) and isinstance(seq, int):
                wanted.setdefault(session, set()).update(range(max(1, seq - 3), seq + 4))
    result = {}
    for session, seqs in wanted.items():
        if not session or any(c not in "0123456789T-abcdef" for c in session):
            continue
        found = []
        for path in sorted(directory.glob(f"scenic-{session}-*.jsonl")):
            if path.stat().st_size > 5 * 1024 * 1024:
                continue
            with path.open(encoding="utf-8") as source:
                for line in source:
                    try:
                        row = json.loads(line)
                    except (ValueError, TypeError):
                        continue
                    if row.get("seq") in seqs:
                        found.append(row)
        result[session] = found
    return result


def make_report(directory, output, verify=False, ffprobe="ffprobe"):
    segments = [read_segment(path, verify, ffprobe) for path in sorted(directory.glob("footage-*.jsonl"))]
    if not segments:
        raise ValueError("No footage sidecars found in this directory")
    traces = nearby_trace(directory.parent, segments)
    # Resolve local links relative to the output report; no remote content or scripts.
    import os
    for segment in segments:
        segment["videoLink"] = os.path.relpath(directory / segment["header"]["video"], output.parent).replace(os.sep, "/")
        segment["mapLink"] = os.path.relpath(directory / segment["map"], output.parent).replace(os.sep, "/")
    payload = json.dumps({"segments": segments, "traces": traces}, separators=(",", ":")).replace("<", "\\u003c").replace("&", "\\u0026")
    page = r'''<!doctype html><html lang="en"><meta charset="utf-8"><title>Broadcast footage review</title>
<style>body{font:15px system-ui;background:#10151c;color:#e3eaf0;max-width:1300px;margin:24px auto;padding:0 18px}video{width:100%;max-height:65vh;background:black}select,button{font:inherit;padding:8px;background:#263444;color:white;border:1px solid #567}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#1b2530;padding:14px}a{color:#8bd5ff}.note{color:#b7c8d5}.grid{display:grid;grid-template-columns:1fr 1fr;gap:16px}@media(max-width:750px){.grid{display:block}}</style>
<h1>Broadcast footage review</h1><p class="note">Local diagnostic video: 640×360 at 12 fps, no audio. Short capture gaps repeat the last sample and are labelled <b>diagnosticRepeat</b>; these are not proof of an on-air hold. Larger gaps and compositor boundaries start new segments. Trace neighbours are a convenience; retain the original trace files for full replay.</p>
<select id="segments"></select> <span id="state"></span><p><a id="download">Open video</a> · <a id="mapping">Frame mapping</a></p>
<video id="video" controls preload="metadata"></video><p id="clock"></p><button id="prev">Previous sample</button> <button id="next">Next sample</button>
<div class="grid"><div><h2>Recorded source and capture</h2><pre id="frame"></pre></div><div><h2>Matching trace neighbours</h2><pre id="trace"></pre></div></div>
<script type="application/json" id="data">PAYLOAD</script><script>
const data=JSON.parse(document.getElementById('data').textContent),$=id=>document.getElementById(id);let selected=0,index=0;
function select(n){selected=n;const s=data.segments[n];$('video').src=s.videoLink;$('download').href=s.videoLink;$('mapping').href=s.mapLink;$('state').textContent=(s.complete?'Encoder-confirmed complete':'INCOMPLETE — inspect mapping/video')+(s.decoded===null?' · decoded frame count not independently checked':' · '+s.decoded+' frames decoded');index=0;show();}
function show(){const s=data.segments[selected],rows=s.frames;if(!rows.length)return;index=Math.max(0,Math.min(rows.length-1,Math.floor($('video').currentTime*s.header.fps+0.00001)));const r=rows[index];$('clock').textContent='Video '+Number(r.videoSeconds).toFixed(3)+'s · capture '+Number(r.time).toFixed(3)+'s · source '+Number(r.sourceTime).toFixed(3)+'s · '+r.presentation+(r.diagnosticRepeat?' · DIAGNOSTIC REPEAT':'');$('frame').textContent=JSON.stringify(r,null,2);const trace=(data.traces[r.traceSession]||[]).filter(x=>Math.abs(x.seq-r.seq)<=3);$('trace').textContent=trace.length?trace.map(x=>JSON.stringify(x,null,2)).join('\n\n'):'Matching trace was not retained/provided. Frame metadata remains available.';}
data.segments.forEach((s,i)=>{let option=document.createElement('option');option.value=i;option.textContent=s.header.video+(s.complete?'':' [incomplete]');$('segments').appendChild(option)});$('segments').onchange=e=>select(Number(e.target.value));$('video').ontimeupdate=show;function step(d){$('video').pause();$('video').currentTime=Math.max(0,(index+d)/data.segments[selected].header.fps+0.001);show()}$('prev').onclick=()=>step(-1);$('next').onclick=()=>step(1);select(0);
</script></html>'''.replace("PAYLOAD", payload)
    output.write_text(page, encoding="utf-8")
    return segments


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--verify", action="store_true")
    parser.add_argument("--ffprobe", default="ffprobe")
    args = parser.parse_args()
    directory = args.directory.resolve()
    output = (args.output or directory / "footage-review.html").resolve()
    try:
        segments = make_report(directory, output, args.verify, args.ffprobe)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        parser.exit(1, str(error) + "\n")
    print(f"Created {output}: {len(segments)} segments, {sum(len(s['frames']) for s in segments)} mapped frames; "
          f"{sum(not s['complete'] for s in segments)} incomplete segments. No upload performed.")


if __name__ == "__main__":
    main()
