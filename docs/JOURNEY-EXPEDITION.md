# Journey expedition development branch

The milestone is one fully recorded, independently reviewable trip of at least 2,000 measured metres to the place originally selected, with footage showing mixed terrain. A 2 km request can select a destination at a slightly different distance. Always report both distances. This branch is experimental.

## What changed

The mission records the positions and headings actually rendered. Local search may propose useful sideways and retreating sections into new space. Reversing through a passage and repeating it in the same direction are different decisions. This bounded local search still needs actual loaded observations, a swept camera path, a fresh onward seam and a valid ending. It cannot guarantee a route through every maze.

Speed control uses measured preparation time when a successor is being built and the queue is empty. It reduces speed while there is still braking room. If travel does stall, ordinary direction gets control during repair after a short hold. Repair starts at the last rendered travel position and can search multiple bearings there. A fallback shot in a different place cannot provide a shortcut. An unavailable repair position may still prevent completion.

## Capture on the Windows broadcaster

1. Install the branch client in the sandboxed broadcaster profile. Keep the main-branch supervisor paused for this experiment. Use a world with generated scenery and an authorized read-only observer.
2. On the host, start the collector **before requesting Journey**. Use a new archive directory outside the sandbox. The collector copies the existing rotating JSONL/MP4 streams and stops with an error at its storage limit. It does not delete old evidence. Use PowerShell on the host and supply the diagnostics directory used by your broadcaster client:

```powershell
$diag = Read-Host 'Full host path to the broadcaster BepInEx\diagnostics\ValheimBroadcaster directory'
$archive = Join-Path $env:USERPROFILE ("Videos\ValheimEvidence\Journey-" + (Get-Date -Format yyyyMMdd-HHmmss))
py -3 .\journey-recording.py collect "$diag" "$archive"
```

3. In the Valheim console, run these commands on separate lines. Allow a few seconds of ordinary footage after enabling capture and close the console after starting the trip.

```
broadcast journey off
broadcast mode scenic
broadcast capture on
broadcast journey distance 2000
broadcast journey speed 6
broadcast journey start
```

4. Leave the director unattended until arrival or a terminal failure. Preserve failed attempts too. Do not nudge, teleport, restart, or splice across attempts. After the outcome, run `broadcast capture off`, let the encoder finish writing the last MP4 and mapping, then press Ctrl+C in the collector.
5. In the same PowerShell window, review the archive. Supply the FFmpeg tools that accompany the broadcaster's FFmpeg installation:

```powershell
py -3 .\journey-recording.py review "$archive" --ffprobe 'C:\Tools\ffmpeg\bin\ffprobe.exe'
```

For a different experiment, pass the actual requested scale with `--distance <metres>`. The milestone uses the 2,000 m default. If multiple missions are present, the tool lists their session and mission IDs. Select one with `--session <id> --mission <number>`. To export a single review MP4 for a mechanically complete attempt, add `--export "$archive\Journey.mp4" --ffmpeg 'C:\Tools\ffmpeg\bin\ffmpeg.exe'`. Export retains the full first and last video segments. The diagnostic footage is a silent 640×360, 12 fps copy of the program picture. The normal RTMP receiver is the source for full-quality audio/video assessment.

## Acceptance

The report checks the original request and destination, actual measured travel, presented arrival, multiple sections, trace completeness, video coverage and decoded frame counts. It reports motion gaps, mission states, generated biome and sampled land/water context. A reviewer must still watch the whole trip and evaluate terrain variation, smooth movement, section handoffs, fallback cuts, arrival framing and any operator interference. Check successive travel sections before and after each cut. Geometrically different routes in one biome can qualify as mixed terrain if the footage supports it. A successful report can only mark footage **ready for review**. Gameplay success requires that review.

Run an independent Scenic session with automatic Journey off to inspect shot variety and readability. Release validation also needs other lengths, higher speeds, obstacle layouts and repeated unattended runs. One successful recorded trip is the first milestone, not the general reliability claim.
