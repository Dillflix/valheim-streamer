# Scenic evidence capture and replay — 0.8.29

This is an evidence build based directly on 0.8.28 (`da0c7ed99884c34dc418d95dae39778091001ea6`). It preserves observer protocol 3 and existing camera admission, survey lifetime, path selection and travel policies. A server update is not required. It does not claim to repair the reported Scenic blackout or complete the persistent regional observation architecture.

## Capture a failure

1. Replace the client payload as usual, then start the broadcaster. Capture starts automatically; repeated diagnostic commands are unnecessary.
2. Let a bad Scenic sequence happen. Continue briefly so the capture also includes recovery or travel.
3. Exit Valheim normally (or use `broadcast trace off` to finish the files while keeping the game running).
4. Zip the files from `BepInEx/diagnostics/ValheimBroadcaster/` in the broadcaster's own installation/profile. Share that ZIP and the matching `BepInEx/LogOutput.log`. With Sandboxie, use the files belonging to the sandboxed broadcaster, not the ordinary playing profile.

The startup log and `broadcast diagnostics` show the absolute capture directory, written/queued/dropped records and I/O errors. Optional controls: `broadcast trace status`, `broadcast trace off`, `broadcast trace on`. Off closes/drains the writer; on starts a new capture segment. Capture defaults on at the next application start.

No upload occurs. The recorder uses a background file writer, a queue of at most 2,048 records and eight rotating files of approximately 4 MiB each. Only `scenic-*.jsonl` in its dedicated directory are rotated. Exit bounds the writer join to one second; forced process termination or a stuck filesystem can lose queued records. Normal flush latency is at most approximately one second of writer operation. Leave capture running only until evidence is collected: old evidence rotates out. I/O failure disables capture and reports the error without disabling camera output.

Records include game-space coordinates, lens/orientation, anonymous numeric plan IDs, clock/frame/scene epoch, survey owner/centre/radius/age, retained/omitted counts, nearest omitted bounds, detail budget, survey duration, query stage, refresh action, unknown-query/candidate deadlines, picture availability and rejections. They deliberately do not include Steam IDs, player names, server addresses, configuration dumps, passwords, stream keys, game DLLs or saves.

## Replay on Windows, Linux or macOS

Download the separate `ValheimBroadcaster-Replay` artifact and extract it. It requires the .NET 8 runtime, not Valheim, Unity, a GPU or the game assemblies:

```text
dotnet ValheimBroadcaster.Replay.dll "path-to-extracted-capture-folder"
```

From a source checkout with the .NET 8 SDK:

```text
dotnet run --project tools/ValheimBroadcaster.Replay -- "path-to-extracted-capture-folder"
```

Use the replay artifact from the same build as the capture. Files are read chronologically by their generated filenames. Each rotated file includes its build/schema header. Replay evaluates the actual Core policy used by the plugin, comparing recorded refresh, coverage and deadline decisions. It reports missing/duplicate records, malformed/truncated tails and mismatches rather than treating missing observations as clear space. Exit 0 means at least one decision replayed without malformed records or divergence; it does not mean the camera behaved well. The report distinguishes coverage-radius loss, detail-radius loss, throttled refresh, expired unknown waits and expired overall search budgets.

The committed `tests/fixtures/scenic-evidence/competing-deadlines.jsonl` is a **synthetic** tool smoke fixture. It is not a recording from the user's game and establishes no runtime cause.

## What this can and cannot establish

An incomplete survey is missing evidence. With these records we can identify whether the requested pose moved outside detailed coverage, the retained set was truncated too close to the camera, a survey was repeatedly reset/recentred, or a search deadline interrupted pending observation. Physical overlap rejections are recorded separately. Picture samples distinguish unavailable output from a merely unfinished future route.

Replay covers the extracted **coverage and deadline policies**, not the entire planner or its Unity adapters. It does not reproduce physics, scene/network loading, foliage alpha pixels, native camera rendering or video. Recorded poses support investigation but there is no in-game pose-replay command in this version. The trace contains summary geometry and the limiting omitted bounds, not a full scene mesh.

Persistent scene-owned spatial observations, first-picture admission independent of a complete translation route, purposeful pan/tilt/short-route alternatives and cause-specific relocation remain follow-up implementation work. We need a runtime capture to reproduce the demonstrated failure before changing those policies again. The legacy `No connected readable movement` message now names a camera-route deadline, counts measured viewpoints/corridors and retains the preceding observation reason; it never meant that players or creatures had to move.
