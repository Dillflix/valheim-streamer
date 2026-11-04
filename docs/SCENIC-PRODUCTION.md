# Scenic journeys, camera fixtures and timelapse — 0.8.37

Built directly on 0.8.36 (`0de075d3213e76e3abd41c4ce627c4f071ed49a7`). This version adds longer continuous Scenic journeys, persistent camera fixtures, operator-only path previews, render-pose verification, interval timelapse recording and optional empty-server environment control. It also fixes reduced-speed Scenic routes being reported complete before reaching their endpoint.

The previous director primarily chose short local camera moves. Native Dolly support supplied a way to execute those moves; it did not by itself supply long-distance planning, continuing world acquisition or long-shot editorial ownership. These additions address that distinction.

## Install and compatibility

- Replace the **three client DLLs and included ThirdParty notices together**, preserving the existing configuration.
- Journey, fixture, preview, pose-audit and recorded-timelapse features work with an existing compatible **protocol-3** companion.
- Install the **0.8.37 server/core pair** for the new optional empty-server `Environment1` permission. Earlier companions leave remote environment overrides inactive. Ordinary players need no mod update.
- `Observer.CinematicEnvironmentEnabled=true` permits an already-authorized observer to request this optional capability. Client environment overrides default off.
- Admission, pre-admission opt-in and observer isolation remain protocol 3. No new game, Unity, BepInEx or Jötunn binaries belong in either payload.

Compilation and automated tests do not establish in-game camera quality, graphics-backend behavior or multiplayer join timing. See the validation section below.

## Continuous Scenic journeys

The journey family extends a suitable readable natural shot through successive **32-meter maximum route sections**. Each section is prepared and validated ahead of presentation. The native execution path joins only the exact accepted predecessor endpoint, aim, lens and orientation. Interior joins retain cruise motion; the initial/final sections can use acceleration ramps. The camera's protected carrier follows continuously, so newly nearby world regions can stream without an in-shot teleport.

Default automatic behavior offers journeys after at least two shown Scenic shots, with a three-minute cooldown after a journey ends. A suitable natural route and a validated continuation are required; the default duration is 180 seconds, with a target speed of 1 m/s before the existing `ShotMotion` multiplier. Duration can be configured from 30 to 900 seconds and speed from 0.25 to 2 m/s. A separate bound limits one journey to 64 sections / approximately 1,500 meters of credited motion.

Journeys are one member of the Scenic portfolio. Building/site sequences, finite pans, ordinary local shots and player coverage remain available. Player acquisition and missing-picture recovery retain priority. Ordinary region-dwell travel and idle wildlife cuts do not interrupt an active useful journey simply because their shorter timers expire.

The future world is not assumed clear. Terrain, water, near-plane/corridor clearance, foliage and readability still apply. If no usable continuation is found, the camera finishes the currently valid section and returns to ordinary local coverage. A pending continuation can retain the readable endpoint for at most six seconds. Journey preparation has its own bounded work/deadline. The configured duration is a goal/limit for a usable journey, not a guarantee that every biome supports that much uninterrupted movement.

```text
broadcast journey start
broadcast journey status
broadcast journey duration 180
broadcast journey speed 1
broadcast journey stop
broadcast journey off
broadcast journey on
```

`start` requests an eligible journey from current protected-observer Scenic coverage; it does not cut immediately or bypass validation. `stop` ends the current request and resumes ordinary direction; `off` also disables future automatic journeys. The diagnostics report shown time, credited distance, section count and continuation status.

| Configuration | Default | Purpose |
| --- | --- | --- |
| `Idle.ScenicJourneys` | `true` | Offer automatic journeys |
| `Idle.ScenicJourneySeconds` | `180` | Desired maximum developed journey time, 30–900 seconds |
| `Idle.ScenicJourneySpeed` | `1` | Target m/s before `Camera.ShotMotion`, 0.25–2 |

## Correct motion and editorial clocks

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

```text
broadcast timelapse start 2 600 30
broadcast timelapse status
broadcast timelapse stop
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
| Open coast/ridge, then forest | Longer useful travel without a stop/cut at each section; correct body/aim/lens continuity; constrained space ends locally without recovery churn |
| Region dwell expires during a journey | No unnecessary teleport; a newly ready player still preempts promptly |
| Half-speed route and operator hold | Route reaches its endpoint; hold/resume does not jump; stationary mode reports a dwell outcome |
| Saved local and distant fixture | Correct world/start acquisition, repeatable camera geometry, unchanged settings, bounded failure if construction/foliage invalidates the route |
| Preview plus RTMP/diagnostic/timelapse | Overlay visible only to operator; all outputs remain clean and upright |
| Environment cycle/weather; another account starts joining | Fresh authorization, prompt restoration/revocation, no shared server-time change, no stale permit after reconnect |
| Long fixture plus interval recording | Correct acceleration and finalization; live audio/stream unaffected; no unwanted scene splice or memory/disk growth |
| Stop/return/world exit during acquisition, preview, recording or environment override | No stale pending fixture, retained override, unfinished owned process or resource leak |

No live client installation, dedicated-server update or runtime test is implied by these source changes.
