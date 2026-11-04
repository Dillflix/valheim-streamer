# Scenic pacing, sequences and arrival preparation — 0.8.41

This version builds on the complete 0.8.40 repair. It addresses ordinary Scenic movement, soft editorial interruptions, incomplete site sequences and unnecessary planning after native travel. The journey departure repair, distance objective and independent cruise remain in place.

Replace the **three client DLLs and included ThirdParty notices together**, preserving configuration. **No server update is required; observer admission remains protocol 3.** Newly written camera fixtures use schema 3; older schema-1 and schema-2 fixtures remain readable without automatic rewriting.

## Evidence and changed behavior

| Finding | Change |
| --- | --- |
| Spatial Structure Detail plans 20 and 23 in the supplied 0.8.39 capture last 30 seconds at roughly 0.33–0.37 m/s. The spatial and authored plan constructors used different ordinary timing rules. | Both use one role-aware pacing policy. Native preparation measures the actual camera and aim path before selecting duration and progress. |
| Slow translation does not imply gentle screen motion: a close-subject route can barely turn early on, then swing quickly as it passes its focus. | An immutable timing profile allocates more time to those turns while letting low-change stretches develop. Anticipation and monotone interpolation smooth rate changes without changing accepted geometry or lens. |
| A pixel concern or changed destination subject requested replacement through the same flag that stopped physically blocked movement. | Soft editorial requests continue validated movement while another composition prepares. Actual blockage, missing/unreadable output and completed routes retain their independent stop/recovery behavior. A soft request alone cannot replace a usable moving shot with a static graph-node picture. |
| Two seconds of novelty history could advance the preferred shot role before the six seconds of context required for a site sequence. | Establishing context uses actual shown footage before ordinary sequence development. Interrupted context can be completed before attempting the associated entrance, building or NPC detail. |
| Expiring region dwell could start travel during an unfinished shot. | Ordinary dwell-driven departure waits for an edit boundary for up to 15 seconds of continuous unheld operation. Recovery, content exhaustion and player priority do not wait for this editorial window. |
| The recorded relocation takes about 8.02 seconds in native travel, then 7.17 more seconds before new live footage; arrival performs two region resets in one frame. | Loaded destination observations and route preparation can run off air during native travel. Matching arrival preserves that work and performs one reset path; it does not start its survey again merely because native travel ended. |
| The operator overlay covered the already-rendered recovery card with black. | The operator sees the owned card, and intentional Scenic travel is labeled as a Scenic transition. The clean feed remains separate from diagnostic overlays. |

The capture has constant FOV within its Scenic shots; it does not establish a second lens writer. Apparent zoom can result from physical approach/retreat or an edit between shot scales. This change addresses measured motion instead of adding another lens controller.

## Movement contract

Ordinary shot proposals use role-dependent minima: eight seconds for Detail, ten for Traverse, and twelve for Establish/Reveal. These are not guarantees that every route will have that duration. Actual camera travel, angular change, lens and restrained NPC/room/opening treatment can require more time. The implementation does not uniformly halve every former 30-second shot or force an unsafe angular turn into a short duration.

Native preparation supplies a bounded time-to-distance profile, shared by preflight and playback. It preserves both route endpoints and cannot overshoot or reshape the validated path. Finite pans remain available. Operator holds and failed presentation do not create catch-up movement; existing final surface, corridor, foliage and picture checks remain authoritative.

Explicit fixture durations, live timelapse timing and constant-speed journey execution retain their timing ownership. An explicit fixture duration rescales its stored progress profile rather than changing the path. Journey speed remains independent of decorative `ShotMotion` after admission. The ordinary `ShotMotion` and dynamic/steady controls remain effective.

## Recorded-pose timing comparison

The production pacing policy was also run over the paired camera/aim poses recorded during preflight for the two supplied Detail routes. This is an interpolated recorded-pose estimate, not new Valheim footage or a complete native runtime replay.

| Capture plan | Old duration | New estimate | Old peak view rotation | Uniformly shortened peak | New profile peak |
| --- | ---: | ---: | ---: | ---: | ---: |
| 20 | 30 s | 16.80 s | 5.47°/s | 9.76°/s | 6.13°/s |
| 23 | 30 s | 20.66 s | 5.54°/s | 8.05°/s | 5.09°/s |

Plan 20's peak increases modestly against its original long duration; the profile avoids the much larger rush caused by a uniform speedup. This is a change in how development time is distributed, not a claim that every instantaneous angular speed decreases. Native spline lookup retains its existing interpolation granularity; the native regression compares the same curve and duration to ensure the timing profile does not amplify the measured baseline discontinuity.

## Arrival preparation and limits

Preparation starts only for a matching automatic Scenic travel request, after actual destination geometry is ready near the protected carrier. It is bounded to 20 seconds and scoped to world, travel token, destination and scene/carrier binding. Construction observation revisions still invalidate candidate proofs through the existing planner; they do not restart the destination survey or renew the preparation deadline.

Off-air preparation cannot move the camera or carrier, present footage, earn dwell or finish native teleportation. A successful matching arrival retains observations and staged work, then applies existing freshness and final live-pose checks. Changed requests, worlds, carrier/scene bindings, failed travel and expired work cannot adopt old preparation.

The inspected game implementation includes its own distant teleport wait. In the supplied capture, native travel starts at **190.825668**, finishes at **198.844345**, and live footage resumes around **206.014**. The two old region resets occur at that same arrival frame. Overlapping planning with the native wait can remove avoidable serial work; it does **not** prove that this entire interval will disappear. There is still one loaded rendering view, no simultaneous distant live camera and no prerecorded bridge. Unready scenes still use a clearly identified card rather than invalid or stale world footage.

## Fixture compatibility

Schema 3 stores the bounded timing profile as well as the rejected-continuation provenance introduced by schema 2. The new client reads schemas 1, 2 and 3. Reading older files does not rewrite them; explicit replacement retains the existing backup behavior. **Clients older than 0.8.41 cannot read newly written schema-3 fixtures.** Preserve an original file or backup when testing rollback. Live configuration, game saves, plugin identities and observer protocol are unchanged.

## Runtime acceptance — not yet run

Use the existing synchronized capture, with `broadcast capture on`, `broadcast camera dynamic` and the preferred motion strength. Collect matching Scenic JSONL, footage MP4/JSONL and BepInEx logs promptly; see [capture locations and rotation](EDITORIAL-CAPTURE.md#capture-the-picture-and-its-decisions).

| Scenario | Required observation |
| --- | --- |
| Structure Detail with a close focus, open landscape traverse and finite pan | Purposeful development through the whole shot, no early crawl followed by a short angular rush, stable within-shot FOV and no recurring short-shot churn. |
| NPC, entrance or confined local view | Restrained movement and readable context; limited space does not provoke forced large translations. |
| Pixel concern or destination-subject change during a valid path | Current motion continues while a replacement prepares; actual physical invalidation still stops/replaces the view. |
| Short or interrupted establishing shot near a base/trader | Actual context is completed before dependent detail coverage; repeated partial context does not indefinitely lock out useful subsequent views. |
| Ordinary region timer expires mid-shot | Departure occurs at a real boundary or the bounded extension expires; hold, player acquisition and genuine recovery retain priority. |
| Automatic Scenic travel to an already loading destination | Trace shows off-air preparation when readiness allows it, one arrival reset path, and retained matching work. Compare native travel time and arrival-to-first-live time separately. |
| Change world/request, return, stop, disable automation or lose authorization during travel | No old preparation reaches another destination; native lifecycle and isolation remain authoritative. |
| Save/replay a paced fixture; use an explicit duration; read legacy fixtures | Same route and timing shape unless duration is explicitly changed, supported legacy reads, no silent fixture rewrite. |
| RTMP and operator display during travel, recovery and first live return | Upright intentional transition/recovery card, no stale scene mislabeled live, no operator diagnostics in the clean stream. |

Build and automated results are recorded in [RUNTIME-TESTS.md](RUNTIME-TESTS.md). Policy and native mathematical tests do not establish Valheim loading latency, cinematographic quality or unattended runtime reliability.
