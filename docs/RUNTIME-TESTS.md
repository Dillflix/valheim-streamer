# Validation and runtime test record

## 0.8.48 Journey interruption repair

The supplied 0.8.47 footage/trace demonstrates a queued water-clearance contradiction leading directly to a premature Journey ending, plus matched moving footage earning zero measured distance. [Investigation and acceptance cases](JOURNEY-0.8.48-RECOVERY.md) document the evidence and repair. Deterministic tests cover changing water, subsecond travel sections, captured-frame identity, proof revocation/revalidation, queue-prefix retention, scene rebinding and mission/picture ownership. Real-assembly audits verify the water-envelope assumptions against Valheim 1.0.21. These checks do not run the Unity scene: sustained 5,000m travel at 12m/s, actual local repair cuts, forest obstruction and shoreline transitions still require captured in-game acceptance.

## 0.8.48 Windows updater audit

The updater formerly existed only on `feature-unattended-client-updates`; it was absent from the complete 0.8.47 main-line client. Its startup/bridge and host supervisor are now integrated with current observer, camera and encoder code, with startup disabled by default. Observer admission remains protocol 3. The [audit report](UPDATER-0.8.48-AUDIT.md) records demonstrated defects, fixes and the exact remaining acceptance sequence; the [setup guide](UNATTENDED-UPDATES.md) describes the required new CI-built bootstrap client.

Automated scope includes source-ancestry qualification, the current twelve-file payload and manifest, archive/path checks, filesystem transaction fault injection, unresolved-journal launch prevention, stale/mismatched heartbeat rejection, manual takeover, exact Sandboxie process ownership, and all three encoder lifecycles. Local executable Python cases pass. A Windows junction case and three real-process cases are explicitly skipped on this host: the latter require a runner whose `/proc` namespace matches reported process IDs. The normal Linux CI job executes those subprocess cases; Windows CI covers Windows filesystem/ACL behavior. Final test totals and real-reference build outcomes belong to the completed workflow for the delivered source revision.

**Actual Windows 11 / Sandboxie / Steam / Valheim cold startup, authentication, NVENC publishing, automatic update, rollback and long-run operation remain not run here.** The native adapter fixtures and Linux temporary executable do not establish those outcomes. No live installation, sandbox termination or server change is performed by the audit.

## 0.8.47 Journey continuation

The user reports that 0.8.46 now starts Journey but ends after a short trip. No new capture accompanies that report. Source inspection establishes a one-section startup reserve, fixed-window loading gates, incompatible preparation/playback timing, maintenance contention, latched preparation failures and missing queued-proof renewal. [Defects, correction and runtime acceptance](JOURNEY-0.8.47-CONTINUATION.md).

The repair prepares multiple connected sections before departure, fits continuation windows to body/aim loading reach, prioritizes work by actual runway and proof urgency, renews immutable queued geometry and retires dependent work transactionally. Cruise adapts within a bounded range to measured production timing; genuine blockage retains finite validated endings. Native gaze verification is incremental and measured. Observer protocol remains 3; no server update is required. **In-game sustained Journey travel, loading, performance and visual continuity remain unverified here.**

Local client/server/replay compilation passed against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5 / Jötunn 2.30.2 references with warnings treated as errors. **1,193/1,193 Core cases** and **97/97 native camera-math cases** passed. Deterministic kilometre timelines exercise 30/60 fps with 3.5-second, seven-second and variable producer delays, plus finite producer failure, startup reserve, predecessor/proof identity and speed bounds. Seven seconds exceeds the old six-second successor deadline. The standalone editorial-capture fixture replays with zero divergences; it does not execute the Unity planner. Normal project builds, real assembly audits and Windows/Linux media suites run in CI before packaging.

## 0.8.46 Journey departure and Scenic comparison

The supplied 0.8.45 screenshot records destination-selection failure; the retained 161.3-second trace contains fourteen ordinary Scenic admissions and no Journey events. Source inspection identifies a sparse destination feed incorrectly required to form a dense connected travel graph, plus departure prerequisites tied to unrelated ordinary-shot state. The capture does not retain the exact rejected catalogue. [Evidence, repair scope and runtime acceptance](SCENIC-0.8.46-PLANNING.md).

The correction keeps real destination identity, validates every local section and ending, prepares an independent departure, and records pending/selection stages. Normal Scenic comparison distinguishes skipped slots from completed preflights and retains composition alternatives under bounded scheduling; first-picture recovery remains fast. Protocol stays 3; no companion installation is required. Actual Valheim Journey execution, loading continuity and visual quality remain **not run here**.

Local client/server/replay compilation passed against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5 / Jötunn 2.30.2 references, with warnings treated as errors. **1,182/1,182 Core cases** and **94/94 native camera-math cases** passed. The supplied capture replays **27,382 recorded decisions with zero divergences or invalid records**; this reproduces recorded coverage/deadline decisions, not the corrected Unity planner. Normal builds, assembly audits and Windows/Linux media checks run in CI before packaging.

## 0.8.45 Scenic evaluation and editorial correction

The supplied 0.8.43 capture confirms establishing-shot dominance (13/14 admissions), early-prefetch role starvation, duplicate ruin identities and repeated full-catalogue local foliage projection. It does not demonstrate sub-second moving-shot lifetimes in the retained window. [Evidence and repair scope](SCENIC-0.8.45-EDITORIAL.md).

Real-reference client/server/replay compilation with warnings as errors passed. **1,160/1,160 Core cases** and **94/94 native camera-math cases** passed. Tests cover the production search prefix/finish contract, actual-versus-anticipated context, measured structure identity, the captured wide-cut geometry and conservative indexed foliage membership. The supplied 43,174-record capture replays **27,339 recorded decisions, zero divergences or invalid records**. Replay does not execute the corrected Unity planner or establish runtime performance/visual quality. In-game confirmation remains pending. No protocol change or server update is required.

## 0.8.44 operator coverage modes

`broadcast mode auto|scenic|player|creature|status` separates operator coverage intent from actual output and transport automation. Scenic ignores connected-player acquisition while retaining automatic destination travel; Player disables wildlife cutaways; Creature prefers eligible local actors and falls back to scenery. Native travel completes before the new intent controls the arrived scene. No server update is required; observer protocol stays 3.

Real-reference client/server/replay compilation passed with warnings as errors; **1,142/1,142 Core tests** and **94/94 native math tests** passed. New cases cover ready/distant player suppression, continued Scenic travel, in-flight mode changes, Auto restoration, safe missing-subject fallback, creature cooldowns and preference lifecycle. Console/game/rendering validation remains pending; see [operator controls and runtime checks](OPERATOR-MODES.md).

## 0.8.43 Scenic acquisition repair

The supplied 0.8.42 recordings are a failed runtime result: 492 of 517 mapped frames show a slate, and first live output follows two relocations, about 41.8 seconds after region setup. No Journey starts. The trace demonstrates demand-stepped independent picture admission that never completes its cold foliage projection, plus obsolete catalogue traversal after relocation. [Evidence, repair and limits](SCENIC-0.8.43-RECOVERY.md). The exact 0.8.42 change that triggered this sequence remains unresolved; the user’s before/after regression is accepted.

0.8.43 retains and advances pending picture validation independently, and retargets obsolete catalogue work while preserving unknown additions. The missing-picture deadline remains ten seconds. Local client/server/replay compilation against real references passed with warnings treated as errors; **1,132/1,132 Core cases** and **94/94 native math cases** passed. The supplied 0.8.42 trace replays **4,893 recorded decisions with zero divergences**. Replaying the old decisions does not simulate corrected Unity output. In-game validation is pending. No server update is required; observer protocol remains 3.

## 0.8.42 Scenic destination journeys

Built on the complete 0.8.41 source. The supplied Journey 19 footage establishes roughly 145.45 m of travel with a fixed backward gaze, followed by about 32.7 seconds at the endpoint. Its trace reports zero achieved distance because the production adapter rejects the preceding completed render during Update. It also shows ordinary planning beginning cold after the continuation fails. This is failed 0.8.41 runtime evidence, not validation of the replacement.

0.8.42 selects a named catalogue destination and itinerary, directs forward gaze separately from movement, admits a fully validated departure and an ending with every section, and keeps ordinary local planning active. Captured position/progress/time and exact render identity drive distance and arrival credit. Missing output breaks continuity even for a one-frame gap. Replay retains compatibility with 0.8.29–0.8.41 captures and admits 0.8.42.

Regression scope includes connected/stale catalogue selection, curved route progress and real arrival, forward/developing native gaze, section/ending continuity, braking commitment, and captured-frame ordering at 60 fps using reduced numeric evidence from the supplied recording. Local client/server/replay compilation against the real references passed with warnings treated as errors; **1,126/1,126 Core cases** and **89/89 native camera-math cases** passed. The standalone editorial-capture replay reports zero divergences. These checks do not execute Valheim's full loading/render loop. **Subsequently supplied 0.8.42 footage failed ordinary Scenic acquisition; see the 0.8.43 evidence above.** The [runtime acceptance guide](SCENIC-0.8.42-JOURNEYS.md#runtime-acceptance--not-yet-run) defines the required paired footage/trace checks. **No server update is required; observer protocol remains 3.**

## 0.8.41 Scenic pacing, sequences and arrival preparation

Built on the complete 0.8.40 source. Ordinary route timing now measures camera travel and angular development instead of applying the old representation-dependent 20/30-second rules. Soft editorial requests continue valid movement while replacement planning runs; actual invalidity still stops movement. Site sequence progression requires six seconds of actual establishing context. Ordinary dwell relocation waits for an edit boundary for at most 15 seconds of continuous unheld operation. Already-loaded destination work can prepare off air during native travel and survive only its matching arrival, with the duplicate reset removed. The operator displays the owned transition/recovery card instead of hiding it with black.

The supplied 0.8.39 relocation starts at **190.825668**, completes native travel at **198.844345**, and resumes live footage around **206.014**. This separates about **8.02 seconds of native travel** from **7.17 seconds of subsequent acquisition/planning**. Both old region resets occur in that arrival frame. The repair overlaps planning only after real destination readiness; it does not bypass the game's teleport/floor checks or claim zero loading gaps. Captured per-shot FOV is constant, so no unsupported extra lens-writer fix is claimed.

Regression scope covers role-aware monotone pacing, native camera/aim profile consistency, explicit timing and journey preservation, fixture schema-3 round trips and legacy reads, soft/hard replacement escalation, site context, bounded relocation and scoped arrival preparation. Local real-reference client/server/replay compilation passed with warnings treated as errors; **1,076/1,076 Core cases** and **84/84 native camera-math cases** passed. This includes paced fixture replay and legacy constant-speed geometry. Normal project builds and the Windows/Linux media suites run in repository CI. **Valheim runtime validation is not run here.** The [quality guide](SCENIC-0.8.41-QUALITY.md#runtime-acceptance--not-yet-run) lists the required paired-capture scenarios. Newly written schema-3 fixtures require 0.8.41; existing schema-1/2 files are not automatically rewritten. **No server update is required; observer protocol remains 3.**

## 0.8.40 Scenic departure and coverage repair

Built on the complete 0.8.39 source tree. The user's supplied 0.8.39 capture is a failed runtime result: journey 71 earns zero distance after trying 21 continuations from a salvaged seed whose following focus geometry was already rejected. The current endpoint remains shown for about 5.08 seconds before fallback. The supplied startup log also contains 377 null-reference exceptions while `ZNet` exists before its `World`.

Corrections preserve rejected-tail provenance, defer journey ownership until a validated departure meets the seed endpoint, retain ordinary preparation during failed attempts, bound explicit pending requests, remove destructive destination-refresh preemption, compare actual recent compositions across feature IDs, and read world identity safely during connection. Accepted-plan, motion-rejection and focus-hit metadata address missing causal evidence. Observer protocol remains 3; **no server update is required**.

Ordinary plans 34 and 39 also pause mid-shot in the capture. The periodic destination-refresh source defect is consistent with their timing, but their old trace does not establish the exact cause. Slow 30-second Structure Detail plans are separately evidenced and remain unchanged; this repair is not a claim that all Scenic pacing or composition is now polished. See [the timeline, changes and runtime matrix](SCENIC-0.8.40-REPAIR.md).

Automated regression scope includes rejected-continuation provenance through transformations, background departure admission/abandonment, held and expired requests, ordinary prepared-successor retention, cross-ID composition novelty, and existing distance/native journey behavior. Local real-reference client/server/replay builds passed with warnings treated as errors; **1,054/1,054 Core cases** and **78/78 native camera-math cases** passed. All eight supplied footage segments decoded with the expected **2,294 mapped frames**, with no incomplete segment. Normal project builds, hook audits and Windows/Linux media suites run in repository CI. **0.8.40 Valheim runtime validation is not run here**; it requires the paired capture scenarios in the repair guide. The mathematical kilometre tests from 0.8.39 did not validate its failed live departure.

## 0.8.39 distance-directed Scenic journeys

Built on the complete 0.8.38 source tree. The former 180-second / 1 m/s default and decorative `ShotMotion` multiplier limited expected travel to about 180 m, or 90 m at half strength. This version uses a 1,000 m objective and independent 6 m/s cruise, goal-directed local sections, gradual observed-terrain clearance and earlier preparation. The protected loading anchor can lead on the current accepted route while preserving existing carrier/world readiness checks. New configuration keys prevent a preserved old speed from retaining that mismatch. Observer protocol stays 3; no companion update is required for these journey changes.

Automated regressions cover measured kilometre traversal at 30/60 fps, native distance-based path execution, independence from decorative motion strength, exact section seams, bounded per-frame displacement, actual versus authored distance, stale/held/missing samples, loop rejection, false short completions, distance-aware remaining windows and terrain-clearance envelopes. These checks do not execute Unity scene loading, physics, rendering or the production frame loop. Local validation passed **1,039/1,039 Core cases**, **77/77 native camera-math cases**, and the competing-deadlines replay fixture with zero mismatches. Client, server and replay compiled with warnings treated as errors against the pinned real game/dependency assemblies using the isolated SDK compiler. The normal project builds and Windows/Linux media checks run in CI. Subsequently supplied Valheim footage demonstrates a failed zero-distance departure; see the 0.8.40 evidence above. No local Valheim execution is claimed.

Use protected-observer Scenic coverage with the existing synchronized capture enabled:

```text
broadcast capture on
broadcast camera dynamic
broadcast journey distance 1000
broadcast journey speed 6
broadcast journey start
broadcast journey status
```

After completion or failure, preserve the matching Scenic JSONL, footage MP4/JSONL and `BepInEx/LogOutput.log` from the broadcaster installation/profile. The [capture guide](EDITORIAL-CAPTURE.md#capture-the-picture-and-its-decisions) explains their locations and bounded rotation; collect them promptly. A diagnostic screenshot of the retained journey status is useful alongside the footage.

| Scenario | Required evidence |
| --- | --- |
| Open coast/ridge, 1,000 m at 6 m/s, sustained 30 then 60 fps | Actual travel reaches approximately 1 km and forward advance reaches at least 650 m before `Completed`; nominal cruise remains near 6 m/s between ramps, with no repeated section cuts or brief velocity spikes. A known obstruction may legitimately produce an explicitly incomplete outcome. |
| After journey admission, repeat with `broadcast camera motion 0`, `0.5` and `1.5` | Active journey cruise remains independent of decorative strength. Restore the preferred value afterwards. New configuration keys retain 1,000 m / 6 m/s even when old journey settings remain in the file. |
| Hold during a pending continuation or at a section endpoint, wait over 12 seconds, then resume | No elapsed pause becomes catch-up motion or an instant planning/endpoint timeout. Held output earns no distance; the route resumes only from valid current evidence. |
| Ridge rise, coast descent, loaded-edge approach and dense forest | No terrain/water penetration, unchecked corridor jump or behind-camera carrier teleport. Loading evidence waits are distinct from known physical obstruction; any short failure retains distance and its real reason. |
| Temporary picture loss, stale render identity or missing captured pose | No invented travel credit or successful completion; the retained outcome and diagnostics expose the gap. |
| Player becomes available, operator stop/return, world exit and reconnect | Prompt ownership handoff, no stale successor/world reuse, no false completion or changed observer admission/isolation. |
| RTMP audio throughout a long journey and several region boundaries | Continuous camera/listener alignment, unchanged A/V transport, bounded query/frame cost and no recurring recovery churn. |

A successful policy/native test is not evidence of successful in-game kilometre streaming. The measured runtime results above determine whether the new journey family meets its intended behavior.

## 0.8.38 live Scenic timelapse

Built on the complete 0.8.37 source tree. Meaningful regressions cover matching rendered-plan progress, stale/held output, permission/owner preemption, bounded preparation and picture loss, editorial spacing, and joining the moving natural clock at both phase endpoints. Matching real-game-reference builds and existing policy/native/media CI remain required. Actual Valheim lighting, native sky smoothing, dark-scene fallback and player-join restoration remain **not run here**. See [runtime acceptance](LIVE-TIMELAPSE.md#runtime-acceptance).

## 0.8.37 Scenic production capabilities

Built directly on 0.8.36 (`0de075d3213e76e3abd41c4ce627c4f071ed49a7`). The new cases cover route completion at reduced speed, exact initial/native window admission, bounded fixture preparation and player preemption, fixture persistence and validation, render-pose/identity comparison, expiring empty-server permissions, and real FFmpeg timelapse frame timing, orientation, storage and failure handling. Existing Core, native camera mathematics, real-game hook, RTMP, diagnostic video and replay suites remain required. Project builds use the matching real game assemblies with warnings treated as errors; repository CI provides an independent build and packaged artifacts.

Actual Valheim long-distance terrain streaming, visual seam quality, seven-day operation, render-hook order on the user's graphics backend, empty-server join/revocation behavior, and Windows/Sandboxie GPU capture remain **not run here**. Automated policy/encoder results do not establish these outcomes. Use the specific [runtime acceptance matrix](SCENIC-PRODUCTION.md#validation-and-remaining-runtime-checks) and synchronized footage/diagnostics.

## 0.8.36 broadcast integration

Built directly on 0.8.35 (`a9fbc66`). See [BROADCAST-INTEGRATION.md](BROADCAST-INTEGRATION.md) for the connected changes, deterministic regression scope and runtime acceptance scenarios. The client and server build against real matching assemblies; the existing protocol-3 companion remains compatible. Visual quality, Unity camera/readback lifecycle, Windows NVENC and Sandboxie runtime results remain pending.

## 0.8.35 broader encounter and crowd coverage

Built directly on 0.8.34 (`0df83a4798b3d40e8124e14fe440dba97aad4fdc`). Adds hostile presence/developing-pressure observation, a larger representative cast with per-shot framing, explicit encounter coverage preference, and actor-to-actor body occlusion. Source identities remain attached through prediction, actual player coverage and synchronized footage. No new server capability or observer protocol is introduced.

Local validation passes **930/930 Core cases**, **64/64 native camera-math cases**, **31 real-assembly audits**, **91 diagnostic-capture/FFmpeg checks**, **5 footage-review tests**, and all three replay fixtures with zero mismatches. Client and companion compile with warnings treated as errors against real matching references. The isolated SDK compiler was used locally; normal project builds and Windows/Linux media tests run in repository CI. Actual Valheim crowded combat, body-silhouette accuracy, camera transitions, Sandboxie operation and frame cost remain **not run here**. Use the [encounter runtime matrix](ENCOUNTER-COVERAGE.md) and automatic paired footage/diagnostics.

## 0.8.34 synchronized capture and editorial coverage

Built directly on 0.8.33 (`3a3f425dd1e70e521cff09e8aee3b807bea75320`). The change adds independently admitted Scenic pictures, local trader/context coverage, observed action phases, bounded opponent/quarry perspectives and paired diagnostic video/frame timelines. Client/server protocol behavior remains unchanged; no new companion deployment is required. Use the [capture instructions and runtime acceptance matrix](EDITORIAL-CAPTURE.md) to collect comparable footage and verify the picture, actual presentation credit, fallback duration, NPC framing, action timing, return continuity, resource bounds and unchanged RTMP audio.

Valheim runtime visual quality, GPU/readback overhead, Sandboxie capture behavior and long-run broadcast performance remain **not run here**. Pure policies, encoder fixtures and real-assembly compilation are separate evidence and do not establish those outcomes.

Local validation: **876/876 Core cases**, **64/64 native camera-math cases**, **91 diagnostic recording/real-FFmpeg checks**, and **5 footage-review tests** passed. The generated review page's JavaScript also passed syntax checking. Client and companion compile with warnings treated as errors against matching real Valheim/BepInEx/Jötunn references. Local SDK command-line startup is affected by this build environment's process metadata, so direct SDK compiler validation was used locally; the repository CI performs the normal project builds, Windows/Linux media tests and artifact staging. Encoder fixtures verify sampled-video timing, frame-map/decoded-count agreement, storage bounds and failure isolation; they do not execute Unity readback.

## 0.8.33 destination variety and purposeful structure coverage

Built directly on 0.8.32 (`c6ab74ca94f0de2f4618aaec3e924486adffe3f6`). Fixes unconditional structural preference, sampled-zone-centre coastline loss, and destination purpose being discarded at arrival. Optional interest-v2 metadata preserves legacy endpoints and observer admission protocol3. Local validation passes **828/828 Core cases**, **64/64 native camera mathematics cases**, and client/server compilation against matching real Valheim assemblies with warnings treated as errors. Regression cases cover highly imbalanced catalogues, bounded completed/attempt history, biome/source/landscape balancing, uncredited proximity, player preemption, dry coast sampling, v1/v2 packets, and subject-specific footage. See [destination runtime checks](WORLD-DESTINATIONS.md) for validation with real coastlines, ruins, camps and sprawling player construction. Visual results, live handshake fallback and production indexing cost remain untested here.


## 0.8.32 server-guided destinations

Built directly on 0.8.31 (`182f1520016165f26f780e5ad32bef1f55ad4f73`). Adds optional protocol-1 interest metadata/feedback alongside unchanged observer admission protocol 3. Local SDK-compiler validation: **791/791 Core cases and 64/64 native camera math cases pass**; client and companion compile against the real Valheim 1.0.21 / Unity 6000.0.75f1 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references with warnings treated as errors. New cases exercise bounded wire parsing, atomic/deletion-aware pages, source freshness, small/sprawling/vertical construction clusters, deterministic IDs, fallback/ranking constraints, world-specific persisted cooldowns and actual-footage visit outcomes. Use the [destination runtime procedure](WORLD-DESTINATIONS.md#inspect-and-test) for solo discovery, base/landmark arrival, feedback, restart, deletion, interruption and older-companion fallback. No production-host installation, Unity runtime performance or resulting visual footage has been validated here.


## 0.8.31 foliage catalogue repair

The supplied 0.8.30 capture fails: approximately 37.15 seconds without a usable picture across two recovery relocations; 1,081 incomplete foliage results and twelve empty candidate searches. See [the recorded evidence and repair limits](SCENIC-EVIDENCE.md). Do not count the preceding policy-replay match as a successful runtime outcome.

0.8.31 must be checked in the same dense forest: catalogue empty/repaired/unresolved counts, unknown cells, local survey radius, accepted viewpoints, time to the first picture, recovery share and scan cost. A distant missing/empty renderer must not zero unrelated camera coverage. A genuinely unresolved nearby volume must remain blocked. Verify thin foliage and actual visible leaves remain detected, preferred-LOD fallback does not omit trees, and stop/travel/rejoin cannot retain a previous region's uncertainty. Existing terrain, water, near-plane, focus and full-route checks remain active. Automatic capture includes anonymous bounded renderer-bounds examples; no repeated console commands are needed.

Local build/math/policy validation is distinct from Unity rendering and client gameplay; no game session is available on the build host. Server protocol and behavior are unchanged and no server installation is required.


## 0.8.30 observation scheduling repair

Based directly on 0.8.29 (`24552cbef081c867d43164d64a207784ec748018`). All three supplied capture sets replay without decision divergence. They demonstrate detailed-neighborhood misses behind the half-second survey throttle, repeated candidate deadline exhaustion, and a long sequence of recovery-driven region changes. They also contain legitimate obstruction and acquisition failures; this is not proof that every rejected route should be accepted. See [SCENIC-EVIDENCE.md](SCENIC-EVIDENCE.md) for measured counts, the timeline and the 0.8.29 owner-label ambiguity.

Local validation: **768/768 core cases**, **57/57 native camera math cases**, and warnings-as-errors client/server compilation against the matching real assemblies. New regressions use reduced real capture inputs for canopy lifts and wide viewpoint changes, verify independent bounded discovery, revision invalidation, unchanged deadlines and truthful travel causes. The replay CLI supports both capture policies. No live Unity session has yet run this new build, so actual latency, local projection cost, foliage correctness, recovery share and cinematography remain runtime acceptance items.

The change introduces scene-owned foliage discovery and immediate local projection; it does not raise timeouts, weaken physical/visibility gates or change observer protocol 3. No server deployment is required.

## 0.8.29 automatic Scenic evidence

Built directly on 0.8.28 (`da0c7ed99884c34dc418d95dae39778091001ea6`). The supplied 0.8.28 log confirms the correct plugin/protocol loaded, successful observer authorization, RTMP publishing and recurring incomplete foliage surveys. The recordings still show long recovery intervals. Those logs do not identify the underlying coverage/scheduling cause. This build extracts existing coverage/refresh/deadline decisions into replayable Core functions, records their runtime inputs and outputs, and adds a bounded asynchronous JSONL writer plus standalone replay CLI. Camera admission/route/travel behavior and protocol 3 are preserved.

Local validation: **763/763 core cases**, **57/57 native math cases**, warnings-as-errors client/server compilation against the real matching assemblies. New tests exercise recenter throttling versus truncated coverage, competing deadlines, divergent/incomplete captures, invariant JSON encoding, actual file rotation/drain and I/O failure isolation. The replay fixture is synthetic. No Unity runtime capture with this build is available yet, so neither a root cause nor a Scenic quality improvement is established. See [SCENIC-EVIDENCE.md](SCENIC-EVIDENCE.md) for collection and the explicit limits of offline replay.

## 0.8.28 shared spatial planning

Built directly from 0.8.27 (`025fe857eac396c4027d7bb1d4c98960cc6df433`). This is an architectural implementation, not a claim that the earlier scenic recordings now pass. Scenic production constructs routes from measured clear viewpoints/corridors; Player/Creature orbital coverage adds bounded adaptive future planning; construction observations supply local hierarchical context. Full native path validation, actual output evidence and observer protocol 3 remain required. No server update is required.

Local validation: **757/757 core cases**, **57/57 actual native camera math cases**, and warnings-as-errors Core/client/server compilation pass. Real references remain Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2, Cinemachine 3.1.7 and selected Splines 2.0.0 source. The local runner uses the SDK compiler because this host's normal MSBuild process-metadata query is unavailable; the artifact workflow performs normal builds. Tests exercise unknown/stale/disconnected space, search bounds, winding routes and slices, actual native spline distance/aim parity, adaptive actor prediction and reachable guidance, hierarchy identity/partial coverage, and parent/child sequence evidence. These are policy/math checks, not Unity physics, rendered scenes, component lifecycle or a Windows gameplay session.

Runtime acceptance remains pending:

| Scenario | Required observation |
| --- | --- |
| Recorded forest, open clearing, ridge/coast and hillside | Valid above-surface viewpoints; useful moving routes, readable subjects, fixed within-shot lens; record shot duration/recovery share across several region changes |
| Winding path around geometry | No skipped bend, wall traversal, near-plane clipping, abrupt speed or aim discontinuity; verify native and authored comparison modes |
| One building, extended hall, clustered village, sprawling base | Scale follows measured extent; parent/child and sibling coverage when visible; partial loaded coverage is not presented as a complete survey |
| Add/remove construction while broadcasting | Place revision changes, future plans invalidate, active valid footage remains usable; no stale building/corridor permission |
| Player/Creature moving straight, turning, fighting or stopping | Smooth bounded correction; predictive motion does not mirror target movement instantly, drift through walls or reset each replan |
| Room, roof, door and exterior/interior transition | Retain native confinement/composition; short horizon in constrained space; unknown evidence cannot force a good current view into recovery |
| Hold, mode change, travel, return, stop and reconnect | No stale plan/world reuse; correct displayed-plan proof; protocol 3 authorization and simulation isolation unchanged |
| Long-running dense scene with RTMP audio | Measure main-thread/frame cost and cache/query diagnostics; no unbounded growth, repeated search starvation or transport regression |

No local or production game installation, restart or live deployment is performed. Cinematic quality, performance and long-running behavior require the runtime evidence above.

## 0.8.27 scenic route recovery and selection

Built directly from 0.8.26 (`e3d9b93114ac63c05fa8146b81727833950614d9`). The user reports most 0.8.26 shots now work, with intermittent long recovery. The supplied `14-38-21` recording lasts 14.436 seconds; recovery occupies approximately 0.050–14.419. Sampled overlays show routes91→96 accepted0, a restarted search2→14 accepted0, then travel to Meadows at about13.5s. RTMP remains publishing. The recurring reason is camera intersection with retained vegetation bounds; two samples incorrectly pair Focus with that reason. The clip does not distinguish real leaf occupancy from conservative false-positive bounds and does not show the preceding successful shots.

Source changes address the underlying proposal/rejection behavior and the separate duration defect. The path evaluator scans for useful connected clear sections after invalid opening/middle poses, without bridging an invalid corridor. Native measured endpoints/aims are preserved in regenerated proposals and fully revalidated. One measured canopy-height alternative can replace an otherwise repeated guessed elevation. The first usable candidate can restore output immediately. Missing-picture recovery is ten seconds across plans, sweep retries, readiness mode changes and failed ordinary creature admissions; expiry does not add another ten-second loading wait. Actual readable retained footage remains valid regional coverage. Ground/water/visibility/collision gates are retained. Diagnostics expose elapsed missing output and distinguish focus from foliage reasons.

Local validation: **674/674 core cases and 45/45 native camera/math cases pass.** Validation covers pure interval continuity, invalid/unknown evidence, measured native versus authored parameterization, complete native route math, bounded canopy proposals, output-clock resets/holds, readiness churn, travel cooldowns and destination restrictions. Local core/client/server builds use warnings as errors and the matching real references. CI additionally runs the repository's real-assembly structural and Windows/Linux streaming checks. These checks do not execute a Unity scene or establish visual acceptance.

**Runtime validation remains pending:** revisit the recorded dense forest, and compare successful shot duration with total recovery share over at least several region changes. Check counters for measured sections and canopy alternatives; preserve footage and diagnostics if all fail. Verify no underground/water views, foreground-leaf acceptance, rapid cut sequences or lens jumps; inspect movement through recovered sections and their final live first pose. Exercise a readable retained predecessor while its successor fails, temporary readiness changes, creature admission failure, operator hold, disabled scenic travel, no fresh destinations and travel cooldown. The ten-second local budget is not an end-to-end maximum slate duration when travel/loading is required. Measure dense-scene CPU cost. No live game or server is installed/restarted here; the existing protocol-3 server can remain.

## 0.8.26 scenic terrain/water placement

Built directly from 0.8.25 (`fa965793ca3306a1dd3ef32563f78d4aa6b7f80e`). All three supplied recordings were inspected. `11-25-28` is recovery for 9.76 of 9.83 seconds, exhausts 96 routes and travels; `11-25-41` shows continuous Scenic footage from below terrain for 36.26 seconds, including successive accepted shots; `11-27-50` is recovery for 10.80 seconds, retries exhausted searches and then displays another underground scenic view. Rejection overlays include camera inside vegetation bounds, hidden focus and solid camera overlap. These are failed runtime results, not successful scenic production or merely a creature-mode misunderstanding.

The missing invariant was camera elevation relative to the actual terrain/water underneath its own footprint. Factory routes used the distant feature's Y; heightmap existence and shell-overlap checks did not prove the camera was above it. A second defect allowed terrain sightline hits inside rock/structure bounds to count as the intended subject surface, including buried centres. Whole-tree renderer bounds also classified open air beneath crowns as leaves.

Corrections: downward queries from above the actual loaded terrain collider; actual active water heights; shared camera-body/near-plane clearance policy; immutable whole-route elevation fitting followed by complete selected-native-path validation; hard live-pose rejection when surface clearance fails; exposed object focus; no terrain exemption in object sightlines; bounded local triangle-group/submesh foliage refinement. The first valid initial route may be admitted immediately, while later prefetched alternatives still undergo editorial comparison. The fitted route, not its original proposal, is used consistently for trimming, staging and playback. There is no runtime Y clamp, new render camera, additional collider/physics scene, modified mesh/readability flag, world generation or protocol change.

Local validation: **640/640 core cases**, **37/37 native camera/geometry math cases**, and warnings-as-errors core/client/server compilation pass against real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references. New fixtures specifically cover below-ground body placement despite a loaded tile, water/waves, pitched and wide near planes, a raised bank under a corner, unknown adjacent terrain, immutable elevation/trim behaviour, native spline parity after fitting, empty space beneath a tree crown, retained triangle extents and rotated/nonuniformly scaled foliage bounds. The actual adapter consumes the tested footprint/clearance policy. Managed tests do not execute Unity physics, rendered foliage, native component lifecycle or the user's Windows runtime. The artifact workflow supplies normal builds, real-assembly audits and existing Windows/Linux streaming checks; inspect that completed run for CI evidence.

**In-game validation is pending.** Revisit the recorded Black Forest terrain/rocks, including an uphill camera position relative to a lower target, partially buried/submerged rocks, shore/waves and open space beneath tree crowns. No body or near-plane position below its measured surface should be admitted. Verify sustained scenic movement, unchanged within-shot lens, no vertical bobbing from terrain adjustment, safe retention/replacement and a substantially smaller recovery share. Also check dense-forest CPU time, stop/rejoin cleanup and unchanged Player coverage. Foliage precision depends on actual asset readability; unsupported meshes still use conservative bounds. Complex terrain fits can safely reject on the existing sample/elevation budgets. No local or production game was launched, updated or restarted. No server update is required; observer admission remains protocol 3.

## 0.8.25 scenic search and idle handoff repair

Built directly from 0.8.24 (`3230c18b1c990c1149883d9076527c17612a39e0`). The supplied 29.23-second recording is a failed runtime result. Visible forest fragments are labelled **Creature**, while black intervals carry the operator recovery label **Scenic**. Black intervals include approximately 1.79–4.06s, 7.81–10.12s and 13.83–19.03s. This footage does not demonstrate successful directed scenic playback. Its overlay does not include the candidate rejection diagnostics needed to establish the exact first route failure. The reported brief zoom is not established as a fixed-lens scenic-plan zoom; the visible mode is the existing actor/creature camera pipeline.

Established source defects and corrections:

- Dedicated scenic screening could be interrupted by a generic three-second failure fallback before its bounded search completed. Fallback now requires explicit search exhaustion. Ordinary creature coverage permits background scenic preparation, but prepared shots cannot activate off air.
- Loaded creatures could go on air with physical camera support but no readable subject, then time out into scenic recovery. Off-air preview admission and final live creature validation now require readability. The preview is bounded, separate from the player rig, and suspended while a creature is on air.
- Region reset/readiness decisions preceded consumption of matching rendered-frame evidence; incidental idle readiness loss could also erase search progress and creature cooldowns. Proof is consumed first, and the broad reset is limited to leaving distant player coverage (completed observer travel already resets explicitly).
- Recovered picture failure remained latched. Fresh readable output now cancels only obsolete missing/unreadable-picture replacements, preserving operator edits, completed shots and persistently blocked routes.
- With a busy survey, the 96-attempt budget could try only wide establishing routes. The bounded schedule now considers all applicable roles within its feature batch before spending more attempts on azimuths; later searches rotate omitted features. Clearing routes use their actual measured horizontal footprint.
- Throttled foliage cache coverage was treated as permanent route rejection. Incomplete coverage now requests an appropriate refresh and retries the identical sample for at most two seconds total per route; it does not bypass collision or foliage checks.
- First-pose validation of a new scenic plan discarded the preceding valid pose. The execution rig retains and revalidates that old live composition until a replacement passes, reports the old plan identity, and cannot credit it to the new shot. Initial scenic admission now also satisfies the geometry predicates required for continuation.

Local validation: **626/626 core cases**, **30/30 native camera math cases**, and warnings-as-errors core/client/server compilation pass against the same real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references. New core cases cover cancelled recovery, retained mandatory edits, search completion versus wildlife opportunity, role-budget coverage, clearing placement and readable wildlife admission. These tests do not execute Unity scenes, physics, rendering, component lifecycle or multiplayer. The artifact workflow supplies normal builds, real-assembly checks and the existing Windows/Linux streaming checks; consult its completed run for CI results.

**0.8.25 runtime validation remains pending.** Revisit the recorded forest/clearing, then open meadow, hillside/coast and a rock/structure. The operator recovery display now includes scenic route count and last rejection. Confirm sustained real Scenic playback, distinct applicable shot roles, no repeated Creature/Scenic recovery cycle, no unreadable wildlife admission, and no new-plan credit for retained old footage. Test wildlife return, temporary area-object churn, genuine geometry loss, observer travel, stop/resume and ordinary player coverage. Capture both operator and RTMP output. Neither geometry tests nor the presence of four shot roles establish engaging cinematography; visual quality, performance and long-running behavior still require observation. No server update, live installation or restart is required/performed; admission remains protocol 3.

## 0.8.24 dedicated scenic production

Built directly from 0.8.23 (`355292ab5179e431984542b4b3f6cb397e50eed0`). The user's continued static/jarring-scenery feedback concerns shot design as well as rejection handling: generic short movement was stretched over a 45-second interval, while actor camera settings could compete with environmental presentation. The new scenic subsystem measures loaded environmental feature hints, plans purpose-specific immutable routes with a fixed lens, derives duration from travel/visual-change estimates, stages upcoming shots and cuts deliberately between them. Player/creature directing, observer protocol 3 and audio/video transport retain their existing boundaries. **No server update is required.**

Acquisition and playback execute the same selected Body/authored-aim solve and pose/corridor validator: native Dolly by default, with an authored-curve comparison latched into newly planned shots when Dolly is disabled. Adaptive route screening includes physical near-plane sweeps, foliage/feature readability, loaded geometry and carrier reach. The first scenic route must also pass readability. Useful prefixes are independently regenerated and revalidated. Actual matching live output commits progress; persistent blocked movement requests local replacement after two seconds instead of merely holding to the end of the shot. Geometric feature classification and foliage bounds remain approximations, and staged validation cannot prevent later scene changes.

`Idle.ScenicSeconds` remains serialized for compatibility but now controls wildlife opportunities during scenic coverage, not individual shot duration. Normal directed shots typically last 8–20 seconds at default speed, with longer routes retaining a speed bound. Steady/motion-zero hold movement while shot timing continues; operator hold pauses timing and edits. New-shot composition is latched, and FreeLook/actor pacing cannot change a directed scenic lens or route.

Local validation: **610/610 core cases pass**, including 24 dedicated scenic-director cases and ten factory/coverage integration cases. **30/30 native camera math cases pass**, including actual native route/distance parity for all four roles, authored comparison routes, fixed-lens and trim preservation, and useful early/steady motion. Core, client and server compile with warnings as errors against real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references; Cinemachine remains 3.1.7 with selected Splines 2.0.0 source. Local builds use the SDK compiler because the host normal MSBuild process-metadata query is unavailable. The artifact workflow performs normal builds, repeats core/native tests, audits real assemblies and runs Windows/Linux streaming checks; use the completed workflow linked with the delivered artifact for CI evidence. No Unity scene, component lifecycle, physics, graphics backend or live multiplayer session is executed by these logic/math checks.

**Valheim visual and performance validation remains pending.** Run the [directed scenic acceptance matrix](CAMERA-DIRECTOR.md#directed-scenic-acceptance-0824) using matched recordings of the earlier hillside, meadow, dense forest/clearing, coastline/ridge and rock/structure scenes. Verify visible steady development, fixed lens per plan, purposeful scale differences, intentional cuts, staged replacements and bounded blocked recovery; include pause/resume, world/geometry loss, mode changes and continuous clean RTMP audio/video. Diagnostics and pure/native-math tests cannot establish perceived cinematic quality or GPU/CPU cost. Install the three client DLLs and included notices together, preserving configuration. No live installation or server restart is performed by this source change.

## 0.8.23 scenic continuity and motion repair

Built directly from 0.8.22 (`7bda56a63a26930da1f37c76db97b9986f475a97`). The supplied 48.6-second scenic recording shows repeated pauses and brief lateral/panning reframes of a clear snowy hillside, beyond the separately reported rapid replacement/travel cascade. Source inspection found three interacting causes: blocked future motion invalidated otherwise readable current footage; actor pacing and sequence lens changes competed with the longer authored scenic path; and full area-object acquisition checks could revoke already-rendering scenery during background loading. These findings explain plausible failure paths; the recording alone does not identify each internal trigger.

Scenic paths now use a stable configured lens and visibility-driven alternative selection, with one validated body path. A brief eased acceleration/deceleration surrounds steady cruise motion, and paused/blocked progress is consumed without catch-up. Readable held movement continues to count as useful live footage. Persistent picture failure requests bounded local replacement; recovered pictures cancel obsolete requests, while exhausted recovery can still escalate to regional travel. A fresh exact-plan presentation lease permits continued loaded-geometry checks without weakening new-shot acquisition, world/carrier identity or actual geometry requirements. Existing actor features, streaming transport and observer protocol 3 are unchanged. **No server update is required.**

Local validation: **576/576 core cases pass**, including 29 new cases for scenic availability versus useful dwell, recovery/search lifecycle, motion timing and exact-plan readiness. **21/21 native camera math cases pass**, including the native spline cruise profile. Core, client and server compile with warnings as errors against real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references. The client retains pinned Cinemachine 3.1.7 and selected Splines 2.0.0 source. Local validation uses the SDK compiler because this host's normal MSBuild process metadata query fails; the artifact workflow performs normal project builds, repeats tests, audits real assemblies and runs the Windows/Linux streaming suites. Use the completed workflow linked with the delivered artifact for its CI evidence.

**Valheim visual validation remains pending.** Compare the same hillside with the supplied recording and exercise the expanded [camera matrix](CAMERA-DIRECTOR.md#runtime-validation): continuous scenic development, blocked movement with a clear current picture, transient/persistent obstruction, exhausted local alternatives, scheduled novelty, pause/resume, background replication and true geometry/world loss. Inspect both the operator and RTMP picture; passing logic/math checks does not establish smooth rendered motion. Install all three client DLLs and the included notices together, preserving configuration. No live installation, server restart or public release is performed here.

## 0.8.22 encounter direction and pass-by shots

Built directly from 0.8.21 (`9ae20dacca05094e72ec2358bdd67be014c5d5d8`). Loaded scene observation distinguishes inferred ranged hunting, nearby prominent threats and corroborated combat; bounded participant selection reserves the relevant opponent and keeps the primary readable. Actual encounter focus informs shot angle, lead room, intent, sequence credit and visibility preferences without expanding the group lens around distant targets. A finite pass-by family fixes the camera in world space while native Rotation Composer pans; ordinary tracking remains available on interruption. Wildlife cutaways are suppressed by fresh encounter context or observed ranged draw. Existing native camera integrations, output orientation and observer protocol 3 remain unchanged. No server update is required.

Local validation: **547/547 core cases pass**, including 18 encounter-evidence/retention cases, 16 encounter-framing/sequence cases and 11 pass-by route/lifetime cases. Core, client and server compile with warnings as errors against real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references. Actual assembly inspection supports the replicated equipped-item draw-state path and confirms the lack of exact remote reticle/AI target identity. Local builds use the SDK compiler; the normal CI projects repeat validation and run the existing native-math, real-assembly and Windows/Linux streaming suites. Use the completed workflow associated with the delivered artifact for its CI evidence.

**Valheim runtime validation remains pending.** Exercise the expanded [camera matrix](CAMERA-DIRECTOR.md#runtime-validation): distant bow draw/release/turn-away and ambiguous prey, one/multiple players against one/multiple foes, large passive threats, dead/unloaded targets, forest/room visibility, steady travel through a selected pass-by and interruptions by stop/reverse/encounter. Verify both operator and RTMP output, including upright transitions and preserved camera continuity. Pass-by currently frames the primary player; group travel retains the existing group alternatives. No live installation or server restart is performed.

## 0.8.21 observational response, group dolly and elevation profiles

Built directly from 0.8.20 (`94e563fd984930d1746c9df9ab2c5ce78c0f641e`). Two eligible actor views can use native Position Composer with fixed orientation and latched screen/depth preferences, without a competing aim or custom tracking controller. Group Framing retains bounded native Dolly Then Zoom only after complete displacement/route validation; unsafe movement recomputes native zoom at the supported body pose. Native FreeLook profiles coordinate eligible orbit/rise lens, damping and composition with explicit ownership and restoration. The pinned Cinemachine 3.1.7 / selected Splines 2.0.0 dependency and observer protocol 3 are unchanged. No server update is required.

Local core validation: **502/502 cases pass**, including 15 observational eligibility/history cases, ten full-vector group-budget/cooldown cases and ten elevation-ownership/profile cases. **20/20 native camera/math cases pass** against the real compiled Cinemachine/Splines and matching Unity references, including actual upstream lens interpolation, observational guide rectangles and preservation of image-space hard limits under native composition interpolation. These are managed math checks; they do not instantiate Unity scenes or claim runtime component behavior. Core, client and server compile with warnings as errors against real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references. Local validation uses the SDK compiler; CI performs normal builds, repeats both suites, audits real game assemblies and runs the existing Windows/Linux RTMP checks. Use the completed workflow associated with the delivered artifact as its CI evidence.

**Valheim visual validation is pending.** Run the expanded [camera-director matrix](CAMERA-DIRECTOR.md#runtime-validation), especially sideways/depth movement and sustained sprinting during an observational view, groups spreading/rejoining near walls or trees, profile activation/exit during group changes and scenic rises, live setting switches and interrupted/retained views. Verify the actual video as well as diagnostics, including the preceding upright live/held/slate output checks. F10 can exercise solo observation and movement; real multiple players are needed for group behavior. No game process, live installation, server restart or public release is performed here.

## 0.8.20 native Cinemachine camera mechanics and output orientation

Built directly from 0.8.19 (`6fbfb9d614201f9877615f0d0019555416cc47b4`). Native Confiner 3D, Spline Dolly, ClearShot and blend mechanics extend the existing readable-footage director. Interior volume evidence remains conservative and separate from scene readiness; helper volumes do not participate in game physics. Official selected Splines 2.0.0 source is checksummed and compiled into the existing isolated Cinemachine DLL, retaining the three-DLL client payload and adding upstream notices.

The user supplied 0.8.19 screenshots showing a vertical reflection of both live video and the generated recovery slate. Mirrored slate lettering identifies a raster/output-orientation problem, not camera roll. The clean program path lacked explicit normalization between GPU readback row order and rawvideo input. Automatic calibration/normalization now applies to the common live/hold/slate path; automatic mode ignores the preserved legacy manual flip setting. Actual Windows graphics-backend confirmation is pending.

Local validation: **467/467 core cases**, **12/12 native spline-math cases** using the real compiled Cinemachine/Splines and Unity references, and **95/95 Linux streaming checks** pass. The streaming checks decode asymmetric top/bottom markers through actual RTMP, including an old manual flip value with automatic mode enabled. Core, client and server compile against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references; the client uses Cinemachine 3.1.7 with selected Splines 2.0.0 source. Local compiler checks use warnings as errors; CI performs normal builds, the core/native-math suites, existing real-assembly audits and Windows/Linux streaming checks. Use the completed workflow associated with the delivered artifact as its build evidence. No Unity/Valheim scene, camera-component, physics or graphics-backend session has been run here. The client changes require no server update; observer admission remains protocol 3. No live installation, server restart or public release is performed.

Runtime priorities: verify upright live/held/slate video and local-console separation; enter/leave enclosed rooms and doorways; remove structural pieces and change worlds; run scenic paths through hold/resume and scale changes; compare native selection and fallback without candidate churn; exercise interrupted blends; check dense-scene CPU/GPU cost and long-running resource cleanup. See [camera-director matrix](CAMERA-DIRECTOR.md#runtime-validation).

## 0.8.19 predictive production direction and clean output

Built directly from 0.8.18 (`4ecea7d7fd373366775cfc93d43f8a9a30ff13e5`). Bounded future visibility comparison extends the existing active-camera movement preview to currently readable alternatives. Activity sequences retain presentation-qualified beats, while explicit wide/full/qualified medium envelopes and persistent action geography distinguish shot purpose from arbitrary camera changes. A clean program path copies the final post-effects camera texture before operator overlays/console UI, using a dedicated recovery card whenever that output is unavailable. Optional low-frequency rendered-picture measurements add conservative exposure/contrast evidence alongside geometry.

The changes are client-side. Observer admission remains protocol 3, discovery remains protocol 2 and optional context/activity/events exchanges retain their versions. Existing compatible servers continue to work; 0.8.18 server events remain optional, while global-idle wildlife eligibility still needs the 0.8.17-or-later activity feed. Ordinary players require no mod. No live installation, server restart or public release is performed.

Local validation: **433/433 core cases pass**, including 82 added cases for future uncertainty/expiry, sequence interruption and actual-presentation credit, persistent axis changes, intentional actor cropping and companion envelopes, clean program generation/readback freshness, and conservative image measurements. Core and client compile against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. Local validation uses the SDK compiler; the CI workflow repeats the core suite, performs normal client/server builds and real-assembly audits, and runs Windows/Linux RTMP checks. Use the completed workflow for the delivered artifact as its build evidence. **No Unity/Valheim visual or multiplayer session has been run here.** The user's positive 0.8.18 feedback does not establish these new behaviors.

Run the expanded [camera-director matrix](CAMERA-DIRECTOR.md#runtime-validation). Highest-priority cases are sustained corridor/forest travel and reversals, medium-to-full transitions during movement/combat, scene/group handoffs, constrained sequence fallbacks, local console/overlay versus RTMP separation, live/hold/slate orientation on the active graphics backend, resize/reset during asynchronous reads, and night/fog/low-contrast scenes. Compare the same routes with `broadcast camera prediction|sequences|scales|picture on|off`; inspect actual footage as well as diagnostics. Future view quality remains an estimate, foliage bounds remain approximate, and image regions are not semantic subject segmentation. World-space overlays already rendered into the camera are outside clean screen-overlay separation.

Install all three client DLLs and the included `ThirdParty` notices together after closing the broadcaster, preserving configuration. A server update is not needed for these changes. Visual quality, GPU/CPU cost, video orientation, audio/video continuity and long-running behavior remain runtime validation items.


## 0.8.18 readable, scene-aware direction

Built directly from 0.8.17 (`94fb4a1cbaa5aad0b4898bc8bdfef6876f7b3945`). The source audit found actor usability ignored foreground obstruction, selection retained the normal score margin even for unreadable shots, and the finite initial viewpoints had no expanded recovery search. The shared picture-health contract now covers selection, final presentation, motion and transitions. Width-distributed primary samples and foreground measurements feed bounded recovery candidates; poor image quality remains separate from physical readiness and travel authorization.

Matching Valheim assembly inspection confirmed `Character.InInterior` tests the dungeon layer, not constructed houses. A separate structural survey adds inferred room/shelter/opening constraints with persistence, while keeping dungeon/loaded-geometry checks unchanged. Shot roles select purpose and full-body scale; locomotion-aware response preserves observed vertical movement and uses verified replicated swimming or actual ship support. Contextual transitions and validated cooperative-primary handoffs retain appropriate continuity. Generic player actions beside passive enemies do not establish combat.

Ordinary and autonomous player scheduling now use qualified activity, bounded encounter retention and fair coverage. A failed-view budget permits another directly reachable loaded player without quality-triggered travel. Optional server `EventRequest1` / `EventReply1` adds timestamped passive activity evidence and falls back locally when unavailable. Admission remains protocol 3; discovery remains 2; context and the existing idle activity exchange remain 1. The new server is optional for local camera/direction improvements and required only for its richer remote event feed. The 0.8.17 server requirement for global-idle wildlife eligibility is retained. Ordinary players remain unmodded; simulation ownership, spawning, world time and stream transport are unchanged.

Local validation: **351/351 core cases pass**, including 87 added cases for picture health and recovery, rejected shot proposals, environment/locomotion evidence, shot intent/transitions, persistent group handoffs, optional event packets and activity scheduling. Core, client and server compile with warnings treated as errors against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. Local validation uses the SDK compiler because this environment cannot run normal MSBuild process metadata queries. The CI workflow repeats the core suite, performs normal client/server builds and real-assembly audits, and runs Windows/Linux RTMP checks; use the completed workflow for the delivered artifact as its build evidence. **No Unity/Valheim runtime session has been run here.** Visual quality, dense-scene performance, actual building/doorway classification, multiplayer groups and runtime optional-feed compatibility remain pending.

Use the expanded [camera-director runtime matrix](CAMERA-DIRECTOR.md#runtime-validation). Priorities are prolonged versus transient foliage obstruction; all-poor candidates and bounded local fallback; constructed house/canopy/dungeon distinction; vertical/swimming/boat motion; purposeful scale with full-body visibility; cooperative handoffs and discontinuity resets; encounter dwell/fairness/manual controls; older/updated/disabled event servers; and the existing upright live/held/RTMP transitions. Capture short repeatable clips and diagnostics for comparison rather than treating quality scores as visual proof.

Client payload: replace plugin/core/Cinemachine DLLs and included ThirdParty notices together after closing the broadcaster, preserving configuration. Optional server payload: replace the server/core pair together during the operator's normal restart. No live installation, server restart or public release is performed by this work.

## 0.8.17 observed-idle wildlife cutaways

Built directly from 0.8.16 (`04e1361a6aaae3e44b60e3ba65c963362ec22b64`). The new policy permits a bounded nearby creature shot only after complete fresh server evidence establishes that all connected ordinary players have been observed idle for at least 60 seconds. Local action can veto that evidence. Existing creature selection, usable-footage dwell and cooldowns apply; activity, stale/unknown evidence or subject loss resumes player coverage. Another cutaway requires at least 60 seconds of rearm. Manual hold, self-follow, probe and route operations suppress the feature.

A separate optional `ActivityRequest1` / `ActivityReply1` exchange observes existing replicated movement/actions, station/crafting, block/emote and health evidence. It does not establish raw-input inactivity or quiet inventory/menu use. Admission remains protocol 3, discovery 2 and optional camera context 1. Older servers preserve player coverage and explicitly disable cutaways without activity support. Empty-server coverage, spawning, simulation ownership, world time and travel permissions are unchanged.

Local validation: **264/264 core cases pass**, including 37 new cases for idle evidence, optional activity packets/freshness, cutaway eligibility/return and suspended acquisition timing. Core, client and server compile with warnings treated as errors against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. Local validation uses the SDK compiler; CI additionally runs normal builds, real-assembly audits and Windows/Linux RTMP tests. **No Unity/Valheim runtime session has been run here.** The [camera-director matrix](CAMERA-DIRECTOR.md#runtime-validation) adds all-player 60-second eligibility, a distant active player, action/roster/staleness interruption, rearm, unsupported-server fallback and no-creature/suppression cases. Preserve the shared-camera and player-follow regressions below; these runtime procedures are not claimed passes.

For this feature, install the 0.8.17 client plugin/core/Cinemachine DLLs with notices, and the dedicated server plugin/core pair, preserving configuration. Ordinary players need no mod. No live installation or server restart is performed.

## 0.8.16 shared player, creature and scenic direction

Built directly from 0.8.15 (`ca43f2beca6370b9ac95034bc2e91a598f822f15`). Source inspection confirmed the user's report: Cinemachine was selected only for player targets. Scenic/creature coverage still used a fixed offset, legacy position smoothing and immediate centered aim; cinematic controls had no effect there. Idle dwell advanced without usable-image proof, and ordinary coverage/path resets discarded displayed-pose continuity.

The shared `CinemachineShotRig` now accepts typed player, creature and environmental inputs. Player observation, cooperative framing and independent trajectories are retained. Creature observation supplies real dimensions and filtered read-only motion; the current creature profile frames one primary actor, not an inferred herd. Scenic plans provide independent camera/aim paths in five bounded non-looping styles, with ranked loaded-terrain candidates and shown-place/style memory. Landscape quality evaluates the actual presented quaternion and lens without applying humanoid body/headroom assumptions. Ordinary handoffs use the displayed pose; world/travel/probe discontinuities retain explicit resets.

Idle pacing and history require recent live compositor output matched to mode, revision and plan identity. A failed creature view has a separate bounded fallback/cooldown; persistently unusable scenery can attempt an eligible creature without consuming phantom scenic dwell. Replacement scenic search can retain a still-valid developed view. Shared procedural/fixed, dynamic/steady, composition, motion and damping controls apply to all three production modes. The existing `Camera.ProceduralPlayers` serialized key now controls that shared pipeline and is retained for configuration compatibility. Group framing remains a player capability. Observer protocol 3, optional server context, simulation isolation and server behavior are unchanged; no new server deployment is required.

Local validation: **227/227 core cases pass**, including thirteen new cases for environmental path development, bounded geometry, idle presentation clocks/history, failed-view fallback and quality recovery while editorial pacing is paused. Core, client and server sources compile with warnings treated as errors against real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. Local validation uses the SDK compiler; CI additionally runs normal project builds, real-assembly structural checks and Windows/Linux RTMP tests. **No Unity/Valheim visual session has been run here.** Core tests and compilation do not establish scenic quality, creature framing, smooth cross-mode handoffs or runtime cost. Follow the expanded [camera-director runtime matrix](CAMERA-DIRECTOR.md#runtime-validation): broadcaster-only scenic variety, forest/structure clearance, stationary and moving creatures of different sizes, control changes after developed movement, mode/plan transitions, held-frame pacing and probe/travel separation. Preserve the 0.8.15 player movement and group regression cases.

Install all three client DLLs and included third-party notices together after closing the broadcaster, preserving configuration. The existing protocol-3 server can remain installed. No live installation or public release is performed.

## 0.8.15 independent player-camera trajectories

Built directly from 0.8.14 (`0cf7f345d63df68cc3370d3756e77221a689fadb`). The user reports that camera translation mirrors a moving target despite noticeable independent movement while idle. Source and actual game-assembly inspection found active Cinemachine damping, valid unscaled delta time and no later native pose overwrite. The issue is architectural: the follow point is rebuilt from the player every frame, with 0.2 seconds of velocity prediction. Cinemachine's default 0.8-second position damping means approximately 99% convergence in that period (a roughly 0.174-second time constant), so the prediction nearly cancels its steady-motion lag. Outgoing blends additionally copy the target's exact displacement. The aim dead zone affects rotation and does not prevent that body translation. These are source-supported causes, not a captured runtime measurement.

The new trajectory policy gives each candidate a persistent world-space anchor and velocity while retaining the live subject/group as its separate aim target. Shot/activity intent, bounded acceleration and continuous framing/range correction determine tracking. It removes the positional prediction and raw target-displacement blend path, preserves 0.8.14 placement/group framing, and keeps aesthetic quality separate from physical readiness and observer travel permission. Recovery from a held or failed presentation blends from the last displayed pose even when the same candidate returns; rejected selected trajectories synchronize to their last presented anchor. Observer protocol 3, optional server context and server behavior are unchanged; no new server deployment is needed.

Local validation: **214/214 core cases pass**, including ten new trajectory cases covering movement onset, three-minute sustained travel across shot families, stopping/reversal, activity-area stability, cooperative motion, group/shot continuity, variable/standby update cadence, constraint recovery and pressure settling. Core, client and server sources compile with warnings treated as errors against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. Local validation uses the SDK compiler; the CI workflow additionally runs normal project builds, real-assembly audits and Windows/Linux RTMP checks. **No Unity/Valheim visual session has been run here.** Core policy tests and compilation cannot establish final camera feel, multiplayer behavior or runtime cost. Follow the updated [camera-director runtime matrix](CAMERA-DIRECTOR.md#runtime-validation), especially another player's sustained walking/sprinting, stopping/reversal, long travel across zone boundaries, constrained spaces, group joins/splits and shot transitions. Compare the local display with RTMP and record `broadcast camera status` while the symptom occurs.

Install all three client DLLs and the included third-party notices together after closing the broadcaster, preserving configuration. Existing protocol-3 servers remain compatible. No live installation or public release is performed.

## 0.8.14 deliberate composition

Built directly from 0.8.13 (`180f9b2`). The user reported that a single framed player remains centered. Source review confirmed the centered screen preference, common follow/look-at point and near-zero lead offset; a dead zone permits drift but does not choose off-center composition. Review also found competing group/aim adjustments, unconditional final recentering, a motion reset on dynamic-to-steady changes and pacing based on physical validity without sufficient readability. These are source findings; no captured runtime trace was available to attribute every perceived camera movement.

This revision introduces persistent screen-placement intent, evidence-qualified movement/facing/activity space, body-envelope framing and headroom evaluation. It keeps physical readiness, observer authorization and travel separate from composition quality. `Camera.DeliberateComposition` defaults true; `broadcast camera composition on|off` compares deliberate versus centered preference without disabling damping or restarting the observer. Existing protocol-3 servers remain compatible, including the optional 0.8.13 context service. No server behavior or protocol is changed.

Local validation: **204/204 core cases pass** (14 new composition-policy cases and seven additional full-body projection cases). Combined client and server sources compile with warnings treated as errors against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. The independent source review identified and corrected a group-lens initialization defect when companions join a previously solo shot. Local compilation used the SDK compiler; CI performs normal project builds, real-assembly audits and Windows/Linux RTMP checks. **No Unity/Valheim visual session has been run here.** Updated in-game cases remain pending in [CAMERA-DIRECTOR.md](CAMERA-DIRECTOR.md#runtime-validation). In particular, compare requested/actual viewport placement while stationary, walking, sprinting, reversing and entering constrained space; exercise group joins/splits, activity-point changes, dynamic/steady switches after developed movement, framing recovery and held/live RTMP transitions. Subject visibility and camera continuity take priority over thirds placement.

## 0.8.13 cooperative cinematic director

Built directly from 0.8.12 (`6dd447f`), retaining observer admission protocol 3, discovery protocol 2, the existing camera compositor and continuous-follow acquisition geometry. The optional context extension has its own protocol 1 and falls back locally with an older compatible server; it does not change admission or require mods on ordinary players.

The player camera now uses Cinemachine OrbitalFollow, RotationComposer dead zones and independent position/aim damping, TargetGroup/GroupFraming and composition-aware blends. A bounded director adds activity-sensitive movement, shot/movement history, usable-footage pacing, predictive foliage/physical screening and settling after corrections. Group membership is persistent and primary-anchored; companions earn scheduling credit only after sustained readable live footage. Repeated camera corrections reduce decorative movement. Spacious interiors allow slower variety; restricted rooms retain the readable view. Optional authenticated server context reports replicated state/heartbeat ages and recent observed health loss, with bounded identity/world/freshness validation. Activity classifications remain evidence-qualified.

Local validation: **183/183 core cases pass**; combined client and server sources compile with warnings as errors against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and isolated Cinemachine 3.1.7 runtime. New cases cover constrained versus spacious interiors, correction settling, deliberate variety, motion bounds/strength, cooperative membership, readable-coverage fairness, off-center projection and optional-context freshness/identity/packet limits. Local compilation used the SDK compiler because this environment's MSBuild process metadata lookup fails; the existing CI pipeline runs normal project builds and Windows/Linux RTMP checks.

No Unity/Valheim runtime session was executed for 0.8.13. The user's positive procedural-visibility feedback concerns 0.8.12 and does not validate new damping, group framing, visual transitions or runtime cost. Follow the [camera-director runtime matrix](CAMERA-DIRECTOR.md#runtime-validation), including sustained walking/sprinting, open idle, constrained interiors/forests, group merge/split, combat/resource hints, held-frame orientation and old-server fallback. These cases remain **pending**.

Client package: replace the broadcaster's three DLLs together with the included third-party notices, preserving configuration. Server package: optional for richer context; update its server/core pair together during the operator's normal restart. Existing protocol-3 servers remain usable without this update. No live installation or public release is performed.

## 0.8.12 Cinemachine player framing

Built directly from 0.8.11 (`9495313`), retaining protocol 3, the camera compositor and continuous-follow readiness. The client now packages a selected runtime built from official Cinemachine 3.1.7 sources. No server/protocol migration or live installation is performed.

Ten virtual cameras use Cinemachine Follow, HardLookAt and Deoccluder. A custom Cinemachine extension evaluates player head/torso/lower-body visibility and foreground vegetation. Selected views update every frame; alternatives are evaluated at 5 Hz or after significant subject movement. A pure selection policy requires persistent improvement and retains healthy shots to avoid oscillation. Filtered movement prediction is capped at 3 m; interiors use tighter candidate offsets. Actual interpolated camera movement, support geometry, near-plane clearance and carrier reach are validated before presentation. Foliage affects picture quality, not scene readiness or travel permission.

The upstream runtime is built in an isolated assembly, without adding a Brain or another rendering camera. Verified source substitutions cover net48 syntax, late-loaded update-manager initialization, and idempotent cleanup of Cinemachine's hidden noninteracting scratch collider. The source archive hash, upstream revision, exact patches and license notices are included with the client package. See [the dependency build record](../third_party/Cinemachine/README.md).

Validation performed locally: **145/145 core cases pass**. Client, core, server and Cinemachine compile against the existing real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references with warnings as errors. Core policy tests include stable ties, persistent improvement, dwell, immediate physical rejection, low foliage scores, candidate reset and stale evidence. The candidate movement calculation is shared with acquisition/travel and has a large-offset regression fixture. The existing CI pipeline performs normal project builds, core tests, metadata audits and Windows/Linux RTMP loopback checks. No Unity/Valheim session was executed here.

**Client installation changes:** close the broadcaster, then copy all contents of the artifact's `ValheimBroadcaster/` folder into its existing plugin directory. Keep `ValheimBroadcaster.dll`, `ValheimBroadcaster.Core.dll` and **`ValheimBroadcaster.Cinemachine.dll`** together with the included `ThirdParty/` notices. Preserve existing configuration. Do not install these client files on the dedicated server; the deployed protocol-3 server remains compatible.

Runtime cases **not run**:

1. In an ordinary disposable local world, use `broadcast self` or F10, then walk/sprint through trees, stop, turn, and pass near buildings. Compare `broadcast camera fixed` with `broadcast camera procedural`; the mode persists through `Camera.ProceduralPlayers`. The procedural camera should prefer readable alternative views without constantly swapping sides. Inspect `broadcast camera status` for candidate, quality, obstruction and transition information.
2. Follow another player in the authorized read-only observer. Sustain movement across terrain zones and stop beneath a canopy. Reframing should retain the same player and should not start travel merely because foliage obscures them. Verify other-client invisibility and unchanged observer behavior after installing the new client.
3. Exercise wide/high camera settings and briefly unavailable camera geometry near a stationary target. Successful camera evidence must not be refreshed by failed attempts; expiry must not force the old fixed-angle displacement calculation or create a self-blocking acquisition loop.
4. Enter a dungeon, walk through doors and narrow passages, and exit. Physical camera/near-plane collisions must not be hidden by dwell. Transitions must remain outside solids or cut to a validated endpoint. Actual missing scene geometry may still hold/slate.
5. Teleport/respawn, cycle players, return/park/resume, switch camera mode, exit/rejoin and stop broadcasting. Old candidate state, motion prediction and helper objects must not affect the next scene or normal restored camera. Observe both local rendering and RTMP, including `broadcast framehold test`.

Known limits: renderer bounds approximate vegetation and do not measure leaf transparency; unfamiliar custom vegetation may be missed. Dense-scene cost and actual composition quality need GPU/game profiling and tuning. Pure policy tests and successful compilation do not prove Unity component lifecycle, visual quality or multiplayer runtime behavior.

## 0.8.11 camera-image composition

Built directly from 0.8.10 (`80c7c04`), retaining protocol-3 opt-in and continuous player following. The user reports a brief upside-down flash just before recovery/travel. The transition audit found explicit travel already forces a slate; the preceding bounded Live → Hold interval still switched from the camera image to GUI screenshot playback. The extra screenshot copy in 0.8.9 did not establish consistent orientation across those routes. This is a source-supported hypothesis, not a captured GPU diagnosis.

The active game camera now receives an appended `OnRenderImage` component. A ready live camera source is copied to one matching render texture; both Live and Hold draw that same texture through the camera destination. No held image is drawn through GUI or captured after GUI. A same-frame camera readiness stamp is required; missing compositor callbacks fall back to the opaque slate. Hard transitions invalidate prior footage, including when Watching resumes before the old hold would expire. Source-size/format changes, lost textures and camera lifetime changes also invalidate it. Every compositor path writes its destination and preserves the sRGB write flag. The final camera output remains bound for Unity's image-effect chain.

Local validation: **132/132 core cases pass**, including quick travel/recovery without resurrection of a pre-travel frame. Client and server compile against the existing real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references with warnings as errors. Normal project builds and the existing streaming/metadata checks run in CI. No Valheim/GPU session is available here; the visual cases below are **not run**.

1. With a live broadcast picture, run `broadcast framehold test`, close the console, and wait two seconds. The scene should hold for half a second, upright and with unchanged color, then resume. Repeat on scenic footage or `broadcast self`; no second player is required.
2. Repeat while watching the local display and RTMP receiver. Keep the existing `Streaming.FlipVertical` setting; this change does not alter encoder orientation.
3. Observe an actual readiness gap followed by travel/recovery. Brief gaps may hold for less than 0.75 seconds; travel must show the slate and must not resurrect the old frame on arrival. Confirm that new live footage resumes without a flash.
4. Exercise resize/fullscreen, off/on, observer return/resume and world exit/rejoin. No stale held scene should survive those boundaries. Confirm the ordinary camera restores on shutdown. Verify on the user's graphics backend and active post-processing configuration.

Replace both client DLLs together after closing the broadcaster. Preserve configuration and the deployed protocol-3 server. The server artifact's version label changes only; no server upgrade, admission change or live installation is required.

API references: [OnRenderImage ordering and pipeline support](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnRenderImage.html), [Graphics.Blit state and color handling](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.Blit.html). Actual game inspection confirms built-in `OnRenderImage` effects on the game camera; this does not validate third-party effects appended afterward.

## 0.8.10 continuous player following

Built from 0.8.9 (`52e3435`), preserving the corrected protocol-3 admission, movement, scenery and held-image changes. Actual Valheim 1.0.21 inspection established that `ZNetScene.IsAreaReady` waits for the queried zone plus instances for known network objects in a 3×3 zone neighbourhood. Using it as a per-frame presentation veto causes unnecessary freezes when otherwise usable footage exists.

Full readiness now establishes each player shot. A recently presented shot then uses the exact live character/world binding, bounded incremental target movement and existing local geometry at the target, camera and carrier. Outdoors this requires active generated terrain meshes/collider; interiors require nearby static geometry at interior height. Interior camera offsets are clipped before checking camera support, so an offset beyond a small room is not automatically treated as an unloaded shot. Character changes, death, scene/world changes, expired footage and substantial 3D relocation return to acquisition. Travel/return/probe/scenic/creature readiness and the server protocol remain unchanged.

Local validation: **131/131 core cases pass**, including roughly 3.9 km of sustained walking/sprinting, subsequent stationary following, same-binding selection, reset/respawn identity, horizontal/vertical teleport rejection and expiry that readiness probes cannot extend. Client compilation against actual matching game references passes with warnings as errors. Normal MSBuild/test execution remains assigned to the existing CI pipeline because the local CLI cannot inspect process start times. No new protocol audit framework was added.

Runtime retest remains pending:

- Follow a stationary player while nearby objects appear, then follow sustained walking/sprinting across terrain-zone boundaries. Background object queues should not freeze established footage; diagnostics should report continuous follow with local geometry ready.
- Enter and move through a dungeon. Entry still requires acquisition; once established, room-object loading outside the shot should not veto it. Verify the actual clipped camera retains valid geometry and ordinary objects/characters under it do not invalidate floor support.
- Exercise distant portal travel, death/respawn, target cycling, off/on and return/rejoin. These must establish a new shot; unavailable actors or geometry must still hold/slate rather than allow unsupported carrier movement.

Install both client DLLs together after closing the broadcaster. Keep the deployed protocol-3 server and existing configuration. No live installation or in-game validation was performed here.

## 0.8.9 held-frame presentation

The supplied session log loads client **0.8.7**, protocol 3, with Valheim 1.0.21, Unity 6000.0.75, BepInEx 5.4.23.6 and Jötunn 2.30.2. It contains no camera/graphics exceptions, one successful NVENC/AAC startup and no RTMP failure/retry. Five status samples report waiting for a valid shot; thirteen report brief interruptions, mostly target-area loading. Six player travels are distant (approximately 754–2405 metres). These observations do not establish which individual frame inverted.

The strongest source-level hypothesis is held-frame presentation during those loading gaps: 0.8.7/0.8.8 draw a raw screenshot render texture directly through GUI, while streaming normalizes it through `Graphics.Blit`. The actual game's `GameCamera.LateUpdate` calls `UpdateCamera`, then `UpdateListner`, then returns; camera shake is inside `UpdateCamera`, before our postfix. No later native rotation overwrite was found in that path.

0.8.9 routes held and streamed screenshots through the same GPU normalization helper, restores `RenderTexture.active` and `GL.sRGBWrite`, and requires a Live repaint in the same frame before refreshing the held image. It does not change camera geometry, admission, observer protocol, or the encoder's `FlipVertical` setting. The client compiles against real references with warnings as errors; the existing CI pipeline remains the package producer. No GPU/game runtime is available here, so this is not a confirmed runtime fix.

Primary API references: [Unity render-texture conventions](https://docs.unity.cn/Manual/SL-PlatformDifferences.html), [Graphics.Blit state](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.Blit.html), and [end-of-frame capture ordering](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/WaitForEndOfFrame.html).

Retest continuous walking/sprinting and dungeon/terrain loading while watching both the local client and RTMP receiver. Brief held frames should retain the live image's orientation and color, then return to live motion; longer gaps should still expire to the recovery slate. Check resize/fullscreen changes and broadcast off/on. Keep the existing `Streaming.FlipVertical` setting: a global output flip is not a correction for intermittent held-frame inversion. If the symptom recurs, `broadcast diagnostics` now includes the last presentation mode and graphics backend alongside held/slate counts. All these runtime cases remain pending.

## 0.8.8 scenic composition

Continues from corrected protocol-3 client 0.8.7 (`4113d7f`). The reported failure is a scenic camera buried in foreground leaf planes. Selection now evaluates alternate headings/heights and five points along the intended path, rejects solid collisions and heavily obscured foreground foliage, and retires a persistently invalid shot after 1.5 seconds. Bounds screening is conservative; it does not measure leaf transparency or promise that every accepted shot is attractive. `broadcast diagnostics` includes the scenic rejection reason, search state and rejected-view count for the current region.

Both plugins compile against the pinned real game references with warnings as errors, and **126/126 core cases pass**, including replacement-shot dwell and immediate player preemption. The normal local `dotnet run` command remains blocked by this container's process-introspection failure; these local results use direct Roslyn compilation, with the normal project build left to the existing CI workflow. No game runtime is available here. Server admission, protocol and streaming code are unchanged.

Retest with the new client plugin and Core DLL together, keeping the deployed protocol-3 server and existing configuration:

- Let solo scenic coverage run in a forest canopy and an open meadow through multiple shot changes. A camera inside vegetation or facing a close wall of leaves should be rejected, while open landscapes should still produce usable shots.
- Check that a newly obstructed path recovers to a different composition rather than repeatedly collapsing into the same branch. If no local shot passes, the existing unready-region timeout should initiate autonomous region travel when available.
- Exercise hold/resume and allow a real player to join; player acquisition should still preempt scenery. Check `broadcast diagnostics` if recovery slates persist, and note scene/region plus rejection reason.

These in-game cases remain pending; no live installation was performed.

## 0.8.3 optional observer admission — 2026-10-31

The allowlist now grants permission to request observer mode, rather than forcing a Steam account into that role. The client sends protocol-3 intent before native admission; no request means ordinary gameplay. The entire accepted observer connection remains read-only until disconnect.

- **119/119 core cases passed**, including eight new cases for default ordinary admission, pre-admission intent, rejected late promotion, protocol mismatch, idempotence, authorization/lease separation, timeout/revocation and reconnecting as an ordinary player.
- **28/28 real-assembly audits passed on each of the actual Linux server and Windows client assemblies**, including `SendPeerInfo` signature, native RPC dispatch, authenticated readiness and reliable FIFO Steam transport. These inspect metadata; they do not execute multiplayer joins.
- Client and server **0.8.3** compiled with zero warnings/errors and warnings treated as errors, against real installed Valheim 1.0.21 references, BepInEx **5.4.23.6** and Jötunn **2.30.2**. The existing CI baseline remains BepInEx 5.4.23.2. Both artifacts contain only their role's plugin and matching Core DLL.
- An isolated Linux Docker server with networking disabled loaded **22 plugins, zero skipped and zero failed**, including all 18 existing ServersideQoL modules, their framework, YAML detector, Jötunn and Broadcaster. It logged `Observer server 0.8.3 ready`; no plugin exceptions, Harmony failures, unavailable-server messages or discovery-suspension errors were observed. Discord Connector and production credentials/worlds were excluded. The test container was stopped afterward. Offline Steam connectivity errors are expected and do not constitute a successful network join.
- The existing production server was not modified or restarted. The original 0.8.0 deployment was halted before any production files were installed.

**Still required:** a vanilla join using an allowlisted identity lasting beyond the former 15-second deadline; the same account joining with the mod installed but observer mode off; an explicit observer join with matching 0.8.3 endpoints; unauthorized/mismatched/late requests; and the visibility, interactions, sleep and ownership matrix in [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md). Startup and policy tests do not prove those runtime behaviors.

## 0.8.7 corrected protocol-3 build

Based directly on main commit `0348013737799f8131b589bddc1edd84319ca15b`, which contains the deployed protocol-3 opt-in implementation (`8625cd3`, labelled 0.8.4). Carries the movement/reacquisition fix and rejected-world client cleanup onto that baseline. The protocol definition and server admission implementation are preserved; server source changes only its version label/startup banner. No account-based automatic reservation or 0.8.6 reservation policy is included.

The `fix-player-reacquisition` builds labelled 0.8.4 (`b0de64e`), 0.8.5 (`a2af65f`) and 0.8.6 (`31f7c53`) were incorrectly based on protocol 2. Do not deploy those builds or their server companion to replace a protocol-3 installation. A protocol-2 client omits the explicit `Observer.Request3` message and is incompatible with the deployed opt-in server. The rejected-world spawn storm was a separate cleanup defect.

Local results: 125/125 existing/carried core cases pass, and both plugins compile against the real reference assemblies with warnings treated as errors. The existing CI pipeline builds and packages the commit. No extra protocol/artifact check framework was added. In-game authorization, continuous following and rejection cleanup still require runtime validation.

Installation: fully exit the broadcaster client and replace its plugin plus Core DLL together with the 0.8.7 client artifact. Keep the verified protocol-3 server 0.8.4 and existing configuration. If the protocol-2 0.8.6 server was installed, restore the prior protocol-3 pair or install the 0.8.7 server plugin/Core pair after a normal stop. Ordinary players need no mods. Client startup reports protocol 3, and an opted-in join logs its request before native admission. Test joining first, then sustained walking/sprinting and a distant portal. A rejected session should return to the menu without repeated local character allocation.

## Evidence available

- The **0.7.0** director's **55 automated cases passed**, including observer exclusion, identity ordering, manual selection, wrapping, dwell, hold, target loss, readiness timeout, lone-target behavior, invalid time inputs, isolated self-target selection, return to normal observer exclusion, idle alternation/fallback, player preemption, pending-player presence, scene recovery, stable creature rotation, hold/reset, bounded acquisition, and a 24-hour simulated idle schedule. This simulated policy test is not a Valheim soak test.
- The core compiled against actual .NET Standard 2.0 reference assemblies with warnings treated as errors. Its executable test harness ran on .NET 8 against that compiled core.
- The development container's ordinary `dotnet`/MSBuild path fails during process introspection (`Process.StartTime` / `ProcessName`). Direct invocation of the SDK's Roslyn compiler was used for the observed core checks. This is an environment limitation, not a replacement for checking the normal project build in CI or on a development machine.
- The plugin compiled with warnings treated as errors using Roslyn 4.11 from .NET SDK **8.0.427**, actual Microsoft .NET Framework 4.8 reference assemblies (**1.0.3**), Jötunn **2.30.2**, BepInEx **5.4.23.2**, and real Valheim references. This checked the source against those assemblies; it did not establish that the normal MSBuild graph executes successfully in this container.
- Real game references were obtained from the official Steam dedicated-server app **896660**, public Windows depot **896662**, manifest **171106503873955013** dated **2026-10-27**. Assembly metadata inspection found `Version` initializes `GameVersion(1, 0, 21)`, identifying this reference build as **Valheim 1.0.21**. Only reference DLLs were downloaded; no server was launched. These server references do not prove that a separately installed client matches.
- Jötunn's NuGet package identifies upstream commit `671c486ac8123a9b240eae80639b6c33f63b3884`. No client runtime result is implied by a dependency's availability or compilation success.
- A user ran 0.5.0 and supplied the failed distant-probe result recorded below. The user subsequently reported successful **0.7.0 server-companion authorization, loading, manual travel, return, park and resume** while testing alone. This is user-reported runtime evidence, not a developer-run session or a completed multiplayer-isolation matrix. No second-client visibility/ownership result, resolved flicker result, stream-quality pass or GPU-performance pass has been supplied.

Run the normal core check with:

```sh
dotnet run --project tests/ValheimBroadcaster.Core.Tests -c Release
```

CI checks this core harness. A separate `plugin` job downloads pinned real game and BepInEx references and executes the normal `dotnet build` pipeline with warnings treated as errors. The current workflow uploads separate client/server artifacts containing only the respective plugin and Core DLLs. The dependency archives and game assembly are checksum-verified by `scripts/fetch-build-references.py`. See the [Actions runs](https://github.com/Dillflix/valheim-streamer/actions) for the result at a particular commit. Green jobs do not establish in-game behavior.

## 0.8.0 autonomous milestone — local checks, 2026-10-31

| Check | Observed result |
| --- | --- |
| Core harness | **111/111 passed**, including bounded discovery, fair player acquisition, native-travel deadlines, scenic novelty/backoff, route cancellation, reconnect policy and camera reachability |
| Real-assembly metadata audits | **21/21 passed**, including inspected native reconnect entry points and the existing observer boundary |
| Streaming harness | **66/66 passed** with real software FFmpeg/ffprobe: stereo AAC, decoded flash/tone synchronization (0.030 seconds), refused-receiver cleanup, receiver restart followed by a new publishing session, and a 48-hour simulated outage/retry schedule |
| Client and server compilation | Both compiled against the pinned real game/dependency references with warnings treated as errors |

The policy and outage simulations do not run Valheim or establish an actual multi-day soak. Metadata audits do not execute game code or prove Harmony patches applied. These local checks do not claim a successful CI run for a future commit. **Not run:** 0.8.0 Valheim/Unity capture, physical NVIDIA NVENC, multiplayer visibility/ownership, live automatic acquisition/scenic travel, native reconnect, or unattended runtime acceptance. Follow [AUTONOMOUS-BROADCAST.md](AUTONOMOUS-BROADCAST.md) for those cases. No live installation or public release was performed.

## Direct RTMP validation (0.3.0)

The production encoder/session code passed **26 streaming checks** in the development container with real FFmpeg/ffprobe: validated settings, mono upmix, stereo ring wrap, bounded overflow, silence on underflow, latest-frame replacement, missing-executable handling, actual RTMP ingest, H.264/AAC codec and size/channel metadata, decoded flash/stereo tone, synchronization within 250 ms, idempotent process cleanup, and unavailable-receiver termination. These are synthetic media tests, not Unity capture tests. CI runs the same harness. The full plugin compiles against the real assemblies listed above with warnings as errors.

The following remain **pending inside Valheim**, including Windows/Thunderstore and VM testing:

| Case | Required result |
| --- | --- |
| Solo A/V | F10 then F11 produces audible game effects/music and the correct self-follow shot on the receiver |
| Stereo/spatial sound | Left/right sounds match the broadcast camera, including a target far from the observer; local playback remains audible |
| Input format | Stereo at 44.1 and 48 kHz works; mono duplicates to stereo; surround is rejected clearly |
| Listener restoration | F11/off restores listener position/rotation; F8, mode switch, death, exit, and disable also stop output |
| Device loss/change | Output stops with a safe message; after recovery, explicit restart works without duplicate taps |
| Picture | Upright image, correct colors/aspect ratio, resized game window, HUD/slate/overlay capture |
| A/V timing | Tool strikes/jumps match sound, initially and after a 30-minute stream; record measured skew |
| Low frame rate | Repeated video frames, no unbounded queue; audio remains continuous within supported encoder load |
| Minimize/focus | Confirm whether game keeps rendering/audio; input watchdog stops output after five seconds without callbacks |
| Receiver loss | Disconnect receiver during publishing: game stays responsive, stream stops, explicit restart works |
| CPU/network stall | Bounded buffer or output watchdog stops overload; no growing memory or game/audio-thread I/O stall |
| Repeated start/stop | Twenty cycles leave no FFmpeg processes, listener filters, or textures behind |
| Private configuration | No ingest URL/key appears in plugin status/logs; config and process arguments remain private |

Setup and receiver commands: [STREAMING.md](STREAMING.md). The mode is client-only; neither a broadcaster server plugin nor mods on other players are part of this test.

## Record the target before testing

Use a disposable world and character, a separate mod profile, and an unchanged copy of any world needed for comparison. Record:

| Field | Test value |
| --- | --- |
| Source commit and owned DLL SHA-256 hashes | Pending |
| Valheim client build / server build | Pending |
| BepInEx / Jötunn versions | Pending |
| OS, native machine or VM, GPU, driver | Pending |
| Connection backend: Steam or crossplay | Pending |
| Other client/server mods | Pending |
| Relevant configuration and target position-sharing settings | Pending |
| Test world/character and log location | Pending |

Install only the two owned plugin/core DLLs in the observer's profile. Other players should initially use unmodified clients and the server should initially have no broadcaster mod. Test the default loaded-target mode before enabling distant targeting.

## Solo self-follow checks

These cases are **pending in-game validation**. Start alone in a local test world, then press F10 (or run `broadcast self` in the F5 console and close it).

| Case | Required result |
| --- | --- |
| Solo activation | Own character follows the configured shot; list contains one target; SELF-FOLLOW TEST label is visible |
| Movement and obstacles | Walking, running, jumping, walls, and trees exercise framing, smoothing, and obstruction handling |
| Cycling and hold | Next/previous, automatic dwell, and hold/resume retain the single self target without errors |
| Another player joins | Test roster remains self-only |
| Remote setting enabled | Self mode uses normal local reference updates; entering from a distant shot restores the previous override |
| Mode switch | `broadcast on` excludes self again and enters scenic/creature coverage when alone; `broadcast self` re-enters test mode |
| Cleanup | F8/off, death, world exit, and component disable restore view/HUD; re-entry via F8 is normal mode |
| Capture | Direct RTMP captures the actual self-follow shot and test label, including main/sky FOV alignment |

Passing these tests does not establish multiplayer replication or distant streaming behavior.

## Default-mode checks

Every case below is **pending in-game validation**.

| Case | Required result |
| --- | --- |
| Startup/menu | Plugin logs its version and starts disabled; normal menus and camera work |
| Headless load | No camera/UI initialization or graphical errors; plugin does not require clients to install it |
| No other players | Enabling starts new scenic terrain paths around the loaded region, then creature/scenic alternation; stop restores ordinary view |
| Two nearby players | Both appear in `broadcast list`; the observer does not; duplicate names remain independently selectable |
| Follow shot | Target stays framed; FOV, height, azimuth, smoothing, and obstruction response match configuration |
| Manual cycle | Next/previous wrap predictably and reset the camera pose without displaying a stale target |
| Automatic cycle | Only ready footage consumes dwell time; rotation does not skip several targets after a frame stall |
| Hold/resume | Hold stops timed switching; resume continues; manual switching remains possible |
| Death/unavailable target | An unready target leaves the ready roster; another ready player wins, otherwise idle runs; invalidation overrides hold |
| Observer death | Broadcasting stops, the ordinary HUD/camera return, and experimental reference override is removed before respawn |
| Target disconnect | Selection falls back to a surviving target; an empty ready roster enters idle coverage, keeping connected-but-unavailable presence distinct |
| Controls | Hotkeys are ignored in recognized UI/text entry; console commands report useful status and reject unknown IDs |
| Stop/disable | Camera/FOV and prior HUD state restore; the observer can continue normally |
| World/menu transitions | Leaving a world stops broadcasting; returning does not duplicate handlers or restore stale references |
| Failure | Adapter exception logs once for that activation, stops broadcasting, and restores owned state |
| RTMP | Correct game framebuffer, waiting slate, target changes, audio, and focus changes are captured as intended |

Observe the parked character during these tests. Document damage, movement, enemy behavior, sleep participation, simulation ownership, and player-count effects; do not infer that an invisible HUD or detached camera makes the character passive.

## Experimental distant-mode checks

Set `Experimental / RemoteTargets = true` only after the default matrix succeeds. Use two target players in widely separated areas with public map positions enabled.

| Case | Required observation |
| --- | --- |
| Initial distant selection | Reference position changes and the target scene object eventually appears; no target footage is shown before local readiness |
| Scene completeness | Terrain, structures, creatures, effects, and player motion are compared with the target client's view; local readiness alone is insufficient evidence |
| Repeated cross-world cycling | Both areas load reliably through repeated switches without accumulating errors, stale objects, or memory growth |
| Hidden location | A distant private-position player is not silently exposed through a new relay; document nearby-player visibility separately |
| Stale position / teleport / logout | Loading waits remain bounded, alternatives work, and stop is always available |
| Restoration | Turning remote mode off or stopping restores normal reference updates and reloads the observer's own area |
| Parked observer | Body persistence, physics, health, and world interaction remain understood while its region unloads or reloads |
| Server effects | Compare ownership transfer, spawning, raids, enemy targeting, sleeping, and player-count behavior before/during/after observation |
| Backend variants | Record Steam and crossplay outcomes separately; one does not establish the other |

Do not promote distant mode to the default until the actual client/server combination passes these cases and the remaining observer effects are acceptable. Preserve logs and configuration with each result.

## Continuous coverage (0.4.0) runtime checks — pending

- Alone, F8 starts a changing local scenic paths; at default dwell values it alternates 45-second moving scenic shots with 25 seconds following a loaded living non-player actor within 50 metres. No fresh actor means another scenic path; exhaustion is reported explicitly.
- Killing/despawning/moving the subject beyond the radius returns to scenery. Test wildlife, tamed animals, and enemies. No actor is spawned or controlled.
- Joining ready players preempt idle and rotate; a lone ready player remains selected. Connected players outside the loaded area show awaiting-scene status while valid idle shots continue.
- Run F11 throughout those automatic transitions and verify one uninterrupted RTMP connection with matching spatial audio. Explicit camera mode restarts (F10 / on / self) still stop output as documented.
- Move the observer and run `broadcast survey`; the new survey region takes effect without stopping output. World exit/re-entry never reuses an old survey origin or creature binding.
- Test empty/unready scenes and camera positions near the edge of loaded terrain: a recovery slate must hide invalid footage, and valid coverage must resume when ready.
- With experimental remote mode enabled, verify attempt/cooldown bounds, reference restoration, eventual ready-player selection, and the documented recovery slate if remote loading unloads the local survey region.
- Run actual eight-hour and 24-hour sessions before claiming unattended reliability. Record memory/texture/process counts, observer survival, A/V skew, target changes, and failures. These runs have not been performed here.

## 0.5.0 additional checks — pending in Valheim

- Record at least twenty idle transitions: new paths, no immediate repeated locations, no repeated creature within five minutes, stationary-subject abandonment, and honest exhaustion status when no path is viable.
- Verify normal footage excludes the broadcaster and newly equipped items; stopping restores rendering. F10 remains visible. Use another client to confirm local masking does not falsely imply network invisibility.
- Exercise probe mark/start/stop/status with a distant unloaded destination, timeout, readiness loss, total-duration expiry, F8, death and world exit. Confirm return to ordinary interest and no stale cross-world marker.
- A real game soak test remains necessary. The automated timing simulations do not establish dynamic shot quality, terrain fidelity, passive observer behavior, or long-running stream health.

## Observed 0.5.0 failure / 0.5.1 containment

User screenshot: Valheim 1.0.21, probe Stopped after 0.1s, first-ready never, destination/player displacement unavailable. User confirmed probe stop and broadcast off did not restore the black view and exited the world. This is a failed runtime result, not a timeout or successful acquisition.

Source investigation found that scene removal destroys instances outside lists generated around the overridden reference, including the observer. 0.5.1 adds a mandatory observer-retention guard and skips the unsafe removal pass while stopping/restoring owned state. Three new regressions cover observer exclusion/missing identity, normal/retained cases, and persistent failure reporting across cleanup. Both experimental remote following and solo probes use the guard.

Pending runtime check: on a disposable world, a distant attempt must abort before observer removal and return to ordinary control. Check local terrain reload, player survival, HUD/render restoration and no missing-player exception. Actual dual-region loading remains unimplemented; the new guard is containment, not an acquisition success claim.

### Log confirmation

The supplied LogOutput.log identifies Windows x64, Valheim 1.0.21, Unity 6000.0.75, BepInEx 5.4.23.6 (pack 5.4.2360), Jötunn 2.30.2, and Broadcaster 0.5.0. Immediately after `SOLO DISTANT SCENE TEST | Acquiring | 0s | RTMP: Stopped`, Unity logs `Local player destroyed`. No intervening exception appears. The later disconnect corresponds to the user exiting the world. Thus this failure occurred without RTMP publishing and confirms local-player destruction rather than an encoder blackout. The full user log is not committed.

The 0.5.1 CI build still uses the existing BepInEx 5.4.23.2 compile baseline; the supplied log records the actual tested runtime version separately. No successful 0.5.1 game runtime result has yet been recorded.

## 0.6.0 observer foundation — code checks passed, runtime pending

Nine new pure-policy cases cover the closed-world gate (including a hosted world with zero peers), all combinations of native teleport/readiness/position arrival signals, travel timeout with retained active state, outbound cancellation, return completion, repeated return commands without timer reset, retry after failure, world cleanup/reset, and invalid/paused/large time inputs. Total: **45/45**. These do not exercise Unity, Harmony, physics, renderer state or real teleportation.

The plugin compiles with warnings treated as errors against the same real Valheim 1.0.21, Unity, BepInEx 5.4.23.2 and Jötunn 2.30.2 references. The actual user's loader remains BepInEx 5.4.23.6; compilation is not a runtime compatibility result. No new dependency or game binary is packaged.

Metadata/IL inspection verified the exact teleport fields and methods used by the adapter, the player update paths being suppressed, `ZNet.OpenServer`/offline predicates, and the scene-removal path. The resulting code uses real game APIs and reflection, not invented stubs. The supplied failure log and raw inspected game implementation are not committed.

Follow [OBSERVER-TESTS.md](OBSERVER-TESTS.md). Every in-game case there is still pending, including start/stop restoration, body/camera following, teleport/return, timeout recovery, and RTMP continuity. This branch must not be described as multiplayer invisible, simulation-neutral or ready for unattended use.

## 0.6.0 user feedback and 0.7.0 validation

The user reports that 0.6.0 seems to work, with frequent brief camera flickers and upper-left text visible for one/few frames. This is positive partial runtime feedback, not a completed test matrix. The text/location is consistent with the immediate recovery slate. No new log/video establishing the exact readiness trigger was supplied. 0.7.0 removes provisional director invalidations, adds a bounded hold of a captured valid frame and exposes presentation diagnostics; the flicker fix still needs a user retest.

Ten additional policy checks cover captured-frame expiry and hard-failure precedence, reset/invalid clock handling, bounded exact Steam-ID configuration, protocol/deadline/revocation behavior, and parked multiplayer return/resume versus ordinary offline cleanup. **55/55 core cases passed.** Both client and new server assemblies compile against the pinned real 1.0.21 baseline with warnings treated as errors.

A separate metadata-only harness passed **15 real-assembly structural checks** and verifies the real game's interest-position independence, gameplay-query patch sites, capacity accounting, sleep/character discovery, ownership delegation, object-send path, transport sender signatures, interest packet shape, Steam authentication order and bounded routed-request envelope layout. It never executes game code or supplies fake game types. These structural checks do not establish that Harmony hooks applied or multiplayer behavior works.

Every multiplayer case in [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md) remains **not run here**. No live server installation or public release was performed. Existing synthetic FFmpeg tests cover media transport; they do not verify the new held-frame rendering, GPU overhead, or in-game audio during multiplayer travel.

## 0.7.0 user-reported server-companion results — 2026-10-31

After the local dedicated-server setup instructions, the user reported: “All checks are working as intended. Authorization, loading, travel, and return/resume.” The follow-up confirmed normal `Watching` status and continuous `Scenic` coverage while the broadcaster was the only client. Treat the following as **user-reported passes** of the 0.7.0 workflow, with exact installed hashes and fresh logs not supplied:

| Workflow | Reported result |
| --- | --- |
| Observer authorization and character/scene loading | Working as intended |
| Explicit observer travel | Working as intended |
| Return, protected parking and resume | Working as intended as part of the requested return/resume checks |
| Solo coverage | `Watching` / `Scenic` observed |

`Scenic` alone is consistent with the current creature eligibility rules and dormant regions; it is not evidence that the server feed, autonomous region selection or creature simulation exists. The user explicitly deferred creature improvements. **Still pending:** visibility and interactions observed from an ordinary client, simulation-ownership handoff, sleep/events/capacity behavior, negative authorization cases, reconnect/soak testing, confirmation that the flicker is resolved, and actual RTMP video/audio quality. The subsequent autonomous milestone needs its own runtime tests; the 0.7.0 report does not validate new code.

## 0.8.1 audio startup regression

A one-second executable-start delay reproduced the screenshot's exact ambiguous audio error using the 0.8.0 encoder code with stereo input. The repaired encoder passed the same real FFmpeg fixture, with decoded audio/video synchronization checked. All 86 local streaming checks passed (66 normal checks plus 20 delayed-start loopback/recovery checks). The delay fixture uses a POSIX exec wrapper and runs on Linux; Windows CI runs the normal 66-check suite. Client compilation against pinned real references passed with warnings as errors. This demonstrates a startup race, but does not prove that every instance of the screenshot error had that cause. Retest actual Valheim audio capture and NVENC on the user's machine.

## 0.8.2 diagnostic build

The user's 0.8.1 retest still fails after the audio socket connects, at 48000 Hz and two channels, without FFmpeg stderr. The startup repair is therefore insufficient for this machine. This build adds frozen failure-time capture-rate, queue, media-age, publishing and audio/video writer measurements; it does not claim a runtime fix. A production-session test deliberately submits twice the nominal sample rate and checks that the recorded failure distinguishes excess capture from an unexplained encoder failure. Existing queue limits and retry behavior are retained. Server protocol is unchanged.

Additional user log: attempts 3, 4 and 6 reached NVENC H.264/AAC publishing; attempts 3 and 4 subsequently tripped the shared 1500 ms writer-lateness limit. Automatic scenic travel transitioned from Travelling back to Watching while publishing. This establishes runtime encoder/mux progress and one scenic arrival, not verified receiver playback or long-run stability. The diagnostic build names the late writer and measures its lag.
