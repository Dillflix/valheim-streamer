# Observer design and current limits

The final broadcaster should remain close to its camera for world loading, never appear as a subject, and avoid gameplay interactions. Invisibility to ordinary clients and exclusion from player-presence rules require separate verification. Loading and simulating a previously inactive region is itself a world effect; this design does not promise literally zero simulation impact.

## Implemented foundation: offline experiment

0.6.0 on `observer-foundation` adds an explicit `broadcast observer` mode. Normal F8/F10 modes retain their previous ordinary-player behavior. All new game behavior is **pending runtime validation**.

| Concern | Offline experiment | Remaining multiplayer work |
| --- | --- | --- |
| Body and camera | Body placed two metres below each valid camera shot; ZDO position and interest synchronized | Coordinate acquisition with authoritative observer role and ownership rules |
| Distant travel | Native player teleport to a marked destination; bounded wait, protected suspension and return | Obtain legitimate target positions and validate travel across network backends |
| Local visibility | Existing child-renderer mask, including later discovered renderers; flags restored on stop | Hide the actor and effects from unmodified clients, including late joiners |
| Input/actions | Suppress observer's ordinary player update, controls and character fixed-update paths; retain console/broadcast controls | Audit every action path and interactions executed by another simulation owner |
| Physics/damage | Disable Rigidbody collision/gravity; freeze constraints; enable local god/ghost flags | Verify authoritative damage, collision, triggers, projectiles, vehicle and combat behavior |
| AI | Local ghost flag; player update suppression | Verify all relevant simulation owners and paths; ghost is not universal isolation |
| Player presence | No exclusion implemented | Counts, sleep, scaling, events, spawning, proximity, map/player lists and time rules |
| Simulation ownership | Ordinary game's ownership behavior remains | Decide which peer simulates observer-only regions and constrain observer ownership |
| Cleanup | Native return first; restore saved flags, camera, HUD and renderer state after arrival | Disconnect/rejoin, revocation and server restart without lingering invisible actors |

The experiment is restricted to a closed local world with no peers. It rejects a connected dedicated-server client or an open hosted world, including when the broadcaster is alone. It blocks `ZNet.OpenServer` while active and rechecks its offline predicate before moving. There is no permissive configuration switch to turn this into multiplayer observer mode.

The scene-removal guard introduced after the 0.5.0 black-screen failure also covers this path. It suspends travel and skips a removal pass if the observer's ZDO is absent from the retained lists. The old reference-only probe is mutually exclusive with observer mode. Keeping body and interest together avoids deliberately separating them; the guard remains necessary until the runtime ordering is proven.

The return point is the character's position at observer activation. It is a recovery destination, **not** an idle camera anchor. Existing scenery paths and creature selection continue to change shots. Manual distant travel resets scene scouting in the destination region; autonomous selection and traversal between regions are future work.

## Verified game evidence

The audit used the repository's pinned real Valheim 1.0.21 references, not a guessed API or a substitute assembly. See [RUNTIME-TESTS.md](RUNTIME-TESTS.md) for provenance and the user's actual installed versions.

- `Player.TeleportTo(Vector3, Quaternion, bool)` and private `UpdateTeleport(float)` provide the native distant travel sequence. The latter normally runs in `Player.FixedUpdate`; the observer adapter must explicitly advance it when suppressing that loop.
- The native teleport can wait on scene readiness without its own overall deadline. The core policy adds a 45-second unpaused limit. Cancellation uses verified `m_teleporting`, `m_teleportTimer`, and `m_teleportCooldown` fields only while the mod owns the observer session.
- `SetGodMode` and `SetGhostMode` set local player state. Finding these methods is not evidence that an unmodified remote client sees an invisible player.
- Ghost checks were found in AI hearing/vision/enemy selection, monster sleep, egg spawning and damage-related paths. They do not establish that every interaction ignores the observer.
- Player-count consumers include drop generation and network/time handling. Observer-specific exclusion is not implemented in those systems.
- `ZNetScene.RemoveObjects` removes instances excluded by the current near/distant lists. Moving network interest away from an ordinary parked player can destroy that player; the supplied 0.5.0 log confirms it happened.

## Planned server companion

This section is a design contract, not implemented server code. The target remains one modded broadcaster plus a server companion, with ordinary players unmodded where the protocol permits it.

1. **Authorize the actual connected peer.** The server assigns an observer role from an explicit allowlist/admin decision tied to the authenticated connection. Never accept a claimed player/platform ID inside an RPC as authorization. Bound request size/rate, validate coordinates, and clear role/session state on disconnect. Default to ordinary behavior when authorization is absent.
2. **Separate interest from gameplay presence.** Maintain the broadcaster's loading/interest position while excluding its role from explicitly audited gameplay queries. A transport connection may still consume a server slot; gameplay-count exclusion must not silently rewrite connection management or capacity.
3. **Prevent remote actor exposure.** Verify whether the server can withhold the observer player ZDO and related effects from ordinary peers while preserving the broadcaster's own object, world updates and protocol bookkeeping. Cover initial spawn, late join, travel, reconnect and ownership transfer. Local renderer flags cannot implement this. If relevant state is distributed peer-to-peer or required by vanilla clients, revisit the protocol instead of claiming a server-only guarantee.
4. **Account for simulation ownership.** Server patches do not govern AI or interactions executed by an unmodified client. Either prevent that peer from receiving an observer actor as a participant, provide behavior the existing game already honors, or acknowledge that additional client support is required. Do not advertise a partial workaround as full isolation.
5. **Test before enabling multiplayer.** Use at least an unmodified observer of the broadcaster, a simulation-owning ordinary player, and the server. Verify visibility/effects, aggro, collisions, pickups, damage, sleep, scaling, raids/spawns, saves, reconnection and ownership. Run Steam and crossplay separately. Only then connect the role to automatic distant player selection and autonomous idle travel.

Until those checks succeed, the development branch deliberately does not enable observer travel on a shared world. The test procedure is [OBSERVER-TESTS.md](OBSERVER-TESTS.md).
