# Journey planning and stationary repair — 0.8.50

The supplied **0.8.49 recording is a failed Journey result**. An automatic
2,000 m request at 12 m/s moved about 34.6 m, then kept showing the same live
camera position for about 69 seconds. Mission ownership survived, but route
production and replacement coverage did not deliver the intended trip.

0.8.50 addresses that recorded planning stall, the terrain fit that pulled
higher viewpoints back down, and the presentation rules that concealed a
stationary repair. It retains the 0.8.49 separation of mission, travel playback,
picture availability and ordinary Scenic editing. Observer protocol remains
**3**; this client repair does not require a server update.

## What the supplied capture establishes

Times below are elapsed times from the original capture, not wall-clock times.
The request was automatic; the recording does not show a successful long trip.

| Time | Recorded result |
| --- | --- |
| 100.611 s | Journey 13 selects a destination about 1,992.6 m away for a 2,000 m / 12 m/s request. |
| 117.028 s | Travel starts after about 16.4 seconds of departure preparation. Plan 18 provides a 35.049 m section. |
| 121.291 s onward | Continuation evaluation rejects foreground foliage. The on-air picture remains readable. |
| About 125.16 s | Measured travel stops at 34.587 m, with about 0.525 m of certified reserve left. |
| 135.164 s | Ten seconds without measured travel requests local replanning. |
| 147.167–184.258 s | Four repair batches expire; subsequent batches restart the same opening-search prefix. |
| 194.258 s | Explicit stop/world teardown ends the attempt. The stationary interval lasts about 69 seconds; the 90-second no-progress deadline did not end this recording. |

The retained picture metadata reports fresh **Live / Matched** frames during
the stall, with neither held textures nor recovery slates in the sampled
stationary interval. The camera had stopped while the world continued to
render. This evidence identifies a planning/presentation stall; it does not
demonstrate an encoder freeze.

The existing test suite had not established successful world traversal. The
new capture is direct evidence that the 0.8.49 integration remained inadequate
for this terrain, even though its ownership and policy tests passed.

## Demonstrated causes and repair boundaries

| Recorded or source-confirmed defect | Consequence | 0.8.50 correction |
| --- | --- | --- |
| Each short preparation batch reset the independent-opening cursor and rejected/measured history | Five batches revisited only 15–16 of 108 proposals, despite finding 8–9 readable openings per batch | Preserve bounded search history across compatible retries; continue through different measured origins and elevations |
| Terrain fitting converted the selected altitude into a local clearance capped at 18 m | A deliberately high opening could be fitted down into the canopy; the admitted section drops about 4 m, then rises about 4.8 m over a 35 m route | Keep cruise altitude separate from minimum surface clearance while retaining measured terrain, grade and exact-seam constraints |
| Foreground foliage could hide the proposed view without containing the camera | The earlier inside-foliage escape condition offered no measured canopy alternative for these rejections | Derive a bounded height proposal from complete nearby foliage observations after a measured foreground obstruction; revalidate the whole new route |
| A late foreground rejection discarded the whole candidate | One measured candidate remained readable through roughly 94% of its evaluated interval, yet supplied no shorter continuation | Recover only eligible connected prefixes, then validate the resulting route and its stopping refuge again |
| Retaining a readable Journey pose prevented ordinary prepared coverage from taking over during repair | Prepared plan 29 remained off air while plan 18 stayed stationary | Permit fresh local coverage after successful ordinary admission, without relinquishing the travel mission |
| Operator presentation described the live stationary route as moving and hid repair work | A fresh render looked like ongoing travel even with zero cruise and a growing no-progress timer | Present mission phase, measured distance, cruise setting, no-progress duration, certified reserve, pending work and last rejection separately |

A readable camera position does not prove that a forward corridor is clear.
Conversely, a rejected forward corridor does not prove that the current
picture is invalid. The repair preserves that distinction: it must produce
another physically validated route or independently validated local coverage,
not relax visibility or treat a static endpoint as completed travel.

## Search lifetime and route geometry

Opening history belongs to a bounded local search for the same Journey and
scene. Retained history avoids repeating known work; it is not retained safety
permission. The episode is bounded to 120 seconds and is reset by a different
mission/scene or material changes in its seed, goal or carrier position. Deferred
observations remain unknown until the scene owner supplies evidence, and a
candidate still needs current route and stopping-pose validation before use.
Changing the requested window length can retry a shorter corridor from a known
opening without restarting the full opening survey.

Higher origins are useful only if their resulting route keeps the intended
altitude and remains safe. The terrain fit therefore preserves a selected
world altitude independently of the local minimum clearance. Rising terrain
can still force a bounded climb or reject a corridor. A connected continuation
must preserve its incoming position and tangent. Non-arrival sections preserve
the admitted cruise altitude; they do not yet optimize a gradual descent after
a ridge. Arrival approaches can use the measured terrain profile. The fitter
does not grant permission to cross foliage, terrain, water or unloaded space. A foreground
foliage rejection can also inform a new altitude from nearby observed canopy
bounds. Incomplete observations cannot produce that escape, and a required
lift beyond the bounded search space is rejected rather than clipped to an
unsafe height.

Measured readable samples can justify trying a shorter route; they cannot
certify a newly restricted native spline. Prefix recovery must retain the
entry seam, exclude the failed part, and pass full evaluation and endpoint
validation again. It must not jump to a disconnected readable suffix, convert
pending observations into clearance, or fabricate producer throughput from
failed work.

## Repair pictures and operator presentation

A stationary Journey view may remain useful while local replanning runs.
It is no longer an exclusive picture owner once fresh ordinary local coverage
has been admitted. A rejected or still-pending replacement cannot remove the
current readable view. Successful replacement releases only that picture
claim: the destination, measured travel, request deadlines and Journey travel
authority persist. An independent cut earns no travel distance.

The operator distinguishes **Preparing departure**, **Travelling**,
**Stationary**, **Replanning**, **Arrival** and **Operator paused**. Effective
and requested cruise are controller settings, not a claim of instantaneous
camera speed. An advancing label also requires recent measured movement and
permission to advance. Fresh stationary rendering remains **Live**; it is not
mislabelled as a held texture. When ordinary coverage replaces the stationary
Journey view, its picture label changes while the mission status remains.

Current work and the last rejection are separate: an earlier foliage failure
must not be presented as the cause of every later wait. Recorded diagnostics
retain the route, queue, certified reserve and no-progress duration so that
another stall can be distinguished from presentation or transport failure.

## Validation and acceptance

Automated validation for this source revision is recorded in
[RUNTIME-TESTS.md](RUNTIME-TESTS.md): real-reference client/server/replay
compilation, 1,289/1,289 Core cases, 106/106 native camera-math cases, and
zero-divergence coverage/deadline fixture replay. Normal project builds and
platform-specific checks run in CI before artifact delivery. The supplied
0.8.49 capture is failure evidence, not a successful run of 0.8.50.

The sanitized fixture in
[`tests/fixtures/journey-mission/0.8.49-planning-stall.json`](../tests/fixtures/journey-mission/0.8.49-planning-stall.json)
retains relative camera positions, candidate samples, repeated opening batches,
mission transitions and synchronized picture metadata. It omits absolute world
positions and identifiers. It does not contain terrain, collider or foliage
geometry; replay can verify measured symptoms and scheduling decisions, but
cannot reconstruct the Unity scene or prove that a proposed repair is clear.
Terrain-profile regressions may combine recorded horizontal samples with
explicitly constructed floor heights to test the altitude rule. Those tests
are counterfactual geometry checks, not reconstructed terrain or observed
post-fix visibility.

Repeat the failed request with synchronized footage and diagnostics:

```text
broadcast mode scenic
broadcast capture on
broadcast journey distance 2000
broadcast journey speed 12
broadcast journey start
```

This is an explicit reproduction of the automatically selected request scale
and speed. It need not choose the identical destination; retain the destination
and route diagnostics to distinguish a same-site reproduction from a different
terrain test. Also test a conservative 1,000 m / 6 m/s trip and the earlier
5,000 m / 12 m/s case after the baseline works.

| Case | Required evidence |
| --- | --- |
| Repeated local repair | Distinct origins/elevations continue across compatible preparation batches; rejected early proposals do not consume every new batch |
| High opening above trees | The proposed and rendered route retain the selected altitude unless measured terrain requires a climb; no forced descent to the old 18 m local-clearance target |
| Late foreground obstruction | Any accepted prefix has fresh complete path and refuge proof; failed or unknown portions remain unused |
| Readable stationary view during repair | New admitted local footage can replace it while the same mission remains active; diagnostics explicitly report stationary/replanning state |
| Continued travel | Multiple connected sections, measured destination progress and successful arrival presentation; one short section followed by a static view is a failure |
| Genuine blockage or insufficient capacity | Clear work/failure evidence and bounded terminal outcome; no false completion, unexplained teleport or movement into uncertified space |
| Player/operator intervention | Player acquisition in Auto, hold, stop, return and world exit retain their existing authority and cleanup |

Actual game loading, procedural route production, visual continuity, achieved
cruise and sustained audio/video performance still require these captured
runtime checks. A successful compile or deterministic fixture does not prove
an uninterrupted multi-kilometre cinematic trip.
