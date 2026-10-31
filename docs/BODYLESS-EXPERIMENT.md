# Bodyless spectator experiment — 0.7.0

**Implemented experiment; in-game validation pending.** This branch tests whether the graphical Valheim client can receive and render existing world scenes without ever spawning its own player. It is separate from `observer-foundation` and does not include that branch's body-following or teleport features.

Target: **Valheim 1.0.21, Steam backend, dedicated server**. Crossplay, local single-player worlds and listen servers are excluded from this experiment. Use a disposable dedicated world or a copy of one. You can perform the scene-loading test with no other players connected. No OBS is used.

## What is implemented

- Startup-only client role: blocks `Game.SpawnPlayer`, respawn requests and character saving. It does not delete an already-spawned player. Restart to return to ordinary gameplay.
- A server companion reserves explicit SteamID64 identities for spectators. Normal Steam authentication still runs; identity comes from the connection's Steam socket, not a client-supplied name or ID.
- The spectator's ordinary server reference stays outside the world. Only synchronous selection of outgoing object data temporarily uses the requested camera position; a Harmony finalizer restores the reference even if selection throws.
- Reserved spectators are removed from the player list sent to clients, denied active-area eligibility and ownership assignment, and denied post-authentication gameplay RPCs. They still consume a network connection/server slot. This is not a universal guarantee about every server counter or third-party mod.
- Client ownership checks report false, ownership claims and routed gameplay RPCs are blocked, and attempts to spawn a player, author a new network prefab or acquire ownership fail the test and request disconnection.
- A/B camera locations, arbitrary bounded `goto`, camera-direction buttons, readiness/failure overlay, and existing FFmpeg RTMP video plus game-audio output. RTMP settings are read from the existing broadcaster configuration.
- Fifteen-second companion timeout and 45-second acquisition timeout. Missing required runtime hooks cause an armed client to quit rather than enter as an ordinary player. A missing companion disconnects the client.

This is a scene-acquisition experiment, not the final automated idle director. It does not generate unexplored regions or activate live AI in dormant areas. Dormant creatures may stay at their last replicated positions. Client rendering necessarily does presentation work; the guards aim to prevent authoritative gameplay changes, not all CPU activity.

## 1. Install and mark two regions in ordinary mode

1. Close the test client and test server. Install **both** `ValheimBroadcaster.dll` and `ValheimBroadcaster.Core.dll` from the same 0.7.0 build into `BepInEx/plugins/ValheimBroadcaster/` on each. Both need BepInEx 5 and Jötunn 2.30.2. Ordinary players need neither this mod nor Jötunn for this experiment.
2. Launch each once to generate `BepInEx/config/com.dillflix.valheimbroadcaster.bodyless.cfg`. Leave both `ClientEnabled` and `ServerEnabled` at `false` during preparation.
3. Join the **test dedicated server** normally. Visit a recognizable built/generated location. In the F5 console, run:

   ```text
   spectator mark a
   ```

4. Travel at least **512 metres** to another recognizable location, let it finish loading, and run:

   ```text
   spectator mark b
   ```

5. Leave the world and close the client. Stop the test server normally so preparation is saved. The marks persist in the client's bodyless config and include the world UID; they will not silently apply to another world. Marks place the camera 12 metres above the character. Choose open, above-ground areas for this first test.

If your console is not enabled, add `-console` to the profile's Valheim launch arguments and use F5. The running experiment also has clickable controls, so you do not need console commands to switch the saved locations or disconnect.

## 2. Enable the two explicit roles

On the **server**, edit the bodyless config while stopped:

```ini
[Experiment]
ClientEnabled = false
ServerEnabled = true
AllowedSteamIds = YOUR_17_DIGIT_STEAMID64
```

Use the broadcaster account's exact SteamID64 (the Steam socket identity recorded by the server), not its display name or this mod's changing session UID. Multiple reserved accounts are comma-separated. An empty list authorizes nobody. A reserved account cannot play normally until removed from this list or the server experiment is disabled and restarted.

On the **broadcaster client**, edit its bodyless config:

```ini
[Experiment]
ClientEnabled = true
ServerEnabled = false
```

Keep the generated `[Points]` values. Restart both, launch the client modded through its Thunderstore test profile, select a disposable character/profile for the normal login UI, and join the same server. The selected character must **never spawn** in this mode. This experiment does not change Steam authentication or solve simultaneous-use entitlements.

After the companion acknowledges, the client requests point A automatically. If A is missing for this world, use `spectator goto x y z` with known camera coordinates; otherwise acquisition will time out. Do not run `broadcast probe start` or `broadcast observer start` for this test.

## 3. Run the solo experiment

| Action | Expected evidence |
| --- | --- |
| Join with client role enabled | `BODYLESS EXPERIMENT armed` in client log; server reports a reserved authenticated Steam peer. No local player appears. |
| Wait at A | Loading slate changes to a world view. Overlay reports `Viewing`, `player=none`, `owned=0`, nonzero object count and received data batches. |
| Inspect A | Use Left/Right/Up/Down buttons. Compare visible terrain/buildings with the location you marked. Camera update counts alone do not prove correct imagery. |
| Press F7 / Point B | Loading slate, then the second region. `readyViews` increases after at least two seconds of continuous readiness. |
| Press F6 / Point A | Return to A without spawning or teleporting a body. Repeat A/B at least ten times. |
| Press F11 / Stream | Start existing H.264/AAC output using `com.dillflix.valheimbroadcaster.cfg` streaming settings. Check the receiver for actual picture and audible, spatially appropriate game sound. |
| Change region while streaming | Same encoder connection stays open; the loading slate is captured during acquisition. No stream restart is needed. |
| Press F8 / Disconnect | Leave the world. This deliberately does not restore a normal player in the current session. The ordinary game menu should return. |
| Rejoin | New handshake and reset per-session counters; no stale camera request from the previous world. |

Readiness requires server approval, a received object-data batch since the request, an instantiated destination object explicitly identified by the server, loaded network objects, the game's area-ready signal and a two-second stable interval. **This is still not proof of complete or current scene delivery.** The companion re-sends one existing destination object on every request, including revisits to unchanged scenery, by resetting only that peer's delivery record for that object. It does not change world-object revisions. A timeout is a failed acquisition result, not permission to weaken the write guards. Verify terrain edits, buildings, animation, effects and audio visually/audibly.

To return to normal gameplay, close the client, set `ClientEnabled=false`, remove that SteamID from the server reservation list (or set `ServerEnabled=false`), restart, and reconnect.

## Diagnostics and failure cases

`spectator status` and the client log report phase, request number, ready-view count, local-player presence, instantiated object count, actual owned-object count, received data batches, camera callback count, and blocked RPC/ownership/creation/spawn attempts. `blockedRpc` or `ownershipAttempts` may be nonzero because the experiment rejects vanilla attempts; these are not successful writes. `spawnAttempts` or `creationAttempts` fail the experiment. Preserve **both** client and server `BepInEx/LogOutput.log` files after a run.

The server logs view approvals and rejected gameplay RPC hashes. It never accepts a view request from an unreserved peer. Requested positions must be finite, bounded and within an already-generated destination zone containing a network object to use as acquisition evidence. This does not guarantee every surrounding zone is present. The spectator does not request generation around its off-world gameplay reference.

| Failure test | Expected result |
| --- | --- |
| Companion disabled or account not reserved | No character spawn; handshake fails after 15 seconds once vanilla connection is ready; disconnect. |
| Wrong protocol | No viewing permission; failure/timeout, never ordinary gameplay fallback. |
| Ungenerated destination | Explicit rejection and disconnect; no region generation requested by the spectator. |
| Invalid/out-of-bounds coordinate | Command rejects it before transmission; server independently rejects malformed requests. |
| Request B before A acknowledgement arrives | A's stale acknowledgement cannot authorize B. |
| Missing terrain/network objects | Slate remains until ready or the 45-second acquisition timeout disconnects. |
| Unexpected ownership, local player or newly authored network prefab | Failure recorded and disconnect requested; no ordinary player fallback. |
| Server disconnect/restart | No stale view on the next connection; manual rejoin starts a fresh handshake. |
| Local/hosted world selected accidentally | Experiment rejects it and leaves without character saving. |

After solo scene tests pass, use a second **unmodified client** to check player-list absence, absence of any observer avatar/effects, sleep/difficulty/raid behavior, and unchanged ownership of active creatures. Those multiplayer results cannot be established by the solo test. This branch does not claim zero impact on server load, bandwidth, world time, arbitrary mods or every gameplay rule.

## Validation performed before delivery

- Local C# compilation against real pinned Valheim 1.0.21, Unity, BepInEx 5.4.23.2 and Jötunn 2.30.2 references, warnings treated as errors.
- 38 core test cases, including bodyless session lifecycle, two-region transitions, acknowledgement ordering, timeouts, coordinate bounds and post-handshake RPC policy.
- 54 metadata/IL contract checks against the pinned **dedicated-server** assembly: private hook signatures, referenced fields, readiness identity, object-selection reference, RPC hash dispatch and raw ownership audit.
- GitHub CI performs normal project builds, those checks and the existing real RTMP loopback tests. Consult the linked run for its result.
- **Not run here:** graphical Valheim startup, client-assembly runtime patching, a real server connection, scene fidelity, game audio, unmodified-peer observations, or a soak test. Server-assembly compilation and IL inspection do not establish client behavior.

Optional offline hook check against your actual client assembly:

```powershell
dotnet run --project tests/ValheimBroadcaster.Contract.Tests -c Release "-p:BepInExCoreDir=C:\path\to\test-profile\BepInEx\core" -- "C:\path\to\Valheim\valheim_Data\Managed\assembly_valheim.dll"
```

The checker reads metadata only and does not launch the game or alter assemblies. No game/dependency DLLs are included in the downloadable test build.
