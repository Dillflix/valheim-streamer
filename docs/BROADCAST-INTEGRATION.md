# Broadcast integration — 0.8.36

This version starts from 0.8.35 commit `a9fbc66`, retaining the opt-in protocol-3 observer and existing streaming pipeline. It addresses the end-to-end audit as connected systems rather than changing isolated timeouts.

## Shared observations and work

Player, Creature and Scenic visibility use the same persistent foliage catalogue, bounds repair, refined geometry and local projection implementation. Their framing rules remain mode-specific. A completed observation remains available during an incremental refresh; omitted or unobserved space remains unknown. Camera-adjacent refinement receives priority, and overlapping catalogue coverage survives ordinary recentering.

`CameraWorkBudget` measures CPU time across camera rigs, observation work and Scenic planning. Background work shares a 3 ms allowance with per-category limits. Live physical/readability validation is retained. A deferred alternative is not a newly failed on-air picture. Unity calls cannot be interrupted, so an individual query can exceed the allowance; its cost is recorded and subsequent background work defers. This is a cooperative scheduler, not a guaranteed frame-time ceiling.

## Recovery, ranking and timing

- An established, loaded Player target that stops producing a picture enters the same 12-second local fallback policy as sustained unreadable footage. Initial acquisition, deliberate hold, travel and wildlife cutaways remain separate. A longer no-picture failure still has a bounded exit if no local alternative is available.
- A newly validated Scenic recovery picture may reuse a previously shown composition. Reuse does not renew the region's content/exhaustion allowance or count as fresh developed footage.
- Encounter purpose and raw picture quality are separate selection dimensions. ClearShot and the director agree about admissible candidates; quality improvement is not compressed away by purpose bands.
- The manual Cinemachine bridge accounts for elapsed time once, including irregular standby evaluation intervals and native skipped-frame compensation.
- RTMP capture follows an anchored slot clock instead of starting a new interval after each render. Late or busy slots are skipped without a catch-up burst. Existing encoder pacing, bounded queues and audio behavior remain in force.
- Diagnostic capture retry waits for the previous writer and GPU requests to retire before opening another recording.

## Actual picture and Player direction

The live clean picture is measured at 96×54, at most twice per second, independently of RTMP. The sample copy's row orientation is calibrated, so the actor ROI measures the intended part of the picture rather than choosing the better of mirrored regions. Holds, recovery slates and operator UI supply no pixel evidence; stale callbacks cannot affect a new shot or world.

Persistent poor exposure or subject separation can request a local replacement in both actor and Scenic modes. It does not invalidate terrain, discard a valid current picture or request travel. Persistence, cooldown and normal continuity rules limit changes. These measurements supplement geometry; they are not semantic image understanding and cannot prove that every branch or character is visually readable.

Threat presence, developing pressure and active engagement receive distinct treatment. Ordinary nearby hostiles can earn context without permanently replacing travel or work. Developing encounters favor informative shared framing; engagement emphasizes the exchange. Encounter context memory is tied to actual shown coverage and meaningful changes in the encounter rather than a permanent global flag.

## Scenic editorial behavior

Site coverage favors progression through observed settlement/district context, buildings, entrances and loaded NPC subjects where available. A building shot does not establish the whole district. Entrances and NPC details need their relevant establishing context, and shown credit comes from sustained actual presentation. Partial observed sites remain partial; this does not infer a complete floorplan or invent NPC actions.

The final lifted/native-spline pose is checked against immutable measured subject bounds and relevant member coverage, not only a visible center point. A nominally valid route can therefore be rejected if it crops away the intended subject after terrain fitting.

Coastal routes use measured shoreline direction for dry-side establishment and shoreline-parallel motion. Confined readable viewpoints can support a finite, restrained pan without pretending a static camera has a traversable route. Pans retain the same final-view validation and fixed lens ownership as other Scenic plans.

Appearance history records only sustained actual Scenic footage. Quantized local sun, light, fog and named weather can modestly favor a changed-looking revisit among otherwise comparable eligible destinations. It preserves travel eligibility, source/biome balance and cooldowns. It is a local observation hint, not a forecast of conditions at an unloaded remote destination.

## Operator and deployment

The local overlay distinguishes `ON AIR` (live, held or recovery slate), `DIRECTOR`, `PREPARING`, `SESSION` and `RTMP`. Subject and intent metadata stay attached to the captured source, including a held picture. Status is sampled after camera rendering, and the overlay remains outside the clean feed. `broadcast diagnostics` includes shared camera CPU usage and capture-slot counters.

Install the client artifact's `ValheimBroadcaster.dll`, `ValheimBroadcaster.Core.dll`, `ValheimBroadcaster.Cinemachine.dll` and third-party notices into the broadcaster profile with Valheim stopped, replacing the existing payload and preserving configuration. The server artifact is version-aligned for deployments that need it, but these changes require no server upgrade and introduce no RPC, admission or simulation changes.

## Validation and runtime acceptance

Automated regressions cover capture rates that are not render-rate divisors, skipped readbacks, missing versus unreadable Player output, emergency Scenic reuse, shared work admission, purpose/quality ranking, native elapsed damping math, image-row/ROI mapping, persistent pixel concerns, site hierarchy credit, measured subject envelopes, finite pans and appearance revisits. Builds use real Valheim/BepInEx/Jötunn/Unity references; native tests use the pinned Cinemachine/Splines sources. Existing streaming tests use a real FFmpeg receiver and synchronized capture tests exercise video/frame mapping.

These tests do not execute a Valheim world, Unity camera-component lifecycle, GPU readback, the Windows NVENC driver or the user's Sandboxie setup. Validate the following in-game with synchronized footage/diagnostics enabled:

1. Follow a moving player through open ground, dense forest, rooms and doorways; compare candidate deferral with live picture continuity and shared foliage knowledge during mode changes.
2. Observe nearby ordinary mobs, an approaching group and active combat; verify useful enemy context without rapid edits or permanently suppressing unrelated activity.
3. Run Scenic coverage at a settlement, a loaded trader site and a coast; confirm the named subject is actually framed, site details follow context, and confined pans remain restrained.
4. Compare the local overlay with the clean RTMP picture during live footage, held-frame tests, travel and actual loss of output. Inspect cadence counters at a render rate above the configured stream rate.
5. Review pixel-driven local replacements in darkness/fog and the automatic evidence capture. Persistent poor pixels should not by themselves cause relocation or a recovery slate.

Visual quality and frame-time behavior remain runtime acceptance work; compilation and deterministic tests do not substitute for that evidence.
