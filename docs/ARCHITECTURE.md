# Architecture

The first milestone is a single broadcasting client following and cycling eligible player characters while other players remain unmodded. The existing game camera supplies the picture. The mod captures that framebuffer and the game audio listener, then starts FFmpeg to publish an RTMP feed.

## Components

| Component | Responsibility |
| --- | --- |
| `ValheimBroadcaster.Core` | Pure .NET Standard 2.0 director: identities, roster ordering, selection, dwell, hold, timeout, and states |
| `BroadcasterPlugin` | BepInEx/Jötunn lifecycle, configuration, controls, console command, camera patch, and experimental network-reference override |
| `TargetRoster` | Track connected-player presence separately from ready candidates and public acquisition positions; exclude the observer |
| `CoverageDirector` / `IdleShots` | Player priority, scenic/creature timing, recent-use exclusions, bounded creature bindings and terrain paths |
| `AcquisitionWindow` | Bounded opt-in remote interest attempts with local cooldown |
| `ObserverTravel` / `ObserverController` | Offline-only observer lifecycle, camera-relative body/interest, native teleport, bounded return and restoration |
| `FrameContinuity` / `FrameHold` | Hold a recent valid framebuffer through short readiness gaps; expose presentation diagnostics |
| `RemoteObserverClient` / `ObserverServerPlugin` | Connection-lifetime read-only role, authorization, network publication and simulation guards |
| `CameraRig` | Smooth a wide shot, check obstructions, hide/restore the HUD, restore camera state |
| `StreamCapture` / `GameAudioTap` | End-of-frame asynchronous GPU readback, listener placement, and non-mutating game audio capture |
| `EncoderSession` / media buffers | Bounded transfer, paced video/audio workers, FFmpeg lifetime, watchdogs, and redacted status |
| External FFmpeg executable | H.264/AAC encoding and RTMP/RTMPS publishing; launched and stopped by the mod |

0.7.0 adds a separate optional server companion and a versioned observer handshake. No persistent player database, embedded HLS/DASH server, web controller or automatic game launcher is implemented. RTMP remains client-side.

## Director contract

The adapter supplies roster snapshots and elapsed time. Targets have a string ID and display name; the adapter currently uses the character's ZDO ID. Names never decide identity. The core excludes the observer, deduplicates by ID, and sorts ordinally, giving reproducible next/previous behavior independent of input enumeration order.

`Disabled` enters a waiting state when enabled. A nonempty roster selects a target; readiness transitions it to `Following`. Dwell time advances only with ready footage. An unavailable target resets dwell and accumulates a separate timeout; the timeout advances to an alternative target. Holding pauses both automatic behaviors. A disappearing target advances to the next surviving ID. One tick makes at most one selection change, so a stalled frame cannot skip the entire roster.

The adapter must re-read the selection after `Tick`, because that call can change the target. `SelectionRevision` lets the camera reset smoothing on a cut. In 0.5.0 the adapter supplies only ready players to the rotation director. `CoverageDirector` independently selects player, scenic, creature, or recovery coverage. Idle does not restart the encoder. Player availability preempts idle; target invalidation falls back immediately (or within the next roster sample). The camera still rechecks readiness in its rendering hook.

Network presence is sampled every half second even when distant targeting is disabled. A connected player without a loaded character is reported as awaiting a scene, not an empty server. The survey origin bounds local discovery around the observer; `broadcast survey` resets it. It is never the camera focus. `IdleShots` scans the existing character registry once per second and stores at most 32 eligible non-player actors within the configured radius. Its reusable ready-ID list checks current liveness/range/readiness each update. Creature selection excludes recently shown IDs using a bounded cooldown history and rejects stationary subjects. Scenic intervals select changing terrain paths with a bounded recent-location exclusion queue; no fixed orbit remains. Both target/focus and desired/final camera positions require area readiness; failed validation shows a recovery slate.

Scene unavailability does not itself stop RTMP. Observer death, loss/change of the world connection, explicit stop, or component failure still stop and clean up the session. Player/creature bindings, history and paths are cleared on stop.

## Game lifecycle

The plugin checks Jötunn's headless predicate before graphical initialization and starts disabled. It verifies the expected camera patch target at startup and disables itself if that method is absent. A Harmony postfix on `GameCamera.UpdateCamera(float)` applies the shot after the normal camera update. Camera rendering stays in the game's normal pipeline.

The normal HUD root is hidden while requested and its previous state is retained for restoration. Both main and sky camera FOVs are updated and restored. Stop, observer death, world exit, component disable, and destruction restore owned state; destruction also removes this plugin's Harmony patches. In ordinary modes, runtime adapter errors stop broadcasting and log the exception. The offline observer experiment instead suspends movement and offers return, as described below. These are implemented cleanup paths, not evidence that every game transition has passed a runtime test.

In ordinary F8/F10 modes the observer remains an ordinary character with normal gameplay controls. It can be hurt, affect gameplay, or move if given input. This implementation does not claim the observer is passive or exempt from player-count and simulation rules.

## Self-follow test

`broadcast self` activates a session-only adapter flag. The roster then contains only the live local player, and `BroadcastDirector.SetSelfTarget` explicitly selects that singleton without normal observer exclusion. The same readiness check and `CameraRig` render the shot. The flag bypasses remote roster acquisition and reference-position overrides, and the overlay identifies test footage even if the ordinary status overlay is disabled.

Every activation first stops/restores the previous mode. `broadcast on` starts normal targeting, and stop/death/world exit/disable clears the self-test flag. Normal roster updates continue excluding the observer. No synthetic network player, spawned object, persistent config, or server protocol is introduced.

## Distant targets

`RemoteTargets=false` uses instantiated player objects. Camera candidates are intentionally limited to the client's loaded scene. Presence comes separately from the network roster.

The experimental option combines those objects with the network roster's public map positions. A public position can bootstrap streaming, but the shot is shown only when the corresponding player object exists and `ZNetScene.IsAreaReady` accepts its position. Distant players who do not publish a location cannot be reliably discovered by this route.

When no player is ready, experimental acquisition waits a configurable local cooldown, then tries one eligible public position for at most `TargetTimeoutSeconds`. It restores local interest between attempts and rotates acquisition candidates, including retrying a lone candidate. Already ready players take priority. Remote loading can unload the local idle area, in which case recovery is shown until a shot is ready; this mode cannot promise uninterrupted local scenery.

While an experimental target is being acquired or followed, the adapter changes `ZNet.SetReferencePosition` and intercepts subsequent setter calls for its own client. It retains the normal reference position for restoration. It does not alter send timers or teleport the character. Changing the interest position may still affect scene unloading, object ownership, simulation, and the parked observer; local readiness does not certify complete server delivery. These are open runtime questions.

A future server companion may supply approved target positions and explicit load/observer coordination. That requires a defined protocol and authorization based on the actual sender peer, bounded messages, target consent rules, and ordinary-client compatibility. It must not trust a claimed identity inside a payload. Such a companion is not necessary for the implemented nearby-player prototype.

## Version and build boundary

The plugin targets .NET Framework 4.8; the pure core targets .NET Standard 2.0. The project pins Jötunn 2.30.2 for compilation and reads matching local game/BepInEx assemblies. Jötunn build assets are excluded, so the template's implicit deployment/publicizing hooks are not inherited.

Do not substitute fake game API stubs to claim a successful mod build. Compilation and pure-core tests establish different facts from client startup, camera correctness, multiplayer compatibility, and a successful broadcast. Record each separately.

## Audio/video transport

`StreamCapture` copies the final framebuffer at end of frame into a screen-sized render texture, blits to the fixed output size, and requests asynchronous RGBA32 GPU readback. One request is allowed in flight. Three reusable managed pixel arrays exchange ownership through a latest-frame mailbox; no frame backlog grows with receiver latency. Low rendering FPS repeats the latest frame at the configured output cadence. This adds copies/readback and encoding overhead, but no second camera render.

`GameAudioTap` attaches to the existing active `GameCamera.m_listner` AudioListener. `OnAudioFilterRead` copies interleaved float samples into a fixed single-producer/single-consumer ring without modifying local playback or accessing Unity APIs/network I/O on that thread. Mono is duplicated to stereo; surround layouts are rejected. The listener is moved to the broadcast camera by a postfix on the actual `GameCamera.UpdateListner` method, after vanilla normally places it at the observer's eyes. Stop restores its last vanilla position and original local rotation. Device changes require an explicit restart.

Two background workers send RGBA video to FFmpeg stdin and float32 stereo audio through a loopback-only ephemeral TCP port. The transport is local, unauthenticated IPC; it assumes a trusted machine. Both workers use the same monotonic session clock: video is paced at output FPS and audio at 20 ms per block. Audio underflow inserts silence; half a second of queued audio or excessive scheduling lag fails the session rather than hiding unbounded drift. The latest input frame is repeated during short gaps. This is a bounded software timing approach, not sample-accurate hardware clock synchronization; long-run skew remains a runtime check.

A supervisor watches FFmpeg exit/progress and input callbacks. Missing input for five seconds, prolonged output stalls, unsupported audio data, or pipe errors stop publishing. The main/audio threads never wait on encoder pipe writes or process exit. Stop signals the supervisor, which kills the owned process and bounds worker joins; a replacement session cannot start until cleanup finishes. Stop can truncate the final codec packet/GOP. There is no automatic retry or automatic publishing after a game/world transition.

Arguments are constructed from validated numbers and a quoted RTMP/RTMPS URL, with no shell invocation or arbitrary encoder flags. Video encoding is explicitly selected between CPU libx264 and NVIDIA h264_nvenc, with encoder-specific presets and optional GPU selection. There is no automatic fallback. FFmpeg stderr is sanitized before retention: URLs and configured host/auth/app/playpath/query components are redacted, and only eight bounded lines are retained. A brief background exit/drain wait preserves causal errors when a pipe failure races process termination. The Unity update loop logs the completed failure once, and the console can retrieve the retained diagnostics. Stream keys remain visible in the private config and local process arguments. The child executable is user supplied; only this project's two DLLs are distributed.

## Observer presentation and solo testing

`ObserverVisualMask` masks child renderers using local `forceRenderingOff` values, preserving originals for restoration. It discovers equipment renderers each camera update with a reusable list. It does not disable the player, colliders, network view, or input, and makes no claim about what other clients see.

`SceneProbe` has bounded Acquiring/Viewing/TimedOut/Completed/Stopped states. The plugin stores a marker only for the current world connection, requires an initially unloaded destination at least 200 metres away, and exercises the actual SetReferencePosition hook. It never fabricates a player or teleports the observer. Target and intended camera area readiness gate the test shot. A 30-second uninterrupted acquisition failure or 120-second total limit restores normal interest. Existing RTMP remains running when starting/stopping the probe in normal coverage. A visible test label distinguishes probe footage. First-readiness time and observer displacement are diagnostic evidence, not a complete-scene verdict.

## Reference-override safety guard (0.5.1)

Reference overrides now require the exact scene-removal hook as well as the reference setter. Capture the observer ZDO before overriding. Before `ZNetScene.RemoveObjects`, require that identity in the near or distant retained lists. Otherwise fail the probe explicitly, stop and restore the override, and skip the stale removal pass. Merely restoring reference within the pass would be insufficient because its lists were computed earlier for the remote position. The next game cycle recomputes lists from local interest. This does not pin terrain or retain two regions; attempts needing that support abort.

## Offline observer adapter (0.6.0)

This opt-in experiment is separate from normal F8/F10 mode and the old reference-only probe. See [OBSERVER.md](OBSERVER.md) for the boundary between implemented local protections and planned multiplayer isolation, and [OBSERVER-TESTS.md](OBSERVER-TESTS.md) for the solo procedure.

Activation requires a closed local server with zero peers, a living grounded player, ownership of its ZDO, a ready origin, and every required game hook. It snapshots god/ghost and Rigidbody gravity/collision/constraint flags. Only that player's `Player.Update`, `SetControls`, and `Character.CustomFixedUpdate` calls are suppressed. Its `Player.FixedUpdate` is replaced by the private native `UpdateTeleport(float)` routine plus observer enforcement and body/ZDO/reference synchronization. Other characters continue their ordinary updates. The console and plugin update loop remain usable. This deliberately broad suppression is experimental and has not been validated in-game.

On each valid camera shot the body moves to two metres below the camera; it is not the scene's visual focus. Immediate movements are restricted to ready positions within 100 metres of the body. Larger/unready moves suspend rather than silently bypassing travel. Native teleport moves the actual player for distant travel; normal interest remains with the body, and ZDO position is synchronized before moving reference. The scene-removal prefix also protects the observer from stale/excluding retention lists. An unsafe pass is skipped, travel suspends, and the next fixed tick resynchronizes interest. Reference-only probes cannot run concurrently.

Travel/return uses a 45-second unpaused-time budget. Native teleport completion, local readiness and position within five metres of the requested destination are all required before proceeding. The world can still be incomplete despite these checks. Return cancels only the observer-owned teleport using verified private fields and starts a new native teleport to the activation point. Repeated return/off commands do not reset its timer. Only successful return releases physics/input, restores saved flags and camera/HUD/renderers, and stops RTMP. Errors enter a protected suspended state with a return command and an explanatory slate; they do not release a character beside an airborne camera. Forced world exit/component disable releases references and saved flags immediately and cannot promise to finish the return trip.

The native `ZNet.OpenServer` call is blocked for the observer's world while the experiment is active. The offline predicate is also rechecked before movement and on update. This is a test restriction, not a multiplayer observer implementation. The offline path remains separate from the opt-in 0.7.0 server protocol described below.

## Frame continuity and read-only connections (0.7.0)

`FrameHold` captures completed valid frames into one GPU texture. `FrameContinuity` allows a hold for less than 0.75 seconds after that actual capture; provisional director cuts no longer overwrite presentation readiness. Resize, stop and world transitions discard the snapshot. Hard travel/failure states immediately show the slate. Capture failure is reported and falls back to the slate. Diagnostics count actual GUI repaint frames and retain the last failure reason. The extra GPU copy/memory cost and visual result still need runtime measurement.

`RemoteObserverClient` pins the pre-join setting to the actual server connection. It blocks object publication and gameplay routed calls before the player can be published, negotiates protocol 1 over that server's ZRpc, and requires authorization before starting the camera. The native ZDO ownership predicate returns false for world objects while preserving the local avatar. A multiplayer return transitions `ObserverTravel` to Parked, retaining protections; normal offline return still transitions to Off and restores controls.

`ObserverServerPlugin` runs its guards only on dedicated servers. An allowlisted Steam transport identity is reserved as read-only before character publication; normal Steam authentication/readiness precedes acknowledgement. The server drops object writes and character IDs, permits a bounded authenticated RequestZDO read, strips interest metadata down to a finite coordinate, refuses world ownership, and filters gameplay peer enumerations used by player lists, events and server generation. Native connection capacity uses the real connection count. The role remains pinned through timeout/revocation until disconnect; plugin shutdown disconnects observers before unpatching.

This removes the broadcaster character from other peers' ordinary scene data, rather than asking their renderers to hide it. No client-side renderer flag is treated as remote invisibility. Dormant regions are not simulated/generated just for this connection. See [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md) for remaining limits and the not-yet-run runtime matrix.
