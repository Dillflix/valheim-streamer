# Journey mission, execution and picture ownership — 0.8.49

A Journey is a sustained camera trip toward an identified destination. Local
route sections are bounded execution windows within that trip. They are not
ordinary Scenic shots, and their endpoints are not editorial cut points.
The requested distance selects the itinerary scale; it does not turn a short
local camera move into a completed journey.

This document specifies the ownership and acceptance contract for the 0.8.49
implementation. Automated checks establish policy, geometry mathematics and
adapter compilation. They do not establish successful Valheim world streaming
or the visual quality of a completed long-distance trip.

## Why the previous integration could still interrupt a Journey

The earlier recovery policy preserved a destination locally, but several
surrounding owners could still discard it:

| Coupling in the previous implementation | Consequence | Required separation |
| --- | --- | --- |
| `JourneyActive` depended on a currently usable picture | Missing output could remove the outer travel lock before local repair ran | Mission ownership is independent of picture availability |
| Autonomous Scenic relocation respected Journey only while the picture was ready and its recovery budget unexpired | Ordinary dwell or missing-picture recovery could issue a teleport during the trip | Only the mission owner releases ordinary Scenic travel |
| `TickSearch` returned before Journey work when the scene was unready | Loading failure could suspend the very controller needed to classify or recover it | Authority and deadlines run every update; planning runs subject to its own evidence/budget |
| The ordinary `ScenicDirector` advanced travelling sections | Editorial replacement and picture timers also governed physical trip execution | Dedicated travel playback owns section progress |
| A fixed number of sections and estimated preparation latency governed departure | A physically useful route could be rejected because a speculative reserve did not fit local loading reach | Admission uses certified distance and stopping room; production estimates schedule replenishment |
| Independent departures inherited one current Scenic origin | Many differently shaped routes could repeat the same obstructed first pose | Survey distinct observed departure openings before spending full-route validation work |

A longer timeout, more variants of the same blocked origin, or a larger list
of proposed routes would not remove these ownership conflicts.

## Owners and authority

| Owner | Owns | Does not establish |
| --- | --- | --- |
| `JourneyTravelController` | World-bound mission identity, departure/travel deadlines, explicit preemption, physical navigation state and terminal outcome | A valid picture or safe route merely because the mission remains active |
| `ScenicJourneyPolicy` | Selected itinerary, requested scale/cruise, actual captured distance, route progress and destination/arrival evidence | Distance credit for proposals, cuts, held textures or teleport displacement |
| `JourneyRoutePlayback` | Accepted section, previewed progress, matching live presentation and a reversible physical pause | Editorial replacement, ordinary Scenic dwell or mission expiry |
| Journey planner, section/ending leases and runway policy | Measured openings, complete local route/ending validation, connected certified reserve and safe playback speed | Distant clearance from server metadata or a guarantee that requested cruise is sustainable |
| `ScenicProduction` and ordinary `ScenicDirector` | Local subject discovery, independently validated fallback pictures and ordinary Scenic editing | Permission to replace or teleport an active mission because an ordinary shot is due |
| `AutonomousDirector` and `CoverageDirector` | Player priority, coverage selection and ordinary region visits after mission release | Permission to equate a recovery slate with Journey cancellation |
| `ObserverController` | Protected carrier/reference movement and its scene, ownership and distance limits | Movement into an unloaded or uncertified future position |
| Programme capture/compositor | Actual rendered frame identity, clean output and bounded hold/slate presentation | A claim that held output is live motion or that a planned route was rendered |

`IdleShots.JourneyOwnsTravel` exposes persistent mission ownership to the outer
directors. It is separate from fixture playback and from whether a Journey
section is currently on air. In particular, ordinary Scenic dwell, content
exhaustion and picture-recovery expiry cannot issue an unrelated Scenic
teleport while that property is true. An unstarted ordinary Scenic request
can be withdrawn without reporting a travel failure. Native travel already
underway is never silently cancelled.

Ready or distant players retain priority in Auto mode. Operator commands to
select Player/Creature coverage, stop a Journey, play a fixture or return the
observer remain explicit interventions. Holding pauses the mission clock and
motion; it does not refresh geometry proofs or accumulate catch-up movement.
`broadcast mode scenic` retains its existing meaning when a test must exclude
player acquisition.

## Lifecycle and execution

The mission has Departure, Travelling, Replanning and Arriving phases, followed
by an explicit Completed or Cancelled outcome. Departure starts before a
picture is admitted. Accepting a fresh, physically validated route starts the
travel clock; merely preparing a candidate does not. Local scene-generation
changes invalidate old route permission and require revalidation, while
retaining the world-scoped mission and original elapsed time.

Authority is checked before the ordinary automatic director on every plugin
update, including Recovery coverage. Missing or obscured footage is reported
separately from observation pending, loading pending, physical obstruction
and a genuinely exhausted physical search. Temporary unavailability may pause
advance while preserving mission ownership. World change/loss, revoked
observer authority, actual player/operator preemption, unrecoverable loading
or physical navigation failure, and the original deadlines are terminal.
A retry, fallback cut or local cache refresh cannot silently restart a deadline.
After travel begins, ten active seconds without measured camera travel requests
a local physical alternative. Ninety active seconds without measured progress
ends the attempt with an explicit stalled-navigation reason. Only actual
captured travel renews this watchdog; route IDs, cuts and renewed proofs do not.
The overall travel budget still applies when occasional progress is made.

The dedicated playback clock accepts only progress that it proposed and that
a fresh matching readable capture actually presented. A missing picture
retains the accepted route pose and stops advancement. It does not age the
section into an ordinary shot replacement. Later valid output can resume the
same section. At a certified seam the next section continues the same mission;
a genuinely necessary independent repair cut is diagnosed as a cut and earns
no distance across the discontinuity.

Completion requires actual progress to the selected place and readable arrival
presentation. The server's destination name, reaching an artificial heading
point, consuming a duration, or displaying a prepared ending is insufficient.
Partial/blocked outcomes retain their actual measured distance and reason.

## Spatial planning, loading and production capacity

Server discovery supplies destination identity and useful location/site
context. It is a sparse catalogue, not a remotely certified camera navigation
mesh. The client still has to observe the loaded space around its carrier.

Independent departure planning surveys distinct camera origins, elevations
and forward bearings, records unknown observations as pending, and compares
measured readable openings before constructing full routes. A known blocked
opening is not retried under many later-knot variants. Every selected route still passes complete native-path validation; its safe
endpoint refuge or arrival receives the matching complete pose/sweep checks. Continuations
preserve their exact seam position and tangent; their gaze can develop within
bounded angular speed instead of remaining fixed to an inherited backwards
composition.

Runway is the distance of fresh, connected, validated geometry. A nonempty
queue or a proposed length does not count. One route plus its validated endpoint refuge or arrival
can establish useful departure when it has enough room for movement and a
safe stop. Splitting the same distance into two sections supplies no additional
physical reserve. Replenishment targets distance within the observed camera
and gaze horizon, with a bounded queue for memory use.

Cruising sections certify a stationary refuge at their exact endpoint, including
near-plane, surface, loaded-scene and foliage checks. They do not have to invent
and validate a decorative six-second pan just to prove that the camera can
stop. An arrival already facing its observed subject may use that stationary
composition; an arrival requiring a turn validates the entire finite pan.
Neither a refuge nor an arbitrary endpoint is credited as destination coverage
without the actual observed subject and presentation evidence.

Only completed route-and-refuge/arrival jobs contribute produced metres and successful
preparation time. Failed searches and renewal of already-counted geometry do
not fabricate producer throughput. Requested cruise is an upper target;
actual certified reserve and measured production determine the supported
speed. A contracting reserve can brake below the normal useful launch speed
and reach zero. Useful fresh reserve can resume that same path. A stop is not
permission to move into the next unproved section, and an indefinitely stopped
trip is not success.

The protected carrier may lead only along admitted connected geometry within
its existing tether and loaded-position checks. Loading advance, survey work,
route preparation, proof renewal, ordinary fallback preparation and rendering
share finite resources. Those costs are coupled: extending a path requires
both camera and gaze coverage, while moving the carrier changes retained
geometry. Counters and unit tests cannot establish that the real game produces
certified distance as fast as a 12 m/s camera consumes it.

## Validation and remaining runtime acceptance

The local 0.8.49 source passed 1,280 Core cases, including captured-failure
replay and synthetic sustained travel; 99 native camera mathematics cases;
and 43 matching-game assembly checks. Client and companion compilation used
the actual Valheim 1.0.21 / Unity 6000.0.75f1, BepInEx 5 and Jötunn 2.30.2
references. Standalone coverage/deadline replay fixtures also passed.
These checks do not launch Valheim or establish in-game Journey completion.

During local repair the last accepted route pose remains the picture while it
passes live validation. Fallback preparation is rebased to the current camera,
not the departure region. On completion or cancellation, a fresh captured exit
pose is independently validated before ordinary Scenic coverage may retain it.
Presentation scene identity and physical observation epoch remain separate.

The automated suite exercises the mission lifecycle independently from picture
availability, travel playback independently from ordinary editorial timers,
physical runway admission and braking, measured departure-space selection,
and the complete outer automatic director through sustained footage loss,
ordinary dwell expiry and real player preemption. A sanitized observed timing
sequence complements synthetic boundary cases. Full-route/native mathematics
and real-reference compilation remain separate checks.

Use synchronized diagnostic capture for acceptance. Confirm the installed
client version first, retain both footage and trace, and record the requested
scale/speed and selected destination. These checks do not require changing the
production server or observer protocol.

```text
broadcast mode scenic
broadcast capture on
broadcast journey distance 1000
broadcast journey speed 6
broadcast journey start
```

After this representative run, repeat the reported demanding case at
`distance 5000` and `speed 12`. Explicit departure has a 90-second unheld bound;
it must either admit validated travel or report its actual expiry/blocker.
That is an acquisition limit, not a claim that a five-kilometre trip finishes
within 90 seconds. A capacity-limited or stalled trip must expose its measured
speed, pending work and eventual outcome.

| Runtime case | Acceptance evidence |
| --- | --- |
| Open-terrain sustained trip | One persistent mission/destination; substantial measured camera travel and destination progress; continuous connected seam handoffs; no ordinary Scenic teleport at region dwell expiry |
| 5,000 m at requested 12 m/s | Measured distance, achieved/effective cruise and production/runway history support the result; braking or capacity limitation is explained; a short trip never reports completion |
| Shoreline, changing water and terrain | Native clearance remains valid across actual crests and slopes; loading advance stays with certified geometry; no below-ground/water views |
| Forest or obstructed departure | Distinct observed openings are attempted; unknown coverage remains pending; a blocked first origin is not repeated under cosmetic path variations |
| Missing picture or delayed observations | Mission identity and travel ownership persist; advancement/credit stops where required; ordinary Scenic deadlines cannot teleport the observer |
| Late or contradicted continuation | Invalid geometry is never used; the camera slows/stops safely, preserves certified prefixes where possible, and diagnoses repair toward the same destination; endpoint refuge does not insert an unnecessary decorative pan |
| Local repair/fallback | Any cut is visible in diagnostics and earns no jump distance; subsequent travel retains the mission rather than silently starting another Journey |
| Arrival | The selected place is actually reached and shown; remaining travel is not replaced by an unexplained long static endpoint hold |
| Player joins in Auto mode | Actual player acquisition preempts the mission; changing back to Scenic does not revive stale travel permission |
| Operator hold/stop/return and world exit | Hold has no catch-up surge; stop/return releases ownership; another world cannot inherit the old mission or route |
| Extended broadcast | RTMP/audio continuity, CPU/frame cost, bounded observations and absence of repeated recovery churn are verified from actual footage and diagnostics |

Until those captured runtime cases pass, the implementation should be described
as architecture and regression coverage awaiting gameplay acceptance, not as a
proven uninterrupted multi-kilometre cinematic system.
