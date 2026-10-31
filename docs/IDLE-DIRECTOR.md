# Continuous coverage director — 0.4.0

The first local coverage implementation is now present. This document describes shipped source behavior; Valheim runtime and long-duration validation are still pending.

| Condition | Implemented coverage |
| --- | --- |
| Several ready players | Stable-ID rotation using configured player dwell, next/previous and hold |
| One ready player | Continuous follow |
| Players connected but none ready | Continue local scenic/creature coverage, recheck every 0.5 seconds, and report awaiting-scene status |
| No other players | Alternate scenic orbit and nearby creatures |
| No suitable creature | Continue the scenic orbit |
| No ready scene/camera position | Recovery slate, with output session retained |

## Providers and timing

`TargetRoster` separates network presence from ready scene objects. The observer is excluded; self-follow remains a deliberate F10 override. Only ready players enter `BroadcastDirector`, so an unloaded player cannot stall rotation of available players. An immediately invalidated current player triggers a roster refresh rather than waiting out its dwell.

`IdleShots` captures the observer position as a session-local scenic anchor. It scans `Character.GetAllCharacters()` once per second, retaining at most 32 living active non-player characters within the configured radius with valid network identities and ready positions. The current subject is revalidated every update and at camera application. Candidates include wildlife, tamed creatures, and enemies; the implementation does not classify combat or score activity. It never spawns or controls actors.

`CoverageDirector` has Player, Scenic, Creature, Recovery and Disabled modes. Default idle timing is 45 seconds scenic / 25 seconds creature. Creature IDs rotate ordinally independent of enumeration order, with only one previous ID retained. No creature means scenery continues; a later candidate can be chosen once the scenic interval has elapsed. Missing/dead/out-of-range creatures immediately fall back to scenery. Scene recovery returns to a fresh scenic interval.

Scenery slowly orbits the anchor at two degrees per second, using configured camera distance/height/FOV and obstruction handling. The angle stays bounded. The focus, desired camera position, and final obstruction-adjusted position require scene readiness. `broadcast anchor` resets the anchor to the observer's current location while normal coverage is active, without stopping RTMP. Anchors are never persisted into another world.

Ready players always preempt idle, including when idle is held. Hold pauses idle timing/orbit or player cycling; invalidation still falls back. A departed last player enters idle immediately. Join hysteresis, departure grace, motion-interest scoring, group shots, and multiple saved landmarks are not part of this implementation.

## Acquisition and loading boundary

Default mode rechecks loaded players and network presence without changing world interest. A distant private-position player can remain connected and unavailable; status does not claim that the server is empty. There is no new server companion or position relay.

Opt-in `Experimental.RemoteTargets` uses public positions only. With no ready player, a local cooldown (default 30 seconds) precedes an acquisition attempt (default maximum 20 seconds). The acquisition window restores local interest during cooldown and advances to another candidate after an attempt. A lone candidate is also bounded and retried. Once a player becomes ready, normal player coverage takes priority and maintains its reference position while followed.

Remote interest changes can unload the local scenic area. Valid idle footage continues while available; recovery is used if it becomes unavailable. A camera coordinate is not proof that terrain/actors are loaded. This release does not promise arbitrary world roaming or complete remote scene delivery.

## Continuous output and lifetime

Automatic changes between players, creatures, scenery and recovery never call `Stop` or restart FFmpeg. The existing audio listener follows the selected camera while streaming. The actual game framebuffer, including recovery and status overlays, is captured normally.

Explicit F8/off, camera mode restart (F10 or on/self), death, world connection loss/change and component disable still stop the stream. The session anchor, player/creature bindings, acquisition state and timing are cleared. No automatic respawn, teleport, reconnect, or observer protection is added. The observer remains an ordinary character.

## Verification

The core harness includes 29 cases, including coverage selection, pending-player distinction, scene recovery, invalidation, stable creature rotation, hold, reset, bounded acquisition and a 24-hour *simulated policy* run. The plugin is compiled against real game assemblies, and CI also runs the existing 26 RTMP tests. These are not substitutes for the pending [runtime matrix](RUNTIME-TESTS.md), especially spatial sound, scene completeness, performance and actual unattended operation.

## Follow-up: moving the observer with the camera

The inspected Valheim 1.0.21 assembly provides `Player.TeleportTo(Vector3, Quaternion, bool distantTeleport)` and `IsTeleporting()`. Its distant path moves the character, waits for area readiness, and includes an approximately eight-second minimum before normal completion checks. It also performs floor/solid-height handling. This is a viable implementation route to investigate for ordinary player-centered world loading, but it is not enabled or implemented in 0.4.0.

A travel controller should move only the local broadcaster to a validated position near the subject, save a return location, wait for actual completion/readiness, bound retries, and handle death/disconnection/cancellation. The elevated camera position is not a safe character destination. Teleporting can unload the previous idle shot, so transit needs an honest loading/recovery view while keeping RTMP connected. Remote target coordinates are still required; teleportation does not discover private player positions. Observer presence, vulnerability, world effects, and compatibility with the real server remain runtime checks.
