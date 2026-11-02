# Validation and runtime test record

## 0.8.20 native Cinemachine camera mechanics and output orientation

Built directly from 0.8.19 (`6fbfb9d614201f9877615f0d0019555416cc47b4`). Native Confiner 3D, Spline Dolly, ClearShot and blend mechanics extend the existing readable-footage director. Interior volume evidence remains conservative and separate from scene readiness; helper volumes do not participate in game physics. Official selected Splines 2.0.0 source is checksummed and compiled into the existing isolated Cinemachine DLL, retaining the three-DLL client payload and adding upstream notices.

The user supplied 0.8.19 screenshots showing a vertical reflection of both live video and the generated recovery slate. Mirrored slate lettering identifies a raster/output-orientation problem, not camera roll. The clean program path lacked explicit normalization between GPU readback row order and rawvideo input. Automatic calibration/normalization now applies to the common live/hold/slate path; automatic mode ignores the preserved legacy manual flip setting. Actual Windows graphics-backend confirmation is pending.

Local validation: **467/467 core cases**, **12/12 native spline-math cases** using the real compiled Cinemachine/Splines and Unity references, and **95/95 Linux streaming checks** pass. The streaming checks decode asymmetric top/bottom markers through actual RTMP, including an old manual flip value with automatic mode enabled. Core, client and server compile against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references; the client uses Cinemachine 3.1.7 with selected Splines 2.0.0 source. Local compiler checks use warnings as errors; CI performs normal builds, the core/native-math suites, existing real-assembly audits and Windows/Linux streaming checks. Use the completed workflow associated with the delivered artifact as its build evidence. No Unity/Valheim scene, camera-component, physics or graphics-backend session has been run here. The client changes require no server update; observer admission remains protocol 3. No live installation, server restart or public release is performed.

Runtime priorities: verify upright live/held/slate video and local-console separation; enter/leave enclosed rooms and doorways; remove structural pieces and change worlds; run scenic paths through hold/resume and scale changes; compare native selection and fallback without candidate churn; exercise interrupted blends; check dense-scene CPU/GPU cost and long-running resource cleanup. See [camera-director matrix](CAMERA-DIRECTOR.md#runtime-validation).

## 0.8.19 predictive production direction and clean output

Built directly from 0.8.18 (`4ecea7d7fd373366775cfc93d43f8a9a30ff13e5`). Bounded future visibility comparison extends the existing active-camera movement preview to currently readable alternatives. Activity sequences retain presentation-qualified beats, while explicit wide/full/qualified medium envelopes and persistent action geography distinguish shot purpose from arbitrary camera changes. A clean program path copies the final post-effects camera texture before operator overlays/console UI, using a dedicated recovery card whenever that output is unavailable. Optional low-frequency rendered-picture measurements add conservative exposure/contrast evidence alongside geometry.

The changes are client-side. Observer admission remains protocol 3, discovery remains protocol 2 and optional context/activity/events exchanges retain their versions. Existing compatible servers continue to work; 0.8.18 server events remain optional, while global-idle wildlife eligibility still needs the 0.8.17-or-later activity feed. Ordinary players require no mod. No live installation, server restart or public release is performed.

Local validation: **433/433 core cases pass**, including 82 added cases for future uncertainty/expiry, sequence interruption and actual-presentation credit, persistent axis changes, intentional actor cropping and companion envelopes, clean program generation/readback freshness, and conservative image measurements. Core and client compile against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. Local validation uses the SDK compiler; the CI workflow repeats the core suite, performs normal client/server builds and real-assembly audits, and runs Windows/Linux RTMP checks. Use the completed workflow for the delivered artifact as its build evidence. **No Unity/Valheim visual or multiplayer session has been run here.** The user's positive 0.8.18 feedback does not establish these new behaviors.

Run the expanded [camera-director matrix](CAMERA-DIRECTOR.md#runtime-validation). Highest-priority cases are sustained corridor/forest travel and reversals, medium-to-full transitions during movement/combat, scene/group handoffs, constrained sequence fallbacks, local console/overlay versus RTMP separation, live/hold/slate orientation on the active graphics backend, resize/reset during asynchronous reads, and night/fog/low-contrast scenes. Compare the same routes with `broadcast camera prediction|sequences|scales|picture on|off`; inspect actual footage as well as diagnostics. Future view quality remains an estimate, foliage bounds remain approximate, and image regions are not semantic subject segmentation. World-space overlays already rendered into the camera are outside clean screen-overlay separation.

Install all three client DLLs and the included `ThirdParty` notices together after closing the broadcaster, preserving configuration. A server update is not needed for these changes. Visual quality, GPU/CPU cost, video orientation, audio/video continuity and long-running behavior remain runtime validation items.


## 0.8.18 readable, scene-aware direction

Built directly from 0.8.17 (`94fb4a1cbaa5aad0b4898bc8bdfef6876f7b3945`). The source audit found actor usability ignored foreground obstruction, selection retained the normal score margin even for unreadable shots, and the finite initial viewpoints had no expanded recovery search. The shared picture-health contract now covers selection, final presentation, motion and transitions. Width-distributed primary samples and foreground measurements feed bounded recovery candidates; poor image quality remains separate from physical readiness and travel authorization.

Matching Valheim assembly inspection confirmed `Character.InInterior` tests the dungeon layer, not constructed houses. A separate structural survey adds inferred room/shelter/opening constraints with persistence, while keeping dungeon/loaded-geometry checks unchanged. Shot roles select purpose and full-body scale; locomotion-aware response preserves observed vertical movement and uses verified replicated swimming or actual ship support. Contextual transitions and validated cooperative-primary handoffs retain appropriate continuity. Generic player actions beside passive enemies do not establish combat.

Ordinary and autonomous player scheduling now use qualified activity, bounded encounter retention and fair coverage. A failed-view budget permits another directly reachable loaded player without quality-triggered travel. Optional server `EventRequest1` / `EventReply1` adds timestamped passive activity evidence and falls back locally when unavailable. Admission remains protocol 3; discovery remains 2; context and the existing idle activity exchange remain 1. The new server is optional for local camera/direction improvements and required only for its richer remote event feed. The 0.8.17 server requirement for global-idle wildlife eligibility is retained. Ordinary players remain unmodded; simulation ownership, spawning, world time and stream transport are unchanged.

Local validation: **351/351 core cases pass**, including 87 added cases for picture health and recovery, rejected shot proposals, environment/locomotion evidence, shot intent/transitions, persistent group handoffs, optional event packets and activity scheduling. Core, client and server compile with warnings treated as errors against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. Local validation uses the SDK compiler because this environment cannot run normal MSBuild process metadata queries. The CI workflow repeats the core suite, performs normal client/server builds and real-assembly audits, and runs Windows/Linux RTMP checks; use the completed workflow for the delivered artifact as its build evidence. **No Unity/Valheim runtime session has been run here.** Visual quality, dense-scene performance, actual building/doorway classification, multiplayer groups and runtime optional-feed compatibility remain pending.

Use the expanded [camera-director runtime matrix](CAMERA-DIRECTOR.md#runtime-validation). Priorities are prolonged versus transient foliage obstruction; all-poor candidates and bounded local fallback; constructed house/canopy/dungeon distinction; vertical/swimming/boat motion; purposeful scale with full-body visibility; cooperative handoffs and discontinuity resets; encounter dwell/fairness/manual controls; older/updated/disabled event servers; and the existing upright live/held/RTMP transitions. Capture short repeatable clips and diagnostics for comparison rather than treating quality scores as visual proof.

Client payload: replace plugin/core/Cinemachine DLLs and included ThirdParty notices together after closing the broadcaster, preserving configuration. Optional server payload: replace the server/core pair together during the operator's normal restart. No live installation, server restart or public release is performed by this work.

## 0.8.17 observed-idle wildlife cutaways

Built directly from 0.8.16 (`04e1361a6aaae3e44b60e3ba65c963362ec22b64`). The new policy permits a bounded nearby creature shot only after complete fresh server evidence establishes that all connected ordinary players have been observed idle for at least 60 seconds. Local action can veto that evidence. Existing creature selection, usable-footage dwell and cooldowns apply; activity, stale/unknown evidence or subject loss resumes player coverage. Another cutaway requires at least 60 seconds of rearm. Manual hold, self-follow, probe and route operations suppress the feature.

A separate optional `ActivityRequest1` / `ActivityReply1` exchange observes existing replicated movement/actions, station/crafting, block/emote and health evidence. It does not establish raw-input inactivity or quiet inventory/menu use. Admission remains protocol 3, discovery 2 and optional camera context 1. Older servers preserve player coverage and explicitly disable cutaways without activity support. Empty-server coverage, spawning, simulation ownership, world time and travel permissions are unchanged.

Local validation: **264/264 core cases pass**, including 37 new cases for idle evidence, optional activity packets/freshness, cutaway eligibility/return and suspended acquisition timing. Core, client and server compile with warnings treated as errors against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. Local validation uses the SDK compiler; CI additionally runs normal builds, real-assembly audits and Windows/Linux RTMP tests. **No Unity/Valheim runtime session has been run here.** The [camera-director matrix](CAMERA-DIRECTOR.md#runtime-validation) adds all-player 60-second eligibility, a distant active player, action/roster/staleness interruption, rearm, unsupported-server fallback and no-creature/suppression cases. Preserve the shared-camera and player-follow regressions below; these runtime procedures are not claimed passes.

For this feature, install the 0.8.17 client plugin/core/Cinemachine DLLs with notices, and the dedicated server plugin/core pair, preserving configuration. Ordinary players need no mod. No live installation or server restart is performed.

## 0.8.16 shared player, creature and scenic direction

Built directly from 0.8.15 (`ca43f2beca6370b9ac95034bc2e91a598f822f15`). Source inspection confirmed the user's report: Cinemachine was selected only for player targets. Scenic/creature coverage still used a fixed offset, legacy position smoothing and immediate centered aim; cinematic controls had no effect there. Idle dwell advanced without usable-image proof, and ordinary coverage/path resets discarded displayed-pose continuity.

The shared `CinemachineShotRig` now accepts typed player, creature and environmental inputs. Player observation, cooperative framing and independent trajectories are retained. Creature observation supplies real dimensions and filtered read-only motion; the current creature profile frames one primary actor, not an inferred herd. Scenic plans provide independent camera/aim paths in five bounded non-looping styles, with ranked loaded-terrain candidates and shown-place/style memory. Landscape quality evaluates the actual presented quaternion and lens without applying humanoid body/headroom assumptions. Ordinary handoffs use the displayed pose; world/travel/probe discontinuities retain explicit resets.

Idle pacing and history require recent live compositor output matched to mode, revision and plan identity. A failed creature view has a separate bounded fallback/cooldown; persistently unusable scenery can attempt an eligible creature without consuming phantom scenic dwell. Replacement scenic search can retain a still-valid developed view. Shared procedural/fixed, dynamic/steady, composition, motion and damping controls apply to all three production modes. The existing `Camera.ProceduralPlayers` serialized key now controls that shared pipeline and is retained for configuration compatibility. Group framing remains a player capability. Observer protocol 3, optional server context, simulation isolation and server behavior are unchanged; no new server deployment is required.

Local validation: **227/227 core cases pass**, including thirteen new cases for environmental path development, bounded geometry, idle presentation clocks/history, failed-view fallback and quality recovery while editorial pacing is paused. Core, client and server sources compile with warnings treated as errors against real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. Local validation uses the SDK compiler; CI additionally runs normal project builds, real-assembly structural checks and Windows/Linux RTMP tests. **No Unity/Valheim visual session has been run here.** Core tests and compilation do not establish scenic quality, creature framing, smooth cross-mode handoffs or runtime cost. Follow the expanded [camera-director runtime matrix](CAMERA-DIRECTOR.md#runtime-validation): broadcaster-only scenic variety, forest/structure clearance, stationary and moving creatures of different sizes, control changes after developed movement, mode/plan transitions, held-frame pacing and probe/travel separation. Preserve the 0.8.15 player movement and group regression cases.

Install all three client DLLs and included third-party notices together after closing the broadcaster, preserving configuration. The existing protocol-3 server can remain installed. No live installation or public release is performed.

## 0.8.15 independent player-camera trajectories

Built directly from 0.8.14 (`0cf7f345d63df68cc3370d3756e77221a689fadb`). The user reports that camera translation mirrors a moving target despite noticeable independent movement while idle. Source and actual game-assembly inspection found active Cinemachine damping, valid unscaled delta time and no later native pose overwrite. The issue is architectural: the follow point is rebuilt from the player every frame, with 0.2 seconds of velocity prediction. Cinemachine's default 0.8-second position damping means approximately 99% convergence in that period (a roughly 0.174-second time constant), so the prediction nearly cancels its steady-motion lag. Outgoing blends additionally copy the target's exact displacement. The aim dead zone affects rotation and does not prevent that body translation. These are source-supported causes, not a captured runtime measurement.

The new trajectory policy gives each candidate a persistent world-space anchor and velocity while retaining the live subject/group as its separate aim target. Shot/activity intent, bounded acceleration and continuous framing/range correction determine tracking. It removes the positional prediction and raw target-displacement blend path, preserves 0.8.14 placement/group framing, and keeps aesthetic quality separate from physical readiness and observer travel permission. Recovery from a held or failed presentation blends from the last displayed pose even when the same candidate returns; rejected selected trajectories synchronize to their last presented anchor. Observer protocol 3, optional server context and server behavior are unchanged; no new server deployment is needed.

Local validation: **214/214 core cases pass**, including ten new trajectory cases covering movement onset, three-minute sustained travel across shot families, stopping/reversal, activity-area stability, cooperative motion, group/shot continuity, variable/standby update cadence, constraint recovery and pressure settling. Core, client and server sources compile with warnings treated as errors against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. Local validation uses the SDK compiler; the CI workflow additionally runs normal project builds, real-assembly audits and Windows/Linux RTMP checks. **No Unity/Valheim visual session has been run here.** Core policy tests and compilation cannot establish final camera feel, multiplayer behavior or runtime cost. Follow the updated [camera-director runtime matrix](CAMERA-DIRECTOR.md#runtime-validation), especially another player's sustained walking/sprinting, stopping/reversal, long travel across zone boundaries, constrained spaces, group joins/splits and shot transitions. Compare the local display with RTMP and record `broadcast camera status` while the symptom occurs.

Install all three client DLLs and the included third-party notices together after closing the broadcaster, preserving configuration. Existing protocol-3 servers remain compatible. No live installation or public release is performed.

## 0.8.14 deliberate composition

Built directly from 0.8.13 (`180f9b2`). The user reported that a single framed player remains centered. Source review confirmed the centered screen preference, common follow/look-at point and near-zero lead offset; a dead zone permits drift but does not choose off-center composition. Review also found competing group/aim adjustments, unconditional final recentering, a motion reset on dynamic-to-steady changes and pacing based on physical validity without sufficient readability. These are source findings; no captured runtime trace was available to attribute every perceived camera movement.

This revision introduces persistent screen-placement intent, evidence-qualified movement/facing/activity space, body-envelope framing and headroom evaluation. It keeps physical readiness, observer authorization and travel separate from composition quality. `Camera.DeliberateComposition` defaults true; `broadcast camera composition on|off` compares deliberate versus centered preference without disabling damping or restarting the observer. Existing protocol-3 servers remain compatible, including the optional 0.8.13 context service. No server behavior or protocol is changed.

Local validation: **204/204 core cases pass** (14 new composition-policy cases and seven additional full-body projection cases). Combined client and server sources compile with warnings treated as errors against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and Cinemachine 3.1.7. The independent source review identified and corrected a group-lens initialization defect when companions join a previously solo shot. Local compilation used the SDK compiler; CI performs normal project builds, real-assembly audits and Windows/Linux RTMP checks. **No Unity/Valheim visual session has been run here.** Updated in-game cases remain pending in [CAMERA-DIRECTOR.md](CAMERA-DIRECTOR.md#runtime-validation). In particular, compare requested/actual viewport placement while stationary, walking, sprinting, reversing and entering constrained space; exercise group joins/splits, activity-point changes, dynamic/steady switches after developed movement, framing recovery and held/live RTMP transitions. Subject visibility and camera continuity take priority over thirds placement.

## 0.8.13 cooperative cinematic director

Built directly from 0.8.12 (`6dd447f`), retaining observer admission protocol 3, discovery protocol 2, the existing camera compositor and continuous-follow acquisition geometry. The optional context extension has its own protocol 1 and falls back locally with an older compatible server; it does not change admission or require mods on ordinary players.

The player camera now uses Cinemachine OrbitalFollow, RotationComposer dead zones and independent position/aim damping, TargetGroup/GroupFraming and composition-aware blends. A bounded director adds activity-sensitive movement, shot/movement history, usable-footage pacing, predictive foliage/physical screening and settling after corrections. Group membership is persistent and primary-anchored; companions earn scheduling credit only after sustained readable live footage. Repeated camera corrections reduce decorative movement. Spacious interiors allow slower variety; restricted rooms retain the readable view. Optional authenticated server context reports replicated state/heartbeat ages and recent observed health loss, with bounded identity/world/freshness validation. Activity classifications remain evidence-qualified.

Local validation: **183/183 core cases pass**; combined client and server sources compile with warnings as errors against the matching real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references and isolated Cinemachine 3.1.7 runtime. New cases cover constrained versus spacious interiors, correction settling, deliberate variety, motion bounds/strength, cooperative membership, readable-coverage fairness, off-center projection and optional-context freshness/identity/packet limits. Local compilation used the SDK compiler because this environment's MSBuild process metadata lookup fails; the existing CI pipeline runs normal project builds and Windows/Linux RTMP checks.

No Unity/Valheim runtime session was executed for 0.8.13. The user's positive procedural-visibility feedback concerns 0.8.12 and does not validate new damping, group framing, visual transitions or runtime cost. Follow the [camera-director runtime matrix](CAMERA-DIRECTOR.md#runtime-validation), including sustained walking/sprinting, open idle, constrained interiors/forests, group merge/split, combat/resource hints, held-frame orientation and old-server fallback. These cases remain **pending**.

Client package: replace the broadcaster's three DLLs together with the included third-party notices, preserving configuration. Server package: optional for richer context; update its server/core pair together during the operator's normal restart. Existing protocol-3 servers remain usable without this update. No live installation or public release is performed.

## 0.8.12 Cinemachine player framing

Built directly from 0.8.11 (`9495313`), retaining protocol 3, the camera compositor and continuous-follow readiness. The client now packages a selected runtime built from official Cinemachine 3.1.7 sources. No server/protocol migration or live installation is performed.

Ten virtual cameras use Cinemachine Follow, HardLookAt and Deoccluder. A custom Cinemachine extension evaluates player head/torso/lower-body visibility and foreground vegetation. Selected views update every frame; alternatives are evaluated at 5 Hz or after significant subject movement. A pure selection policy requires persistent improvement and retains healthy shots to avoid oscillation. Filtered movement prediction is capped at 3 m; interiors use tighter candidate offsets. Actual interpolated camera movement, support geometry, near-plane clearance and carrier reach are validated before presentation. Foliage affects picture quality, not scene readiness or travel permission.

The upstream runtime is built in an isolated assembly, without adding a Brain or another rendering camera. Verified source substitutions cover net48 syntax, late-loaded update-manager initialization, and idempotent cleanup of Cinemachine's hidden noninteracting scratch collider. The source archive hash, upstream revision, exact patches and license notices are included with the client package. See [the dependency build record](../third_party/Cinemachine/README.md).

Validation performed locally: **145/145 core cases pass**. Client, core, server and Cinemachine compile against the existing real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references with warnings as errors. Core policy tests include stable ties, persistent improvement, dwell, immediate physical rejection, low foliage scores, candidate reset and stale evidence. The candidate movement calculation is shared with acquisition/travel and has a large-offset regression fixture. The existing CI pipeline performs normal project builds, core tests, metadata audits and Windows/Linux RTMP loopback checks. No Unity/Valheim session was executed here.

**Client installation changes:** close the broadcaster, then copy all contents of the artifact's `ValheimBroadcaster/` folder into its existing plugin directory. Keep `ValheimBroadcaster.dll`, `ValheimBroadcaster.Core.dll` and **`ValheimBroadcaster.Cinemachine.dll`** together with the included `ThirdParty/` notices. Preserve existing configuration. Do not install these client files on the dedicated server; the deployed protocol-3 server remains compatible.

Runtime cases **not run**:

1. In an ordinary disposable local world, use `broadcast self` or F10, then walk/sprint through trees, stop, turn, and pass near buildings. Compare `broadcast camera fixed` with `broadcast camera procedural`; the mode persists through `Camera.ProceduralPlayers`. The procedural camera should prefer readable alternative views without constantly swapping sides. Inspect `broadcast camera status` for candidate, quality, obstruction and transition information.
2. Follow another player in the authorized read-only observer. Sustain movement across terrain zones and stop beneath a canopy. Reframing should retain the same player and should not start travel merely because foliage obscures them. Verify other-client invisibility and unchanged observer behavior after installing the new client.
3. Exercise wide/high camera settings and briefly unavailable camera geometry near a stationary target. Successful camera evidence must not be refreshed by failed attempts; expiry must not force the old fixed-angle displacement calculation or create a self-blocking acquisition loop.
4. Enter a dungeon, walk through doors and narrow passages, and exit. Physical camera/near-plane collisions must not be hidden by dwell. Transitions must remain outside solids or cut to a validated endpoint. Actual missing scene geometry may still hold/slate.
5. Teleport/respawn, cycle players, return/park/resume, switch camera mode, exit/rejoin and stop broadcasting. Old candidate state, motion prediction and helper objects must not affect the next scene or normal restored camera. Observe both local rendering and RTMP, including `broadcast framehold test`.

Known limits: renderer bounds approximate vegetation and do not measure leaf transparency; unfamiliar custom vegetation may be missed. Dense-scene cost and actual composition quality need GPU/game profiling and tuning. Pure policy tests and successful compilation do not prove Unity component lifecycle, visual quality or multiplayer runtime behavior.

## 0.8.11 camera-image composition

Built directly from 0.8.10 (`80c7c04`), retaining protocol-3 opt-in and continuous player following. The user reports a brief upside-down flash just before recovery/travel. The transition audit found explicit travel already forces a slate; the preceding bounded Live → Hold interval still switched from the camera image to GUI screenshot playback. The extra screenshot copy in 0.8.9 did not establish consistent orientation across those routes. This is a source-supported hypothesis, not a captured GPU diagnosis.

The active game camera now receives an appended `OnRenderImage` component. A ready live camera source is copied to one matching render texture; both Live and Hold draw that same texture through the camera destination. No held image is drawn through GUI or captured after GUI. A same-frame camera readiness stamp is required; missing compositor callbacks fall back to the opaque slate. Hard transitions invalidate prior footage, including when Watching resumes before the old hold would expire. Source-size/format changes, lost textures and camera lifetime changes also invalidate it. Every compositor path writes its destination and preserves the sRGB write flag. The final camera output remains bound for Unity's image-effect chain.

Local validation: **132/132 core cases pass**, including quick travel/recovery without resurrection of a pre-travel frame. Client and server compile against the existing real Valheim 1.0.21 / Unity 6000 / BepInEx 5.4.23.2 / Jötunn 2.30.2 references with warnings as errors. Normal project builds and the existing streaming/metadata checks run in CI. No Valheim/GPU session is available here; the visual cases below are **not run**.

1. With a live broadcast picture, run `broadcast framehold test`, close the console, and wait two seconds. The scene should hold for half a second, upright and with unchanged color, then resume. Repeat on scenic footage or `broadcast self`; no second player is required.
2. Repeat while watching the local display and RTMP receiver. Keep the existing `Streaming.FlipVertical` setting; this change does not alter encoder orientation.
3. Observe an actual readiness gap followed by travel/recovery. Brief gaps may hold for less than 0.75 seconds; travel must show the slate and must not resurrect the old frame on arrival. Confirm that new live footage resumes without a flash.
4. Exercise resize/fullscreen, off/on, observer return/resume and world exit/rejoin. No stale held scene should survive those boundaries. Confirm the ordinary camera restores on shutdown. Verify on the user's graphics backend and active post-processing configuration.

Replace both client DLLs together after closing the broadcaster. Preserve configuration and the deployed protocol-3 server. The server artifact's version label changes only; no server upgrade, admission change or live installation is required.

API references: [OnRenderImage ordering and pipeline support](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnRenderImage.html), [Graphics.Blit state and color handling](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.Blit.html). Actual game inspection confirms built-in `OnRenderImage` effects on the game camera; this does not validate third-party effects appended afterward.

## 0.8.10 continuous player following

Built from 0.8.9 (`52e3435`), preserving the corrected protocol-3 admission, movement, scenery and held-image changes. Actual Valheim 1.0.21 inspection established that `ZNetScene.IsAreaReady` waits for the queried zone plus instances for known network objects in a 3×3 zone neighbourhood. Using it as a per-frame presentation veto causes unnecessary freezes when otherwise usable footage exists.

Full readiness now establishes each player shot. A recently presented shot then uses the exact live character/world binding, bounded incremental target movement and existing local geometry at the target, camera and carrier. Outdoors this requires active generated terrain meshes/collider; interiors require nearby static geometry at interior height. Interior camera offsets are clipped before checking camera support, so an offset beyond a small room is not automatically treated as an unloaded shot. Character changes, death, scene/world changes, expired footage and substantial 3D relocation return to acquisition. Travel/return/probe/scenic/creature readiness and the server protocol remain unchanged.

Local validation: **131/131 core cases pass**, including roughly 3.9 km of sustained walking/sprinting, subsequent stationary following, same-binding selection, reset/respawn identity, horizontal/vertical teleport rejection and expiry that readiness probes cannot extend. Client compilation against actual matching game references passes with warnings as errors. Normal MSBuild/test execution remains assigned to the existing CI pipeline because the local CLI cannot inspect process start times. No new protocol audit framework was added.

Runtime retest remains pending:

- Follow a stationary player while nearby objects appear, then follow sustained walking/sprinting across terrain-zone boundaries. Background object queues should not freeze established footage; diagnostics should report continuous follow with local geometry ready.
- Enter and move through a dungeon. Entry still requires acquisition; once established, room-object loading outside the shot should not veto it. Verify the actual clipped camera retains valid geometry and ordinary objects/characters under it do not invalidate floor support.
- Exercise distant portal travel, death/respawn, target cycling, off/on and return/rejoin. These must establish a new shot; unavailable actors or geometry must still hold/slate rather than allow unsupported carrier movement.

Install both client DLLs together after closing the broadcaster. Keep the deployed protocol-3 server and existing configuration. No live installation or in-game validation was performed here.

## 0.8.9 held-frame presentation

The supplied session log loads client **0.8.7**, protocol 3, with Valheim 1.0.21, Unity 6000.0.75, BepInEx 5.4.23.6 and Jötunn 2.30.2. It contains no camera/graphics exceptions, one successful NVENC/AAC startup and no RTMP failure/retry. Five status samples report waiting for a valid shot; thirteen report brief interruptions, mostly target-area loading. Six player travels are distant (approximately 754–2405 metres). These observations do not establish which individual frame inverted.

The strongest source-level hypothesis is held-frame presentation during those loading gaps: 0.8.7/0.8.8 draw a raw screenshot render texture directly through GUI, while streaming normalizes it through `Graphics.Blit`. The actual game's `GameCamera.LateUpdate` calls `UpdateCamera`, then `UpdateListner`, then returns; camera shake is inside `UpdateCamera`, before our postfix. No later native rotation overwrite was found in that path.

0.8.9 routes held and streamed screenshots through the same GPU normalization helper, restores `RenderTexture.active` and `GL.sRGBWrite`, and requires a Live repaint in the same frame before refreshing the held image. It does not change camera geometry, admission, observer protocol, or the encoder's `FlipVertical` setting. The client compiles against real references with warnings as errors; the existing CI pipeline remains the package producer. No GPU/game runtime is available here, so this is not a confirmed runtime fix.

Primary API references: [Unity render-texture conventions](https://docs.unity.cn/Manual/SL-PlatformDifferences.html), [Graphics.Blit state](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.Blit.html), and [end-of-frame capture ordering](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/WaitForEndOfFrame.html).

Retest continuous walking/sprinting and dungeon/terrain loading while watching both the local client and RTMP receiver. Brief held frames should retain the live image's orientation and color, then return to live motion; longer gaps should still expire to the recovery slate. Check resize/fullscreen changes and broadcast off/on. Keep the existing `Streaming.FlipVertical` setting: a global output flip is not a correction for intermittent held-frame inversion. If the symptom recurs, `broadcast diagnostics` now includes the last presentation mode and graphics backend alongside held/slate counts. All these runtime cases remain pending.

## 0.8.8 scenic composition

Continues from corrected protocol-3 client 0.8.7 (`4113d7f`). The reported failure is a scenic camera buried in foreground leaf planes. Selection now evaluates alternate headings/heights and five points along the intended path, rejects solid collisions and heavily obscured foreground foliage, and retires a persistently invalid shot after 1.5 seconds. Bounds screening is conservative; it does not measure leaf transparency or promise that every accepted shot is attractive. `broadcast diagnostics` includes the scenic rejection reason, search state and rejected-view count for the current region.

Both plugins compile against the pinned real game references with warnings as errors, and **126/126 core cases pass**, including replacement-shot dwell and immediate player preemption. The normal local `dotnet run` command remains blocked by this container's process-introspection failure; these local results use direct Roslyn compilation, with the normal project build left to the existing CI workflow. No game runtime is available here. Server admission, protocol and streaming code are unchanged.

Retest with the new client plugin and Core DLL together, keeping the deployed protocol-3 server and existing configuration:

- Let solo scenic coverage run in a forest canopy and an open meadow through multiple shot changes. A camera inside vegetation or facing a close wall of leaves should be rejected, while open landscapes should still produce usable shots.
- Check that a newly obstructed path recovers to a different composition rather than repeatedly collapsing into the same branch. If no local shot passes, the existing unready-region timeout should initiate autonomous region travel when available.
- Exercise hold/resume and allow a real player to join; player acquisition should still preempt scenery. Check `broadcast diagnostics` if recovery slates persist, and note scene/region plus rejection reason.

These in-game cases remain pending; no live installation was performed.

## 0.8.3 optional observer admission — 2026-10-31

The allowlist now grants permission to request observer mode, rather than forcing a Steam account into that role. The client sends protocol-3 intent before native admission; no request means ordinary gameplay. The entire accepted observer connection remains read-only until disconnect.

- **119/119 core cases passed**, including eight new cases for default ordinary admission, pre-admission intent, rejected late promotion, protocol mismatch, idempotence, authorization/lease separation, timeout/revocation and reconnecting as an ordinary player.
- **28/28 real-assembly audits passed on each of the actual Linux server and Windows client assemblies**, including `SendPeerInfo` signature, native RPC dispatch, authenticated readiness and reliable FIFO Steam transport. These inspect metadata; they do not execute multiplayer joins.
- Client and server **0.8.3** compiled with zero warnings/errors and warnings treated as errors, against real installed Valheim 1.0.21 references, BepInEx **5.4.23.6** and Jötunn **2.30.2**. The existing CI baseline remains BepInEx 5.4.23.2. Both artifacts contain only their role's plugin and matching Core DLL.
- An isolated Linux Docker server with networking disabled loaded **22 plugins, zero skipped and zero failed**, including all 18 existing ServersideQoL modules, their framework, YAML detector, Jötunn and Broadcaster. It logged `Observer server 0.8.3 ready`; no plugin exceptions, Harmony failures, unavailable-server messages or discovery-suspension errors were observed. Discord Connector and production credentials/worlds were excluded. The test container was stopped afterward. Offline Steam connectivity errors are expected and do not constitute a successful network join.
- The existing production server was not modified or restarted. The original 0.8.0 deployment was halted before any production files were installed.

**Still required:** a vanilla join using an allowlisted identity lasting beyond the former 15-second deadline; the same account joining with the mod installed but observer mode off; an explicit observer join with matching 0.8.3 endpoints; unauthorized/mismatched/late requests; and the visibility, interactions, sleep and ownership matrix in [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md). Startup and policy tests do not prove those runtime behaviors.

## 0.8.7 corrected protocol-3 build

Based directly on main commit `0348013737799f8131b589bddc1edd84319ca15b`, which contains the deployed protocol-3 opt-in implementation (`8625cd3`, labelled 0.8.4). Carries the movement/reacquisition fix and rejected-world client cleanup onto that baseline. The protocol definition and server admission implementation are preserved; server source changes only its version label/startup banner. No account-based automatic reservation or 0.8.6 reservation policy is included.

The `fix-player-reacquisition` builds labelled 0.8.4 (`b0de64e`), 0.8.5 (`a2af65f`) and 0.8.6 (`31f7c53`) were incorrectly based on protocol 2. Do not deploy those builds or their server companion to replace a protocol-3 installation. A protocol-2 client omits the explicit `Observer.Request3` message and is incompatible with the deployed opt-in server. The rejected-world spawn storm was a separate cleanup defect.

Local results: 125/125 existing/carried core cases pass, and both plugins compile against the real reference assemblies with warnings treated as errors. The existing CI pipeline builds and packages the commit. No extra protocol/artifact check framework was added. In-game authorization, continuous following and rejection cleanup still require runtime validation.

Installation: fully exit the broadcaster client and replace its plugin plus Core DLL together with the 0.8.7 client artifact. Keep the verified protocol-3 server 0.8.4 and existing configuration. If the protocol-2 0.8.6 server was installed, restore the prior protocol-3 pair or install the 0.8.7 server plugin/Core pair after a normal stop. Ordinary players need no mods. Client startup reports protocol 3, and an opted-in join logs its request before native admission. Test joining first, then sustained walking/sprinting and a distant portal. A rejected session should return to the menu without repeated local character allocation.

## Evidence available

- The **0.7.0** director's **55 automated cases passed**, including observer exclusion, identity ordering, manual selection, wrapping, dwell, hold, target loss, readiness timeout, lone-target behavior, invalid time inputs, isolated self-target selection, return to normal observer exclusion, idle alternation/fallback, player preemption, pending-player presence, scene recovery, stable creature rotation, hold/reset, bounded acquisition, and a 24-hour simulated idle schedule. This simulated policy test is not a Valheim soak test.
- The core compiled against actual .NET Standard 2.0 reference assemblies with warnings treated as errors. Its executable test harness ran on .NET 8 against that compiled core.
- The development container's ordinary `dotnet`/MSBuild path fails during process introspection (`Process.StartTime` / `ProcessName`). Direct invocation of the SDK's Roslyn compiler was used for the observed core checks. This is an environment limitation, not a replacement for checking the normal project build in CI or on a development machine.
- The plugin compiled with warnings treated as errors using Roslyn 4.11 from .NET SDK **8.0.427**, actual Microsoft .NET Framework 4.8 reference assemblies (**1.0.3**), Jötunn **2.30.2**, BepInEx **5.4.23.2**, and real Valheim references. This checked the source against those assemblies; it did not establish that the normal MSBuild graph executes successfully in this container.
- Real game references were obtained from the official Steam dedicated-server app **896660**, public Windows depot **896662**, manifest **171106503873955013** dated **2026-10-27**. Assembly metadata inspection found `Version` initializes `GameVersion(1, 0, 21)`, identifying this reference build as **Valheim 1.0.21**. Only reference DLLs were downloaded; no server was launched. These server references do not prove that a separately installed client matches.
- Jötunn's NuGet package identifies upstream commit `671c486ac8123a9b240eae80639b6c33f63b3884`. No client runtime result is implied by a dependency's availability or compilation success.
- A user ran 0.5.0 and supplied the failed distant-probe result recorded below. The user subsequently reported successful **0.7.0 server-companion authorization, loading, manual travel, return, park and resume** while testing alone. This is user-reported runtime evidence, not a developer-run session or a completed multiplayer-isolation matrix. No second-client visibility/ownership result, resolved flicker result, stream-quality pass or GPU-performance pass has been supplied.

Run the normal core check with:

```sh
dotnet run --project tests/ValheimBroadcaster.Core.Tests -c Release
```

CI checks this core harness. A separate `plugin` job downloads pinned real game and BepInEx references and executes the normal `dotnet build` pipeline with warnings treated as errors. The current workflow uploads separate client/server artifacts containing only the respective plugin and Core DLLs. The dependency archives and game assembly are checksum-verified by `scripts/fetch-build-references.py`. See the [Actions runs](https://github.com/Dillflix/valheim-streamer/actions) for the result at a particular commit. Green jobs do not establish in-game behavior.

## 0.8.0 autonomous milestone — local checks, 2026-10-31

| Check | Observed result |
| --- | --- |
| Core harness | **111/111 passed**, including bounded discovery, fair player acquisition, native-travel deadlines, scenic novelty/backoff, route cancellation, reconnect policy and camera reachability |
| Real-assembly metadata audits | **21/21 passed**, including inspected native reconnect entry points and the existing observer boundary |
| Streaming harness | **66/66 passed** with real software FFmpeg/ffprobe: stereo AAC, decoded flash/tone synchronization (0.030 seconds), refused-receiver cleanup, receiver restart followed by a new publishing session, and a 48-hour simulated outage/retry schedule |
| Client and server compilation | Both compiled against the pinned real game/dependency references with warnings treated as errors |

The policy and outage simulations do not run Valheim or establish an actual multi-day soak. Metadata audits do not execute game code or prove Harmony patches applied. These local checks do not claim a successful CI run for a future commit. **Not run:** 0.8.0 Valheim/Unity capture, physical NVIDIA NVENC, multiplayer visibility/ownership, live automatic acquisition/scenic travel, native reconnect, or unattended runtime acceptance. Follow [AUTONOMOUS-BROADCAST.md](AUTONOMOUS-BROADCAST.md) for those cases. No live installation or public release was performed.

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

## 0.7.0 user-reported server-companion results — 2026-10-31

After the local dedicated-server setup instructions, the user reported: “All checks are working as intended. Authorization, loading, travel, and return/resume.” The follow-up confirmed normal `Watching` status and continuous `Scenic` coverage while the broadcaster was the only client. Treat the following as **user-reported passes** of the 0.7.0 workflow, with exact installed hashes and fresh logs not supplied:

| Workflow | Reported result |
| --- | --- |
| Observer authorization and character/scene loading | Working as intended |
| Explicit observer travel | Working as intended |
| Return, protected parking and resume | Working as intended as part of the requested return/resume checks |
| Solo coverage | `Watching` / `Scenic` observed |

`Scenic` alone is consistent with the current creature eligibility rules and dormant regions; it is not evidence that the server feed, autonomous region selection or creature simulation exists. The user explicitly deferred creature improvements. **Still pending:** visibility and interactions observed from an ordinary client, simulation-ownership handoff, sleep/events/capacity behavior, negative authorization cases, reconnect/soak testing, confirmation that the flicker is resolved, and actual RTMP video/audio quality. The subsequent autonomous milestone needs its own runtime tests; the 0.7.0 report does not validate new code.

## 0.8.1 audio startup regression

A one-second executable-start delay reproduced the screenshot's exact ambiguous audio error using the 0.8.0 encoder code with stereo input. The repaired encoder passed the same real FFmpeg fixture, with decoded audio/video synchronization checked. All 86 local streaming checks passed (66 normal checks plus 20 delayed-start loopback/recovery checks). The delay fixture uses a POSIX exec wrapper and runs on Linux; Windows CI runs the normal 66-check suite. Client compilation against pinned real references passed with warnings as errors. This demonstrates a startup race, but does not prove that every instance of the screenshot error had that cause. Retest actual Valheim audio capture and NVENC on the user's machine.

## 0.8.2 diagnostic build

The user's 0.8.1 retest still fails after the audio socket connects, at 48000 Hz and two channels, without FFmpeg stderr. The startup repair is therefore insufficient for this machine. This build adds frozen failure-time capture-rate, queue, media-age, publishing and audio/video writer measurements; it does not claim a runtime fix. A production-session test deliberately submits twice the nominal sample rate and checks that the recorded failure distinguishes excess capture from an unexplained encoder failure. Existing queue limits and retry behavior are retained. Server protocol is unchanged.

Additional user log: attempts 3, 4 and 6 reached NVENC H.264/AAC publishing; attempts 3 and 4 subsequently tripped the shared 1500 ms writer-lateness limit. Automatic scenic travel transitioned from Travelling back to Watching while publishing. This establishes runtime encoder/mux progress and one scenic arrival, not verified receiver playback or long-run stability. The diagnostic build names the late writer and measures its lag.
