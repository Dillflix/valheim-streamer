# Journey expedition development branch

The milestone is one fully recorded, independently reviewable trip of at least 2,000 measured metres to the place originally selected, with footage showing mixed terrain. A 2 km request can select a destination at a slightly different distance. Always report both distances. This branch is experimental.

## What changed

Journey selects an actual place from discovery and steers the camera toward it through the loaded world. The guide checks the space ahead, adjusts its heading and speed around obstructions, and can brake when loading or clearance limits its view ahead. The observer tracks the on-air camera through the same loading mechanism used by Player Follow. The travel shot can continue across regions under one shot identity. Inspect actual captured camera positions to measure travel and distinguish a moving picture from a stationary guide.

The new client and the existing protocol-3 server companion can be used together. Ordinary Scenic direction, the in-game recorder, and the rolling retention settings remain available.

## Capture on the Windows broadcaster

Install the experiment's client into the sandboxed broadcaster profile and keep its main-branch supervisor paused. In an authorized observer session, run the existing Valheim console commands:

```
broadcast journey off
broadcast mode scenic
broadcast capture on
broadcast journey distance 2000
broadcast journey speed 12
broadcast journey start
```

After arrival or failure, run `broadcast capture off` and allow the last MP4 to finalize. Share the normal `BepInEx/diagnostics/ValheimBroadcaster` folder (including `footage`) and `BepInEx/LogOutput.log`. This uses the same automatic recorder for all coverage modes. No external collector is required.

Automatic capture now retains up to 2 GiB each of footage (including its mappings) and trace data by default, on a rolling basis. The limits apply across sessions in the active profile. For longer retention, edit `Diagnostics.VideoRetentionMiB` and `Diagnostics.TraceRetentionMiB` in the broadcaster configuration and restart the client. Limits govern disk use, not guaranteed recording durations. Copy finished evidence before later recording fills the budget.

For offline analysis, `tools/journey-recording.py review <diagnostics-directory>` reads the original recording directly. The analyst can use `--ffprobe` with an absolute executable path, select `--session` and `--mission`, and optionally export a review MP4 with `--export` and `--ffmpeg`. For a different experiment, use `--distance` to match its requested scale.

## Acceptance

The report checks the original request and destination, actual measured travel, presented arrival, continuous travel in the recorded camera frames, trace completeness, video coverage and decoded frame counts. It reports motion gaps, mission states, generated biome and sampled land/water context. A reviewer must still watch the whole trip and evaluate terrain variation, travel speed, stationary periods, route continuity, arrival framing and any operator interference. A candidate that spends most of the recording stationary, teleports, or cuts to ordinary Scenic footage during the trip fails the milestone. Geometrically different routes in one biome can qualify as mixed terrain if the footage supports it. A successful report can only mark footage **ready for review**. Gameplay success requires that review.

Run an independent Scenic session with automatic Journey off to inspect shot variety and readability. Release validation also needs other lengths, higher speeds, obstacle layouts and repeated unattended runs. One successful recorded trip is the first milestone, not the general reliability claim.
