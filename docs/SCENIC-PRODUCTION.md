# Scenic journeys, camera fixtures and timelapse — 0.8.46

Version 0.8.46 corrects sparse-catalogue selection and independent departure scheduling; see [evidence and current behavior](SCENIC-0.8.46-PLANNING.md).

Version 0.8.42 replaced the projected Journey endpoint and inherited view offset with a selected catalogue destination, an observed itinerary and independently directed forward gaze. Each admitted section includes a prepared six-second ending; ordinary Scenic preparation runs concurrently. Actual captured-frame identity, position, progress and time now drive travel and arrival evidence. See [the recorded failure, new contract and limits](SCENIC-0.8.42-JOURNEYS.md).

The ordinary-shot pacing, context-qualified site sequences, fixture schema 3 and matching native-arrival preparation introduced in 0.8.41 remain in place. See [the ordinary Scenic quality changes](SCENIC-0.8.41-QUALITY.md).

Camera fixtures, operator previews, render-pose auditing, interval recordings and empty-server environment controls remain available. Autonomous live timelapse shots added in 0.8.38 are documented in [LIVE-TIMELAPSE.md](LIVE-TIMELAPSE.md).

## Install and compatibility

- Replace the **three client DLLs and included ThirdParty notices together**, preserving the existing configuration.
- Journey, fixture, preview, pose-audit and recorded-timelapse features work with an existing compatible **protocol-3** companion. **No server update is required for 0.8.42.**
- Install the **0.8.37 server/core pair** for the new optional empty-server `Environment1` permission. Earlier companions leave remote environment overrides inactive. Ordinary players need no mod update.
- `Observer.CinematicEnvironmentEnabled=true` permits an already-authorized observer to request this optional capability. Client environment overrides default off.
- Admission, pre-admission opt-in and observer isolation remain protocol 3. No new game, Unity, BepInEx or Jötunn binaries belong in either payload.

Compilation and automated tests do not establish in-game camera quality, graphics-backend behavior or multiplayer join timing. See the validation section below.

## Continuous Scenic journeys

A journey selects a **known scenic destination near a requested itinerary scale**, default **1,000 m**, and cruises at an independent **6 m/s**. The selected route may be shorter or longer than the requested scale. `Camera.ShotMotion` still controls decorative movement in ordinary shots; it does not scale admitted Journey cruise. A distance request cannot manufacture a destination or certify a clear path through unloaded terrain.

The destination catalogue supplies landscape/coast regions and available structure/site observations. A bounded graph joins known observations into an itinerary. Selection considers route length, direction, destination freshness, retry deadlines and recent destination families; structures do not automatically outrank natural or coastal destinations. If no connected known destination fits, ordinary scenery continues. Catalogue edges are navigation intentions only: **each local camera section still requires loaded terrain/water, corridor, foliage and native readability validation**.

Execution sections remain locally bounded, generally **32–64 m** with speed. Up to two successors can prepare ahead while the protected loading anchor supports the accepted route. The anchor may lead up to 24 m only where its carrier geometry is already loaded; this does not permit a camera jump or teleport through an unknown corridor. Native distance-based camera movement follows the itinerary, while an independently evaluated aim path looks forward through turns and can develop toward an observed arrival subject. Continuations retain the exact accepted seam and lens. A section boundary is not an editorial cut.

**Departure preparation does not own the picture.** Journeys require readable Scenic coverage and a current catalogue. Their own departure prepares while ordinary movement and recovery continue; it does not require an ordinary prepared successor or the old shot's endpoint. A connected metadata guide is preferred when available; sparse catalogues can supply a bearing to a real known destination, with every local corridor still independently validated. Admission requires the first section and its independently validated ending. Departure deliberately reframes with a validated cut; it does not inherit a backward-facing ordinary composition. Changed or failed preparation leaves ordinary coverage in charge.

**Every admitted section has a prepared ending.** When a successor is not ready before the braking point, the director commits to that ending. A late successor cannot cancel braking and create a speed surge. The six-second ending is validated with the section before admission, rather than searched for after motion runs out. Ordinary survey and replacement planning continue near the live carrier during Journey playback. Scene invalidation and player takeover retain priority; a prepared ending is not permission to display an invalid picture.

Completion requires measured presented travel and progress along the itinerary, proximity to the selected destination, and readable coverage of an observed arrival subject. Reaching a coordinate or spending a distance budget alone is insufficient. Held, missing, stale, duplicate or mismatched captured frames earn no movement. Actual capture time and progress are consumed once from the current or preceding completed render; a missing sample breaks continuity without advancing a fabricated clock. Diagnostics retain selected destination, route progress, measured distance, displacement, achieved speed, shown time, arrival time, prepared distance ahead and the actual outcome.

Automatic journeys have a three-minute cooldown after an admitted attempt ends. Ordinary region dwell and wildlife timers do not interrupt useful admitted Journey coverage. A player, known obstruction, unavailable geometry or bounded runtime can still end an attempt early. A partial attempt remains explicitly incomplete; no full distant-world route clearance or guaranteed kilometre flight is claimed.

```text
broadcast journey distance 1000
broadcast journey speed 6
broadcast journey start
broadcast journey status
broadcast journey stop
broadcast journey off
broadcast journey on
```

`start` requests a journey from protected-observer Scenic coverage for up to 90 seconds of unheld operation; it does not cut immediately or bypass validation. An unsuccessful departure can leave the request pending for another validated departure within that bound. `stop` ends the current request and resumes ordinary direction; `off` also disables future automatic journeys. Distance and speed changes apply to the **next** journey. For compatibility, `broadcast journey duration <seconds>` sets desired itinerary scale to seconds multiplied by the current cruise speed, provided the result is 256–5,000 m. It no longer establishes a hard shot-duration goal.

| Configuration | Default | Purpose |
| --- | --- | --- |
| `Idle.ScenicJourneys` | `true` | Offer automatic journeys |
| `Idle.JourneyDistanceMetres` | `1000` | Desired known-destination itinerary scale, 256–5,000 m |
| `Idle.JourneyCruiseMetresPerSecond` | `6` | Independent cruise speed, 2–12 m/s |

**Configuration migration:** preserve the existing configuration file. The old `Idle.ScenicJourneySeconds` and `Idle.ScenicJourneySpeed` entries are no longer read. New keys give existing installations the new defaults instead of silently retaining the previous 1 m/s speed. An old `Camera.ShotMotion` value does not scale admitted journey cruising. Ordinary coverage keeps its existing movement setting while departure is pending; independent Journey cruise is not multiplied by `ShotMotion`. `Camera.DynamicShots=false` still disables moving journeys.

## Correct motion and editorial clocks

Ordinary Scenic plans now share one role-aware timing policy. During preparation, up to 257 paired camera/aim samples describe the actual path. A monotone time-to-distance profile spends more time on close-subject turns while allowing low-change stretches to progress. The accepted geometry and fixed lens remain unchanged. NPCs, rooms and openings use restrained rates; difficult motion can legitimately require a longer shot. Explicit fixture durations, timelapses and constant-speed journeys retain their own timing contract.

Soft requests for improved picture composition do not pause otherwise valid movement or allow an unranked static picture to displace it. Physical blockage and unavailable/unreadable output retain their recovery behavior. Ordinary region dwell can wait up to 15 unheld seconds for a real edit boundary; destination preparation can overlap native travel after actual loading readiness, without changing native completion or final picture validation.

Previously, `ShotMotion=0.5` could end an 18.18-second planned route after 18.18 seconds while only half the route had been traversed. Movement now completes from actual presented route progress. A deliberately stationary shot has a separate dwell outcome and is not reported as a completed movement. Operator holds and unavailable/held pictures do not accumulate catch-up motion.

Every newly accepted route first presents its exact starting pose before advancement. This is also necessary for continuous section joins at 30/60 fps: advancing one step before certifying the seam could otherwise reject a valid continuation.

## Save and replay camera fixtures

While a moving Scenic shot or finite pan is actually on air:

```text
broadcast fixture save coast-pass
broadcast fixture list
broadcast fixture play coast-pass
broadcast fixture play coast-pass 600
broadcast fixture status
broadcast fixture stop
```

The optional playback duration is **1–604800 seconds**. A moving fixture played with an explicit duration uses normal motion speed so a captured half-speed setting cannot silently double the requested time. Without a duration override, recorded movement/dynamic settings are retained locally for that playback; global settings are not overwritten. A fixture captured while decorative motion was disabled retains that stationary behavior.

Fixtures preserve camera and aim geometry, lens, composition, motion profile, duration, subject envelopes, source version and selected settings. Their stable world UID prevents playing a route in another world. The file records the original subject envelope for comparison; it does not prove that the building, vegetation or actor still exists unchanged.

If the saved start is distant or unready, playback can use the existing authorized observer travel path to acquire it. Acquisition is bounded to 90 seconds; route preparation then has its own 20-second bound. The complete saved route must pass current terrain/water/foliage and final-pose validation. A route is rejected if fitting would change its saved geometry. Ready player coverage can preempt playback.

`fixture stop` cancels pending/active playback. An already-issued native travel operation can still finish safely; use `broadcast observer return` to return and park. A new explicit observer-travel command cancels pending fixture and journey ownership.

Saving during a continuous journey captures the **current route section**, not the entire future journey. Future sections have not yet been selected or observed. Fixtures replay camera inputs; they do not make the live world simulation deterministic.

```text
broadcast fixture save coast-pass replace
broadcast fixture delete coast-pass
```

Data lives under `BepInEx/config/ValheimBroadcaster/CameraFixtures/<world UID>/`. Files use a bounded, versioned, checksummed format. A world supports up to 64 fixtures, each at most 32 KiB. Replacement is atomic and retains a `.bak`; no silent delete-and-rewrite fallback is used if atomic replacement is unavailable. Names are normalized and cannot select arbitrary filesystem paths.

Version 0.8.41 writes fixture schema 3, which preserves the bounded timing-to-distance profile and the rejected-continuation provenance introduced in schema 2. It reads schemas 1, 2 and 3; loading an old fixture does not rewrite it. Schema 1 retains unknown continuation provenance, and legacy timing remains explicit rather than being silently retimed. Explicit replacement keeps the existing backup behavior. **Clients older than 0.8.41 cannot load newly written schema-3 fixtures**; retain the original file or its backup if rollback is needed. Existing files, live configuration and saves are not migrated automatically.

## Operator-only spatial preview

```text
broadcast fixture preview live
broadcast fixture preview coast-pass
broadcast fixture preview off
```

Cyan shows the planned camera route; amber shows aim paths/rays; green shows available recorded/measured subject bounds. The current path sample is highlighted. Saved routes remain labeled as awaiting current geometry validation.

The preview uses projected GUI drawing after the clean source render, with clipped screen/near-plane segments. It creates no scene renderer, collider or extra camera. RTMP, diagnostic recordings and timelapse capture read the clean compositor before this overlay. Preview is omitted when the screen displays a held image or recovery slate, since the current camera projection may not describe that image.

## Applied versus rendered camera diagnostics

The camera records four boundaries: final accepted pose, applied camera state, pre-render state and clean-capture state. Evidence contains camera/frame, shot, revision, scene and source-generation identity; translation, quaternion angle, FOV and projection differences; and separate raw/non-jittered matrix observations. Ordinary bounded temporal jitter is not reported as an unexplained camera override.

Captured/held frames retain their original audit identity. A later shot cannot rewrite the metadata belonging to a held image. Brief anomalies are retained across the logging throttle and include their original observation frame/time. `broadcast diagnostics` reports the current audit and accumulated discrepancies; synchronized video/JSONL and timelapse metadata include the source audit.

This audit is observational. Missing diagnostic evidence does not invalidate an otherwise usable picture or initiate recovery. Camera rotation and GPU row inversion remain distinct failure mechanisms; the existing output orientation calibration remains in place.

## Real interval timelapse recording

In 0.8.38 this separate export feature uses `timelapse record`; autonomous live timelapse shots are described in [LIVE-TIMELAPSE.md](LIVE-TIMELAPSE.md).

```text
broadcast timelapse record start 2 600 30
broadcast timelapse record status
broadcast timelapse record stop
```

Arguments are **capture interval in real seconds**, **capture duration in real seconds**, and **output FPS**. Omitting them uses 2 / 600 / 30: up to 300 photographs from ten real minutes, encoded as ten seconds of silent 1280×720 MP4. Intervals range from 0.25 to 3600 seconds, source duration up to seven days, output FPS 1–60, with at most 216,000 photographs in one clip.

For one deliberately slow shot, first play a saved fixture with a matching long duration. Otherwise the recording follows ordinary camera edits within the current compositor epoch. It ends at a hard scene/travel generation change rather than concatenating unrelated distant locations.

Only fresh live pictures are sampled. Held/slate intervals and missed deadlines are omitted and recorded in the timeline; no catch-up burst or synthetic repeated photographs is inserted. Fewer captured photographs mean a shorter final clip. GPU readback uses the existing orientation-calibration path, bounded reusable buffers and at most one pending request. Encoding/finalization occurs off the Unity thread.

Output is written below `BepInEx/recordings/ValheimBroadcaster/timelapse/`, with paired source/output timing and camera metadata. Each video is bounded to 256 MiB and metadata to 32 MiB, with a separate 2 GiB folder allowance. Existing recordings are not automatically deleted to make room. The configured `Streaming.FFmpegPath` supplies FFmpeg.

Accelerated clips are deliberately **silent**. Live RTMP remains real-time H.264/AAC with its existing game audio and independent recovery. Interval recording does not speed up the game or server clock.

## Empty-server cinematic environment control

The user permits environment changes while no other players are online. This implementation applies visual time/weather overrides to the broadcaster and obtains fresh permission from the companion before doing so remotely.

```text
broadcast environment list
broadcast environment cycle 120
broadcast environment weather Clear
broadcast environment status
broadcast environment weather normal
broadcast environment cycle 0
broadcast environment off
```

Use `list` to select an environment name actually available in the current game; `Clear` is only an example and is validated before use. A 120-second cycle produces a live lighting day/night pass over two real minutes. The cycle starts from the current displayed phase; changing its speed preserves phase. `cycle 0` restores normal time while leaving an independently requested weather override; `weather normal` does the inverse; `off` releases both.

| Configuration | Default | Purpose |
| --- | --- | --- |
| Client `ScenicEnvironment.DayCycleSeconds` | `0` | Off, or 30–86400 real seconds per visual day cycle |
| Client `ScenicEnvironment.Weather` | empty | Preserve normal weather, or use an existing environment name |
| Server `Observer.CinematicEnvironmentEnabled` | `true` | Permit the optional capability for authorized observers on an otherwise empty server |

The server counts **ordinary connections including joining/loading peers**, not merely visible or ready characters. New connections/admission push revocation; the client also expires permission 2.5 seconds from request send time. A delayed reply cannot create a fresh lease. Permission is authenticated to the actual server/observer connection and world. Old companions do not grant it.

Controls apply during protected Scenic coverage and release on player takeover, travel, loss of permission, stop, world change or disposal. A short held-frame gap within the same Scenic coverage retains the existing override rather than repeatedly resetting phase. Prior settings are restored only when still owned by this controller; another writer's changes are not overwritten. A strictly closed local world is also supported.

This is a **live lighting/weather effect**, separate from real interval recording. It does not accelerate character motion or compress real events into live RTMP. It does not modify shared `ZNet` world time or server simulation speed. Native `EnvMan` debug phase also changes local day/night classifications; those may affect local behavior in a closed offline host. On a dedicated server the override stays on the broadcaster; ordinary players never receive it.

## Validation and remaining runtime checks

Automated verification covers route timing and starting-pose admission, journey section pacing/preemption, fixture serialization/persistence/geometry, pose audit identity and jitter, environment authorization/expiry and visual clock behavior, and actual FFmpeg timelapse encoding/timeline/orientation/failure handling. Final build/test evidence is recorded in `RUNTIME-TESTS.md`.

The following require Valheim runtime evidence and are not established by compilation:

| Scenario | Required observation |
| --- | --- |
| Open coast/ridge, turning itinerary, then forest | Verify a named destination, forward/developing gaze, actual distance/speed and continuity at section joins. A constrained route uses its prepared ending and records an incomplete outcome; completion requires readable arrival coverage. |
| Region dwell expires during a journey | No unnecessary teleport; a newly ready player still preempts promptly |
| Half-speed route and operator hold | Route reaches its endpoint; hold/resume does not jump; stationary mode reports a dwell outcome |
| Saved local and distant fixture | Correct world/start acquisition, repeatable camera geometry, unchanged settings, bounded failure if construction/foliage invalidates the route |
| Preview plus RTMP/diagnostic/timelapse | Overlay visible only to operator; all outputs remain clean and upright |
| Environment cycle/weather; another account starts joining | Fresh authorization, prompt restoration/revocation, no shared server-time change, no stale permit after reconnect |
| Long fixture plus interval recording | Correct acceleration and finalization; live audio/stream unaffected; no unwanted scene splice or memory/disk growth |
| Stop/return/world exit during acquisition, preview, recording or environment override | No stale pending fixture, retained override, unfinished owned process or resource leak |

No live client installation, dedicated-server update or runtime test is implied by these source changes.
