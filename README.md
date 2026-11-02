# Valheim Broadcaster

A Jötunn/BepInEx mod for a dedicated broadcasting client: follow another player's character with a wide camera view, cycle targets automatically, and stream the rendered game view with game audio directly to an RTMP receiver.

**0.8.26 repairs scenic terrain placement and foliage rejection.** Built directly on 0.8.25 (`fa96579`), it measures loaded terrain and water beneath the actual camera/near plane, fits the complete route before admission, and rejects unsafe live poses. Rock/structure targets use exposed bounds above those surfaces. Nearby foliage can use triangle-group or submesh bounds instead of treating an entire tree's bounding box as solid leaves. The supplied 0.8.25 recordings are failed runtime results; the new build still requires in-game validation. Install the three client DLLs and included notices together, preserving configuration. No server update is required; observer protocol remains 3. See [camera behavior](docs/CAMERA-DIRECTOR.md) and [validation evidence](docs/RUNTIME-TESTS.md).

**0.8.23 repairs scenic continuity and motion.** Built directly on 0.8.22 (`7bda56a`), it separates blocked camera motion from readable footage and loaded-scene availability, prevents obsolete recovery searches from replacing recovered shots, and removes actor-style timed viewpoint/lens changes from scenic paths. Scenic movement uses short smooth ramps and steady cruising, with no catch-up after pauses. Acquired scenic plans can continue on verified geometry while unrelated objects load. Replace the three client DLLs and included notices together, preserving configuration. No server update is required; observer protocol remains 3. See [camera behavior](docs/CAMERA-DIRECTOR.md) and [validation evidence](docs/RUNTIME-TESTS.md); in-game visual confirmation remains pending.

**0.8.22 adds encounter-aware direction and deliberate pass-by shots.** Built directly on 0.8.21 (`9ae20da`), it separates active combat, inferred hunting targets and nearby powerful threats, preserves relevant participants, and chooses encounter-oriented compositions without sacrificing primary-player readability. Eligible travel shots can hold a world position and pan as players approach, pass and recede. See [camera behavior](docs/CAMERA-DIRECTOR.md) and [validation evidence](docs/RUNTIME-TESTS.md). Replace the three client DLLs and included notices together, preserving configuration. No server update is required; observer protocol remains 3. In-game visual and replicated-encounter validation remain pending.

**0.8.21 adds observational Position Composer shots, safe group Dolly Then Zoom, and coordinated FreeLook profiles.** Built directly on 0.8.20 (`94e563f`), it gives eligible actor shots native screen/depth freedom with a fixed viewing direction, lets groups use validated open-space camera movement before zooming, and assigns elevation-dependent lens/damping/composition to the native modifier. Existing visibility, continuity and observer constraints remain authoritative. All three options default on; compare them with `broadcast camera observation|groupdolly|freelook on|off`. Replace the three client DLLs and included notices together, preserving configuration. No server update is required; observer protocol remains 3. See [camera behavior](docs/CAMERA-DIRECTOR.md) and [validation evidence](docs/RUNTIME-TESTS.md). Visual behavior and performance still require Valheim runtime confirmation.

**0.8.20 integrates native Cinemachine Confiner 3D, Spline Dolly, ClearShot and blend curves.** It builds directly on 0.8.19 (`6fbfb9d`). Interior confinement uses verified local camera volumes isolated from game physics; scenic paths use native spline traversal; ClearShot ranks evaluated viewpoints while usable-footage pacing and final validation remain authoritative. Automatic readback-orientation calibration addresses the vertically mirrored live/slate output reported after 0.8.19. Replace all three client DLLs and the included notices together, preserving configuration. These changes require no server update and retain observer protocol 3. See [camera behavior and native-component audit](docs/CAMERA-DIRECTOR.md) and [validation evidence](docs/RUNTIME-TESTS.md). In-game visual and graphics-backend validation remain pending.

**0.8.19 adds predictive production direction and a clean broadcast feed.** Built directly from 0.8.18 (`4ecea7d`), it ranks currently readable viewpoints by their expected useful lifetime, develops short evidence-based shot sequences, and adds deliberate wide/full/qualified medium compositions with persistent screen geography. Streaming now reads the camera compositor before operator overlays and console UI; unavailable scenes use a clean recovery card. Low-frequency analysis of the actual live picture supplements geometry with conservative exposure/contrast evidence. All five changes are client-side; observer admission remains protocol 3 and the existing server can remain. The 0.8.18 server is optional for remote events; the 0.8.17-or-later activity feed remains required for global-idle wildlife cutaways. Replace the three client DLLs and included notices together, preserving configuration. New controls are `broadcast camera prediction|sequences|scales|picture on|off`; see [camera behavior and Cinemachine capability audit](docs/CAMERA-DIRECTOR.md) and [validation evidence](docs/RUNTIME-TESTS.md). Actual Valheim visuals, graphics-backend orientation and long-run cost still require runtime validation.

**0.8.18 prioritizes readable footage and adds scene-aware direction.** Built directly from 0.8.17 (`94fb4a1`), it adds shared picture-health checks, expanded compact recovery viewpoints, measured rooms/roofs/openings, deliberate shot roles and scale, locomotion-sensitive movement and contextual transitions. Validated handoffs within a loaded cooperative group retain the scene; activity-aware scheduling balances observed encounters with fair coverage. These camera/local-direction improvements work with existing compatible protocol-3 servers. The optional 0.8.18 server update adds a separate `Events1` feed for activity outside the loaded area; ordinary players need no mod. The 0.8.17 server requirement for global idle/wildlife eligibility remains unchanged. Replace all three client DLLs and included notices together, preserving configuration; update the server/core pair only for the new remote feed. See [CAMERA-DIRECTOR.md](docs/CAMERA-DIRECTOR.md) and [validation evidence](docs/RUNTIME-TESTS.md). In-game visual and multiplayer checks remain pending; no live installation or restart is performed.

**0.8.17 adds nearby wildlife cutaways when every connected ordinary player has been observed idle for at least 60 seconds.** It builds directly on 0.8.16. Fresh complete server activity evidence is required; movement or observed action returns coverage to players. Cutaways use already-loaded creatures and the existing creature camera/dwell, with at least 60 seconds before another cutaway. This is observed inactivity, not proof that a player is away from their keyboard. Install the 0.8.17 server/core pair for this feature, plus the client's three DLLs and included notices, preserving configuration. Older protocol-3 servers retain player coverage and report why cutaways are unavailable. See [CAMERA-DIRECTOR.md](docs/CAMERA-DIRECTOR.md#observed-idle-wildlife-cutaways) for limits and pending runtime checks.

**0.8.16 brings the shared Cinemachine pipeline to scenic and creature coverage.** It builds directly on 0.8.15. Scenery now uses ranked environmental paths with independent camera/aim movement and five non-looping shot styles; creatures use their actual body dimensions and observed movement. Composition, damping, movement controls and validated handoffs now apply across all three production modes. Idle pacing and history require usable live footage, not held images or a successful camera calculation alone. See [CAMERA-DIRECTOR.md](docs/CAMERA-DIRECTOR.md) for mode behavior and pending runtime tests. Replace all three client DLLs and included notices together, preserving configuration. No server update is required.

**0.8.15 separates camera trajectories from player translation.** It builds directly on 0.8.14 and retains its deliberate composition. Each procedural candidate now develops a persistent position in the world while aiming at the current subject, with activity-dependent tracking and bounded corrections. This addresses the target-relative follow point, velocity prediction and blend displacement that made the camera feel attached during movement. See [CAMERA-DIRECTOR.md](docs/CAMERA-DIRECTOR.md) for behavior, diagnostics and pending visual tests. Replace all three client DLLs and included notices together, preserving configuration. The existing protocol-3 server can remain installed.

**0.8.14 adds deliberate screen composition to the damped player director.** It builds directly on 0.8.13: the camera now chooses persistent subject placement, movement lead room and activity space, with coordinated group framing and compact fallbacks. `broadcast camera composition on|off` compares the new placement policy while retaining the same damping and visibility checks. See [CAMERA-DIRECTOR.md](docs/CAMERA-DIRECTOR.md) for behavior and pending visual validation. Replace the three client DLLs and included notices together, preserving configuration. Observer protocol and server behavior are unchanged; this update requires no new server deployment.

**0.8.13 adds damped, activity-aware player direction and cooperative framing.** Players can move within the frame; readable shots develop through bounded movements and fresh compositions, with quieter behavior in confined spaces or after obstruction corrections. Persistent nearby companions and relevant opponents can share a shot, and sustained visible coverage informs automatic player rotation. See [CAMERA-DIRECTOR.md](docs/CAMERA-DIRECTOR.md) for controls, evidence limits and pending runtime tests. Built on 0.8.12, retaining protocol 3; the existing server remains compatible. A companion update optionally adds fresh observation/health-loss hints. Replace the three client DLLs and included notices together, preserving configuration.

**0.8.12 adds Cinemachine player cameras with foliage-aware shot selection.** Candidate views adapt angle, distance and height, retain stable compositions and respect the existing loaded-geometry/carrier limits. Player-shot foliage is a quality penalty rather than a loading failure. Enabled by default through `Camera.ProceduralPlayers`; use `broadcast camera fixed` / `procedural` for comparison and `broadcast camera status` for diagnostics. This builds directly on 0.8.11 and remains protocol 3. **Install all three client DLLs and the included ThirdParty notices folder.** The existing server and configuration can remain; visual runtime verification is pending.

**0.8.11 replaces GUI screenshot holds with a camera compositor.** Live and held scenes use the same final camera-texture path; travel/recovery invalidate the prior image. `broadcast framehold test` schedules a half-second hold so orientation can be checked without waiting for travel. Includes 0.8.10 continuous following. Replace both client DLLs; keep the deployed protocol-3 server and configuration. In-game visual verification is pending.

**0.8.10 separates player acquisition from continuous following.** An established shot can continue while nearby network objects instantiate, provided the same living character and usable local geometry remain present. Initial acquisition, respawn, significant relocation and travel still require full readiness. Includes 0.8.8 scenery and 0.8.9 held-frame changes. Replace both client DLLs; the deployed protocol-3 server and existing configuration can remain. Runtime retest is pending.

**0.8.9 changed the held-frame display path implicated in brief upside-down flashes (superseded by 0.8.11).** Saved frames now use the same GPU normalization path as streaming, capture restores graphics state, and only an actually presented live frame can refresh the hold. The reported inversion still requires an in-game retest. Includes the 0.8.8 scenery improvements and remains compatible with the deployed protocol-3 server; replace both client DLLs together.

**0.8.8 improves scenic composition.** It tries alternate angles and heights, screens the camera path against solid geometry and nearby foliage, and replaces persistently obstructed shots. This continues from the corrected protocol-3 0.8.7 build; the deployed protocol-3 0.8.4 server can remain installed. Replace both client DLLs together. Visual results in Valheim remain pending; see [RUNTIME-TESTS.md](docs/RUNTIME-TESTS.md).

**0.8.4 makes observer mode optional per connection.** The server's `Observer.SteamIds` list grants permission to request the role; it does not force those accounts into it. Vanilla clients and modded clients with `Multiplayer.ObserverConnection=false` join as normal players, including allowlisted accounts. Set that client option to `true` **before joining** to request read-only observation. Use matching 0.8.4 client/server builds (observer protocol 3); disconnect before changing roles. Multiplayer runtime verification of this change is still pending.

**0.8.0 adds autonomous direction and opt-in recovery to the read-only observer.** The user reported successful 0.7.0 authorization, loading, manual travel and return/resume while alone on a dedicated server. New automatic acquisition, scenic travel, reconnect/output recovery, other-client invisibility and simulation isolation still require the runtime checks in [RUNTIME-TESTS.md](docs/RUNTIME-TESTS.md). Ordinary players need no mod. The server companion is required for the read-only multiplayer role and its discovery feed.

In normal F8/F10 modes the broadcaster remains an ordinary connected player. Those modes do not provide network invisibility, invulnerability, teleportation, input suppression, or protection from gameplay effects. The ordinary mode follows already-loaded player objects. Choose the separately configured read-only observer role for autonomous multiplayer broadcasting.

## Autonomous broadcasting (0.8.0)

Follow [AUTONOMOUS-BROADCAST.md](docs/AUTONOMOUS-BROADCAST.md) for the configuration, controls, solo route exercise and runtime matrix. Use protocol-3 client/server builds; the current client supports the deployed protocol-3 0.8.4 server. Protocol 3 adds explicit intent before admission and rejects older observer handshakes. The superseded movement-fix 0.8.4/0.8.5/0.8.6 builds used protocol 2; do not use those artifacts.

- The companion supplies bounded, authenticated snapshots of connected players and generated scenic destinations. Server policy can permit observer acquisition of players whose map position is not public.
- The director rotates across connected players, using controlled native travel for distant targets and waiting for actual loaded characters. Only usable player footage advances viewing time. Stale/unreachable targets receive bounded retries and cooldowns.
- Idle coverage changes local scenic paths and can travel to another previously generated land region, with revisit/failure history and explicit exhaustion diagnostics. It does not generate untouched regions or simulate dormant creatures.
- Diagnostics distinguish observer lifecycle, target acquisition, discovery freshness, travel, presentation and output/reconnect recovery.
- Direct RTMP supports software H.264 or NVIDIA NVENC plus game audio. Optional automatic output and retries retain intent through recoverable failures while respecting manual stops.
- Opt-in connection recovery handles supported unexpected losses after an initial manual observer join. Explicit stop/logout cancels it. In-process recovery does not restart a crashed game or sleeping machine.

Automation defaults on **within an authorized read-only session**. The observer role, automatic streaming and unattended connection recovery remain explicit opt-ins. No live client/server installation or public release is performed by these source changes.

## Flicker handling and multiplayer observer (0.7.0)

Brief readiness gaps now hold the last captured valid picture for up to 0.75 seconds instead of immediately flashing the recovery slate. Longer gaps still show the slate; travel and explicit faults bypass the hold. `broadcast diagnostics` reports held/slate frames and the last readiness failure. This uses a GPU framebuffer copy, without a second camera render or CPU readback. The reported flicker still needs a user retest.

The optional server companion adds a **read-only observer connection**: the broadcaster's character stays local, world writes/gameplay RPCs are rejected, and the observer is excluded from gameplay player lists and simulation ownership. Other players remain unmodded. Support is dedicated Steam servers. Basic solo connection/travel is user-reported working; other-client visibility and simulation behavior remain pending.

Follow [MULTIPLAYER-OBSERVER.md](docs/MULTIPLAYER-OBSERVER.md) for the separate server/client artifacts, Steam-ID allowlist, pre-join client setting, permanent connection-role semantics and test matrix. Dormant creatures do not become active merely because the observer loads their region; untouched regions are not generated for the observer. Return/off parks the multiplayer observer until disconnect or camera resume.

## Offline observer experiment (0.6.0 onward)

The offline observer path adds an explicitly activated **closed local world** test. `broadcast observer start` hides the local character using the existing renderer mask, suppresses its ordinary input/character update loops, enables local god/ghost flags, disables Rigidbody gravity/collision, and keeps its body two metres below the broadcast camera. Character ZDO position and network interest move together. This is a foundation for a camera-relative observer, not multiplayer spectator isolation.

Mark a destination before activation with `broadcast probe mark`. Use `broadcast observer travel` to move the body through the game's native distant teleport routine to that marker. `broadcast observer return`, F8, or `broadcast off` requests a return to the activation position; normal controls/physics and the camera are restored **after** the return completes. Travel is bounded to 45 unpaused seconds; failures suspend movement and retain protections while the return command remains available. An RTMP session can remain connected during travel/return slates and stops when return finishes.

Offline activation rejects dedicated-server connections and hosted worlds, even when no other players are online. Opening the local world to multiplayer is blocked while it is active. Use the separately configured read-only connection for supported multiplayer observation. Loading a region can still activate its simulation. Read the [solo observer test procedure](docs/OBSERVER-TESTS.md) before trying it; **in-game results remain pending**.

## Implemented behavior

- Configurable camera distance, height, FOV, preferred azimuth, independent procedural position/aim damping and a screen-space dead zone.
- Shared Cinemachine damping, composition and validated transitions for players, creatures and scenery, with explicit shot envelopes, environmental path development, future-readability comparison and mode-specific foliage scoring.
- Evidence-based activity sequences and persistent action geography; low-frequency rendered-picture feedback complements geometric readability.
- Clean program output separated from operator console/diagnostic overlays, with a dedicated recovery card and no second world render.
- Bounded deliberate movement, usable-footage pacing and persistent cooperative player framing; scenic place/style history and creature repeat/failure cooldowns support idle variety.
- Stable-ID target selection, next/previous, timed rotation, hold, and disconnect fallback.
- Continuous coverage: player acquisition takes priority; idle uses changing scenic paths and eligible nearby creatures, with autonomous generated-region travel in the configured read-only mode. A recovery slate appears when no ready shot can be rendered or controlled travel is in progress.
- Separate connected-player presence and scene readiness, with bounded experimental remote acquisition attempts.
- HUD hiding and restoration; matching main/sky camera FOV; restoration of camera state when broadcasting stops or the observer dies.
- The older **experimental** reference-only distant-target mode remains separate from protected native observer travel. It starts disabled; use the companion's discovery/travel path for autonomous read-only coverage.

The mod captures the clean post-effects camera texture and game audio listener, then manages a local FFmpeg process to publish H.264 video plus stereo AAC audio. No OBS, second rendered camera or microphone capture is used. RTMP alone does not require the server companion. FFmpeg is a separate executable you supply. An embedded HLS/DASH web server is not implemented; a remote media server can convert the RTMP feed for browser playback.

## Build

Install the .NET 8 SDK, a matching Valheim installation, BepInEx 5, and Jötunn. The project targets .NET Framework 4.8 and pins its compile-time Jötunn package to **2.30.2**. Match runtime dependencies to the build; the installed game version is part of the test record, not inferred from this package number.

Run the director tests without game files:

```sh
dotnet run --project tests/ValheimBroadcaster.Core.Tests -c Release
```

Before a client build, prepare the pinned Cinemachine 3.1.7 runtime sources (the core/server builds do not need this):

```sh
python scripts/fetch-cinemachine.py
```

This fetches verified upstream sources into an ignored build directory. The selected runtime builds as `ValheimBroadcaster.Cinemachine.dll`; it uses the matching game's existing Unity assemblies and does not ship them. See [dependency build notes](third_party/Cinemachine/README.md).

Build the plugin on Windows PowerShell:

```powershell
dotnet build src/ValheimBroadcaster.Plugin -c Release "-p:ValheimDir=C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

Or on Linux:

```sh
dotnet build src/ValheimBroadcaster.Plugin -c Release -p:ValheimDir="/path/to/Valheim"
```

For a mod-manager profile or separate assembly directory, specify both paths explicitly:

```sh
dotnet build src/ValheimBroadcaster.Plugin -c Release \
  -p:ValheimManagedDir="/path/to/valheim_Data/Managed" \
  -p:BepInExCoreDir="/path/to/profile/BepInEx/core"
```

The build requires real game and loader assemblies. It reads those files, excludes Jötunn's build hooks, and has no automatic installation step. Do not commit or distribute the referenced game/Unity/BepInEx/Jötunn DLLs.

GitHub Actions runs the core tests, real FFmpeg loopback checks on Linux/Windows, game-metadata audits and client/server builds against a pinned Valheim 1.0.21 baseline. The build job obtains real references from the free official dedicated-server depot using a checksummed downloader; it never launches the game. Successful runs provide separate `valheim-broadcaster-<commit>` and `valheim-broadcaster-server-<commit>` artifacts. The client contains its plugin, Core, isolated Cinemachine runtime and notices; the server contains its plugin and Core only. These are test builds, not runtime-validated releases. Download them from the [Actions page](https://github.com/Dillflix/valheim-streamer/actions), or build locally against your own matching assemblies.

Expected output directory: `src/ValheimBroadcaster.Plugin/bin/Release/net48/`.

## Install and try locally

1. Use a separate test profile with BepInEx 5 and Jötunn 2.30.2 available. Close that client before changing plugins.
2. From the client artifact, copy the contents of `ValheimBroadcaster/` into `BepInEx/plugins/ValheimBroadcaster/` in that profile: `ValheimBroadcaster.dll`, `ValheimBroadcaster.Core.dll`, `ValheimBroadcaster.Cinemachine.dll` and the included `ThirdParty/` folder. Replace all three DLLs together; preserve your configuration. The server does not need Cinemachine.
3. Start Valheim, join the test world, and park the observer character safely near another player. Broadcasting starts disabled.
4. Press **F8** to start. Use **PageDown** / **PageUp** to cycle and **F9** to hold or resume. Press **F8** again to restore the normal camera.
5. Check `BepInEx/LogOutput.log` for the plugin startup line, state changes, or errors. Validate restoration before capturing a session.

Hotkeys are ignored while typing in chat/console or using the recognized menu, inventory, and text-input screens. The observer's normal gameplay controls remain active in ordinary F8/F10 modes. Explicit offline observer mode suppresses them; console and broadcast controls remain available.

To remove the prototype, close the client and remove its three DLLs and owned ThirdParty notices. Keep any existing configuration and test records. Do not remove dependencies used by other mods.

## Solo camera test

No second player or server mod is needed. This works in a local single-player world or while you are alone on your server.

1. Install the complete client artifact (three DLLs and ThirdParty notices) into your active mod profile. With Thunderstore, open that profile folder and copy it into `BepInEx/plugins/ValheimBroadcaster/`, then launch modded as usual. No console launch argument is required.
2. Enter a test world with a living character and press **F10**. Alternatively, with the console enabled, run `broadcast self` and close the console.
3. Walk, run, jump, and move near walls or trees. The same broadcast camera used for other players now follows you, exercising framing, smoothing, obstruction checks, main/sky FOV, HUD hiding, and direct stream capture.
4. Press **F8** or run `broadcast off` to stop and restore the normal view. Run `broadcast on` to switch directly back to ordinary player targeting; alone, that mode starts varied scenic/creature coverage.

Self-follow is session-only and always shows a `SELF-FOLLOW TEST` status label, even when `ShowStatus=false`. Its roster contains only you, so next/previous and timed cycling stay on you. Other players joining will not become test targets. Distant-streaming overrides are bypassed even if `RemoteTargets=true`; entering self-follow restores any previous override. Death, world exit, and plugin disable stop the test. Starting again with F8 uses normal mode; press F10 or run `broadcast self` to re-enter the test.

Your normal movement controls remain active. This mode tests actual camera rendering with your own character; it does not validate remote-player replication, multi-target shots, or distant world loading.

For the new director, compare `broadcast camera dynamic` with `broadcast camera steady`; use `broadcast camera motion 0` to isolate tracking/damping from decorative movement. Restore `dynamic` and `motion 1` afterward. See the [camera runtime matrix](docs/CAMERA-DIRECTOR.md#runtime-validation) for open terrain, sustained movement, forests, interiors and cooperative sessions.

## Controls and console

Use Valheim's `-console` launch option to make the F5 console available. Commands are also useful when hotkeys conflict with another mod.

| Command | Effect |
| --- | --- |
| `broadcast self` | Start solo testing with your own character as the only target |
| `broadcast on` / `broadcast off` | Start normal other-player mode / stop either mode |
| `broadcast next` / `broadcast previous` | Select the next / previous eligible target |
| `broadcast hold` / `broadcast resume` | Pause / resume automatic cycling and load-timeout skipping |
| `broadcast list` | Refresh and print eligible target IDs and names |
| `broadcast follow <id>` | Select an exact listed ID and hold it; start with `broadcast on` first |
| `broadcast status` | Show director state, target, hold/auto mode, and roster count |
| `broadcast stream on` / `off` / `status` / `diagnostics` | Request/cancel RTMP audio + video and inspect encoder/retry failures; publishing requires an eligible camera |
| `broadcast wildlife on` / `off` / `status` | Enable/disable observed-idle wildlife cutaways or inspect activity freshness, minimum idle time, rearm time and blocking reason |
| `broadcast survey` | Reset the loaded-region search origin; it is not a camera focus (`anchor` remains a compatibility alias) |
| `broadcast probe mark/start/stop/status` | Mark, run, stop or inspect a reference-only solo distant-scene test |
| `broadcast observer start/mark/travel/return/status` | Activate/resume, mark, travel, return/park, or inspect diagnostics; `travel x y z` accepts explicit coordinates |
| `broadcast camera prediction on` / `off` (also `sequences`, `scales`, `picture`) | Toggle future-view ranking, short activity sequences, deliberate shot scales or actual-picture feedback |
| `broadcast camera procedural` / `fixed` / `status` | Select procedural actor/dedicated scenic cameras or legacy fixed framing; inspect mode/quality diagnostics. The existing `Camera.ProceduralPlayers` key is retained for compatibility |
| `broadcast camera dynamic` / `steady` | Enable/disable deliberate movement; scenic steady holds the developed pose while shot scheduling continues, actor tracking remains active |
| `broadcast camera composition on` / `off` | Enable deliberate screen placement or use the centered comparison preference; retain damping, groups and visibility checks |
| `broadcast camera groups on` / `off` | Enable/disable player cooperative and opponent group framing; creature coverage currently uses one primary actor |
| `broadcast camera motion <0-1.5>` | Set actor movement strength or directed scenic traversal speed; zero pauses scenic movement while shot scheduling continues |
| `broadcast camera damping <position> <aim>` | Set independent damping in seconds, each 0.1–3 |
| `broadcast framehold test` | After two seconds, hold the last valid camera scene for half a second; close the console to compare live/held orientation |
| `broadcast diagnostics` | Inspect discovery age/counts, target/travel reasons, frame holds/slates, and output/reconnect state |
| `broadcast auto on/off/status` | Resume/pause/inspect automatic player acquisition and scenic travel in a read-only session |
| `broadcast route add [x y z]/start/stop/clear/status` | Run a world-session solo acquisition route through the same travel path |
| `broadcast unattended on/off/status` | Arm/inspect recovery; `off` cancels it, pauses automation and stops requested output |
| `broadcast help` | Print command usage |

Names are labels. Ordinary loaded targets use character ZDO IDs; the discovery-backed director uses connection session IDs and separately tracks character IDs across respawns. IDs are session-specific and should not be saved as permanent player identities.

## Configuration

After the first launch, close the game and edit `BepInEx/config/com.dillflix.valheimbroadcaster.cfg` in the active profile. Restart to apply the edited file.

Camera commands also update their settings while running. See [CAMERA-DIRECTOR.md](docs/CAMERA-DIRECTOR.md) for how damping, dead zones, movement and constrained-space behavior interact.

| Section / key | Default | Meaning |
| --- | --- | --- |
| `Camera / ProceduralPlayers` | `true` | Procedural actor and dedicated scenic coverage; false selects legacy fixed framing. The serialized key name is retained for compatibility |
| `Camera / DynamicShots` | `true` | Activity/space-aware shot duration and deliberate movement |
| `Camera / GroupFraming` | `true` | Persistent nearby player companions and relevant opponents when a readable composition is available; not creature-herd inference |
| `Camera / PositionDamping`, `AimDamping` | `0.8`, `1` | Cinemachine damping in seconds; larger values soften response, range 0.1–3. Position damping smooths the planned trajectory, not a raw player-follow point |
| `Camera / DeadZone` | `0.12` | Screen fraction allowing movement before recentering, range 0–0.3 |
| `Camera / ShotMotion` | `1` | Actor movement strength or scenic traversal-speed multiplier, range 0–1.5; accepted scenic routes are not reshaped |
| `Camera / Distance` | `18` | Horizontal metres from target |
| `Camera / Height` | `10` | Metres above the focus point |
| `Camera / FieldOfView` | `75` | Vertical degrees |
| `Camera / Azimuth` | `225` | Preferred world-space direction; procedural shots can reframe around obstructions |
| `Camera / Smoothing` | `5` | Legacy fixed camera response per second; actor cameras use position/aim damping and directed scenery uses its authored route |
| `Director / DwellSeconds` | `30` | Ready player footage before rotating targets; separate from composition duration |
| `Director / TargetTimeoutSeconds` | `20` | Maximum experimental remote acquisition attempt |
| `Director / AcquisitionCooldownSeconds` | `30` | Local-interest interval between remote attempts |
| `Idle / WildlifeCutaways` | `true` | Allow nearby wildlife cutaways after all connected ordinary players are observed idle for 60 seconds; requires the 0.8.17 server activity service |
| `Idle / ScenicSeconds` | `45` | Usable scenic footage between opportunities to try a loaded creature; directed scenic shot duration is independent |
| `Idle / CreatureSeconds` | `25` | Usable creature footage before returning to scenery |
| `Idle / CreatureRadius` | `50` | Loaded-creature search radius around the survey origin (metres) |
| `Display / HideHud` | `true` | Hide the ordinary HUD during broadcast |
| `Display / ShowStatus` | `true` | Show the director overlay |
| `Multiplayer / ObserverConnection` | `false` | Select the read-only connection before joining; requires matching companion authorization |
| `Automation / Enabled`, `ScenicTravel` | `true`, `true` | Acquire players and traverse generated scenic regions in a read-only session |
| `Automation / RegionSeconds`, `RegionRevisitSeconds` | `120`, `1800` | Usable region coverage and revisit cooldown |
| `Automation / AcquireSeconds`, `RetrySeconds` | `65`, `30` | Acquisition budget and initial failure cooldown |
| `Automation / MinimumRegionDistance`, `MaximumRegionDistance` | `256`, `10000` | Scenic destination distance bounds in metres |
| `Unattended / Enabled` | `false` | Opt-in same-session connection recovery and background running |
| `Streaming / AutoStart`, `AutoRetry` | `false`, `true` | Automatic output intent and recoverable-failure retries |
| `Streaming / VideoEncoder`, `NvencGpu` | `libx264`, `-1` | Software/NVIDIA encoder selection and optional NVIDIA device index |
| `Experimental / RemoteTargets` | `false` | Include public distant positions and change the network reference position |
| `Controls / StreamToggle` | `F11` | Start/stop RTMP audio + video |
| `Controls / SelfTest` | `F10` | Start self-follow test mode |
| `Controls / Toggle`, `Next`, `Previous`, `Hold` | `F8`, `PageDown`, `PageUp`, `F9` | BepInEx keyboard shortcuts |

In ordinary mode, only ready players enter rotation. The read-only autonomous director instead schedules the connected discovery roster and acquires distant scenes. Hold pauses cycling/scenic travel, while readiness and subject loss still require checks. Connected but unavailable players remain visible in diagnostics.

For `RemoteTargets=true`, test first with two players sharing their map positions in widely separated areas. The client waits for both a target scene object and local area readiness. These signals do **not** prove complete server delivery or harmless observer behavior. Follow the remote test matrix before relying on this mode.

## Direct RTMP video and audio

See [STREAMING.md](docs/STREAMING.md) for Thunderstore setup, receiver testing, configuration, and troubleshooting. Install an actual FFmpeg executable with the selected `libx264` or `h264_nvenc` encoder and `aac`, set `Streaming.FFmpegPath` and `Streaming.RtmpUrl`, launch modded, press **F10** for an ordinary solo camera test, then **F11** to request output. In a read-only session the camera starts after authorization; F11 requests its output. Automatic output is available with `AutoStart=true`, but explicit stream off wins over automatic retries. F8 stops output and requests protected observer return/park where applicable.

A VM needs accelerated 3D rendering and a working audio device, even if no physical speakers are attached. NVIDIA encoding requires a usable NVIDIA device/driver and matching FFmpeg support on the broadcaster. Capture/readback, conversion and AAC retain CPU/GPU costs. Streaming uses an authenticated game client; this project does not change platform authentication or game licensing.

See [architecture](docs/ARCHITECTURE.md), [runtime checks and evidence](docs/RUNTIME-TESTS.md), and [implementation sources](docs/SOURCES.md).

## Continuous coverage and solo idle test

Press **F8** while alone to test normal idle coverage (F10 remains the self-follow test). The loaded region is a search boundary, never a fixed camera focus. The scenic director infers ridge, coastline, clearing, rock, structure and open-landscape subjects from loaded terrain, active water and static-object bounds. These are geometric hints, not semantic landmark recognition. It develops establishing, traversing, revealing and eligible detail shots with a fixed lens per shot and prepares the next route before editing. Typical shot durations are 8–20 seconds at default movement speed; longer routes retain a speed bound. A new shot cuts directly to its validated viewpoint rather than making a short, fast blend.

`Idle.ScenicSeconds` remains 45 seconds by default, but now measures usable scenic coverage between opportunities to try loaded wildlife; it no longer stretches every scenic camera route to 45 seconds. Creature dwell remains 25 seconds. Held images and slates earn no footage. A successfully shown creature has a five-minute repeat cooldown; a creature view unusable for three seconds yields to scenery with a separate 30-second failed-view cooldown. Read-only autonomous sessions retain generated-region travel; ordinary/offline modes stay local. No suitable subject or fully readable route produces an explicit diagnostic and bounded retry rather than an invented scenic view.

Use `broadcast camera dynamic` and `broadcast camera motion 1` for normal scenic movement. `steady` or `motion 0` retains the developed pose while normal shot scheduling can still advance. `broadcast hold` also pauses scenic timing and edits; `resume` continues without catching up to held time. Directed scenery uses a prepared native Dolly route by default; `broadcast camera dolly off` selects an authored-curve comparison for newly planned shots. Composition changes likewise apply to future plans. Actor damping, group, sequence, scale and FreeLook switches do not rewrite the active scenic route or lens. `broadcast camera fixed` remains the legacy framing comparison. See the [dedicated scenic design](docs/CAMERA-DIRECTOR.md#dedicated-scenic-direction-0824) for controls and validation limits.

Normal coverage locally masks the broadcaster's child renderers, restoring their previous settings on stop. F10 deliberately shows the character for self-follow testing. This is local presentation only: in ordinary modes other clients can still see the character, and player input, collision, damage, AI interactions, and simulation effects are not removed. See [OBSERVER.md](docs/OBSERVER.md).

When other players become ready, coverage switches to them and rotates if several are available. Authorized autonomous sessions can briefly show nearby wildlife when all connected ordinary players meet the observed-idle rule; see [wildlife cutaways](docs/CAMERA-DIRECTOR.md#observed-idle-wildlife-cutaways). When none remain ready, normal idle coverage resumes. Automatic transitions keep the same RTMP session alive. Press **F11** to publish as before. Use `broadcast survey` to restart scouting around your character’s current region without restarting RTMP. Search state is cleared on stop/world exit.

[IDLE-DIRECTOR.md](docs/IDLE-DIRECTOR.md) documents the local shot layer and its earlier design. [AUTONOMOUS-BROADCAST.md](docs/AUTONOMOUS-BROADCAST.md) documents the current read-only acquisition, scenic traversal and recovery layers. Multiplayer isolation remains experimental and requires the companion. Offline observation retains manually selected travel; automatic discovery requires the authorized server feed.

## Solo distant-scene test

**0.5.0 runtime failure:** distant reference movement can exclude and unload the broadcaster itself, leaving a black view that `off` cannot repair. 0.5.1 adds a pre-removal guard that aborts the attempt and stops broadcasting if the observer is not retained. This is containment; dual-region loading is still unsolved. Rejoin after a failure on the older build.

No second player is needed to test scene acquisition. See [SOLO-ACQUISITION.md](docs/SOLO-ACQUISITION.md) for the mark → travel away → acquire workflow. This exercises network interest and rendering, not remote-player replication or teleport travel.
