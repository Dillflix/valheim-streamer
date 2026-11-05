# Connected Journey navigation and travelling views — 0.8.51

The supplied **0.8.50 Journey failed after 34.091 m** of a manual 2,000 m /
12 m/s request. It exposed another design error: the planner treated an
artificial point in front of the camera as a required visible subject. A solid
surface 27.590 m away could reject a travelling view even with recorded
foreground foliage coverage of zero and a clear camera position. Shortening
the route preserved its safe prefix, but did not provide a way around the
remaining local geometry.

0.8.51 separates travelling-view readability from named-subject visibility,
introduces bounded connected navigation through the observed local space,
and rebases pending work when a fallback view leaves its loading context.
It preserves the existing mission, physical clearance, proof freshness and
actual-render progression requirements. Observer protocol remains **3**;
this client repair does not require a server update.

## Recorded failure

Times below are relative to the manual Journey request in the sanitized
0.8.50 fixture. They are not wall-clock times.

| Time | Observation |
| --- | --- |
| 0 s | Manual request selects 2,000 m at 12 m/s. |
| 1.480 s | Candidate query 17 fails the old Focus predicate at progress 0.655. Foreground coverage is 0; the camera is outside foliage; the synthetic aim is in frame; a collider named `Lod0` blocks the focus ray at 27.590 m. |
| 1.480 s | Prefix recovery proposes 34.615 m from the original 55.000 m candidate. |
| 2.449 s | Plan 17 starts with that 34.615 m prefix. |
| About 10.319 s | Measured travel stops at 34.091 m, leaving about 0.524 m of certified reserve. |
| 20.334 s | Ten seconds without measured travel requests local replanning. |
| 21.651 s | Ordinary fallback plan 29 presents a camera about 75.341 m from the recorded repair seed. |
| 24.562 s onward | Pending repair repeatedly requests the same camera/aim and fails acquisition. |
| 32.486 s | The completed repair batch finally rebases its context. |
| 33.451 s | The attempt ends without further measured travel. |

The fixture records 58 Focus-veto samples and also contains genuine physical
vegetation rejections. A distant focus veto and a camera inside foliage are
different failures; the latter remains a hard rejection. The historical
validation order establishes that body, near-plane, surface and loading checks
preceded the selected Focus veto. Those old checks are not reusable permission
for a new route.

The fallback displacement is measured from captured camera positions. The
fixture does not contain the broadcaster carrier transform, complete terrain,
collider hierarchy or foliage geometry. `Lod0` alone does not identify the
obstructing object. The recording also lacks the angular depth grid introduced
in 0.8.51, so it cannot prove that the new view predicate would accept that exact
scene.

## Travelling context and actual subjects

A travelling camera can show a forest, rock face, building or coastline. Its
synthetic forward aim defines orientation; it is not an object that must remain
visible through everything in front of it. Treating every obstruction along
that distant ray as failure rejected scenery that could itself be the picture.

The Journey view predicate samples a 5 × 3 angular grid in the actual composed
view. Solid depth is observed locally to 12 m and foliage to 6 m. It considers
near opaque coverage, foliage covering the first visible layer, useful picture
columns and mean visible depth. A nearer opaque surface hides foliage behind it;
that hidden foliage does not count as a visible leaf veil. Central samples
carry additional weight, but a single central tree does not automatically
invalidate the surrounding forest.

This is a geometric readability estimate, not semantic recognition or a
photographic-quality score. It rejects a nearby wall dominating the frame,
a heavy foreground foliage veil, a camera inside vegetation, and collision
with the camera/near plane. Missing observations remain unknown. Loaded-scene,
terrain, water and motion-corridor checks continue independently; a pleasant
view never certifies safe travel.

The same Journey-view semantics are used for applicable opening checks,
route preflight, live poses and renewed proof. Generic cruise refuges and
retained travel pictures preserve those semantics. **Named arrival coverage
still requires the actual locally observed destination subject to be framed
and visible.** A general readable forest view cannot earn arrival credit for
a hidden ruin, trader or other selected subject. Ordinary Scenic subjects and
Player/Creature framing keep their subject-oriented validation.

## Connected local navigation

The previous planner selected from a fixed family of complete curves. Moving
the origin, trying another curve or preserving a readable prefix could improve
an individual proposal, but did not search a sequence of connected alternatives
around an obstacle. Repeating that family did not establish that the local
space was impassable.

The replacement searches connected local poses and motion segments within the
observed loading horizon. It can retain useful partial exploration, change
heading/elevation around measured obstacles, and reconstruct a connected
candidate toward the selected destination. Expansion is progressive across
frontier nodes and interleaves spatial exploration with goal-directed ranking.
That allows a temporary retreat from the goal to escape a U-shaped obstacle,
rather than spending every bounded attempt on its attractive dead end. Synthetic
regressions exercise that case; they do not establish escape from every real
forest or structure. Search work, retained alternatives and the observed
horizon are bounded. This is not a global world navigation
mesh, an exhaustive search of every possible camera path, or a claim of a
globally optimal route.

A proposed travelling section also needs a separately observed onward edge matching
the exact two-metre body tangent and aim derivative required to start the next section;
a physically reached local goal is the exception. That edge is evidence of
an available next step, not a promise of unlimited future clearance. A native
preflight rejection resumes the retained connected alternatives. Arbitrarily
shortening a failed native curve is not used to bypass that onward requirement.

Intermediate search observations only guide proposals. The final route still
passes complete native-path and gaze evaluation, current scene/foliage and
surface checks, carrier reach, and stopping-refuge or named-arrival validation.
A connected continuation retains the admitted seam and its incoming geometry.
Pending queries cannot become clear edges, and stale/contradicted evidence
cannot remain an admission certificate. An independent repair cut is identified
as a cut and earns no distance across its discontinuity.

The search episode can survive the outer preparation batch boundary when its
mission, scene, exact first two camera/aim knots, destination, window, speed
and lens remain compatible. Retention is bounded to 120 seconds and to the
required loaded observation context. Returning a path refreshes aged evidence;
remembered search work does not retain an expired safety certificate. Explicit
mission/world cleanup discards the episode.

The current adapter bounds each local graph to 512 nodes and 8,192 edge
expansions, with at most eight unresolved frontier probes and 16 route waypoints.
A full pending set applies backpressure and fairly resumes exact queries instead of
discarding them. The adapter retains those eight partial observations plus one
selected-route refresh. Serviced physical sweeps survive across frames, while
idle work and observations older than five seconds are still refreshed.
Its proposed local route is at most 64 m; successive admitted sections, not one
unbounded spline, provide long travel. Up to eight search operations run per
planner step, stopping when the shared Scenic work budget is consumed. Selected
edge evidence older than five seconds must be refreshed before a proposal is
returned. These are resource/freshness limits, not a promised planning latency.

Search results distinguish **waiting for observations**, **local graph
exhaustion**, **capacity exhaustion** and **scene invalidation**. Reaching a
capacity bound does not establish that no world route exists. The remote
destination provides direction; it supplies no collision certificate.

## Turn limits, section cruise and playback

Each admitted travel section records its authored cruise cap, from **2 to
12 m/s**, separately from the operator's requested cruise and the live effective
speed. Connected-edge proposals limit body/view divergence to 70° and author
gaze development at no more than 6°/s. The regenerated native camera/aim curve
then receives its separate sampled angular-rate check, capped at 8°/s. A
geometrically clear turn that is too tight at the requested pace can be searched
or verified at a lower
tier, capped by the request, down through 6 and 2 m/s. If the actual native
curve still fails its checks at minimum pace, it is rejected.

The accepted native distance, section duration and cruise cap travel together.
Actual playback obeys that cap; it cannot retime a slower validated section
back to the original requested speed. Certified runway can reduce effective
speed further or stop it. A fresh connected queued section with a lower cap
also constrains braking before the seam, rather than waiting until the camera
enters that slower section. Operator hold and missing/invalid rendered evidence
still earn no movement or catch-up time.

This supplies explicit timing constraints, not a claim of visually perfect
turns. Smoothness, achieved speed and the cost of producing the next section
remain runtime acceptance questions.

## Loading context during picture repair

Fresh fallback coverage may keep the programme readable while a Journey is
being repaired. That picture can also move the camera/reference context far
enough that the pending search's forward observations leave loading reach,
even if its original starting point still appears nearby.

Retention therefore checks the required route and viewing neighbourhood,
including pending endings, against the actual carrier context. Journey travel
uses its local angular viewing depth; subject-oriented arrival work retains
its actual focus requirements. A fallback with a fresh matching captured
identity can trigger an early rebase when that work no longer fits. Compatible
work is retained. A fallback cut does not automatically reset all planning on
every frame, renew the mission deadline or earn travel distance.

This corrects the stale-search context illustrated by the recorded 75 m camera
cut. It does not infer unloaded geometry from a catalogue location or treat a
camera position as proof of complete network delivery.

## Saved fixture compatibility

Newly saved camera fixtures use **schema 4**, preserving the stable-water
clearance and Journey-view flags plus the authored section cruise cap. A saved travelling section keeps the local
angular readability contract; a saved named arrival keeps its subject check.
This prevents playback from silently changing the validation semantics of the
recorded shot. Saving a Journey still records its current route section, not
the unseen future navigation graph.

Schemas 1–3 remain readable with their existing meaning, absent flags set
to false and an unspecified cruise value of zero. Zero means the legacy timing
contract applies; it is not a command to stop the camera. Loading them does not rewrite or silently migrate them. Explicit
replacement retains the existing backup. **Clients older than 0.8.51 cannot
read newly written schema-4 fixtures**; keep older originals/backups if rolling
back. Neither the schema change nor graph planning modifies world saves or
upgrades the observer protocol. See [fixture controls](SCENIC-PRODUCTION.md#save-and-replay-camera-fixtures).

## Validation and runtime acceptance

Local real-reference client/server/replay builds, **1,329 Core cases**, and
**110 native camera-math cases** pass. Native graph-to-spline cases use the
production graph bounds and analytic obstacle geometry. The coverage/deadline
fixture replays with zero divergences or invalid records. Details and remaining
runtime boundaries are recorded in [RUNTIME-TESTS.md](RUNTIME-TESTS.md). The supplied 0.8.50 run is
failed runtime evidence, not a successful run of the new implementation.

The sanitized
[`0.8.50-focus-veto.json`](../tests/fixtures/journey-mission/0.8.50-focus-veto.json)
retains relative positions, focus-ray distances, physical/foreground outcomes,
selected lifecycle events and repair-context changes. Replaying those values
can establish the old veto and ownership/timing consequences. It cannot
synthesize the missing angular grid or prove a clear connected corridor in the
original world. Synthetic graphs, depth grids and native curves test their
respective policy/math contracts separately.

Capture the repeated request and retain both footage and diagnostics:

```text
broadcast mode scenic
broadcast capture on
broadcast journey distance 2000
broadcast journey speed 12
broadcast journey start
```

| Runtime case | Required evidence |
| --- | --- |
| Distant tree, rock or building in a readable travelling view | Local picture observations explain acceptance/rejection; a distant synthetic-focus ray alone does not veto movement |
| Near opaque wall, leaf veil, camera inside foliage or below a surface | Physical/readability rejection remains effective; no unsafe route is admitted |
| Obstacle requiring more than one heading change | Connected exploration finds and fully validates a useful route, or reports bounded pending/exhausted search; a larger count of similar rejected curves is not success |
| Continuation at changing loading boundaries | Exact seam, fresh proof, readable live output and measured distance continue across several sections |
| Tight turn and faster-to-slower seam | Native turn/gaze constraints choose a valid section pace; measured playback respects it and brakes before entering a slower queued section |
| Fallback cut during repair | Work outside its required loading neighbourhood rebases promptly from fresh captured context, while compatible work survives |
| Named arrival | Actual selected subject is reached and shown; general readable context is insufficient |
| Sustained trip | Substantial measured travel and destination progress, understandable speed/capacity behavior and a valid arrival; a 34 m prefix followed by a stop fails the test |
| Hold, stop, world exit and player acquisition in Auto | Existing authority, deadlines, no-catch-up behavior and cleanup remain intact |

Actual world streaming, navigation throughput, visual composition and long-run
audio/video performance require captured Valheim testing. Neither a clear
angular grid nor a successful synthetic connected search proves a complete
multi-kilometre cinematic journey.
