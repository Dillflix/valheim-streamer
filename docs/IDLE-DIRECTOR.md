# Continuous coverage director — 0.5.0

The intended product is an autonomous camera seeking varied subjects and locations, with a broadcaster that is not a subject and ultimately has no gameplay role. A fixed orbit and repeated coverage of one creature do not meet that goal.

Current implementation:

- Ready players retain priority and stable-ID rotation.
- A loaded-region survey origin bounds discovery; it is not a scenic camera focus.
- Scenic shots select new terrain paths using bounded angular/radial sampling, excluding the previous twelve selected locations within twelve metres. Each shot moves its focus along a twelve-metre path, with ground/area/obstruction checks.
- After a scenic interval, fresh moving creature subjects are considered. Actors shown within five minutes are excluded. Stationary actors become ineligible after ten seconds; liveness/range/readiness loss also abandons a shot.
- With no fresh creature, another scenic path is selected. No valid fresh path produces an explicit region-exhaustion/recovery message, with bounded rescanning. It does not silently repeat the same orbit.
- Local broadcaster renderers are masked during normal coverage and restored on stop. F10 is an explicit visible-character test. This does not change network visibility or gameplay behavior.
- A marked, world-session-scoped solo scene probe can exercise distant network interest without another player. See [SOLO-ACQUISITION.md](SOLO-ACQUISITION.md).

Finite loaded regions have finite new content. The next required layer is a traversal controller that acquires new regions, maintains visit history, selects viable destinations, waits for real readiness and recovers on failure. It is not implemented in this version. Neither indefinite engaging coverage nor a passive/invisible observer is claimed; see [OBSERVER.md](OBSERVER.md).

Scenic/creature changes keep the existing RTMP session. Explicit stop, camera mode restart, death, connection loss/change and component disable stop output and restore owned state. `broadcast survey` resets the survey region; `broadcast anchor` remains only a compatibility alias and no longer creates an orbit point.

## Follow-up: moving the observer with the camera

The inspected Valheim 1.0.21 assembly provides `Player.TeleportTo(Vector3, Quaternion, bool distantTeleport)` and `IsTeleporting()`. Its distant path moves the character, waits for area readiness, and includes an approximately eight-second minimum before normal completion checks. It also performs floor/solid-height handling. This is a viable implementation route to investigate for ordinary player-centered world loading, but it is not enabled or implemented in 0.4.0.

A travel controller should move only the local broadcaster to a validated position near the subject, save a return location, wait for actual completion/readiness, bound retries, and handle death/disconnection/cancellation. The elevated camera position is not a safe character destination. Teleporting can unload the previous idle shot, so transit needs an honest loading/recovery view while keeping RTMP connected. Remote target coordinates are still required; teleportation does not discover private player positions. Observer presence, vulnerability, world effects, and compatibility with the real server remain runtime checks.
