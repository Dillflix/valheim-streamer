# Shared camera director (0.8.19)

Player, creature and scenic coverage use one Cinemachine pipeline for damping, composition, candidate selection, physical validation and continuity. Version 0.8.19 builds directly on 0.8.18 (`4ecea7d`). It adds future-readability ranking, short activity sequences, deliberate wide/full/qualified medium framing, persistent action geography, clean program output and actual-picture feedback. Existing readable-view recovery, room/opening measurements, group continuity and observer limits remain in force. The 0.8.17 wildlife cutaways remain available under their existing global-idle requirements.

Scenery supplies planned world camera and aim paths; creatures supply their actual dimensions and observed movement. Neither is represented as a fake player. The observer's travel/authorization rules remain separate. Probe viewing stays a fixed acquisition measurement, while travel and recovery use the existing held-frame/slate presentation.

Build and test evidence is recorded in [RUNTIME-TESTS.md](RUNTIME-TESTS.md). **The 0.8.19 runtime cases have not been run in Unity/Valheim here.** Pure tests and compilation do not establish composition quality, runtime cost or multiplayer behavior.

## Controls and configuration

Use `-console` when launching Valheim, then F5. The operator console remains local; the clean program feed is taken before overlay UI. Close the console to inspect the local scene unobstructed. The following commands update the saved client configuration; no server restart is needed for camera changes.

| Command | Effect |
| --- | --- |
| `broadcast camera status` | Show scene/activity evidence, shot role, measured environment/locomotion, readability and reason, normal/expanded recovery search, transition and camera settings |
| `broadcast camera dynamic` | Enable timed fresh compositions and deliberate movement when space permits |
| `broadcast camera steady` | Disable that editorial layer; retain procedural visibility selection, damping and group framing |
| `broadcast camera composition on` / `off` | Enable deliberate subject placement or select the centered comparison preference; preserve the same damping and visibility checks |
| `broadcast camera prediction on` / `off` | Enable/disable bounded future-readability ranking without changing present visibility or travel eligibility |
| `broadcast camera sequences on` / `off` | Enable/disable short activity sequences; usable live footage fulfills beats, while hold/steady pause editorial development |
| `broadcast camera scales on` / `off` | Enable wide/full/qualified medium envelopes or use the prior full-body comparison behavior |
| `broadcast camera picture on` / `off` | Enable/disable low-frequency clean-picture exposure/contrast feedback; geometry remains active |
| `broadcast camera groups on` / `off` | Enable/disable player cooperative and opponent group compositions; creature coverage currently frames one primary actor |
| `broadcast camera motion 0` | Stop decorative movement while retaining tracking, damping and dynamic shot selection |
| `broadcast camera motion 1` | Restore default decorative movement strength; accepted range is 0–1.5 |
| `broadcast camera damping 0.8 1` | Set position and aim damping in seconds; each accepts 0.1–3 |
| `broadcast camera procedural` / `fixed` | Select shared procedural or legacy fixed framing for player, creature and scenic coverage |
| `broadcast wildlife on` / `off` / `status` | Enable/disable wildlife cutaways or inspect activity freshness, minimum idle time, rearm time and blocking reason |
| `broadcast diagnostics` | Include observer/discovery/output state, optional context/events freshness, activity selection and wildlife status |

The camera controls apply to all three production modes. `composition off` eases actor or environmental placement toward the centered comparison preference without disabling damping or visibility checks. Actor body framing can still require a corrective offset. `steady` retains the developed decorative offset/path sample rather than snapping back to the origin; necessary actor tracking continues. `motion 0` likewise stops discretionary development while keeping tracking and framing. Normal coverage scheduling can still choose another subject or scenic plan; `broadcast hold` pauses that scheduling rather than freezing all camera mechanics. The probe remains a fixed measurement independent of these cinematic controls.

| `Camera` key | Default | Meaning |
| --- | --- | --- |
| `ProceduralPlayers` | `true` | Use the shared player/creature/scenic pipeline; the old serialized key name is retained for compatibility |
| `DynamicShots` | `true` | Enable activity/space-aware pacing and deliberate movement |
| `DeliberateComposition` | `true` | Choose persistent subject placement, lead room and activity space; false uses the centered comparison preference |
| `GroupFraming` | `true` | Consider persistent player companions and relevant opponents; creature-herd inference is not implemented |
| `PredictiveShots` | `true` | Rank currently readable candidates using bounded geometric forecasts and movement confidence |
| `ShotSequences` | `true` | Develop observed activities through interruptible beats; manual hold and steady mode pause progress |
| `ShotScales` | `true` | Explicit wide/full/qualified medium envelopes with whole-body alternatives |
| `RenderedPictureEvaluation` | `true` | Conservative clean-picture exposure/contrast feedback; no remote image upload or lighting changes |
| `PositionDamping` | `0.8` | Cinemachine damping in seconds for the planned trajectory; increasing it softens position response |
| `AimDamping` | `1` | Seconds; increasing it softens aim response |
| `DeadZone` | `0.12` | Screen fraction allowing subject movement before recentering; range 0–0.3 |
| `ShotMotion` | `1` | Decorative movement strength, range 0–1.5 |

Cinemachine's damping parameter is a settling duration, not a pure time delay: `PositionDamping = 0.8` gives approximately 99% convergence in 0.8 seconds for a stationary target. Trajectory planning now determines how camera translation responds to player motion; increasing damping alone is not the independence mechanism.

`Distance`, `Height`, `Azimuth` and `FieldOfView` remain composition preferences. Procedural views can adapt them within limits. `Smoothing` governs legacy fixed response; use the two damping settings for all shared procedural modes. `Director.DwellSeconds` controls player rotation, independently of a composition's duration.

## Readable footage and bounded recovery

Physical eligibility and picture health are distinct. A candidate must first have valid geometry, camera clearance and carrier reach. Its actual solved orientation/lens then determines whether the actor or environmental subject is readable. Actor health includes body framing and scale, solid/foliage visibility, foreground obstruction and survey completeness; a few clear centerline rays cannot by themselves certify a usable actor shot. The same health result governs candidate retention, deliberate movement, transitions and readable-coverage credit.

Healthy views retain ordinary persistence and continuity preferences. When the current view is unreadable, a sustained readable replacement can win without the old 0.15 score-improvement requirement. When every view is poor, a smaller sustained improvement can still advance recovery, with hysteresis to avoid alternating equally bad angles. Physically supported poor footage remains available during the search; foliage alone never reports an unloaded scene or requests observer travel.

The actor rig starts with ten ordinary viewpoints. After 0.6 seconds of unreadable footage it enables eight compact, lower/eye-level alternatives, with creature distances scaled to actual body size. Standby recovery directions advance on a bounded two-second cadence; the live candidate's search direction stays fixed. Four seconds of readable footage disables unused recovery candidates without removing the selected shot. Search can request a throttled foliage refresh. Compact views retain body framing and can temporarily widen the lens within limits when needed. A readable source is retained over an unreadable destination; interpolation that loses visibility can cut to a validated readable endpoint.

The foliage estimate remains an approximation: renderer bounds are not leaf silhouettes or alpha coverage, unfamiliar vegetation can be missed, and dense truncated surveys cannot certify completeness. Expanded search is bounded; it cannot guarantee a clear angle in every scene. Inspect the readability reason and search status when reporting prolonged obscuration.

## Measured rooms, shelter and openings

Valheim's `Character.InInterior` distinguishes the elevated dungeon layer; it does **not** identify a player-built house. That flag remains unchanged for loading/readiness. A separate local survey measures constructed overhead `Piece` geometry, surrounding walls and supported openings. Tree canopies are excluded from roof evidence. The inferred states are open, sheltered, room, doorway, dungeon or unknown; incomplete measurements remain unknown.

Surveys run at bounded cadence with fixed nonallocating hit buffers. Room/open transitions require persistent evidence, and doorway restraint continues briefly after leaving an opening. A measured ceiling caps optional rise; restricted rooms and doorways favor compact framing, quieter movement and shorter validated handoffs. These are local geometric hints, not named rooms, building ownership, a navigable room graph or proof of complete network delivery. Large halls can retain measured camera freedom. Players and creatures use this survey; scenic plans retain their own validated world paths.

## Purpose, scale and locomotion

Shot purpose is separate from decorative movement. The director first establishes a readable scene, then uses persistent, sufficiently confident evidence to choose a role. Nonurgent changes require usable footage; combat and doorway constraints can respond promptly. Roles ease their distance, height, lens, group emphasis and movement preferences instead of snapping to a new scale.

| Role | Purpose |
| --- | --- |
| Establish | Show the actor in their surroundings, initially and when activity is uncertain |
| Tracking | Readable sustained movement with appropriate lead room |
| Interaction | Emphasize the primary with admitted companions or relevant opponents |
| Action | Preserve a readable observed encounter with decorative movement suppressed |
| Detail | Show an observed interaction/work point; a qualified stationary player may use a deliberately validated medium envelope, otherwise whole-body coverage remains |
| Interior | Compact framing when a doorway or constrained room limits camera space |
| Landscape | Retain the environmental plan's independent camera/aim paths |

Locomotion profiles distinguish stationary/slow movement, walking, fast travel, vertical traversal, observed swimming and support on a loaded ship. Player velocity retains its vertical component. Remote running/swimming local fields are not assumed authoritative: speed is measured, swimming uses replicated animation evidence, and boat support uses an actual supporting collider. A vessel can supply a live interest point and heading. Faster travel responds sooner; swimming/boats use steadier response; vertical traversal reduces decoration. These classifications have persistence and confidence, and do not claim keyboard intent or identify stairs semantically.

Transition decisions consider scene identity, readable endpoints, physical path, angle, screen displacement, lens/role change and activity. Small same-scene adjustments can blend; recovery, new scenes, large changes and unsuitable paths use a validated cut. Doorways, rooms and combat shorten permitted blends. Angular-rate restraint applies only while the restrained pose remains readable, so smoothing cannot preserve an obscured image at the expense of a valid correction.

## Useful lifetime, sequence beats and shot scale

`PredictiveShots` compares the expected useful lifetime of viewpoints which are already physically eligible and readable. The bounded forecast considers the active view and up to two readable alternatives on a 0.4-second cadence, sampling observed subject/group motion or an authored scenic path at 0.5, 1.0 and 1.5 seconds. It evaluates predicted framing, foliage/solid visibility, path clearance and existing scene support. Forecasts expire quickly; sudden course changes, stale observations and unknown future geometry reduce confidence or leave the comparison neutral. Future obstruction lowers an editorial score, never present physical eligibility, readable-coverage credit or permission to travel. The active view remains available while a better alternative is qualified.

`ShotSequences` retains a short plan for the observed scene: establish context, develop the activity, optionally show a complementary detail, re-establish geography and settle. Combat can enter immediately and retain bounded aftermath only when action subsides; no kill, completed build, destination or outcome is invented. Beat progress requires actually presented readable footage fulfilling the resolved role/scale. A solved or rejected candidate earns nothing. A requested beat that remains unavailable yields to a readable sustaining view instead of blocking indefinitely. Long gaps, changed scenes and invalid evidence re-establish context. Supported handoffs inside the same loaded group retain the sequence. Hold/steady pause editorial progression, and a finished sequence settles rather than repeating a rigid loop.

Wide, full and medium are distinct composition requests rather than labels on the same distance. Preferred distance/lens account for actor dimensions, aspect ratio and required screen envelope. Medium is restricted to sufficiently confident stationary player activity with no relevant opponent or doorway transition; work additionally needs an observed interest point. Its required envelope retains the head and torso while deliberately allowing the legs below the waist to leave the frame. Movement/combat, unknown evidence and creature subjects retain whole-body coverage. Companion coverage is always evaluated against each companion's full envelope, so a cropped nearby player does not silently earn normal coverage credit. Scenery retains environmental camera/aim paths and interprets editorial beats through those plans.

The action axis is persistent observed geography, not the actor's instantaneous body turn. Brief turns do not swap sides. A sustained material change prefers a readable wider re-establishment before committing new screen relationships. Recent role/scale and angle history discourages redundant cuts and unnecessary scale jumps. These are soft preferences: clear footage and physical safety override them.

## Clean program output and rendered-picture feedback

RTMP now receives the current post-effects camera-compositor texture before operator IMGUI/overlay UI. F5 console and diagnostic banners are not the stream input. Live and bounded held footage use the same regular-texture copy path; there is no final-screen screenshot fallback. Missing/stale compositor output, travel and recovery produce an owned **VALHEIM BROADCAST / RETURNING TO THE WORLD** card without operational details. Reset, resize, camera replacement and hard boundaries invalidate queued frame identities. Pending GPU reads own separate output buffers until completion. The existing explicit `Streaming.FlipVertical` option remains available; actual local-versus-stream orientation still needs D3D/Vulkan runtime checks.

UI already rendered into the world camera, including world-space labels from another mod, remains part of that scene image. The existing HUD-hiding controls still apply. This change does not add another rendering camera, desktop capture, a web controller, automatic exposure adjustment or new audio mixing.

`RenderedPictureEvaluation` samples only clean **live** pictures, independently of RTMP, at up to 2 Hz using one 96×54 asynchronous readback. It measures overall darkness/clipping and coarse contrast/detail around the projected primary envelope. Shot/scene identity, source frame, age and program generation must match; hold/slate, resize, reset and stale callbacks clear or reject evidence. Persistent concern applies only a small, bounded editorial penalty to the measured displayed shot. It cannot certify an unrendered alternative, declare missing geometry, teleport the observer or override geometric readability. Correction settling limits repeated pixel-driven changes when the entire environment is dark or bright.

These measurements are intentionally conservative. The region is a projection rectangle, not segmented player pixels; background can contaminate it, and both vertical row conventions are considered conservatively until backend orientation is runtime-verified. Normal night, fog, silhouettes and bright sky remain legitimate scenes. The evaluator cannot identify individual transparent leaves or prove that a new camera will look better. `broadcast camera status` and `broadcast diagnostics` show the evidence and penalty without burning those details into the program output.

## Scene-specific direction

| Coverage | Observations and direction |
| --- | --- |
| Player / self-test | Independent actor trajectories, deliberate body placement and optional cooperative/opponent groups |
| Creature | One loaded living actor, real body dimensions, observed 3D movement/facing/activity and size-adjusted framing |
| Scenic | Ranked environmental plans, separate world camera/aim paths, landscape placement and actual-pose foreground/feature quality |
| Probe viewing | Fixed marked-point measurement for the acquisition experiment |
| Travel / recovery | Bounded held footage or recovery slate; no invented cinematic scene |

Scenic plans develop through five styles: **lateral**, **arc**, **lift**, **reveal** and **approach**. Movement starts and ends gently and does not repeat a full orbit. Camera and aim progress differ, creating changing scale, parallax or viewpoint instead of sliding both together at a constant offset. The landscape placement guide leaves room around the observed terrain feature; it does not pretend to recognize a building, landmark or horizon semantically.

The loaded-region search compares multiple physical paths using feature visibility, foreground openness, measured terrain relief and recent style use. It checks sampled positions and swept segments across the allowed motion envelope. Location/style history is recorded only after two seconds of usable live footage. Accepted replacement plans change the scene, and scenic selection continues when no eligible creature is available. The existing autonomous director can later travel to another generated region within its configured limits.

A still-valid scene remains available while a replacement is sought, with its developed progress retained. Poor foreground visibility can prompt a new composition without declaring the terrain unloaded. Search failure remains visible in diagnostics and retryable. This is a bounded search over available loaded content, not a guarantee of endlessly novel or unobstructed scenery in every region.

Creature observation uses `Character.GetHeight`/`GetRadius`, stable character identity, filtered movement and body facing. Vertical movement is retained for flying/swimming actors. Stationary creatures remain eligible and can receive a calm developing shot; locomotion uses bounded tracking. An observed attack can support a combat hint, but the camera does not invent an opponent or infer herds from proximity. Dormant creatures are not awakened, simulated or repositioned for the broadcast. Their body envelope approximates character dimensions rather than every animated wing, tail or weapon.

Scenic and creature dwell advance only on a usable live image matched to the current mode, revision and scenic plan ID. Held framebuffers, slates and previous-plan footage do not count. During normal idle coverage, a creature that cannot produce usable footage for three seconds yields to scenery and receives a short failed-view cooldown. After three seconds without usable scenery, the director can try an eligible creature without claiming that scenic footage was shown. Successfully shown creatures retain the separate five-minute repeat cooldown. Ready players preempt normal idle coverage; the observed-idle wildlife policy below is the bounded exception.

## Observed-idle wildlife cutaways

`Idle.WildlifeCutaways` defaults to `true` in an authorized autonomous observer session. A cutaway requires a fresh, complete server roster showing **every connected ordinary player** observed idle for at least 60 seconds, including players outside the broadcaster's loaded area. Missing, stale, unspawned or otherwise unknown activity blocks it. Local observation of a loaded player's movement or action can veto the cutaway sooner than the next server reply.

The server observes existing replicated movement and action evidence, including attack/block/emote, station/crafting state and health changes. It does not receive raw keyboard/mouse input or reliably identify quiet inventory/menu use. **Observed idle does not mean AFK.** A motionless player may still be engaged with the game; diagnostics describe the evidence rather than claiming intent. Ordinary players need no mod.

Only eligible living creatures already loaded within the smaller of `Idle.CreatureRadius` and 30 metres of the selected player can be used. Selection retains the existing creature repeat/failure cooldowns, shared Cinemachine framing and default 25 seconds of usable creature footage. Cutaways also have a wall-time limit of `Idle.CreatureSeconds`, so repeated presentation gaps cannot extend one indefinitely. No suitable creature means continue player coverage. Activity, stale/incomplete evidence, roster changes, or loss of the creature ends the cutaway and resumes player coverage/acquisition. At least 60 seconds must pass before another cutaway can start, with all-player inactivity still required. Manual hold, self-follow, probe and solo route operations suppress cutaways.

This feature does not seek distant wildlife, spawn creatures, activate dormant simulation or change world time. Broadcaster-only sessions continue the existing scenic/creature idle behavior. `broadcast wildlife status` reports the blocking reason, minimum observed idle duration and remaining rearm interval; it distinguishes an unsupported server from players who are active or not yet observed long enough.

## Deliberate actor composition

Actor direction chooses a stable left/right placement instead of aiming every shot at the screen center. It gives the subject space ahead of sustained movement, toward observed body facing, or toward a supported activity point. Activity takes priority over movement, then facing. Facing is the character body's orientation, not eye tracking or knowledge of what the player is looking at. With no stronger cue, shots use a persistent thirds-like placement; some idle establishing shots remain centered.

Brief turns do not immediately swap sides. Opposite-direction evidence must persist, stopping retains the established side, and the requested position eases toward its next placement. The aim dead zone still allows the player to move around that guide. The guide is a preference, not a fixed screen coordinate imposed on every frame. Its evidence clock advances with usable footage rather than treating a long interruption as continuous movement.

Placement accounts for the required shot envelope: head and feet for wide/full views, head and torso for qualified medium views. Framing quality measures body visibility, readable size, safe frame coverage and headroom, rather than rewarding proximity to the image center. Available space, interiors and recent corrective moves reduce the offset. When the envelope cannot fit, the policy asks for neutral or primary-centered framing so the adapter can consider a wider or compact alternative. It never treats an aesthetic framing failure as missing world geometry or authorization for travel.

## Actor movement, framing and shared continuity

Each actor candidate keeps a persistent world-space trajectory anchor and velocity. Its look-at target remains the actual actor or supported player group, so aiming and screen placement can respond without translating the camera by the same amount. Establishing and activity-area shots retain a world origin; arc/dolly anchors can drift with part of coherent travel, while tracking uses the subject/group velocity. Combat prioritizes tracking. Continuous framing/range feedback and bounded acceleration correct each course as separation grows. A new shot, activity or group center does not by itself snap the anchor to the subject. This is shot-dependent behavior, not one global hold zone followed by a chase.

Position and aim retain separate Cinemachine damping. Screen-space lead room remains in the composition policy, but the old 0.2-second positional prediction is removed. A planned shot transition continues the outgoing anchor's bounded velocity, or holds it in restricted space, without copying the player's raw displacement into the outgoing camera position. Sudden turns and stops therefore have to be handled by the trajectory and aiming policies, rather than moving every shot with the player. During long uninterrupted travel the camera eventually matches the subject's average speed so they remain in view. Starting, stopping and reversing are not copied directly, and the shot can continue its own bounded development around that course. Independence does not mean remaining at a fixed world point indefinitely.

Readable shots develop through bounded left/right arcs, approaches, reveals or rises. Movement eases in and out and ends at rest. Open-space composition durations are approximately 16–21 seconds for idle, 20–25 for travel and 19–24 for work; these are usable-footage targets, not guaranteed cut intervals. Selection retains recent shot/movement history and requires a readable, near-best alternative. While the current view is healthy, selection discourages reversing the established movement/facing direction in the image. Combat suppresses discretionary cuts and movement, while visibility corrections remain available.

Interiors lengthen dwell and greatly reduce decorative motion. Cramped passages, few readable alternatives and repeated obstruction corrections favor holding the viable view. A spacious hall can still change composition when several good views exist. Corrections impose at least five seconds of settling; repeated corrections within ten seconds suppress extra camera activity. This prevents obstruction-driven changes from being followed immediately by an unnecessary planned move.

Current tracking is checked against physical clearance and scene support. A physically unsafe world-anchor step is rolled back in both the helper transform and planner, so a hidden plan cannot run ahead of a retained view. An approximately 0.75-second prediction screens framing, visibility and support for discretionary movement; an uncertain future view can withhold decoration without stopping otherwise valid current tracking. An unsafe decorative move retains its last accepted offset. Candidate evaluation runs at 5 Hz, with the selected camera and final presented pose checked each frame. Ordinary subject/mode/path changes use a fresh same-world displayed pose for a validated handoff rather than unconditionally resetting into the next view. World changes, controlled travel and probe boundaries still reset the camera state. A blocked blend cuts to its validated destination; a viewpoint change larger than 80 degrees also uses a validated cut instead of sweeping through the intervening arc. Physical failure can override dwell; if no valid picture remains, the existing bounded hold/recovery-slate path applies.

Foliage reduces picture quality rather than reporting missing world geometry. Scoring uses actual camera orientation, projection, near-plane clearance, primary visibility and weighted group visibility. Neither a low foliage score nor an outlying companion is a reason to teleport the observer.

## Player cooperative groups and activity

Groups contain the primary and up to three nearby loaded players, plus up to two relevant loaded opponents. The observer is excluded, except when it is the explicit self-follow subject. Membership must persist before joining, retains existing companions preferentially and has a short separation grace. Entry/retention distance, maximum group spread, vertical separation, interior state and structural line of sight prevent chains of distant players or players across walls/floors from pulling the shot apart. Member weights ease in; the group focus stays near the primary. Four compact primary-only alternatives remain available when a group cannot fit safely. Group views use a smaller placement offset and bias the shared composition toward the primary player. Cinemachine determines bounded group lens size before the aim composer applies the placement guide, so a second group-centering rotation cannot overwrite the intended composition. If the solved view loses the primary or crowds its safe frame/headroom, the adapter relaxes placement toward a primary-centered view only as far as needed. It preserves position damping and holds the corrected composition for three seconds before easing back toward the preference.

Activity points have their own short-lived evidence. A relevant observed opponent can supply combat space; gathering uses the actual nearby tree/rock collider hit during an attack, not a guessed resource center. A directly supporting loaded ship can supply a vessel interest. Competing points must persist before replacing the current point, and absent evidence expires. Local placement/crafting state or replicated crafting animation can support a work label without inventing an exact task or work target. Remote build/craft intent remains unknown.

A fresh handoff to an already admitted companion in the same world, within bounded distance/height and clear structural connection, retains the scene identity, observations and member weights. The previous primary can remain a companion while the new primary receives emphasis. The rig preserves that scene's trajectories and editorial history. An unrelated selection, respawn, teleport, world change, stale frame gap or separated floor/room resets the binding. This is continuity for a supported loaded group, not permanent party membership or an inferred shared objective.

Activity labels describe available evidence:

- **Travel:** filtered sustained movement. **Idle:** no stronger activity evidence.
- **Combat:** relevant hostile activity, owner-valid AI targeting, or combined attack/health-loss proximity evidence. An enemy merely existing nearby does not establish a fight.
- **Work:** observed local placement/crafting state, replicated crafting animation, or an attack aimed at a recognized tree/rock resource. Resource gathering is an inference. Exact remote build/craft intent is not assumed to be replicated.
- Health loss alone remains **cause unknown**. Replicated base value is only a **base-area hint**, not ownership. Unclassified actions stay explicitly uncertain in diagnostics.

Group framing does not automatically count everyone as watched. A player earns recent-coverage credit only after six continuous seconds of readable live footage. Held frames, slates, hidden members and failed poses earn none. Automatic scheduling can then avoid immediately revisiting a recently visible companion; manual next/previous and exact follow remain explicit controls.

## Activity selection and encounter retention

At an ordinary player-rotation boundary, fresh activity sustained for at least two seconds can influence the next selection. Observed combat/action, work and movement receive progressively lower interest weights; already-loaded candidates and supported members of the current scene receive a continuity preference. This does not preemptively chase every remote event. Unknown evidence keeps ordinary stable rotation available. An unshown player waiting 120 seconds receives priority over activity/retention; this is a fairness threshold, not a guaranteed maximum wait with many unavailable players.

Readable locally evidenced combat can extend a completed dwell by at most 20 seconds of wall time, including up to three seconds of aftermath. The extension does not renew indefinitely as new participants become primary. An overdue alternative, stale/uncertain evidence, unreadable footage or manual control ends retention. `next`, `previous`, exact selection and hold retain their existing authority. After 12 seconds of continuously unreadable player footage, the director can try another already-loaded, directly reachable player and avoid retrying the failed view for 30 seconds. This fallback does not authorize travel for picture quality; with no suitable local alternative it keeps bounded camera search and diagnostics. A readable image or manual hold resets that failed-view timing.

The optional 0.8.18 companion supplies `EventRequest1` / `EventReply1` when `Observer.EventsEnabled=true`. It reuses passive replicated movement, generic action, health-loss and crafting observations, with source/heartbeat/receipt freshness and bounded history. Generic action or health loss is never labeled proof of combat. Specific work evidence precedes generic action; fresh remote activity can supplement uncertain local observation. Exact targets, intent and private input are not invented. Server policy for private positions still applies.

Older compatible protocol-3 servers simply omit this feed: local camera direction, loaded-group continuity and locally observed activity remain available. Admission stays at protocol 3, discovery at 2, and existing context/idle activity extensions at 1. Server 0.8.18 is optional for richer remote activity; server 0.8.17 or later is still needed for the separate global-idle wildlife eligibility feature. Ordinary players require no mod.

## Reading camera diagnostics

`broadcast camera status` identifies the active scene kind. Actor diagnostics report trajectory mode, `anchor lag`, planned anchor `speed`, `rendered camera` speed and `subject` speed, plus the trajectory validation/correction reason. The anchor is a planned position reference; it is not the observer body or necessarily the final camera position. Rendered-camera speed is a smoothed estimate with a cap for cuts, not an isolated measurement of one camera component. Similar camera/player speeds during sustained travel are expected. Look at the response to starts, stops, reversals and shot development when checking whether movement is still being copied.

Actor diagnostics also report `placement requested`, `applied`, `primary focus`, `body height` and `safe`, followed by the placement and recovery reasons.

- **Requested** is the envelope-constrained policy destination. **Applied** is the eased guide currently sent to the aim composer, including a temporary corrective placement when needed.
- **Primary focus** is the selected actor's measured focus point in the actual presented camera pose. It can differ from the guide because of damping, the dead zone, a blend, or a shared group anchor.
- These coordinates are fractions of image width/height relative to its center: `(0,0)` is center, positive X is right and positive Y is up. They are not pixel coordinates.
- **Body height** is the projected body envelope as a fraction of image height. **Safe** is the fraction of that envelope inside the safe frame. These measurements describe framing, not whether the world has loaded.

A centered-looking shot can therefore be deliberate, restrained by available space, or a temporary correction. The reported reason distinguishes these cases. A large difference between requested and measured positions is useful diagnostic evidence; it does not alone mean a tracking failure.

Scenic diagnostics report the environmental style and phase, current path/progress, quality, search state, viable/rejected candidates and shown-place history. Environmental feature/foreground quality is not a humanoid body-size measurement. Creature context reports the observed activity and body dimensions. Unavailable coverage, probe state and observer travel are labelled separately so stale player-camera text is not mistaken for the current mode.

## Optional server context and compatibility

Observer admission remains **protocol 3**, discovery remains **protocol 2**, and optional camera context remains **protocol 1**. Version 0.8.17 adds a separate optional `ActivityRequest1` / `ActivityReply1` exchange for complete server-observed player inactivity. The 0.8.17 companion is required for wildlife cutaways while players are connected. Older protocol-3 servers continue normal player coverage; missing activity support disables only these cutaways and supplies a diagnostic reason. Damping, local activity evidence and cooperative framing still work without optional telemetry. Ordinary players need no mod.

The companion added in 0.8.13 can supply observation ages, coarse velocity and recent replicated health-loss hints through a separate optional context exchange. `Observer.ContextEnabled` defaults to `true`; observer authorization and position-sharing policy still apply. It reads existing player ZDOs without activating remote creatures, generating terrain or assigning simulation ownership. Health loss has no damage-source attribution.

The client probes support after observer authorization. An older server that does not reply falls back to local context after six seconds. Freshness, character identity and packet bounds are checked; stale or invalid optional context does not revoke an approved observer role. The camera currently uses fresh primary health-loss hints as supporting evidence, not as sufficient proof of combat.

The 0.8.19 production-camera changes require only the client update: install its three DLLs and included `ThirdParty` notices together, preserving configuration. The 0.8.18 server remains optional for remote event evidence; global-idle wildlife cutaways still require the 0.8.17-or-later activity service. The source change does not install files or restart the server. See [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md) for deployment and authorization.

## Reused Cinemachine components

The mod compiles selected official **Cinemachine 3.1.7** runtime sources into its isolated assembly. It directly uses these upstream camera mechanics:

| Component | Use in this mod |
| --- | --- |
| [Orbital Follow](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineOrbitalFollow.html) | Damped actor position around an independent world-space anchor, or direct environmental world-path position; controlled actor orbital offsets |
| [Rotation Composer](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineRotationComposer.html) | Aim damping, dead zone and frame limits |
| [Target Group](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineTargetGroup.html) | Weighted subjects with radii, updated alongside virtual cameras |
| [Group Framing](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineGroupFraming.html) | Reused bounded lens-size calculation before aim; the adapter suppresses its competing centered rotation |
| [Deoccluder](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineDeoccluder.html) | Collider-based obstacle correction |
| CameraState interpolation | Compatible same-scene blends use upstream screen-space aim interpolation; the resulting pose still passes visibility, orientation, path and observer-reachability checks |

Cinemachine supplies geometric camera behavior; the mod adds Valheim scene evidence, foliage scoring, continuity validation, activity/pacing and observer constraints. Virtual cameras are solved manually; the existing main camera remains the only rendering camera. See [the dependency record](../third_party/Cinemachine/README.md) for source pinning, compatibility changes and notices.

Vegetation renderer bounds approximate leaves and transparency; custom vegetation can be missed. Structural room/opening measurements are bounded geometric inferences, not semantic architecture recognition. Activity inference cannot establish player intent or remote AI targets that are not replicated. Pure tests and compilation cannot establish composition quality, GPU cost, absence of visual flicker or multiplayer behavior.

## Cinemachine capability audit

The pinned 3.1.7 runtime already supplies Orbital Follow, Rotation Composer, Target Group/Group Framing, Deoccluder, camera-state interpolation and blend machinery. The new direction keeps those mechanics and adds evidence/presentation policy above them.

| Capability | Current use and remaining opportunity |
| --- | --- |
| [ClearShot](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineClearShot.html) | Included in the selected runtime, but not instantiated. Its candidate-quality model is relevant; automatic ownership of cuts does not currently express readable-first recovery, future-evidence confidence and observer reachability together. The existing selection policies retain those requirements. |
| [Sequencer Camera](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineSequencerCamera.html) | Included but not instantiated. Fixed-duration instructions cannot by themselves credit only actually rendered usable footage or interrupt from uncertain live activity. The core sequence policy performs that role while Cinemachine solves each requested view. |
| Rotation Composer target offset/lookahead | Composer damping, dead zone and placement are used, with scale-aware upper-body aim for qualified medium shots. Additional upstream lookahead predicts aim rather than visibility and must not duplicate the rig's filtered movement forecast. |
| [Confiner 3D](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineConfiner3D.html) | Present, but not enabled without a valid bounding volume. Current roof/wall/doorway hints are not a reliable room-volume graph. Explicit validated room volumes would enable deeper reuse. |
| [Spline Dolly](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineSplineDolly.html) | Eight spline-specific files are intentionally excluded from this build, and Unity Splines is not shipped. Authored scenic paths are a future integration opportunity requiring a compatible dependency and runtime validation. |
| Timeline and render-pipeline integrations | Optional integrations remain disabled. No Timeline scene, render-pipeline replacement, extra camera or game-asset bundle is introduced. |

The audit is against the pinned package/source selection recorded in [the dependency notes](../third_party/Cinemachine/README.md), not a claim that every upstream feature is packaged or exercised. More Cinemachine components are useful when their scene inputs and lifecycle match this mod; they do not replace Valheim-specific evidence, isolation or clean-feed requirements.

## Runtime validation

**The 0.8.19 runtime cases below are pending.** Use a disposable profile/world first. F10 / `broadcast self` needs no second player or server mod and exercises the actual camera; it does not validate replicated groups or opponents. Record build, game/dependency versions, mode, camera status, relevant diagnostics and a short local/RTMP clip when reporting a defect. Compare obscured-shot duration, corrective cuts per minute and camera motion across the same short routes; a favorable numeric score alone is not visual validation.

| Exercise | Expected result to verify |
| --- | --- |
| Run toward a wall/doorway or sustained forest corridor, then reverse abruptly; compare prediction on/off | Future-use comparison reduces avoidable near-term corrections when evidence is stable; sudden turns make stale forecasts neutral, and prediction never starts travel |
| Observe work, cooperation, combat, travel and scenic coverage long enough to develop a sequence | Beat/role/scale progress follows displayed readable footage; constrained or rejected detail yields safely; no invented outcomes or rigid repeating cycle |
| Hold, steady, framehold test and a prolonged unavailable scene during an active sequence | Editorial time pauses or re-establishes correctly, while necessary following/recovery remains active |
| Stationary player with work interest, then sprint, fight, enter a doorway and observe a creature | Wide/full/medium shots visibly differ; only qualified player shots intentionally omit legs; head/torso stay safe and companion coverage remains honest |
| Reverse a group's travel or change the observed opponent while retaining visibility | Screen geography remains stable through brief changes; sustained changes can re-establish wider context without overriding readability |
| Open F5, inspect diagnostics, show local overlays and resize while publishing | Operator UI stays local; RTMP remains clean, correctly scaled and upright; stale GPU callbacks cannot reintroduce old scenes |
| Disable/re-enable the camera, travel/return, world exit and `broadcast framehold test` while publishing | Live/hold/slate orientation agrees, missing render gives the clean recovery card, and no previous-world picture leaks |
| Bright sky, normal night, fog, near-black image and low-contrast subject; compare picture on/off | Actual-image diagnostics are conservative; persistent concerns only softly affect the measured view, without night-scene churn or teleport/loading |
| Disable RTMP while keeping the director active; then stop/restart or resize during a picture readback | Picture measurements continue independently of streaming and reject stale identity/generation callbacks; no leaked sampling buffers |
| Stand/walk beneath dense foliage; then briefly pass a branch across the image; repeat with a creature and scenic feature | Actual readability reflects foreground/body coverage; transient obstruction does not churn, sustained obscuration enables recovery search, and selected recovery viewpoints remain stable once clear |
| Keep one player unframeable with another loaded nearby, then repeat with only a distant alternative and with one player total | After the failed-view budget, try only a directly reachable local alternative; cooldown applies; foliage never causes a teleport/loading trip; solo search diagnostics remain truthful |
| Enter/exit a player-built house, pass under an open-sided roof and tree canopy, cross a doorway, then enter a dungeon | Structural room/shelter/opening states are distinct from dungeon-layer readiness; canopy is not a roof; transitions settle without rapid classification chatter |
| Stand, sprint, stop, reverse, traverse slopes/stairs, swim and ride a moving ship | Profiles use observed motion/support, preserve vertical travel, and keep the full actor readable while angular/lens response remains bounded |
| Establish a scene, gather a recognized resource, fight near allies, then resume travel | Purpose/scale changes follow evidence and usable footage; qualified medium shots deliberately retain head/torso and whole-body alternatives; uncertain activity does not manufacture an action target |
| Cycle the primary between admitted nearby companions; then split across a wall/floor, teleport, respawn and rejoin | Supported handoffs retain scene/trajectory history; discontinuities reset it; no stale companion positions or cross-scene continuity |
| Reach player dwell during a locally evidenced fight; then use manual next/hold and create an overdue alternative | Encounter retention is bounded, aftermath is brief, manual control wins, and fairness prevents indefinite event monopoly |
| Updated server events on/off, older server, stale/malformed event reply and ordinary nonobserver session | Local operation remains available; optional event failure does not break admission, wildlife evidence or manual controls; generic damage/action is not asserted as combat |
| Updated client/server: one ordinary player idle near an eligible loaded creature; inspect wildlife status | No cutaway before 60 seconds of observed inactivity; a bounded creature shot can follow, then player coverage and at least 60 seconds before another cutaway |
| Two ordinary players, one nearby and idle, one distant and moving/acting | No cutaway until both have fresh confirmed inactivity for at least 60 seconds; the loaded roster alone cannot authorize it |
| During a cutaway, move, attack, block, emote, craft or change station state; separately join/leave, respawn or interrupt activity delivery | Return to player coverage/acquisition when action or incomplete/stale evidence appears; no permission persists across roster/character changes |
| Use an older protocol-3 server, then disable cutaways; separately test manual hold, self-follow, probe and solo route | Normal player coverage remains usable; each suppression or unsupported-feature reason is explicit |
| No eligible loaded creature, a hidden/dead/unloaded creature, then a broadcaster-only server | No wildlife-seeking travel or simulation changes; return to player coverage when required, and retain normal empty-server scenic behavior |
| Broadcaster alone: leave scenic coverage running through several plans and at least one region interval | Different non-looping shot styles and locations; camera and aim do not translate in lockstep; scenery does not settle into one repeated anchor orbit |
| Scenic hillside, coast/open terrain, dense forest and near structures | Readable environmental framing; actual camera orientation/lens guide foliage scoring; solid overlap is rejected while foliage lowers quality and prompts alternatives |
| During a developed scenic shot, compare `steady`, `dynamic`, `motion 0`, `motion 1` and `composition off/on` | Each control affects scenery; no snap back to the plan origin; resumption develops from the retained viewpoint |
| Observe a loaded stationary creature for more than ten seconds, then a moving small and a large creature | Stationary subjects remain eligible; real body scale is readable; movement is damped and independently tracked; no forced AI activation |
| Creature becomes hidden, dies, unloads or leaves the search region | Valid alternatives or bounded return to scenery; failed-view retry differs from successfully shown cooldown; no false player-coverage credit |
| Switch player → scenic → creature → player, and accept a replacement scenic plan in the same coverage revision | Validated transitions from the displayed pose; no stale subject state, progress rewind or previous-plan footage credit |
| Run `broadcast framehold test` during scenery and creature coverage | Held images do not advance idle dwell/history; resume from the displayed pose without an accumulated movement jump |
| Run the distant probe, controlled travel/return and recovery after cinematic idle coverage | Probe remains a fixed measurement; travel/recovery show their correct presentation and diagnostics; previous scenery does not resurrect across a hard reset |
| Solo and another player: stand, walk, sprint continuously, stop, reverse and turn | Camera translation develops and settles independently; the subject can move within the frame without instant mirrored translation, excessive lag or spins |
| Follow another player walking/sprinting in one direction for several minutes across terrain zones | Tracking remains bounded and readable throughout sustained travel; decorative trajectory choices do not trigger loading/travel cycles |
| Stop and change direction during a planned shot transition | No raw player-displacement jump in the outgoing view; the actual transition remains clear and the primary readable |
| Compare `composition on` / `off` while idle, travelling, reversing and looking across the scene | Deliberate placement differs from the centered preference, remains stable through brief turns, and preserves readable head/body space |
| Remain idle in open terrain for a minute; compare `dynamic`, `steady`, `motion 0`, then restore `motion 1` | Deliberate movement and occasional new readable compositions only in the enabled modes |
| Dense forest, near a wall, narrow hallway, stairs and doorway; then spacious interior | Limited space restrains trajectory development and motion/churn; clearance takes precedence and the primary remains readable when a valid option exists |
| Two to four players gather, travel together, separate, cross different floors, then rejoin | Group changes do not snap the camera anchor to a new centroid; bounded framing and the primary remain stable; visible companions affect later automatic rotation |
| Fight near allies; gather wood/ore; stand near a passive enemy | Relevant opponents only; work/combat hints match observed evidence; unknown damage is not automatically combat |
| Updated server with context enabled/disabled, then existing protocol-3 server | Observer authorization and local camera work in all cases; optional-context status/fallback is truthful |
| Target portal travel/respawn, cycle targets, off/on, return/park/resume and world exit/rejoin | Fresh bindings and normal recovery; no stale group, motion or camera state |
| `broadcast framehold test`, real loading gap and travel while comparing local display/RTMP | Upright held/live transitions and correct slate; no pre-travel scene resurrection |
| Long crowded/forested session with RTMP enabled | Profile frame time and allocation cost; no accumulating helper objects or repeated camera exceptions |

Full validation evidence and older regression procedures remain in [RUNTIME-TESTS.md](RUNTIME-TESTS.md).
