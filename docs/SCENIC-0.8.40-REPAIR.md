# Scenic departure and coverage repair — 0.8.40

This version builds on the complete 0.8.39 source tree. The supplied 0.8.39 logs and paired footage demonstrate a failed journey departure and ordinary Scenic pacing problems. Passing mathematical traversal tests did not establish that the live director would find and admit such a route.

Replace the three client DLLs and included third-party notices together, preserving configuration. **No server update is required.** Observer admission remains protocol 3; this repair adds no server capability. The existing 0.8.37-or-later companion continues to support optional environment permission.

## What the recording establishes

Times below are seconds on the supplied capture timeline. Plan numbers identify that capture only.

| Time | Observation and implication |
| --- | --- |
| 232.134 | Seed plan 70's original preflight rejects focus visibility at progress 0.59375. Salvage retains the readable prefix ending at 0.5625. That endpoint is immediately before a known rejected continuation. |
| 241.480–241.513 | Journey 71 begins preparation with seed 70; seed 70 is shown. The old ownership path discards ordinary prepared work before any journey section is admitted. |
| About 242.19 onward | Each of 21 continuation candidates passes its first pose, then fails a physical focus query about 0.195 m further along. The candidates share that opening geometry, so the wider heading/elevation alternatives cannot avoid the repeated failed prefix. |
| 249.522 | Seed 70 reaches its endpoint. No journey section has become available. |
| 252.507 | The departure attempt fails, with zero measured journey travel. |
| 254.600–254.684 | The old seed is last shown, then fallback plan 91 appears. The endpoint remains on screen for approximately 5.08 seconds after the seed finishes. |

The earlier diagnostic label `Focus` does **not** identify which terrain surface or solid collider blocked the sightline. It is evidence of a failed focus check, not proof of a particular tree, rock or ground defect. The new trace records the obstruction category and nearest measured hit.

Ordinary Scenic footage also exhibits a different issue. Plan 34 is a fixed-position pan at approximately 0.55 degrees/second, with its progress stopping near 0.323 between 210.099 and 210.606. Plan 39 stops near progress 0.337 between 220.676 and 223.099, about 2.42 seconds. New searches begin at 210.058 and 220.626, immediately before those stops and 10.568 seconds apart. Their old trace does not record the exact replacement/rejection cause. Source inspection finds a periodic destination refresh that discarded planning and requested shot replacement even when its feature evidence was empty or unchanged. Its timing fits these pauses, but the recording cannot conclusively attribute both pauses to that call.

Some views are intentionally slow ordinary details: plans 20 and 23 use approximately 0.333 and 0.366 m/s over a 30-second plan. They are not timelapses or failed high-speed journeys. Those duration/speed defaults are unchanged from 0.8.38 and are not changed by this repair.

## Changes and their limits

- **Retain rejected-continuation provenance.** A route salvaged before a rejected tail retains that fact through subsequent route transformations. It remains usable ordinary footage but cannot seed a journey into its already-rejected continuation.
- **Prepare without taking ownership.** Departure preparation runs alongside ordinary Scenic coverage. It keeps the current shot's movement settings and its ordinary prepared successor. Ownership transfers only when a valid first journey section is ready at the exact endpoint of the still-current seed. If it is not ready there, ordinary coverage continues; a speculative departure does not impose an endpoint wait.
- **Bound requests without changing the seed.** An explicit request can seek another eligible departure for up to 90 seconds of unheld operation. Preparation starts within the seed's final ten seconds and has its own bounded lifetime. `ShotMotion=0` leaves the ordinary seed stationary and reports that departure is waiting; it does not silently accelerate that shot. Once admitted, journey cruise remains independent of decorative strength.
- **Keep discovery from interrupting valid coverage.** Periodic destination feature refresh no longer discards ordinary route preparation or requests replacement simply because a survey refreshed. Existing live geometry and visibility checks still reject a genuinely invalid picture.
- **Remember the actual view across feature IDs.** Recent-view comparisons include world-space subject, camera/aim path, lens and composition, in both travel directions. A new feature identifier cannot by itself make essentially the same composition novel. Different neighbouring subjects and meaningful angle/scale changes remain eligible.
- **Respect the connecting-world lifecycle.** `ZNet` can exist before its `World`. The supplied startup log contains 377 null-reference exceptions in this interval. World identity now stays unbound until a world exists, instead of dereferencing it through `GetWorldUID`.
- **Record missing causes.** Accepted-plan route/motion metadata, rejected continuation details, replacement reasons and focus obstruction/hit evidence make the next capture distinguish physical blockage from discovery-driven interruption, duplicate views and deliberate slow motion.

New fixture writes use schema 2 to retain rejected-continuation provenance. Schema 1 remains readable without rewriting existing files; its continuation provenance is unknown. Downgrading to 0.8.39 cannot load newly written schema-2 fixtures. Explicit replacement preserves a backup, and existing configuration/saves are unchanged.

The 1,000 m / 6 m/s journey defaults remain. This repair does not claim a clear kilometre was demonstrated in Valheim, does not weaken visibility/terrain checks, and does not redesign every ordinary Scenic shot's duration or movement.

## Runtime acceptance

Keep synchronized capture enabled and collect its matching Scenic trace, footage MP4/JSONL and BepInEx log promptly after the sequence. See [capture locations and rotation](EDITORIAL-CAPTURE.md#capture-the-picture-and-its-decisions).

```text
broadcast capture on
broadcast camera dynamic
broadcast journey distance 1000
broadcast journey speed 6
broadcast journey start
broadcast journey status
```

| Scenario | Required observation |
| --- | --- |
| A readable route salvaged before a failed tail | Ordinary footage remains usable. The trace identifies the rejected continuation and skips journey launch from it; no repeated 21-candidate search into the same known prefix. |
| Departure fails or is not ready at the seed endpoint | The ordinary prepared successor remains available. Journey preparation does not park the completed seed or initiate recovery by itself. |
| A valid first section becomes ready | Ownership transfers at the exact seed endpoint. The first section is presented continuously and only actual captured motion earns journey distance. |
| Request with decorative motion zero; hold/resume; no eligible seed | Clear pending reason, unchanged ordinary motion, no pause-time catch-up and a bounded request outcome. After admission, changing decorative strength does not retime cruising. |
| Ordinary scenery over multiple destination refresh intervals | No periodic clearing of active/prepared work from unchanged/empty discovery. If progress stops, the capture contains its actual replacement or motion-rejection cause. |
| The same physical view appears under another feature ID | Recent-view memory suppresses the duplicate while preserving useful neighbouring subjects and changed scales/angles. |
| Connect, disconnect and reconnect | No world-identity null-reference loop before `World` exists; normal observer authorization and acquisition still work. |
| Open coast/ridge, then constrained terrain | Measure actual distance and achieved speed, not the duration label. Check continuity, visible subject, surface clearance, loading behavior and honest incomplete outcomes. |

Automated departure, provenance, novelty and native-route checks address reproducible state/geometry behavior. They do not run Unity scene streaming or establish visual quality. Build/test results belong in [RUNTIME-TESTS.md](RUNTIME-TESTS.md); 0.8.40 in-game acceptance remains pending.
