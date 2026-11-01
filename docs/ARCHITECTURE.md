# Architecture

The broadcaster is an authenticated Valheim client using the existing main camera, game AudioListener and an owned FFmpeg process. Ordinary players need no broadcaster mod. The optional dedicated-server companion establishes a read-only connection and supplies discovery to that authorized observer. The 0.8.0 autonomous layer uses the same protected character/interest carrier that the user exercised in 0.7.0; the separate bodyless-spectator branch is not integrated here.

## Components

| Component | Responsibility |
| --- | --- |
| `ValheimBroadcaster.Core` | Pure .NET Standard 2.0 scheduling, protocol validation, travel and recovery policies |
| `BroadcasterPlugin` / `BroadcastSettings` | BepInEx/Jötunn lifetime, configuration, controls, camera hook and integration of policies with the game |
| `BroadcastDirector` / `TargetRoster` | Ordinary loaded-player selection and adapters for player identity, presence and readiness |
| `AutonomousDirector` | Connected-player scheduling, bounded acquisition requests, generated-region selection, novelty/failure history and solo route exercise |
| `ObserverDiscovery` / `ObserverDiscoveryService` | Bounded versioned snapshots of server-known players and already-generated scenic destinations |
| `RemoteObserverClient` / `ObserverServerPlugin` | Pinned read-only role, authentication, discovery exchange, publication and ownership guards |
| `ObserverTravel` / `ObserverController` | Local carrier, camera-relative body/interest, native teleport, protected return/park and restoration |
| `CoverageDirector` / `IdleShots` | Actual player/scenic/creature shot selection, local terrain paths and nearby eligible actor bindings |
| `CameraRig` | Wide framing, smoothing, obstruction/readiness checks, HUD and camera-state restoration |
| `CinemachinePlayerRig` / `FoliageShotQuality` / `PlayerShotVisibility` | Manually solved Cinemachine player candidates, foliage-aware quality, stable selection and validated camera transitions |
| `ProceduralShotPolicy` | Pure bounded shot selection with persistent improvement and healthy-shot dwell |
| `ScenicVisibility` | Cached nearby vegetation bounds and multi-ray foreground screening for scenic compositions |
| `FrameContinuity` / `FrameHold` / `FrameHoldRenderer` | Bounded camera-image hold, final image-effect composition and presentation diagnostics |
| `FramebufferCapture` | Screen-to-texture normalization with graphics-state restoration for streaming |
| `PlayerFollowContinuity` / `PlayerShotReadiness` | Acquired-player binding, recent presentation evidence and local geometry checks for uninterrupted following |
| `ConnectionRecoveryPolicy` / `ConnectionRecovery` | Opt-in same-session recovery with cancellation, bounded retry and fresh authorization |
| `StreamCapture` / `GameAudioTap` | End-of-frame asynchronous GPU readback and non-mutating audio capture |
| `EncoderSession` / media buffers | Paced video/audio workers, bounded transfer, FFmpeg lifetime and watchdogs |
| `OutputRecoveryPolicy` / `StreamRecovery` | Desired output, manual-stop precedence, retry/backoff and blocked-failure handling |
| `EncoderDiagnostics` | Bounded, redacted FFmpeg failure evidence |

The client plugin targets .NET Framework 4.8; the core targets .NET Standard 2.0. Jötunn 2.30.2 is the pinned compilation dependency. Builds read real matching game/BepInEx references and exclude Jötunn template deployment/publicizing hooks. CI does not launch the game. Game, Unity, BepInEx and Jötunn DLLs are excluded from project artifacts. The client intentionally includes the isolated `ValheimBroadcaster.Cinemachine.dll` built from pinned Cinemachine 3.1.7 sources, plus upstream notices. The server does not reference or ship Cinemachine.

`FrameHoldRenderer` is appended to the active game camera's existing image effects. Its `OnRenderImage` callback caches the processed camera source only when that same frame's camera/carrier operation passed readiness. Live and Hold both blit that cached texture to the supplied camera destination. The hold never passes through screenshot capture or `GUI.DrawTexture`; GUI adds current status/recovery text only. This uses one cached render texture and GPU copies, without another camera render or CPU readback. Streaming still captures the final framebuffer after camera composition and GUI.

A cached texture matches the source dimensions and graphics format; resizing, lost textures, camera replacement, stop and world cleanup invalidate it. Held/slate outputs never refresh the 0.75-second capture deadline. Hard travel/recovery boundaries clear that deadline immediately, preventing pre-travel footage from reappearing after a quick return to Watching. GUI requires a compositor result in the current frame and otherwise covers the display with a recovery slate. Valheim's built-in pipeline and existing image-effect stack were inspected; conflicting third-party camera effects or replacement render pipelines require separate runtime validation.

`broadcast framehold test` schedules a 0.5-second artificial readiness gap after two seconds, for closing the console. It changes presentation only: camera/carrier/director logic continues normally. Hard failures take precedence, and camera changes/stop cancel the test. It requires a live picture and can run while alone.

## Procedural player cameras

Player framing uses Cinemachine 3.1.7 virtual cameras, solved explicitly from the existing game-camera postfix. No second rendering camera or Cinemachine Brain owns the game camera. An isolated dependency assembly and a small manual-update bridge advance Cinemachine's frame clock and preserve its duplicate-update guard. The adapter validates the resulting position before applying it to Valheim's main camera; the existing carrier/listener and frame compositor then follow their normal sequence.

Candidate poses vary the configured preferred angle, distance and height. The custom `FoliageShotQuality` extension implements Cinemachine's shot-quality interface and combines the resulting camera composition with read-only local visibility estimates. `PlayerShotVisibility` samples head/torso/lower-body sightlines over the full camera-to-player distance and a weighted foreground grid. Solid camera/near-plane overlap makes a candidate physically invalid. Collider-free foliage contributes a bounded quality penalty; an obscured but physically safe image remains available while another angle is sought. It does not turn foliage into a loading or teleport decision.

The vegetation survey keeps up to 512 nearby renderer bounds, deduplicates LODs and refreshes on cadence or movement. Bounds are conservative, not leaf-alpha measurements, and name/component classification can miss custom vegetation. The foliage evaluator does not change world colliders, renderers or shaders. Cinemachine can create a private hidden scratch sphere for penetration queries; its Rigidbody has collisions disabled, and the isolated runtime releases that cache idempotently on teardown. Visibility tests use bounded nonallocating physics buffers. The camera rig validates loaded support geometry and reachable carrier destinations separately from picture quality.

`ProceduralShotPolicy` retains a clear current shot unless another improves quality by at least 0.15 for 0.5 seconds. Healthy shots receive two seconds of minimum dwell; physical invalidation switches immediately and poor shots can escape the dwell. All low foliage scores still leave a physically eligible candidate selectable. Motion prediction and smoothing are reset on binding changes and discontinuities. The interpolated movement is checked before presentation; an unsafe blend can cut to a validated endpoint rather than pass through a wall.

Both autonomous frameability and travel displacement query this same camera adapter's geometry. These queries remain finite and independent of foliage quality for finite known positions. The final applied camera/carrier still obeys the existing 95m/100m movement safeguards. `Camera.ProceduralPlayers=false` restores fixed player framing; scenic/creature, protocol, authorization and streaming behavior are unchanged.

## Observer connection boundary

`RemoteObserverClient` pins `ObserverConnection` when the actual server connection opens. When true, a prefix on native `SendPeerInfo(ZRpc, string)` sends protocol-3 observer intent over that exact server RPC before the vanilla authentication packet. Both messages use the same reliable FIFO Steam transport. No vanilla packet is extended or rewritten. Object publication and gameplay routed calls are blocked before the local character can be published. The existing Hello/Reply endpoint names are retained for explicit version rejection; authorization still occurs only after native authentication. The client ownership predicate remains false for non-avatar world objects. When false, no observer request is sent and these guards do not affect ordinary gameplay.

The dedicated Steam server reserves only connections that explicitly request the matching observer protocol before native admission. `ObserverAdmission` locks ordinary admission at the start of `RPC_PeerInfo`; a late request cannot turn an already admitted player into an observer. Steam-ID allowlist membership alone never reserves a role or starts a handshake timer. Pre-authentication intent only adds restrictions: normal Steam authentication/readiness and the actual transport identity's allowlist check precede acknowledgement and discovery access. Requested but unauthorized observers are disconnected. The server drops observer object writes and character IDs, permits only a bounded authenticated native `RequestZDO` read, accepts only a finite bounded world-interest coordinate from player metadata, and refuses observer world ownership. Filtered gameplay peer enumeration excludes observers from player lists, random events and peer-driven zone generation. Native capacity uses the real ready connection count, so an observer still consumes a slot.

The role cannot be removed mid-connection. Revocation/timeout disconnects while restrictions remain through cleanup. Return/off parks the multiplayer carrier with protections intact; resuming camera coverage does not make it an ordinary player. Plugin shutdown disconnects observers before removing guards. An ordinary client never needs a renderer patch to hide the broadcaster because no broadcaster character is published to it.

This is a boundary around audited vanilla paths, not a sandbox for arbitrary third-party direct RPCs or conflicting Harmony patches. Other-client visibility, interactions, sleep and ownership handoffs remain runtime checks. Dormant regions are not simulated/generated merely for an observer.

## Authenticated discovery

Discovery is a read-only request/reply protocol available only to confirmed observers. `DiscoveryEnabled` controls delivery; `SharePrivatePlayerPositions` determines whether server-known non-public locations can be sent. This setting is a server policy distinct from the ordinary player's public-map flag. With sharing disabled, a private player remains present without an acquisition coordinate.

The player ID is the connection session UID; character ZDO identity is separate so respawn does not become a new connection. `IsSpawned` means the server has a character ZDO, not that the observer has loaded its scene. Positions come from the real character ZDO and require a recent player-metadata heartbeat. The client still requires a living instantiated player, loaded scene and viable camera/body geometry before presenting a player shot.

The client polls every two seconds, the server answers at most once per second per observer, and local snapshots expire after ten seconds. Sequence/world checks reject replayed, reordered and cross-world messages. The codec validates its inner payload before collection/string allocation: 32 KiB, 64 players, 128 regions per page, 64 UTF-16 characters/256 UTF-8 bytes per string. Native ZRpc/ZPackage deserialization occurs first; this application protocol does not protect against arbitrary native allocation flaws or a malicious server. Client region cache and server generated-region catalog are bounded at 1024 and 4096 entries respectively, with rotating pages.

Scenic candidates use the existing generated-zone set, procedural terrain height and biome queries. A persistent coordinate cursor makes at most 1024 cheap probes per second, with bounded nearby-peer seeds and at most 32 terrain queries per second; half the terrain-query budget is reserved for global progress. The cursor continues as the generated-zone set changes. A destination is a dry-land center with a generated 3×3 zone neighborhood, finite height above water plus two metres and strictly below 1000 metres. These queries do not instantiate server content. Local arrival/terrain/camera readiness remains a separate requirement.

## Autonomous direction

`AutonomousDirector` receives immutable player/region snapshots, monotonic time, carrier position, actual presented-player identity and ready-idle footage. It returns a selected subject, reason and a tokenized travel request. The adapter accepts each request once, invokes protected native travel and reports completion/failure. One immutable destination remains in flight; changing player coordinates or manual selection cannot stack teleports.

Players are selected by stable ID with fair rotation across both ready nearby and fresh distant candidates. Viewing time advances only when the selected player's frame was actually ready/presented. A long update cannot charge hours of unseen footage: per-tick footage accumulation is capped. An unavailable target enters acquisition; the default 65-second attempt covers native travel and character readiness and permits at most three relocations. Arrival does not reset the attempt. Failed targets receive exponential cooldown starting at 30 seconds and capped at ten minutes, including the lone-target case.

While a player lacks a usable position/scene, the shot layer can retain valid idle coverage. Missing or expired discovery does not authorize stale travel. Death, respawn, portal movement and disconnect require new binding/readiness checks. Holding pauses automatic rotation/scenic transfers while the selected player's scene still requires validation. Manual selection changes are applied coherently after native travel completes.

Without a usable player, ready idle footage accumulates toward the 120-second region interval. Afterward, or after a separate ten-second unready-region seek interval, the director selects a fresh generated region within the configured 256–10000 metre distance range. It records bounded visit/failure history (128 entries each), applies the default 30-minute revisit interval and reports cooldown/catalog exhaustion. Unexpired region visits are never evicted to make room; a full history waits for expiry instead of permitting an early revisit. It does not promise unlimited novel content from a finite generated world.

The solo route exercise accepts 1–32 explicit marks and uses the same tokenized travel callbacks. Each destination must produce three seconds of ready scenic footage after arrival. A failed destination stops the route with a retained result. This is an acquisition/travel test, not a fake player, replication test or server mutation.

## Carrier and scene lifetime

Scenic selection samples six angle/height variants per terrain path, checks five positions along each path, and limits evaluation to three variants per update. Terrain-only grounding avoids selecting a tree or building collider as landscape ground. Solid-clearance checks match the camera rig; scenic shots reject obstruction rather than moving the camera toward the obstructing branch. Player and creature framing retain their existing behavior.

`ScenicVisibility` caches up to 512 nearby vegetation renderer bounds, refreshed at most every five seconds or after moving 40 metres. LODs are deduplicated. Cameras inside those bounds or with a heavily blocked weighted 3×3 foreground view are rejected. Cache truncation reduces the surveyed radius rather than declaring unseen positions clear. This is conservative bounds geometry, not pixel/alpha analysis; open gaps inside a tree's bounds can be rejected and unrecognized custom vegetation can be missed. No world renderer or simulation object is changed.

Active scenic views are rechecked four times per second. A continuous 1.5-second presentation failure retires the path and starts a fresh search; the existing bounded frame hold/recovery slate covers invalid footage. Accepted replacement paths restart scenic dwell without forcing another selection revision. Recent-location exclusion and autonomous region travel still apply, including the existing unready-region timeout when no clear local shot is available.

The camera-relative carrier is a live local player kept two metres below a ready broadcast camera. Input/player update paths are suppressed only for that carrier; local god/ghost flags and Rigidbody gravity/collision/constraints are enforced. Body, character ZDO position and network interest move together. World objects remain outside observer ownership.

Immediate body following is bounded to a ready destination within 100 metres. The automatic adapter accounts for camera offset before treating a nearby target as directly frameable; larger moves require native travel. `Player.TeleportTo` and the private native teleport update perform distant travel. The policy waits for teleport completion, loaded destination and proximity rather than treating a coordinate as arrival. Each native trip is bounded to 45 unpaused seconds.

Before `ZNetScene.RemoveObjects`, a retention guard requires the local carrier ZDO in the near/distant retained lists. An excluding pass is skipped and travel suspended before the carrier can be destroyed. This is containment of stale interest/removal lists, not dual-region retention. The reference-only experimental probe remains separate and cannot run concurrently with observer travel.

Explicit return cancels the owned teleport and returns to the activation point. Multiplayer completion parks with protections retained; offline completion restores normal flags/controls. Explicit off cancels autonomous, stream and reconnect intent. Automatic failure recovery may request a protected return and resume only through its internal recovery flag. It is bounded; repeated failures request armed connection recovery or retain a protected suspended state with diagnostics.

World exit/destroy releases owned references, restores saved local flags/camera/HUD and stops the owned encoder. It cannot promise that a native return completes during world teardown. This distinction is why internal recovery and explicit user stopping are separate intents.

## Local shots and presentation

`CoverageDirector` chooses the actual player, scenic, creature or recovery shot; `IdleShots` operates within the loaded survey region. The survey origin is a search boundary, never a fixed orbit focus. Scenic shots select changing ground paths with recent-location exclusion and validate start/mid/end terrain. Nearby non-player characters are bounded to 32 candidates, require recent movement and have a five-minute repeat cooldown. Creature-simulation changes are deferred.

Initial player acquisition requires full `ZNetScene.IsAreaReady` at the subject, camera and carrier. This native predicate includes known network objects throughout a 3×3 zone neighbourhood and can fail during background object creation even when the displayed scene is usable. Once a complete player camera/carrier operation succeeds, `PlayerFollowContinuity` permits continued presentation for the same subject/character binding, using the last accepted position rather than the acquisition origin. At most one second may have elapsed and the target must have moved no more than 16 metres in 3D since the last presented frame. These are discontinuity limits, not a total follow radius. Candidate probes cannot renew the evidence.

The adapter additionally binds the actual Player instance, ZDO ID, world, local carrier and interior/exterior state. Death, missing/replaced actors, shot changes, stop/reset, travel and discontinuities require new acquisition. Selection, the autonomous policy, camera and carrier all use the same continuation decision. A continued shot checks existing generated terrain meshes/collider outdoors; in interiors it checks nearby static support geometry at interior height, excluding characters and rigidbodies. Camera support is checked at the actual smoothed/clipped position, then at the carrier: an unrealized desired offset beyond a tile boundary or small room must not veto an otherwise valid picture. A terrain tile far below an interior is never sufficient. These are scene-support checks, not proof of complete replication or artistic shot quality.

Full readiness remains the default for initial acquisition, scenery, creatures, probes, travel and return. Continued following still requires finite coordinates, local actor availability, loaded support geometry and the existing immediate carrier movement limits. Actual missing geometry can still produce a held frame or slate. The main and sky camera settings and normal HUD state are restored on stop. Child renderer/light/audio masking affects the local carrier; server publication guards provide the network visibility boundary.

`FrameHold` keeps one validated camera image on the GPU. Readiness gaps can display it for less than 0.75 seconds after its actual capture. Travel and explicit fault states bypass the hold for an honest slate. Resize, stop and world changes discard the texture. Capture failures disable the hold for that activation and retain the slate fallback. Diagnostics distinguish held frames from slate frames and retain the last readiness reason.

Observer lifecycle (`Watching`, `Travelling`, `Returning`, `Parked`, `Suspended`) is reported separately from programming (`Following`, `Acquiring`, `Scenic`, `Waiting`, route exercise), discovery freshness and recovery. Seeing `Watching` alone is not evidence of a human target.

## Audio/video and output recovery

`StreamCapture` copies the existing end-of-frame framebuffer, blits to the output dimensions and requests asynchronous RGBA32 GPU readback with one request in flight. A latest-frame mailbox swaps three reusable arrays so receiver latency cannot create a frame backlog. The game AudioListener follows the broadcast camera, and `GameAudioTap` copies its mix into a bounded stereo ring without changing local playback or calling Unity/network APIs on the audio thread. Mono is duplicated; unsupported layouts fail clearly.

Video and audio workers use the same monotonic clock. Video is paced at configured FPS and audio in 20 ms blocks; short underflow inserts silence/repeats a frame. Excess audio backlog, prolonged missing callbacks or encoder lag fails the session rather than growing memory or hiding indefinite drift. Stop kills the owned FFmpeg process and bounds worker joins; it can truncate the final packet/GOP.

Software `libx264` and NVIDIA `h264_nvenc` both publish H.264 with stereo AAC over RTMP/RTMPS. NVENC does not remove GPU readback, conversion/copying or AAC costs and does not silently fall back to software. The executable is user supplied. Only validated values are used in process arguments; no shell or arbitrary encoder arguments are introduced.

Output intent survives a recoverable session failure and eligible unexpected world loss. `StreamRecovery` waits for complete encoder/readback cleanup, then applies bounded exponential backoff with periodic long cooldowns. Stable actual publishing resets the burst. Invalid config/capability/authentication and unconfirmed cleanup block automatic replacement. Explicit stream off wins over `AutoStart` and `AutoRetry` until a new explicit start.

`EncoderDiagnostics` retains only bounded redacted stderr/exit information. This preserves useful receiver/NVENC failures without putting configured destination/credential components into retained diagnostics. Config and the real FFmpeg process arguments still contain the stream key. Local loopback media transport assumes a trusted machine. Remote receiver playback, actual Unity capture, A/V drift and hardware NVENC remain separate runtime checks.

## Connection recovery and supported scope

`ConnectionRecovery` is opt-in for an initially manual, authenticated observer session. It captures non-secret join metadata independently of opt-in; password retention requires opt-in at the normal password entry. An authorized passwordless session can therefore arm recovery after joining. Recovery uses inspected native game startup/join APIs, preserves credentials only in memory, requires fresh observer authorization and follows bounded retry policy. It does not release read-only restrictions while waiting or silently join as an ordinary player.

Explicit `unattended off` pauses automation and cancels requested output along with connection recovery, while camera coverage can continue. Automation and output then require their own explicit start commands. Native logout/quit cancels those intents even if recovery was disabled and forgets retained credentials. Unexpected connection loss follows the separate recovery path and preserves requested output.

The integration requests background running while unattended intent is armed and restores its prior setting on cancellation. That does not defeat OS sleep, GPU/audio suspension or application termination. In-process reconnection is not an external game/process supervisor. A world disconnect can end RTMP and require a fresh publisher connection after recovery; a healthy ordinary travel does not itself restart output.

No embedded HLS/DASH server, web controller, arbitrary renderer, operating-system login automation or creature simulation authority is added. See [AUTONOMOUS-BROADCAST.md](AUTONOMOUS-BROADCAST.md) for operational controls and the pending acceptance matrix. Keep source/build/policy/media evidence distinct from observed Valheim behavior in [RUNTIME-TESTS.md](RUNTIME-TESTS.md).
