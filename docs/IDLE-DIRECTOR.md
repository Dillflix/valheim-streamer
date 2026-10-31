# Continuous coverage director — proposed next milestone

Status: design proposal, not implemented in 0.3.0. Current normal mode shows a waiting slate when no eligible player is loaded. Self-follow is a deliberate test mode. Neither currently roams or tracks wildlife.

The broadcast should remain useful with zero, one, or several other players, without requiring anyone else to install a mod. Keep the encoder session independent from shot selection so changing coverage never restarts RTMP or interrupts audio.

## Coverage priorities

| Priority | Mode | Selection and fallback |
| --- | --- | --- |
| 1 | Player coverage | Follow eligible other players; rotate stable IDs when several are ready. Exclude the broadcaster. Preserve explicit hold/manual selection. |
| 2 | Acquiring a player | A player is connected but not yet viewable. Keep a valid idle shot while attempting a bounded acquisition; do not show a false “empty server” status. Try alternatives when acquisition times out. |
| 3 | Idle activity | Follow a loaded, eligible creature or frame a small nearby encounter. Prefer movement and visibility; avoid sitting on a stationary or occluded subject. |
| 4 | Idle scenic | Choose a configured scenic anchor and slowly orbit, pan, or move along a short validated path. Rotate anchors to vary the shot. |
| 5 | Safe view | Hold a validated local wide shot when no other candidate is suitable. Use a slate only if the scene itself is unavailable. |
| — | Recovery | Scene/connection/observer/capture faults suspend unsafe camera operations. Encoder failure and director failure have separate recovery rules. |

Use connected-player presence separately from loaded-player candidates. An empty `Player.GetAllPlayers()`-based target list is not evidence that the server has no other players. Read available network presence, exclude the observer by stable identity, then separately track eligibility, position availability, loading, and readiness. Distant players without a discoverable position remain connected but unavailable. Do not enable experimental interest-position overrides as a side effect of idle coverage.

## First idle providers

**Scenic anchors are the reliable foundation.** Start with an explicitly configured local anchor near the parked observer and optional operator-selected landmarks: a base exterior, coastline, meadow, or mountain outlook. Each anchor defines position, look-at point, safe orbit radius, height range, dwell range, and world identity. Validate terrain/obstruction and scene readiness before showing it. Start with simple slow pans/orbits; arbitrary free flight needs path planning and world-loading work.

**Creature coverage adds variety.** Consider only already-loaded characters and exclude players, dead/despawning objects, invalid transforms, and disallowed types. Support an allowlist for wildlife/tamed animals first, then optional hostile creatures. Use transient runtime IDs and a world/session generation; never persist a reference to an actor across world loads. Require a ready, visible shot. Target death, loss, excessive occlusion, or inactivity should select an alternative immediately or return to a scenic anchor.

**Activity shots are a later provider.** Nearby encounters can be framed as a group instead of jumping between every participant. Gathering interest from motion/proximity is enough for an initial heuristic; actual combat/event signals should be verified against the target game build. Avoid requiring new client RPCs just to add cinematic variety.

A compact idle playlist could use approximately 45–90 seconds of scenery, 20–45 seconds following wildlife, and a short wide establishing shot between subjects. These are proposed tunable defaults. Weight candidates by visibility, activity, and recent use; add a cooldown so one creature or anchor does not dominate. Use stable tie-breaking and a seeded random source for reproducible tests.

## Transitions and camera behavior

- A newly ready player preempts idle coverage promptly, with an intentional cut or short compatible blend. Confirm availability across two roster samples to avoid transient churn.
- When the last other player disappears, allow a short grace period (suggested five seconds) before committing to idle. Retain the last valid view only while it remains safe and ready.
- A connected but unavailable player does not force an endless slate. Continue idle coverage while periodically retrying acquisition with a cooldown.
- Respect manual hold as an explicit operator override; release it only on invalidation or explicit command. Report that hold overrides player priority.
- Cuts between distant subjects must reset smoothing; never interpolate a camera through terrain between unrelated coordinates.
- Move the existing audio listener with the selected camera for all output modes, preserving the same AAC stream. Avoid fast listener motion that creates distracting spatial jumps; runtime-test cuts and attenuation.
- Keep the RTMP encoder alive during normal player/idle transitions. Today's `StartBroadcast` calls `Stop`, which stops streaming; the new director must switch coverage internally rather than reuse this start/stop path.

## Component boundary

Extend the pure core with a coverage policy over provider snapshots. Keep Unity objects and physics out of it.

- `ShotCandidate`: transient ID, provider kind, world/session generation, readiness, priority/interest score, expiry, and timing limits. The game adapter retains the actual transform/anchor binding.
- `IShotProvider` adapters: players, local creatures, scenic anchors, and safe view. The names are proposed interfaces, not existing APIs.
- `CoverageDirector`: player priority, availability/grace/acquisition policy, idle selection, dwell/cooldown, manual hold, and deterministic fallback.
- `CameraRig`: shot strategies for follow/orbit/pan, obstruction checks, cut vs blend, and listener alignment.
- `OutputController`: RTMP lifetime independent from coverage. Explicit operator start/stop remains authoritative; no automatic publishing after startup.

Preserve current player-selection tests and self-follow as an explicit override. Do not change the existing director's meaning of “eligible roster” into a misleading connected-player count.

## World roaming and observer limits

A camera coordinate is not a loaded world. The first milestone is deliberately restricted to the client's ready scene around the observer. Never discover this boundary by showing missing terrain to viewers.

A later roaming milestone needs a dedicated world-interest controller: anchor acquisition, complete scene readiness criteria, timeout/cancellation, last-known-good fallback, and observer simulation tests. The current experimental network reference override is not proof of reliable distant coverage. Decide explicitly how the observer body is kept safe and what server-side support, if any, is required before advertising unattended world travel.

The broadcaster remains a normal character. Camera movement does not prevent damage, hunger-related behavior, enemy interaction, sleep/player-count effects, or simulation ownership. Monitoring and recovery can detect failures; they do not make the observer passive. No idle policy should respawn, teleport, reconnect, or alter world state without an explicitly designed and enabled policy.

## Long-running operation

- Throttle actor discovery (for example, once per second), cap candidates, reuse collections, and expire stale scene references. Do not scan every world object per frame.
- Separate health signals: connection, observer alive/spawned, scene ready, target valid, render callbacks, audio callbacks, encoder progress, and receiver availability.
- On bad subjects, select a safe shot. On unavailable scene, display an accurate slate. Current stop-on-death/world-exit behavior remains until an explicit recovery policy is implemented and tested.
- Add an optional, bounded reconnect policy for transient encoder/receiver failures later: backoff, attempt cap, cooldown, and cancellation on operator stop. Do not silently bypass invalid configuration or repeatedly restart a failing process.
- Record transition reasons, target/provider counts, uptime, and capture/encoder health. Never log stream keys. Rate-limit repeated messages and keep per-session history bounded.
- Set an unattended operating envelope with measured CPU/GPU load, memory, network usage, audio skew, and behavior when the window loses focus. Schedule no periodic restart unless evidence shows it is necessary.

## Acceptance plan

1. Pure policy tests: zero/one/many players; observer exclusion; connected-but-unavailable distinction; join preemption; leave grace; actor invalidation; anchor fallback; bounded candidate/cooldown state; manual hold; world generation reset.
2. Solo runtime: configured local orbit/pan, visible wildlife tracking, target loss, correct spatial audio, and seamless transitions while a receiver records one continuous RTMP session.
3. Multiplayer: player join takes priority, multiple players rotate, last departure enters idle, and distant/unavailable players do not cause a permanent slate or a false empty-server claim.
4. Soak: at least eight hours, then a 24-hour run; record memory/process/texture counts, frame cadence, audio skew, repeated target changes, receiver outage/recovery, world transitions, and observer survival. Test failure recovery explicitly before calling the client unattended.

The first implementation should deliver the provider/policy boundary plus one local scenic orbit and loaded-wildlife following. Arbitrary world roaming and autonomous observer lifecycle management follow after that baseline has runtime evidence.
