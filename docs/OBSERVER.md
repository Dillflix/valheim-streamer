# Observer design and current limits

The final broadcaster should remain close to its camera for world loading, never appear as a subject, and avoid gameplay interactions. Invisibility to ordinary clients and exclusion from player-presence rules require separate verification. Loading and simulating a previously inactive region is itself a world effect; this design does not promise literally zero simulation impact.

## Original foundation: offline experiment (0.6.0)

0.6.0 on `observer-foundation` adds an explicit `broadcast observer` mode. Normal F8/F10 modes retain their previous ordinary-player behavior. All new game behavior is **pending runtime validation**.

| Concern | Offline experiment | Multiplayer requirements identified at that stage |
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

The original offline experiment is restricted to a closed local world with no peers. It rejects a connected dedicated-server client or an open hosted world, including when the broadcaster is alone. It blocks `ZNet.OpenServer` while active and rechecks its offline predicate before moving. The separate 0.7.0 multiplayer path requires a pre-join read-only setting plus matching server authorization; it does not simply bypass this gate.

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

## Read-only server companion (0.7.0)

The first companion implementation now exists on `observer-multiplayer`. It avoids publishing the broadcaster character at all: the server accepts its bounded interest coordinate, supplies scene data, and rejects its world object writes/gameplay requests. The role is attached to an explicitly allowlisted authenticated Steam connection and stays pinned until disconnect. Gameplay player queries, events, zone generation and ownership assignment exclude that role; actual connection slots remain accounted for.

The broadcaster still uses its local character as a camera/scene carrier. Normal return restores ordinary control only in the closed local experiment. Multiplayer return parks the carrier while retaining isolation, with F8 to resume. Local renderer, light and audio masking are presentation measures alongside the network boundary.

These are implemented source paths, **not a successful multiplayer runtime result**. The target remains one broadcaster plus the companion and unmodified ordinary players. Read [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md) for installation, exact supported scope, limits and the pending matrix. Crossplay/listen servers, arbitrary third-party mutation RPCs, autonomous travel and server simulation of otherwise dormant regions remain outside this experiment.
