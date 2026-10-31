# Validation and runtime test record

## Evidence available

- The director's **55 automated cases passed**, including observer exclusion, identity ordering, manual selection, wrapping, dwell, hold, target loss, readiness timeout, lone-target behavior, invalid time inputs, isolated self-target selection, return to normal observer exclusion, idle alternation/fallback, player preemption, pending-player presence, scene recovery, stable creature rotation, hold/reset, bounded acquisition, and a 24-hour simulated idle schedule. This simulated policy test is not a Valheim soak test.
- The core compiled against actual .NET Standard 2.0 reference assemblies with warnings treated as errors. Its executable test harness ran on .NET 8 against that compiled core.
- The development container's ordinary `dotnet`/MSBuild path fails during process introspection (`Process.StartTime` / `ProcessName`). Direct invocation of the SDK's Roslyn compiler was used for the observed core checks. This is an environment limitation, not a replacement for checking the normal project build in CI or on a development machine.
- The plugin compiled with warnings treated as errors using Roslyn 4.11 from .NET SDK **8.0.427**, actual Microsoft .NET Framework 4.8 reference assemblies (**1.0.3**), Jötunn **2.30.2**, BepInEx **5.4.23.2**, and real Valheim references. This checked the source against those assemblies; it did not establish that the normal MSBuild graph executes successfully in this container.
- Real game references were obtained from the official Steam dedicated-server app **896660**, public Windows depot **896662**, manifest **171106503873955013** dated **2026-10-27**. Assembly metadata inspection found `Version` initializes `GameVersion(1, 0, 21)`, identifying this reference build as **Valheim 1.0.21**. Only reference DLLs were downloaded; no server was launched. These server references do not prove that a separately installed client matches.
- Jötunn's NuGet package identifies upstream commit `671c486ac8123a9b240eae80639b6c33f63b3884`. No client runtime result is implied by a dependency's availability or compilation success.
- A user ran 0.5.0 and supplied the failed distant-probe result recorded below. No successful gameplay, observer isolation, dedicated-server coexistence, stream quality, or GPU-performance pass is recorded. The 0.6.0 observer adapter has not been run in Valheim here.

Run the normal core check with:

```sh
dotnet run --project tests/ValheimBroadcaster.Core.Tests -c Release
```

CI checks this same core harness. A separate `plugin` job downloads pinned real game and BepInEx references and executes the normal `dotnet build` pipeline with warnings treated as errors. It uploads only this project's two DLLs. The dependency archives and game assembly are checksum-verified by `scripts/fetch-build-references.py`. See the [Actions runs](https://github.com/Dillflix/valheim-streamer/actions) for the result at a particular commit. Neither green job establishes in-game behavior.

## Direct RTMP validation (0.3.0)

The production encoder/session code passed **26 streaming checks** in the development container with real FFmpeg/ffprobe: validated settings, mono upmix, stereo ring wrap, bounded overflow, silence on underflow, latest-frame replacement, missing-executable handling, actual RTMP ingest, H.264/AAC codec and size/channel metadata, decoded flash/stereo tone, synchronization within 250 ms, idempotent process cleanup, and unavailable-receiver termination. These are synthetic media tests, not Unity capture tests. CI runs the same harness. The full plugin compiles against the real assemblies listed above with warnings as errors.

The following remain **pending inside Valheim**, including Windows/Thunderstore and VM testing:

| Case | Required result |
| --- | --- |
| Solo A/V | F10 then F11 produces audible game effects/music and the correct self-follow shot on the receiver |
| Stereo/spatial sound | Left/right sounds match the broadcast camera, including a target far from the observer; local playback remains audible |
| Input format | Stereo at 44.1 and 48 kHz works; mono duplicates to stereo; surround is rejected clearly |
| Listener restoration | F11/off restores listener position/rotation; F8, mode switch, death, exit, and disable also stop output |
| Device loss/change | Output stops with a safe message; after recovery, explicit restart works without duplicate taps |
| Picture | Upright image, correct colors/aspect ratio, resized game window, HUD/slate/overlay capture |
| A/V timing | Tool strikes/jumps match sound, initially and after a 30-minute stream; record measured skew |
| Low frame rate | Repeated video frames, no unbounded queue; audio remains continuous within supported encoder load |
| Minimize/focus | Confirm whether game keeps rendering/audio; input watchdog stops output after five seconds without callbacks |
| Receiver loss | Disconnect receiver during publishing: game stays responsive, stream stops, explicit restart works |
| CPU/network stall | Bounded buffer or output watchdog stops overload; no growing memory or game/audio-thread I/O stall |
| Repeated start/stop | Twenty cycles leave no FFmpeg processes, listener filters, or textures behind |
| Private configuration | No ingest URL/key appears in plugin status/logs; config and process arguments remain private |

Setup and receiver commands: [STREAMING.md](STREAMING.md). The mode is client-only; neither a broadcaster server plugin nor mods on other players are part of this test.

## Record the target before testing

Use a disposable world and character, a separate mod profile, and an unchanged copy of any world needed for comparison. Record:

| Field | Test value |
| --- | --- |
| Source commit and owned DLL SHA-256 hashes | Pending |
| Valheim client build / server build | Pending |
| BepInEx / Jötunn versions | Pending |
| OS, native machine or VM, GPU, driver | Pending |
| Connection backend: Steam or crossplay | Pending |
| Other client/server mods | Pending |
| Relevant configuration and target position-sharing settings | Pending |
| Test world/character and log location | Pending |

Install only the two owned plugin/core DLLs in the observer's profile. Other players should initially use unmodified clients and the server should initially have no broadcaster mod. Test the default loaded-target mode before enabling distant targeting.

## Solo self-follow checks

These cases are **pending in-game validation**. Start alone in a local test world, then press F10 (or run `broadcast self` in the F5 console and close it).

| Case | Required result |
| --- | --- |
| Solo activation | Own character follows the configured shot; list contains one target; SELF-FOLLOW TEST label is visible |
| Movement and obstacles | Walking, running, jumping, walls, and trees exercise framing, smoothing, and obstruction handling |
| Cycling and hold | Next/previous, automatic dwell, and hold/resume retain the single self target without errors |
| Another player joins | Test roster remains self-only |
| Remote setting enabled | Self mode uses normal local reference updates; entering from a distant shot restores the previous override |
| Mode switch | `broadcast on` excludes self again and enters scenic/creature coverage when alone; `broadcast self` re-enters test mode |
| Cleanup | F8/off, death, world exit, and component disable restore view/HUD; re-entry via F8 is normal mode |
| Capture | Direct RTMP captures the actual self-follow shot and test label, including main/sky FOV alignment |

Passing these tests does not establish multiplayer replication or distant streaming behavior.

## Default-mode checks

Every case below is **pending in-game validation**.

| Case | Required result |
| --- | --- |
| Startup/menu | Plugin logs its version and starts disabled; normal menus and camera work |
| Headless load | No camera/UI initialization or graphical errors; plugin does not require clients to install it |
| No other players | Enabling starts new scenic terrain paths around the loaded region, then creature/scenic alternation; stop restores ordinary view |
| Two nearby players | Both appear in `broadcast list`; the observer does not; duplicate names remain independently selectable |
| Follow shot | Target stays framed; FOV, height, azimuth, smoothing, and obstruction response match configuration |
| Manual cycle | Next/previous wrap predictably and reset the camera pose without displaying a stale target |
| Automatic cycle | Only ready footage consumes dwell time; rotation does not skip several targets after a frame stall |
| Hold/resume | Hold stops timed switching; resume continues; manual switching remains possible |
| Death/unavailable target | An unready target leaves the ready roster; another ready player wins, otherwise idle runs; invalidation overrides hold |
| Observer death | Broadcasting stops, the ordinary HUD/camera return, and experimental reference override is removed before respawn |
| Target disconnect | Selection falls back to a surviving target; an empty ready roster enters idle coverage, keeping connected-but-unavailable presence distinct |
| Controls | Hotkeys are ignored in recognized UI/text entry; console commands report useful status and reject unknown IDs |
| Stop/disable | Camera/FOV and prior HUD state restore; the observer can continue normally |
| World/menu transitions | Leaving a world stops broadcasting; returning does not duplicate handlers or restore stale references |
| Failure | Adapter exception logs once for that activation, stops broadcasting, and restores owned state |
| RTMP | Correct game framebuffer, waiting slate, target changes, audio, and focus changes are captured as intended |

Observe the parked character during these tests. Document damage, movement, enemy behavior, sleep participation, simulation ownership, and player-count effects; do not infer that an invisible HUD or detached camera makes the character passive.

## Experimental distant-mode checks

Set `Experimental / RemoteTargets = true` only after the default matrix succeeds. Use two target players in widely separated areas with public map positions enabled.

| Case | Required observation |
| --- | --- |
| Initial distant selection | Reference position changes and the target scene object eventually appears; no target footage is shown before local readiness |
| Scene completeness | Terrain, structures, creatures, effects, and player motion are compared with the target client's view; local readiness alone is insufficient evidence |
| Repeated cross-world cycling | Both areas load reliably through repeated switches without accumulating errors, stale objects, or memory growth |
| Hidden location | A distant private-position player is not silently exposed through a new relay; document nearby-player visibility separately |
| Stale position / teleport / logout | Loading waits remain bounded, alternatives work, and stop is always available |
| Restoration | Turning remote mode off or stopping restores normal reference updates and reloads the observer's own area |
| Parked observer | Body persistence, physics, health, and world interaction remain understood while its region unloads or reloads |
| Server effects | Compare ownership transfer, spawning, raids, enemy targeting, sleeping, and player-count behavior before/during/after observation |
| Backend variants | Record Steam and crossplay outcomes separately; one does not establish the other |

Do not promote distant mode to the default until the actual client/server combination passes these cases and the remaining observer effects are acceptable. Preserve logs and configuration with each result.

## Continuous coverage (0.4.0) runtime checks — pending

- Alone, F8 starts a changing local scenic paths; at default dwell values it alternates 45-second moving scenic shots with 25 seconds following a loaded living non-player actor within 50 metres. No fresh actor means another scenic path; exhaustion is reported explicitly.
- Killing/despawning/moving the subject beyond the radius returns to scenery. Test wildlife, tamed animals, and enemies. No actor is spawned or controlled.
- Joining ready players preempt idle and rotate; a lone ready player remains selected. Connected players outside the loaded area show awaiting-scene status while valid idle shots continue.
- Run F11 throughout those automatic transitions and verify one uninterrupted RTMP connection with matching spatial audio. Explicit camera mode restarts (F10 / on / self) still stop output as documented.
- Move the observer and run `broadcast survey`; the new survey region takes effect without stopping output. World exit/re-entry never reuses an old survey origin or creature binding.
- Test empty/unready scenes and camera positions near the edge of loaded terrain: a recovery slate must hide invalid footage, and valid coverage must resume when ready.
- With experimental remote mode enabled, verify attempt/cooldown bounds, reference restoration, eventual ready-player selection, and the documented recovery slate if remote loading unloads the local survey region.
- Run actual eight-hour and 24-hour sessions before claiming unattended reliability. Record memory/texture/process counts, observer survival, A/V skew, target changes, and failures. These runs have not been performed here.

## 0.5.0 additional checks — pending in Valheim

- Record at least twenty idle transitions: new paths, no immediate repeated locations, no repeated creature within five minutes, stationary-subject abandonment, and honest exhaustion status when no path is viable.
- Verify normal footage excludes the broadcaster and newly equipped items; stopping restores rendering. F10 remains visible. Use another client to confirm local masking does not falsely imply network invisibility.
- Exercise probe mark/start/stop/status with a distant unloaded destination, timeout, readiness loss, total-duration expiry, F8, death and world exit. Confirm return to ordinary interest and no stale cross-world marker.
- A real game soak test remains necessary. The automated timing simulations do not establish dynamic shot quality, terrain fidelity, passive observer behavior, or long-running stream health.

## Observed 0.5.0 failure / 0.5.1 containment

User screenshot: Valheim 1.0.21, probe Stopped after 0.1s, first-ready never, destination/player displacement unavailable. User confirmed probe stop and broadcast off did not restore the black view and exited the world. This is a failed runtime result, not a timeout or successful acquisition.

Source investigation found that scene removal destroys instances outside lists generated around the overridden reference, including the observer. 0.5.1 adds a mandatory observer-retention guard and skips the unsafe removal pass while stopping/restoring owned state. Three new regressions cover observer exclusion/missing identity, normal/retained cases, and persistent failure reporting across cleanup. Both experimental remote following and solo probes use the guard.

Pending runtime check: on a disposable world, a distant attempt must abort before observer removal and return to ordinary control. Check local terrain reload, player survival, HUD/render restoration and no missing-player exception. Actual dual-region loading remains unimplemented; the new guard is containment, not an acquisition success claim.

### Log confirmation

The supplied LogOutput.log identifies Windows x64, Valheim 1.0.21, Unity 6000.0.75, BepInEx 5.4.23.6 (pack 5.4.2360), Jötunn 2.30.2, and Broadcaster 0.5.0. Immediately after `SOLO DISTANT SCENE TEST | Acquiring | 0s | RTMP: Stopped`, Unity logs `Local player destroyed`. No intervening exception appears. The later disconnect corresponds to the user exiting the world. Thus this failure occurred without RTMP publishing and confirms local-player destruction rather than an encoder blackout. The full user log is not committed.

The 0.5.1 CI build still uses the existing BepInEx 5.4.23.2 compile baseline; the supplied log records the actual tested runtime version separately. No successful 0.5.1 game runtime result has yet been recorded.

## 0.6.0 observer foundation — code checks passed, runtime pending

Nine new pure-policy cases cover the closed-world gate (including a hosted world with zero peers), all combinations of native teleport/readiness/position arrival signals, travel timeout with retained active state, outbound cancellation, return completion, repeated return commands without timer reset, retry after failure, world cleanup/reset, and invalid/paused/large time inputs. Total: **45/45**. These do not exercise Unity, Harmony, physics, renderer state or real teleportation.

The plugin compiles with warnings treated as errors against the same real Valheim 1.0.21, Unity, BepInEx 5.4.23.2 and Jötunn 2.30.2 references. The actual user's loader remains BepInEx 5.4.23.6; compilation is not a runtime compatibility result. No new dependency or game binary is packaged.

Metadata/IL inspection verified the exact teleport fields and methods used by the adapter, the player update paths being suppressed, `ZNet.OpenServer`/offline predicates, and the scene-removal path. The resulting code uses real game APIs and reflection, not invented stubs. The supplied failure log and raw inspected game implementation are not committed.

Follow [OBSERVER-TESTS.md](OBSERVER-TESTS.md). Every in-game case there is still pending, including start/stop restoration, body/camera following, teleport/return, timeout recovery, and RTMP continuity. This branch must not be described as multiplayer invisible, simulation-neutral or ready for unattended use.

## 0.6.0 user feedback and 0.7.0 validation

The user reports that 0.6.0 seems to work, with frequent brief camera flickers and upper-left text visible for one/few frames. This is positive partial runtime feedback, not a completed test matrix. The text/location is consistent with the immediate recovery slate. No new log/video establishing the exact readiness trigger was supplied. 0.7.0 removes provisional director invalidations, adds a bounded hold of a captured valid frame and exposes presentation diagnostics; the flicker fix still needs a user retest.

Ten additional policy checks cover captured-frame expiry and hard-failure precedence, reset/invalid clock handling, bounded exact Steam-ID configuration, protocol/deadline/revocation behavior, and parked multiplayer return/resume versus ordinary offline cleanup. **55/55 core cases passed.** Both client and new server assemblies compile against the pinned real 1.0.21 baseline with warnings treated as errors.

A separate metadata-only harness passed **15 real-assembly structural checks** and verifies the real game's interest-position independence, gameplay-query patch sites, capacity accounting, sleep/character discovery, ownership delegation, object-send path, transport sender signatures, interest packet shape, Steam authentication order and bounded routed-request envelope layout. It never executes game code or supplies fake game types. These structural checks do not establish that Harmony hooks applied or multiplayer behavior works.

Every multiplayer case in [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md) remains **not run here**. No live server installation or public release was performed. Existing synthetic FFmpeg tests cover media transport; they do not verify the new held-frame rendering, GPU overhead, or in-game audio during multiplayer travel.

## 0.7.1 RTMP failure report and encoder changes

The supplied Windows log records Broadcaster **0.6.0**, Valheim **1.0.21**, Unity **6000.0.75**, BepInEx **5.4.23.6** and Jötunn **2.30.2**. Publishing changes from Starting encoder to Video encoder pipe closed. That version discards FFmpeg stderr, so the receiver/encoder cause cannot be recovered from this log. The message mentioning libx264/AAC was a generic fallback, not evidence of missing codecs. The user's RTMP failure is recorded; it is not a successful in-game stream.

0.7.1 adds explicit NVENC selection, no silent fallback, a bounded redacted FFmpeg error tail and exit code, automatic failure logging, and a diagnostics command. The protocol and server role are unchanged; RTMP does not require installing the companion.

Local synthetic validation: **40 streaming checks passed**, including actual software H.264/AAC RTMP reception, decoded stereo tone, flash/tone skew **0.030 seconds**, absent-receiver error preservation, URL/component redaction and bounded diagnostic history. **55 core policy checks passed**, and client/server sources compiled against the real pinned game references with warnings as errors. Normal MSBuild remains blocked by this container's dotnet process-introspection failure; CI supplies the normal project builds.

Windows CI and the full project build are checked on the commit's Actions run. **Actual NVENC hardware encoding and Valheim capture are not tested here.** Run the local NVENC smoke test in STREAMING.md, then the same RTMP harness with `-- --nvenc` on a GPU-equipped host if an SDK is available. In Thunderstore, replace both client DLLs, select the encoder, restart, use F11 and inspect the received game picture/audio. If it fails, retain the new RTMP failure block. Verify F11 stop/start, wrong URL, absent receiver, and no credential disclosure in logs.
