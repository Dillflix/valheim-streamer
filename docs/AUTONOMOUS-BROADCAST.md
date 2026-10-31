# Autonomous broadcasting — 0.8.0

This milestone connects the read-only observer to automatic player acquisition, travel between generated scenic regions, stream recovery and opt-in connection recovery. It retains the 0.7.0 local character carrier and its server-enforced read-only role; it does not integrate the separate bodyless-spectator experiment.

The user reported that 0.7.0 authorization, loading, explicit travel and return/resume work while testing alone. That result does not validate these new behaviors. Builds, pure-policy tests, synthetic media tests and pending Valheim tests are recorded separately in [RUNTIME-TESTS.md](RUNTIME-TESTS.md).

## Upgrade and enable

Use the matching **0.8.0 client and server** artifacts from one successful `autonomous-broadcast` Actions run. Both retain the existing BepInEx 5 / Jötunn 2.30.2 dependencies and Valheim 1.0.21 compilation baseline. Install with each process stopped, replace the two owned DLLs for that instance, retain existing configs, and avoid duplicate Core DLLs. Ordinary players need no broadcaster mod. Protocol 2 rejects a mismatched 0.7.0 observer rather than falling back to normal gameplay.

This requires a dedicated **Steam** server. Crossplay/PlayFab and in-game listen servers remain unsupported. Set up the server allowlist and client role as described in [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md).

Server `BepInEx/config/com.dillflix.valheimbroadcaster.server.cfg`:

```ini
[Observer]
Enabled = true
SteamIds = YOUR_BROADCASTER_STEAM64_ID
DiscoveryEnabled = true
SharePrivatePlayerPositions = true
```

The private-position setting gives confirmed, allowlisted observers server-known locations even when the ordinary player is not publicly sharing a map position. Set it false to respect public-map sharing for acquisition. Presence remains visible when a position is withheld. No untrusted payload identity authorizes this feed.

Broadcaster profile `BepInEx/config/com.dillflix.valheimbroadcaster.cfg`:

```ini
[Multiplayer]
ObserverConnection = true

[Automation]
Enabled = true
ScenicTravel = true
RegionSeconds = 120
RegionRevisitSeconds = 1800
AcquireSeconds = 65
RetrySeconds = 30
MinimumRegionDistance = 256
MaximumRegionDistance = 10000

[Unattended]
Enabled = false

[Streaming]
AutoStart = false
AutoRetry = true
VideoEncoder = libx264
NvencGpu = -1
```

Select the observer role **before joining**. Automation applies only to that authorized read-only observer. Ordinary F8/F10 and offline observer modes retain their separate behavior. `Automation.Enabled=true` does not make an ordinary character noninteractive.

Configure `Streaming.FFmpegPath` and `Streaming.RtmpUrl` before requesting output. `VideoEncoder=h264_nvenc` selects NVIDIA encoding on the broadcaster's GPU; the default remains `libx264`. See [STREAMING.md](STREAMING.md) for the complete audio/video setup and an independent NVENC test.

Begin with `Unattended.Enabled=false` and `Streaming.AutoStart=false` to verify travel and manual stopping. For an unattended session, enable connection recovery before the initial manual server join, then select the intended character/server normally and enter its password through the game. The adapter captures non-secret join metadata even before opt-in, so recovery can also be enabled during an authorized passwordless observer session. A password-protected server requires one manual join with recovery enabled to retain its password in memory; an earlier password cannot be reconstructed. `broadcast unattended status` explains an unarmed session. Automatic recovery remains bounded and requires the normal observer handshake.

## Player acquisition

The server supplies a snapshot of ordinary connected players, including players beyond the broadcaster's loaded region. Session identity drives selection; names are display labels and character identity can change after respawn. The client distinguishes presence, a usable coordinate, an instantiated living character, scene readiness and a successfully presented shot.

With a ready selected player nearby, the director follows and accumulates usable viewing time. To acquire a distant player, it uses the existing protected native observer travel path, waits for arrival and the actual character scene, then begins the shot. Several players rotate even when they are in different regions. A connected player without a usable coordinate or loaded character remains pending; it does not turn the server into an empty roster.

Discovery requests run every two seconds. A snapshot expires ten seconds after receipt. Coordinates are hints for acquiring the scene, not proof that a character is still there. Failure/timeout puts the target on a retry cooldown; a lone unreachable player must not trigger continuous teleporting. Portal movement, respawn and disconnect require a fresh target/scene check. Active travel has one destination at a time; target changes must not stack native teleports.

The acquisition budget defaults to 65 seconds, including native travel; arriving does not reset it. Each native observer trip also has its own 45-second bound, and a single acquisition attempt permits at most three relocations. Failed-target cooldown starts at 30 seconds and doubles up to ten minutes. Player dwell uses `Director.DwellSeconds` and advances only for presented ready-player footage. A loading slate or scenic fallback must not count as time spent watching that player.

## Autonomous scenery

With no usable player shot, local scenic paths continue to vary. When the region dwell expires, the director can acquire another generated scenic region rather than indefinitely repeating paths around one survey origin. Player acquisition takes priority over scenic programming.

The companion's catalog contains dry-land centers with an already-generated 3×3 zone neighborhood. It does not generate a new server zone or awaken creatures to supply a destination. Bounded catalog pages rotate; the client maintains a bounded cache and tracks recent region use. The default destination distance is 256–10000 metres from the current region, with 120 seconds of usable region coverage and a 30-minute revisit interval. A separate ten-second unready-region interval can prompt a new search when no usable local shot exists. Failed destinations receive a cooldown.

A candidate's procedural terrain height is only a travel hint. Native arrival, loaded terrain and camera/ground checks must still pass. The previous picture cannot remain live while its region is unloaded: travel uses the explicit loading/recovery presentation, and the same healthy RTMP session can carry it.

An empty or exhausted catalog is reported in diagnostics. Visit history holds up to 128 regions and does not evict an unexpired visit merely to make space; very fast traversal can therefore wait for a history slot to expire. Neither limitation is permission to generate untouched parts of the world, silently recycle a fixed orbit, or claim that arbitrary scenery is fully loaded. A freshly created server may need an ordinary player to visit several separated areas first. Creature improvements are deferred; dormant creatures can remain stationary and fail the existing motion-freshness filter.

## Controls and diagnostics

| Command/control | Intent |
| --- | --- |
| `broadcast status` | Current coverage, subject, observer phase and output state |
| `broadcast diagnostics` | Discovery age/counts, selection/travel reason, retry/recovery state, frame holds/slates and output diagnostics |
| `broadcast auto on` / `off` / `status` | Resume, pause or inspect automatic player/scenic scheduling |
| `broadcast next` / `previous` / `follow <id>` | Select through the current target roster; names do not determine identity |
| `broadcast hold` / `resume` | Hold/resume automatic shot cycling |
| `broadcast observer travel x y z` | Explicit protected travel override |
| `broadcast observer return`, `broadcast off`, F8 while active | Cancel automatic intent, stop publishing, return and park with protections retained |
| F8 / `broadcast on` while parked | Resume camera coverage; explicitly stopped output stays stopped until requested again |
| `broadcast unattended on` / `off` / `status` | Arm/inspect recovery; `off` cancels it, pauses automation and stops requested output |
| `broadcast stream on` / `off` / `status` / `diagnostics` | Request/cancel output and inspect selected encoder, retry schedule and sanitized failures |

The observer lifecycle and programming state are different. `Watching` means the carrier is active and ready; the director can separately be following a player, acquiring one, providing scenery or waiting for a usable destination. `Travelling`, `Returning`, `Parked` and `Suspended` describe the carrier's movement/protection state. Use the detailed reason and discovery age when coverage does not change as expected.

Manual overrides take precedence over background retries. `broadcast auto off` pauses automatic selection/travel while camera/output can continue; an already-running native trip finishes coherently. Explicit observer travel pauses automatic scheduling until `broadcast auto on`. Explicit stream off suppresses automatic publishing. Explicit broadcast off/observer return cancels autonomous scheduling and reconnect intent while protected return finishes. `broadcast unattended off` also pauses automation and cancels any requested output, although the camera can continue; use `broadcast auto on` and `broadcast stream on` to resume those independently. A background failure must never turn the read-only connection into ordinary gameplay.

## Solo acquisition route

The route exercise uses the same travel/acquisition machinery with known coordinates. It does not spawn fake network players, publish a broadcaster actor, or prove remote-player replication.

1. In a ready test world, add separated, previously generated destinations with `broadcast route add x y z`. One to 32 marks are accepted; two or more make repeated acquisition easier to observe. `broadcast route add` records the current position instead.
2. Inspect `broadcast route status`, then run `broadcast route start`.
3. Observe successive travel, arrival/readiness and three seconds of usable scenic footage per destination. Check `broadcast diagnostics` for target, travel and retry reasons.
4. Use `broadcast route stop` to leave the exercise. `broadcast route clear` removes the route. Route coordinates are scoped to the world session; do not expect them to transfer to another server/world.
5. Exercise explicit off during travel, timeout/recovery, restart/resume and an unavailable destination. Confirm that cancelling the route does not leave a queued teleport or cause later unexpected travel.

A deliberately invalid/unloaded destination is a failure-path test; a recovery slate is not a success. An actual ordinary client is still needed to verify player discovery/replication, invisibility, interactions and ownership handoffs.

## Unattended output and connection recovery

Output intent is separate from a particular FFmpeg process. A recoverable failure stops that process and schedules a bounded retry while output remains requested. The retry cycle backs off and periodically cools down; a stable period of actual publishing resets the burst. Invalid configuration and recognized capability/authentication failures block repeated automatic starts. `broadcast stream diagnostics` reports the selected encoder, attempt and sanitized failure. See [STREAMING.md](STREAMING.md) for exact delays.

Connection recovery is opt-in and concerns an already established observer session. After a successful authorized observer start, it pins the intended endpoint, character profile and world UID. Unexpected loss uses the inspected native return-to-menu and join flow, with fresh normal password/Steam/version/observer authorization. The captured password stays in memory; it is not persisted or added to reconnect process arguments. Internal recovery teardown avoids saving the carrier's temporary travel position.

Connection retry delays grow through 5, 10, 20, 40, 80 and 120 seconds, capped at 120 seconds within a burst. After eight failed attempts, a 15-minute quiet cooldown precedes another burst. Each connection/observer-startup attempt has a 90-second deadline. Sixty seconds of a healthy authorized session resets the burst pressure. Diagnostics distinguish monitoring, backoff, connecting and halted states.

Explicit off/return pauses recovery; a manual resume can re-arm the same captured live session. Native logout/quit cancels recovery, automation and requested output and forgets the password, including when unattended recovery was disabled. Password/version rejection, ban/kick, excluded platform, crossplay privilege failure, already-connected rejection, terminal observer failure or a different world UID halts recovery. Initial login/account selection remains the game's normal flow. An unavailable game API also halts instead of repeatedly invoking a broken adapter.

Unattended support does not establish continuous video through an unloaded menu, OS sleep, a dead graphics/audio device, a stopped game process or a machine restart. In-process recovery is not an external process supervisor. A healthy encoder can survive ordinary camera travel, but world disconnection may end the RTMP session and require the receiver to accept a new publisher connection.

## Runtime acceptance — pending for 0.8.0

Use a disposable dedicated Steam server and the same client/server artifact revision. Start with manual output and recovery disabled, then enable each recovery feature after its normal path passes.

| Case | Required observation |
| --- | --- |
| User's existing solo workflow | Authorization, load, explicit travel and return/park/resume still work |
| One nearby player | Automatic ready-player shot; only valid presented footage advances dwell |
| One distant player | Acquisition travels, waits for the actual character, then follows without manual coordinates |
| Two players far apart | Both receive coverage; the ready nearby player does not starve the distant one |
| Private map position | Allowed observers acquire when server sharing is enabled; disabling it withholds coordinates without hiding presence |
| Loading/respawn/portal/disconnect | No stale actor/coordinate becomes a completed acquisition; bounded recovery selects another shot |
| Missing/stale discovery | Diagnostic states the missing/stale data; no stale coordinate initiates travel |
| No ordinary players | Scenic paths vary and later change to another generated region |
| Finite catalog | Cooldowns/exhaustion are visible; no untouched region is generated solely for the observer |
| Player joins during idle travel | Current native travel remains coherent; player acquisition follows promptly without stacked teleports |
| Solo route exercise | Same acquisition/travel path, bounded retries and cancellation work without another player |
| Manual stop/hold/selection | Overrides win over queued acquisition, output retry and reconnect intent |
| Receiver interruption | Game stays responsive; one encoder at a time; requested output resumes after the receiver returns |
| Explicit output off | No later retry or automatic start reverses the stop |
| Invalid FFmpeg/NVENC/receiver credentials | Clear sanitized blocked reason; no tight retry loop; no credentials in logs |
| Server outage/restart | Armed observer reconnects to the intended server/character and completes fresh authorization |
| Intentional logout/rejection | No surprise rejoin or ordinary-gameplay fallback |
| Background/minimized/device change | Record actual graphics/audio behavior; unavailable capture does not become an indefinitely frozen stream |
| Sustained run | Run at least two hours including target/region changes and interruptions; no growing catalog/queue, orphan encoder, repeated log flood or A/V drift |
| Ordinary-client perspective | Broadcaster stays invisible/noninteractive through travel, recovery, return and reconnection; sleep/count/ownership checks pass |

Retain both BepInEx logs, exact artifact revision, dependency/game versions, commands, state transitions and receiver observations. A successful policy simulation or synthetic RTMP loopback does not replace these runtime results.
