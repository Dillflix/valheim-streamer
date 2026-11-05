# Journey continuation — 0.8.47

Built on the complete 0.8.46 source. The user reports that departure now starts, but travel ends shortly afterward. No new 0.8.46 capture was supplied with that report. The defects below are established by source inspection and deterministic scheduling tests; they do not identify the exact runtime termination reason for that particular flight.

## Why a successful departure could still produce a short trip

At the default 6 m/s, the old first window was 48 metres, approximately eight seconds of travel. The director committed to an ending with twelve metres remaining, giving the next route and its ending about six seconds to become ready. The route planner alone allowed twelve seconds of preparation; its ending had a separate eight-second budget. Loading constraints could postpone preparation further.

Related problems compounded the mismatch:

- A full fixed-size window had to fit before planning began, even when a shorter connected section could fit.
- The camera-body range test omitted the forward aim point, although complete validation also requires loaded geometry at that point.
- Routine ending refresh ran before urgent continuation work under the same work allowance.
- One failed preparation latched an ending decision even while certified travel remained and observations could improve.
- Expiring a queued section could leave unfinished work dependent on its discarded predecessor.
- Queued proofs were not renewed; at 2 m/s, a 32-metre section lasts sixteen seconds, exceeding the twelve-second proof lifetime.
- Final native gaze verification performed hundreds of samples outside the measured camera work allowance.

## Preparation, execution and proof ownership

Departure prepares a connected reserve of multiple fully validated sections and their finite endings before taking ownership. Ordinary Scenic footage continues during preparation. The startup budget represents multiple route-and-ending jobs and remains inside the existing bounded operator request; it grants no permission to show an unfinished route.

Subsequent window lengths fit the currently available loading reach, including space for forward gaze. Camera, aim, terrain, near-plane and foliage checks remain authoritative. A shorter proposed section must pass the same complete validation as a longer one.

Preparation timing and the amount of current certified travel determine whether the requested cruise can be sustained. Effective speed changes smoothly within a bounded range when preparation falls behind. Requested speed remains the ceiling, and diagnostics distinguish requested and effective speed. The controller does not crawl indefinitely to conceal unavailable continuation: a genuine obstruction, unrecoverable evidence loss or exhausted preparation still leads to a finite validated ending.

Urgent continuation and expiring proof work take priority over routine maintenance. Retained queued geometry is revalidated without replacing its authored path. A queue entry counts as a successor only when its predecessor, scene, proof and loading reach are current. Retiring a section also retires work derived from it. New observations may permit a bounded retry while usable runway remains; retries cannot invent clearance or renew the journey forever.

The invisible loading carrier may lead along already certified connected sections while retaining its existing distance limit from the camera. It does not move into speculative route proposals. Final native gaze verification advances incrementally under the measured camera work allowance, retaining the original geometry-proof timestamp.

## Compatibility and operation

Observer admission remains **protocol 3**. This correction requires the updated client package; the existing compatible server companion can remain installed. Preserve configuration and replace the client's three DLLs and included notices together.

Existing operator commands remain valid:

```text
broadcast capture on
broadcast journey distance 1000
broadcast journey speed 6
broadcast journey start
```

A distance request selects a real known destination at approximately that scale. It does not certify a clear kilometre in advance. `broadcast journey status` and the automatically recorded Journey events expose preparation, validated distance ahead, effective speed and termination evidence.

## Validation limits

Regression tests exercise multi-section startup, the old 48-metre timing failure, producer delays, variable local reach, stale proof handling, predecessor chains, bounded speed changes and finite endings. Native tests verify that incremental gaze checks preserve full-check acceptance and rejection. Real-reference compilation and final test counts are recorded in `RUNTIME-TESTS.md`.

These checks do not run Valheim's scene loading, network delivery, physics or render loop. In-game acceptance still requires synchronized footage and diagnostics demonstrating sustained travel across section boundaries, suitable effective cruise, exact seams, current queued proofs, and correct interruption by genuine obstruction or player priority. A passed numerical kilometre test is not evidence of a successful in-game kilometre flight.
