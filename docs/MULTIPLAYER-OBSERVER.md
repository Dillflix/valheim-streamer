# Read-only multiplayer observer (0.8.4)

This branch implements a server companion plus broadcaster client. The user reported successful 0.7.0 authorization, loading, explicit travel and return/resume while testing alone. **Other-client visibility and simulation isolation remain unverified**, and the 0.8.0 additions require new runtime checks. Other players use ordinary clients. Support is a **dedicated Steam server**; crossplay/PlayFab and listen servers are not supported by this protocol.

The broadcaster keeps a local character to satisfy the game's rendering, camera and scene-loading assumptions. That character is not published to the server. Its connection still supplies a world-interest position and receives ordinary world data. The server rejects object updates and gameplay RPCs from the observer, omits its gameplay player-list entry, leaves its server character identity empty, and refuses to assign it world simulation ownership.

The client selects whether to request the role when the connection opens. The server allowlist grants **permission**, not mandatory observer status: the same Steam account can join with a vanilla client, or with `ObserverConnection=false`, and play normally. An observer client sends its intent before native admission. Once selected, the role belongs to the entire connection and cannot switch to ordinary gameplay without disconnecting. Adding an already-connected ordinary player to the server allowlist does not change its role; removing an observer's authorization disconnects it with restrictions retained until cleanup. Return/off parks the local character with protections intact; F8 resumes coverage.

## Installation and configuration

Use matching 0.8.4 client and server artifacts from one successful Actions run. Upgrade both together: protocol 3 requires pre-admission intent and rejects protocol 1/2 observer handshakes. An older server still has the mandatory-account behavior; changing only the client cannot fix that server. Install with those processes stopped; preserve existing configs/worlds and test on copies first. No live installation has been performed by this source change.

| Instance | Install under its BepInEx plugins folder | Dependencies |
| --- | --- | --- |
| Thunderstore broadcaster profile | `ValheimBroadcaster/ValheimBroadcaster.dll` and `ValheimBroadcaster.Core.dll` | Existing BepInEx 5 and Jötunn 2.30.2 |
| Dedicated Steam server | `ValheimBroadcasterServer/ValheimBroadcaster.Server.dll` and `ValheimBroadcaster.Core.dll` | Server BepInEx 5 and Jötunn 2.30.2 |
| Ordinary players | Nothing | Existing game setup |

Keep one copy of the matching core DLL per process. Do not put the server DLL on ordinary clients. Game/Unity/dependency DLLs are not included in either artifact.

Run each installed plugin once to generate configuration, then stop it and edit:

Server: `BepInEx/config/com.dillflix.valheimbroadcaster.server.cfg`

```ini
[Observer]
Enabled = true
SteamIds = YOUR_BROADCASTER_STEAM64_ID
DiscoveryEnabled = true
SharePrivatePlayerPositions = true
```

Replace the placeholder with an account's actual 17-digit Steam64 ID permitted to request observer mode. Multiple IDs are comma-separated. This is an observer-permission allowlist, not a player-name or server-admin check. Restart after editing. Malformed IDs disable observer authorization without restricting vanilla access. A vanilla connection never creates an observer lease or handshake timeout, even for an allowlisted account. Only a connection that explicitly requested observer mode is held read-only and disconnected if its matching authenticated handshake does not finish within 15 seconds of readiness.

`DiscoveryEnabled=true` supplies player presence/positions and already-generated scenic destinations to confirmed observers. `SharePrivatePlayerPositions=true` allows those observers to acquire players who are not sharing a public map position. This setting is an explicit server policy: locations are delivered only to its authenticated, allowlisted observer connections. Set it to false to withhold non-public positions; those players still appear as present but cannot be acquired from hidden coordinates. These discovery settings have no effect unless the observer server role is enabled and authorized.

Broadcaster profile: `BepInEx/config/com.dillflix.valheimbroadcaster.cfg`

```ini
[Multiplayer]
ObserverConnection = true
```

Set this **before joining**. The client pins it to that connection. If the server is absent, disabled, incompatible or does not authorize this Steam identity, the client disconnects instead of silently falling back to gameplay. Password entry uses the game's usual connection flow; the observer handshake deadline starts after the server peer becomes ready. Use an existing test character without the first-login Valkyrie intro.

Ordinary local F8/F10 tests and the closed-world observer experiment remain available with `ObserverConnection=false`. The flicker change and RTMP capture alone do not require the server companion. Follow [AUTONOMOUS-BROADCAST.md](AUTONOMOUS-BROADCAST.md) for automatic acquisition, scenic travel, diagnostics and unattended-session controls.

## Controls and solo checks

The camera starts automatically after authorization and a ready local character. No `broadcast observer start` command is required on join.

| Command/control | Read-only multiplayer behavior |
| --- | --- |
| `broadcast status` | Camera/observer phase and stream state |
| `broadcast observer status` | Current coordinate, origin/destination distances and last camera gap |
| `broadcast diagnostics` | Discovery, director/travel and frame-presentation diagnostics |
| `broadcast observer mark` | Save the current carrier coordinate for this connection |
| `broadcast observer travel` | Native local-carrier teleport to the saved marker |
| `broadcast observer travel x y z` | Controlled travel to explicit finite coordinates within +/-20000m per axis |
| `broadcast observer return`, `broadcast off`, F8 while active | Return to activation position and park; ordinary gameplay controls remain suppressed |
| F8, `broadcast on`, or `broadcast observer start` while parked | Resume camera coverage with the same isolated connection |
| F10 / self-follow | Unavailable in a read-only connection; broadcaster remains hidden |
| F11 / stream commands | Explicit RTMP intent/start/stop; off/F8 also stops publishing and suppresses automatic output |

Travel remains bounded to 45 unpaused seconds. Timeout/errors suspend travel with an explicit return option. Retention guards still prevent an excluding scene-removal pass from destroying the local carrier. The reference-only probe is not the multiplayer travel path.

You can test authorization, coordinate travel, return/park/resume and streaming while alone on the server. Use previously visited/generated locations whose coordinates you know. This establishes neither visibility from another client nor simulation isolation; those require the matrix below.

## Implemented boundaries

- Authorization uses the connected `ZSteamSocket` identity after the game's normal Steam authentication. Custom messages carry a protocol version and decision, never a claimed Steam ID or requested player identity. Replies must come from the broadcaster's actual server RPC object.
- Discovery is a separate versioned, bounded read protocol. The client requests it every two seconds; the server answers at most once per second per observer. A response is limited to 32 KiB, 64 players and 128 scenic destinations. Discovery expires ten seconds after local receipt and is cleared on connection/world changes; delayed coordinates do not establish that a player's scene is loaded. Rotating destination pages use a bounded catalog/cache rather than an unbounded history.
- Scenic destinations are dry-land centers of already-generated regions with an already-generated 3×3 zone neighborhood. Procedural height/biome queries do not instantiate content or generate new server regions. A candidate still requires native travel, terrain/camera checks and actual scene readiness on arrival.
- The client sends protocol-3 intent before its native `PeerInfo` packet on the same reliable Steam RPC stream. The server reserves that connection before character publication, then checks the authenticated transport identity before approving it. No role is inferred from allowlist membership alone. Admission without intent is locked to ordinary gameplay; late observer requests are rejected. The server rejects observer `RPC_ZDOData` packets and character-ID announcements; the client also clears its outgoing object sync list. There is no transition that tries to retract an already-visible character.
- Gameplay routed requests are blocked. The one allowed native `RequestZDO` read is checked against the actual sender, server recipient, empty target object, exact method and a bounded envelope/argument length before native dispatch. Native server save/admin mutation commands are also refused for this role.
- Interest metadata accepts only finite bounded coordinates, keeps map sharing false, and discards observer gameplay metadata. The observer is excluded from gameplay player lists, random-event peer data and peer-driven server zone generation. Sleeping uses the game's character-ZDO list, which has no observer character.
- Actual transport peers and capacity accounting remain intact: the observer still occupies a connection slot. The admission check uses the real ready-connection count, so gameplay-list exclusion does not bypass server capacity.
- The server refuses observer ownership assignment. On the broadcaster, the native ownership predicate remains false for non-avatar objects and ownership claims are blocked. The client therefore renders replicated actors without taking authority over them.
- The local character retains input/physics suppression, god/ghost state, renderer masking, and camera-relative movement. Its child lights and audio sources are suppressed and restored along with the mask.
- Revocation/timeout requests disconnection while the role stays pinned through cleanup. Stopping either plugin disconnects its observer sessions before removing guards. An exception during a live world is not permission to resume gameplay.

## Limits that affect the picture

This is an experimental boundary around the audited vanilla network paths, not a security sandbox for arbitrary third-party plugins. Custom direct RPCs, modified protocol implementations and other Harmony patches require a separate compatibility audit.

**Dormant regions remain dormant.** With no ordinary player simulating a creature, the broadcaster does not take ownership to make it move. The server also does not generate new regions solely because an observer moved there. Previously generated scenery can be viewed; untouched regions may lack server-generated structures/actors. Local terrain readiness does not prove a complete scene. Idle programming must use available scenery when no active creature exists.

With only observers connected, gameplay player count is zero and the native world-time rule can pause time. Rendering, network delivery and local resource loading still consume resources. This does not establish literal zero effect on every local rendering/cache path, every third-party mod, or every server statistic.

The 0.8.0 director uses the authorized discovery feed for automatic distant-player acquisition and scenic-region travel. Manual observer travel remains available. See [AUTONOMOUS-BROADCAST.md](AUTONOMOUS-BROADCAST.md) for selection, stale-data handling, manual overrides and recovery. Creature-simulation improvements remain deferred.

## Multiplayer runtime matrix — not run here

Use a disposable dedicated Steam server, one broadcaster, and at least one unmodified ordinary client. Add a second ordinary client for authority handoffs and late joins.

| Test | Required observation |
| --- | --- |
| Allowed broadcaster joins | Matching 0.8.4 client/server startup lines; authorization succeeds; camera starts; no broadcaster actor/nameplate/equipment/map entry appears on ordinary clients |
| Unauthorized / missing companion / mismatched protocol | Clear rejection/disconnection; no ordinary-gameplay fallback on the configured broadcaster |
| Allowlisted vanilla client without broadcaster mod | Normal login, character publication, movement, interactions and sleep; stays connected beyond 15 seconds |
| Allowlisted modded client with ObserverConnection=false | Same ordinary gameplay behavior; no observer intent, reservation, lease or discovery access |
| Same account changes roles | Disconnect ordinary play, enable ObserverConnection before joining, verify observer authorization; disconnect and disable it, then verify normal gameplay returns |
| Late request or allowlist edit | A ready ordinary connection is never promoted; late observer intent is rejected; editing membership alone does not disconnect vanilla players |
| Disabled/invalid observer settings | Vanilla clients still join normally; explicit observer requests fail without gameplay fallback |
| Password prompt | Intent precedes PeerInfo after password entry; time spent at the prompt does not consume the post-authentication handshake deadline |
| Late ordinary join | No broadcaster appears during initial scene synchronization |
| Nearby interactions | Broadcaster does not block movement, absorb projectiles, collect drops, open doors, attack, damage structures or attract AI |
| Sleep/counts/events | Ordinary players can sleep without the observer; gameplay counts/events match the ordinary-player population; observer still consumes a connection slot |
| Active simulation | A creature owned by an ordinary player moves normally in broadcaster footage; observer does not acquire its ownership |
| Authority handoff | Switching/disconnecting ordinary owners does not assign objects to the observer or leak an actor |
| Alone on server | Travel/return/park/resume works; dormant creatures/time may stay static as documented |
| Untouched destination | No observer-driven server zone generation; report incomplete scene rather than calling readiness a fidelity pass |
| Return/off | Character stays hidden/noninteractive after parking; F8 resumes; RTMP stops as documented |
| Reconnect/config changes | Fresh authorization per connection; toggling client config mid-session does not release isolation; server revocation disconnects; adding a connected ordinary player does not silently convert its role |
| Plugin disable / connection loss | No exposed controllable actor or lingering role; no stale marker/state on another world |
| Sustained session | No growing sync queue, pending ZDO requests, repeated errors, frame textures, orphan FFmpeg processes or A/V drift |

Record both plugin versions/commit, actual Valheim/BepInEx/Jötunn versions, all participating mods, commands and per-instance logs. Verify from the ordinary clients and server, not only from the broadcaster's hidden local view.

The user's earlier 0.7.0 solo pass covers the reported basic connection/travel workflow only. The new discovery, autonomous travel, reconnection and output-retry cases are listed in [AUTONOMOUS-BROADCAST.md](AUTONOMOUS-BROADCAST.md) and remain pending in Valheim.
