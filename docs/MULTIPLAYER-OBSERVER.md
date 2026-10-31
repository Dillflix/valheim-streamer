# Read-only multiplayer observer experiment (0.7.0)

This branch implements a server companion plus broadcaster client. It is **not yet runtime-validated in multiplayer**. Other players use ordinary clients. Initial support is a **dedicated Steam server**; crossplay/PlayFab and listen servers are not supported by this protocol.

The broadcaster keeps a local character to satisfy the game's rendering, camera and scene-loading assumptions. That character is not published to the server. Its connection still supplies a world-interest position and receives ordinary world data. The server rejects object updates and gameplay RPCs from the observer, omits its gameplay player-list entry, leaves its server character identity empty, and refuses to assign it world simulation ownership.

The role belongs to the entire connection and is selected when that connection opens. It cannot be switched to ordinary gameplay without disconnecting. Adding an already-connected ordinary player to the server allowlist takes effect only after reconnecting; removing an observer's authorization disconnects it with restrictions retained until cleanup. Return/off parks the local character with protections intact; F8 resumes coverage. This avoids an invisible-but-interactive state after turning the camera off.

## Installation and configuration

Use the matching client and server artifacts from one successful `observer-multiplayer` Actions run. Install with those processes stopped; preserve existing configs/worlds and test on copies first. No live installation has been performed by this change.

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
```

Replace the placeholder with the broadcaster account's actual 17-digit Steam64 ID. Multiple IDs are comma-separated. This is an explicit observer-only account allowlist, not a player-name or server-admin check. Restart after editing. Malformed IDs disable authorization. An allowlisted connection without a matching client handshake is disconnected after 15 seconds once authenticated.

Broadcaster profile: `BepInEx/config/com.dillflix.valheimbroadcaster.cfg`

```ini
[Multiplayer]
ObserverConnection = true
```

Set this **before joining**. The client pins it to that connection. If the server is absent, disabled, incompatible or does not authorize this Steam identity, the client disconnects instead of silently falling back to gameplay. Password entry uses the game's usual connection flow; the observer handshake deadline starts after the server peer becomes ready. Use an existing test character without the first-login Valkyrie intro.

Ordinary local F8/F10 tests and the closed-world observer experiment remain available with `ObserverConnection=false`. The flicker change does not require the server companion.

## Controls and solo checks

The camera starts automatically after authorization and a ready local character. No `broadcast observer start` command is required on join.

| Command/control | Read-only multiplayer behavior |
| --- | --- |
| `broadcast status` | Camera/observer phase and stream state |
| `broadcast observer status` | Current coordinate, origin/destination distances and last camera gap |
| `broadcast diagnostics` | Held/slate frame counts, last readiness failure, frame capture availability |
| `broadcast observer mark` | Save the current carrier coordinate for this connection |
| `broadcast observer travel` | Native local-carrier teleport to the saved marker |
| `broadcast observer travel x y z` | Controlled travel to explicit finite coordinates within +/-20000m per axis |
| `broadcast observer return`, `broadcast off`, F8 while active | Return to activation position and park; ordinary gameplay controls remain suppressed |
| F8, `broadcast on`, or `broadcast observer start` while parked | Resume camera coverage with the same isolated connection |
| F10 / self-follow | Unavailable in a read-only connection; broadcaster remains hidden |
| F11 / stream commands | Explicit RTMP start/stop as before; off/F8 also stops publishing |

Travel remains bounded to 45 unpaused seconds. Timeout/errors suspend travel with an explicit return option. Retention guards still prevent an excluding scene-removal pass from destroying the local carrier. The reference-only probe is not the multiplayer travel path.

You can test authorization, coordinate travel, return/park/resume and streaming while alone on the server. Use previously visited/generated locations whose coordinates you know. This establishes neither visibility from another client nor simulation isolation; those require the matrix below.

## Implemented boundaries

- Authorization uses the connected `ZSteamSocket` identity after the game's normal Steam authentication. Custom messages carry a protocol version and decision, never a claimed Steam ID or requested player identity. Replies must come from the broadcaster's actual server RPC object.
- Server reservations and client restrictions apply before character publication. The server rejects observer `RPC_ZDOData` packets and character-ID announcements; the client also clears its outgoing object sync list. There is no transition that tries to retract an already-visible character.
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

Automatic distant-player acquisition and autonomous selection of new idle regions are separate remaining work. This branch supplies explicit travel and the isolation boundary; it does not infer hidden player coordinates.

## Multiplayer runtime matrix — not run here

Use a disposable dedicated Steam server, one broadcaster, and at least one unmodified ordinary client. Add a second ordinary client for authority handoffs and late joins.

| Test | Required observation |
| --- | --- |
| Allowed broadcaster joins | Matching 0.7.0 client/server startup lines; authorization succeeds; camera starts; no broadcaster actor/nameplate/equipment/map entry appears on ordinary clients |
| Unauthorized / missing companion / mismatched protocol | Clear rejection/disconnection; no ordinary-gameplay fallback on the configured broadcaster |
| Allowlisted vanilla client without broadcaster mod | Connection held read-only and disconnected after handshake deadline; no published actor |
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
