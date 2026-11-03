# Scenic evidence capture and replay — 0.8.30

0.8.30 is built directly on 0.8.29 (`24552cbef081c867d43164d64a207784ec748018`). Observer protocol 3 is unchanged; a server update is not required. The recorder remains automatic. This build repairs a reproduced observation-scheduling defect; it does not claim complete Scenic runtime acceptance or a finished general spatial planner.

## What the three supplied captures established

| Capture session | Decisions replayed without divergence | Incomplete foliage observations | Candidate deadline expirations |
| --- | ---: | ---: | ---: |
| 01:33 session, supplied segment 4 | 1,876 | 341 | 3 |
| 01:47 session, supplied segment 1 | 2,403 | 487 | 5 |
| 01:51 session, all seven segments | 45,125 | 6,278 | 24 |

Times above identify UTC-stamped capture filenames. All supplied files parsed with no internal sequence gaps or writer-reported drops. The first two are partial sessions; only the seven-segment set supports analysis of its complete retained sequence.

Every incomplete result in these files coincided with the old refresh throttle. A camera request could move beyond the detailed neighborhood while a 0.5-second timer forbade recentering. Urgent candidate searches expired after 1.5 seconds, often after inspecting just a few seeds or canopy elevations. In the seven-segment set, 403 queries also exceeded the retained radius around the *previous* centre; even its smallest newly measured retained radius was 52.79 m. These are not examples of omitted geometry within the six-metre foreground at a freshly centred query.

The long capture also contains real foliage/collision/framing rejections and five waits for actual scene acquisition. It does not establish that every proposed route would become readable after the timing repair. One ordinary trip began with usable footage, followed by three recovery-driven relocations. The picture samples show approximately 64 seconds from loss of usable Scenic output to its return. The old ten-second missing-picture counter reset at each region, so its maximum alone concealed the longer sequence.

## What changed

- `ScenicFoliageScene` owns a bounded renderer catalogue and mesh-refinement cache across candidates. Discovery has one scene-level refresh clock. Region, world/scene epoch and stop invalidate the appropriate observations.
- Per-camera queries project the existing catalogue into a 16 m detailed neighborhood immediately. Candidate changes and canopy lifts no longer trigger a separate half-second discovery wait. Far renderers cannot consume the local foreground budget. Catalogue/local overflow still reduces certified coverage; unknown space never becomes clear by default.
- Mesh reads are bounded across all owners per frame. The catalogue retains at most 8,192 renderers inside a 220 m regional envelope; local projection retains at most 512 renderers and 1,024 detail cells. Geometry remains conservative bounds, not texture-alpha evidence.
- Candidate resets preserve discovery; preflight and on-air projections remain independent. A legacy focus-centred refresh no longer moves the scenic camera's neighborhood. Explicit physical overlap is rejected as obstruction instead of waiting for a survey.
- Geometry availability no longer becomes false merely because picture recovery expired. Travel reasons distinguish footage dwell, missing scene data and exhausted picture recovery. The existing deadlines and safety/visibility thresholds are unchanged.
- Diagnostics now distinguish `preflight`, `live`, `spatial` and `fallback-live` owners. **0.8.29 labelled both preflight and actual live queries `live`; those older rows alone cannot identify an on-air visibility failure.**

Sixteen reduced timing/distance samples from the three sessions reproduce the old failure and exercise immediate local reprojection. No uploaded logs, world coordinates, or full user captures are committed. Local/CI tests verify decisions and builds, not rendered cinematography. Please capture another sequence on 0.8.30 to verify actual shot availability and cost.

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

The 0.8.30 replay artifact supports both 0.8.29 legacy refresh decisions and 0.8.30 local-projection decisions. Use the corresponding policy-aware replay build for later versions. Files are read chronologically by their generated filenames. Each rotated file includes its build/schema header. Replay evaluates the actual Core policy used by the plugin, comparing recorded refresh, coverage and deadline decisions. It reports missing/duplicate records, malformed/truncated tails and mismatches rather than treating missing observations as clear space. Exit 0 means at least one decision replayed without malformed records or divergence; it does not mean the camera behaved well. The report distinguishes coverage-radius loss, detail-radius loss, throttled refresh, expired unknown waits and expired overall search budgets.

The committed `tests/fixtures/scenic-evidence/competing-deadlines.jsonl` is a **synthetic** tool smoke fixture. It is not a recording from the user's game and establishes no runtime cause.

## What this can and cannot establish

An incomplete survey is missing evidence. With these records we can identify whether the requested pose moved outside detailed coverage, the retained set was truncated too close to the camera, a survey was repeatedly reset/recentred, or a search deadline interrupted pending observation. Physical overlap rejections are recorded separately. Picture samples distinguish unavailable output from a merely unfinished future route.

Replay covers the extracted **coverage and deadline policies**, not the entire planner or its Unity adapters. It does not reproduce physics, scene/network loading, foliage alpha pixels, native camera rendering or video. Recorded poses support investigation but there is no in-game pose-replay command in this version. The trace contains summary geometry and the limiting omitted bounds, not a full scene mesh.

Scene-owned foliage discovery is now implemented. Independent first-picture admission, purposeful pan/tilt/short-route alternatives, full planner/Unity replay and further cause-specific recovery remain separate follow-up work; they are not claimed complete by this repair.
