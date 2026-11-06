# Journey expedition development branch

The milestone is one fully recorded, independently reviewable trip of at least 2,000 measured metres to the place originally selected, with footage showing mixed terrain. A 2 km request can select a destination at a slightly different distance. Always report both distances. This branch is experimental.

## What changed

The mission records the positions and headings actually rendered. Local search may propose useful sideways and retreating sections into new space. Reversing through a passage and repeating it in the same direction are different decisions. This bounded local search still needs actual loaded observations, a swept camera path, a fresh onward seam and a valid ending. It cannot guarantee a route through every maze.

Speed control uses measured preparation time when a successor is being built and the queue is empty. It reduces speed while there is still braking room. If travel does stall, ordinary direction gets control during repair after a short hold. Repair starts at the last rendered travel position and can search multiple bearings there. A fallback shot in a different place cannot provide a shortcut. An unavailable repair position may still prevent completion.

## Capture on the Windows broadcaster

Install the experiment's client into the sandboxed broadcaster profile and keep its main-branch supervisor paused. In an authorized observer session, run the existing Valheim console commands:

```
broadcast journey off
broadcast mode scenic
broadcast capture on
broadcast journey distance 2000
broadcast journey speed 6
broadcast journey start
```

After arrival or failure, run `broadcast capture off` and allow the last MP4 to finalize. Share the normal `BepInEx/diagnostics/ValheimBroadcaster` folder (including `footage`) and `BepInEx/LogOutput.log`. This uses the same automatic recorder for all coverage modes. No external collector is required.

Automatic capture now retains up to 2 GiB each of footage (including its mappings) and trace data by default, on a rolling basis. The limits apply across sessions in the active profile. For longer retention, edit `Diagnostics.VideoRetentionMiB` and `Diagnostics.TraceRetentionMiB` in the broadcaster configuration and restart the client. Limits govern disk use, not guaranteed recording durations. Copy finished evidence before later recording fills the budget.

For offline analysis, `tools/journey-recording.py review <diagnostics-directory>` reads the original recording directly. The analyst can use `--ffprobe` with an absolute executable path, select `--session` and `--mission`, and optionally export a review MP4 with `--export` and `--ffmpeg`. For a different experiment, use `--distance` to match its requested scale.

## Acceptance

The report checks the original request and destination, actual measured travel, presented arrival, multiple sections, trace completeness, video coverage and decoded frame counts. It reports motion gaps, mission states, generated biome and sampled land/water context. A reviewer must still watch the whole trip and evaluate terrain variation, smooth movement, section handoffs, fallback cuts, arrival framing and any operator interference. Check successive travel sections before and after each cut. Geometrically different routes in one biome can qualify as mixed terrain if the footage supports it. A successful report can only mark footage **ready for review**. Gameplay success requires that review.

Run an independent Scenic session with automatic Journey off to inspect shot variety and readability. Release validation also needs other lengths, higher speeds, obstacle layouts and repeated unattended runs. One successful recorded trip is the first milestone, not the general reliability claim.
