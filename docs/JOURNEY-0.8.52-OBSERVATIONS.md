# Journey departure failure: captured 0.8.51 evidence and 0.8.52 repair scope

The supplied 0.8.51 attempt never admitted a Journey departure. It selected a
destination, repeatedly searched the same small pocket of forest, and supplied
no complete route before the recording ended. Ordinary Scenic footage remained
live throughout the request. This is a navigation and observation failure;
there is no evidence of an encoder freeze or absent world loading in this run.

This document records the failure before the repair. The implementation work
described below does not establish successful execution in Valheim. Compilation,
offline tests and subsequent game recordings must remain separate evidence.

## Recorded sequence

The capture reports client **0.8.51**, observer protocol **3**, and Unity
**6000.0.75f1**. The manual request is 2,000 metres at 12 m/s. A destination about
2,027 metres away is selected immediately. Times below start at that request;
source sequence numbers identify rows in the private original capture.

| Relative time | Source sequence | Observation |
| --- | --- | --- |
| 0.000 s | 5134–5137 | Request and departure preparation begin. |
| 0.214–0.630 s | 5179–5284 | Four openings are screened. Openings 0 and 3 are readable; the two others are rejected. |
| 0.680 s | 5292 | Query 15 starts graph exploration at opening 0. |
| 0.747 s | 5304–5307 | Probe 5 is rejected because the camera lies inside retained vegetation bounds. All three gaze alternatives repeat this same body-position rejection. |
| 12.009 s | 9158 | The first preparation batch expires. No complete candidate or departure has been admitted. |
| 12.542–13.076 s | 9226–9309 | Query 17 repeats the same four opening screens and their outcomes. |
| 13.092 s | 9313 | Graph exploration begins again at the same opening 0. |
| 23.818 s | 13134 | The attempt is explicitly stopped. The request still has 66.182 seconds remaining. |

The recording therefore establishes an unsuccessful first preparation batch
and a repeated unsuccessful search pattern. It does not record the full
90-second request deadline expiring.

## What the counters actually establish

The message about eight pending observations is not evidence that every query
stopped progressing. Both graphs continued producing resolved observations:

| Recorded result | Query 15 | Query 17 |
| --- | ---: | ---: |
| Completed clear edges | 59 | 55 |
| Completed blocked edges | 556 | 490 |
| Journey view evaluations | 1,907 | 1,724 |
| Retained-vegetation containment results | 1,255 | 1,130 |
| Obscured-view results | 435 | 380 |
| Readable-view results | 123 | 112 |
| Unknown-view results | 94 | 102 |
| Furthest clear endpoint from the opening | 13.901 m | 13.901 m |

There are **zero onward-edge results, zero complete navigation proposals,
zero native Journey candidates and zero Journey admissions**. The second
readable opening never receives a graph search. View counts include repeated
gaze alternatives and retries; they are not counts of distinct obstacles.

Every one of the 3,631 Journey view evaluations reports `sceneReady=true` and
`cameraClear=true`. The `CameraBlocked` view status in this run comes from
`insideVegetation`, not a failed solid body/near-plane collision check.

The distinction matters. The capture does not identify the containing foliage
bound, its extents, its source precision or its actual triangles. Its unrelated
renderer-bound diagnostics report empty particle renderers, not these rejected
camera volumes. The available evidence cannot distinguish actual leaf
intersection from conservative bounding-box containment. It cannot establish
that the forest is physically impassable.

## Observation ownership and repeated search

The shared local foliage projection starts 270 times and commits 263 times
during the request. **76 of those commits do not cover the request being
evaluated when they complete.** This count applies the production coverage
rule to the recorded requested position, retained centre and radius:
`distance + 6 < min(16, coveredRadius)`. The completed projections report no
omitted geometry.

For example, sequence 9141 commits a 16-metre projection while the current
request is 10.130 metres from its retained centre. Its six-metre foreground
neighbourhood therefore lies outside that projection. Sequence 9150 starts a
projection around a different requested point; sequence 9154 still reports
incomplete observations for another pending probe. The batch ends on the next
frame. The physical scene epoch remains unchanged until the explicit stop.

That final message is a real observation-ownership problem, but it is not a
complete explanation of the whole failure. The graph was also repeatedly
rejecting retained foliage bounds and revisiting the same small neighbourhood.

Two existing lifetime rules make this repetition worse. Preparation failure
shrinks the requested window, while graph episode identity includes the exact
requested window length. Ordinary camera movement can also reset departure
opening history through the carrier-distance condition. The recorded ordinary
camera moves 11.208 metres during the first batch, and the next batch repeats
all four opening screens. The carrier transform was not captured, so the
camera displacement must not be presented as a measured carrier displacement.
The source rules and the repeated observations establish the lifetime mismatch;
the recording does not separately identify every reset trigger.

## Repair architecture

The repair targets these ownership and precision problems together:

- **Persistent, fairly scheduled departure origins.** Each viable origin keeps
  its search frontier and negative observations while other origins receive
  bounded work. A twelve-second preparation boundary, a shorter desired section
  or an ordinary Scenic cut must not restart the same pocket indefinitely.
  Current loading reach and scene changes still invalidate incompatible proof;
  retaining search history does not retain authorization to move.
- **Observations owned by the query that needs them.** Endpoint and midpoint
  requests retain their own bounded view context until they complete or are
  explicitly retired. Another pending probe must not retarget that context.
  Pool capacity and fair scheduling must agree, and unresolved observations
  remain unknown rather than becoming evidence of blocked space.
- **Actual foliage intersection after coarse screening.** Bounds locate
  potentially relevant vegetation. Supported triangle geometry then determines
  the camera-volume or sight-ray intersection. A coarse renderer box alone must
  not become a hard physical obstacle. Unsupported foliage precision remains
  an explicit picture-quality uncertainty; it must not masquerade as confirmed
  physical containment. Solid collisions, terrain, water and loading checks
  remain independent requirements.
- **Shorter useful sections through narrow space.** A winding route can reach
  the native waypoint bound before the requested section length. That should
  permit a shorter connected candidate with fresh onward and endpoint evidence,
  followed by full native-curve validation. It must not authorize an unchecked
  prefix, discard the mission, or silently substitute a teleport.
- **Persistent native and live validation.** Each departure origin retains
  its own native preflight. A pending Journey sample yields without consuming
  ordinary Scenic's two-second editorial rejection allowance; the mission
  deadline still bounds the attempt. Productive selected-path proof refresh
  receives bounded priority within the unchanged five-second evidence limit.
  The fitted terminal seam is checked again after ending preparation.
- **Stable movement proposals.** One uncommitted movement step survives
  variable frame times and held-picture rendering. Proposed and retained poses
  keep separate triangle-query work. Live traversal is measured as live camera
  work; new mesh reads and hierarchy construction remain bounded background
  work, with explicit warming requests when the live view needs them.

None of these changes requires an empty frame with no trees or rocks. A forest
or rock face can be the view. The distinction is between meaningful surrounding
scenery, a near foreground veil, an actual camera intersection, and incomplete
evidence. Named arrival subjects retain their visibility requirements.

Triangle precision refers to the retained static mesh. A foliage card still
occupies its whole triangle, including holes made transparent by its texture;
these CPU queries do not sample texture alpha or track shader wind, skinning or
other vertex deformation. They remove empty-bound false positives but do not
establish which rendered pixels are opaque.

Connected search remains local and bounded to sixteen route knots per section.
An ordinary section candidate must regain net progress toward the destination
(the smaller of 8 m and 25% of its route length). A clear passage that requires
several whole sections of retreat or sideways travel can therefore still fail
to produce an admission. The constructed U-shaped detours regain progress
within one section; they do not demonstrate unrestricted world-scale routing.

## Reproduction and remaining evidence

The sanitized fixture is
[`0.8.51-observation-restart.json`](../tests/fixtures/journey-mission/0.8.51-observation-restart.json).
It retains all opening and mission outcomes, aggregate counts from the complete
capture, representative resolved graph edges, and two ordered view/projection
intervals. Coordinates share one rigid translation; times, frames and epochs
are relative. Selection rules and missing data are explicit in the fixture.

The measured projection sequence can exercise ownership and coverage decisions
without inventing geometry. Constructed corridor tests can exercise scheduling,
short sections and native admission contracts, but must be labelled as
constructed. The fixture cannot reconstruct the complete graph because resolved
edge records omit parent relationships; it also cannot reconstruct leaves or
actual carrier movement.

Runtime confirmation still requires a new synchronized capture showing a
departure, sustained measured movement through successive sections, bounded
local replanning, and the identities/precision of any foliage that stops it.
Successful compilation or a synthetic long route is not evidence that this
captured forest, or a five-kilometre Journey, now works.

The automated tests execute the production Core scheduling, ownership,
playback and view policies, plus the triangle hierarchy and native camera math.
They do not execute the Unity renderer/mesh-cache/projection adapter in a loaded
Valheim scene. In particular, actual frame cost, mesh readability, shader-driven
foliage and the complete live query loop still need captured runtime evidence.

Local validation passed with the pinned Valheim 1.0.21 / Unity 6000.0.75f1 /
Jötunn 2.30.2 references: client, server and replay compilation with warnings as
errors; 1,348 Core cases; 122 native camera-math cases; and the standalone
coverage/deadline replay with zero divergences or invalid records. CI runs the
normal project build, assembly-hook audits and Windows/Linux media/supervisor
checks before the release archives are delivered.
