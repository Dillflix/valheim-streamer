# Player camera director (0.8.15)

The procedural player camera combines independent world-space trajectories, position/aim damping, screen-space composition, persistent nearby groups and activity-aware pacing. Version 0.8.15 builds directly on 0.8.14 and separates the camera's translational path from its live subject/group aim. The selected player remains required; a new composition never authorizes observer travel. Scenic/creature idle coverage remains a separate director.

Version 0.8.14 added persistent subject placement, movement lead room and coordinated group composition. Its body position still followed a target-relative point with 0.2 seconds of velocity prediction. That prediction nearly canceled the lag from the default Cinemachine position damping during sustained movement; shot blends also copied target displacement. The aim dead zone could not free the camera's translation. Version 0.8.15 removes those paths and retains the composition policy.

Build and test evidence is recorded in [RUNTIME-TESTS.md](RUNTIME-TESTS.md). **The new 0.8.15 trajectory behavior has not been run in Unity/Valheim here.** Pure tests and compilation are distinct from visual quality, multiplayer behavior and runtime cost.

## Controls and configuration

Use `-console` when launching Valheim, then F5. Close the console to observe the picture. The following commands update the saved client configuration; no server restart is needed for camera changes.

| Command | Effect |
| --- | --- |
| `broadcast camera status` | Show scene/activity evidence, selected shot, quality, available space, movement/settling reason and camera settings |
| `broadcast camera dynamic` | Enable timed fresh compositions and deliberate movement when space permits |
| `broadcast camera steady` | Disable that editorial layer; retain procedural visibility selection, damping and group framing |
| `broadcast camera composition on` / `off` | Enable deliberate subject placement or select the centered comparison preference; preserve the same damping and visibility checks |
| `broadcast camera groups on` / `off` | Enable/disable cooperative and opponent group compositions |
| `broadcast camera motion 0` | Stop decorative movement while retaining tracking, damping and dynamic shot selection |
| `broadcast camera motion 1` | Restore default decorative movement strength; accepted range is 0–1.5 |
| `broadcast camera damping 0.8 1` | Set position and aim damping in seconds; each accepts 0.1–3 |
| `broadcast camera procedural` / `fixed` | Select the procedural rig or the legacy fixed-angle comparison camera |
| `broadcast diagnostics` | Include observer/discovery/output state and optional server-context status |

`composition off` eases toward a neutral placement preference without resetting camera damping; it does not disable groups or deliberate movement. Geometry and full-body framing can still require a corrective offset. `steady` keeps the Cinemachine rig active and holds the accepted decorative offset instead of snapping it back to neutral; its trajectory uses tracking during travel and an activity-area reference otherwise. `fixed` selects the older camera. `motion 0` stops orbital/heading decoration while necessary tracking and framing continue; it can still produce a new composition after sufficient usable footage. `broadcast hold` pauses player rotation; it does not freeze camera movement.

| `Camera` key | Default | Meaning |
| --- | --- | --- |
| `ProceduralPlayers` | `true` | Use the procedural player camera |
| `DynamicShots` | `true` | Enable activity/space-aware pacing and deliberate movement |
| `DeliberateComposition` | `true` | Choose persistent subject placement, lead room and activity space; false uses the centered comparison preference |
| `GroupFraming` | `true` | Consider persistent companions and relevant opponents |
| `PositionDamping` | `0.8` | Cinemachine damping in seconds for the planned trajectory; increasing it softens position response |
| `AimDamping` | `1` | Seconds; increasing it softens aim response |
| `DeadZone` | `0.12` | Screen fraction allowing subject movement before recentering; range 0–0.3 |
| `ShotMotion` | `1` | Decorative movement strength, range 0–1.5 |

Cinemachine's damping parameter is a settling duration, not a pure time delay: `PositionDamping = 0.8` gives approximately 99% convergence in 0.8 seconds for a stationary target. Trajectory planning now determines how camera translation responds to player motion; increasing damping alone is not the independence mechanism.

`Distance`, `Height`, `Azimuth` and `FieldOfView` remain composition preferences. Procedural views can adapt them within limits. `Smoothing` governs legacy fixed/scenic response; use the two damping settings for procedural player cameras. `Director.DwellSeconds` controls player rotation, independently of a composition's duration.

## Deliberate screen composition

The director chooses a stable left/right placement instead of aiming every shot at the screen center. It gives the player space ahead of sustained movement, toward observed body facing, or toward a supported activity point. Activity takes priority over movement, then facing. Facing is the character body's orientation, not eye tracking or knowledge of what the player is looking at. With no stronger cue, shots use a persistent thirds-like placement; some idle establishing shots remain centered.

Brief turns do not immediately swap sides. Opposite-direction evidence must persist, stopping retains the established side, and the requested position eases toward its next placement. The aim dead zone still allows the player to move around that guide. The guide is a preference, not a fixed screen coordinate imposed on every frame. Its evidence clock advances with usable footage rather than treating a long interruption as continuous movement.

Placement accounts for the projected subject envelope, including head and feet. Framing quality measures body visibility, readable size, safe frame coverage and headroom, rather than rewarding proximity to the image center. Available space, interiors and recent corrective moves reduce the offset. When the envelope cannot fit, the policy asks for neutral or primary-centered framing so the adapter can consider a wider or compact alternative. It never treats an aesthetic framing failure as missing world geometry or authorization for travel.

## Movement, framing and pacing

Each candidate keeps a persistent world-space trajectory anchor and velocity. Its look-at target remains the actual player or supported group, so aiming and screen placement can respond without translating the camera by the same amount. Establishing and activity-area shots retain a world origin; arc/dolly anchors can drift with part of coherent travel, while tracking uses the subject/group velocity. Combat prioritizes tracking. Continuous framing/range feedback and bounded acceleration correct each course as separation grows. A new shot, activity or group center does not by itself snap the anchor to the subject. This is shot-dependent behavior, not one global hold zone followed by a chase.

Position and aim retain separate Cinemachine damping. Screen-space lead room remains in the composition policy, but the old 0.2-second positional prediction is removed. A planned shot transition continues the outgoing anchor's bounded velocity, or holds it in restricted space, without copying the player's raw displacement into the outgoing camera position. Sudden turns and stops therefore have to be handled by the trajectory and aiming policies, rather than moving every shot with the player. During long uninterrupted travel the camera eventually matches the subject's average speed so they remain in view. Starting, stopping and reversing are not copied directly, and the shot can continue its own bounded development around that course. Independence does not mean remaining at a fixed world point indefinitely.

Readable shots develop through bounded left/right arcs, approaches, reveals or rises. Movement eases in and out and ends at rest. Open-space composition durations are approximately 16–21 seconds for idle, 20–25 for travel and 19–24 for work; these are usable-footage targets, not guaranteed cut intervals. Selection retains recent shot/movement history and requires a readable, near-best alternative. While the current view is healthy, selection discourages reversing the established movement/facing direction in the image. Combat suppresses discretionary cuts and movement, while visibility corrections remain available.

Interiors lengthen dwell and greatly reduce decorative motion. Cramped passages, few readable alternatives and repeated obstruction corrections favor holding the viable view. A spacious hall can still change composition when several good views exist. Corrections impose at least five seconds of settling; repeated corrections within ten seconds suppress extra camera activity. This prevents obstruction-driven changes from being followed immediately by an unnecessary planned move.

Current tracking is checked against physical clearance and scene support. A physically unsafe world-anchor step is rolled back in both the helper transform and planner, so a hidden plan cannot run ahead of a retained view. An approximately 0.75-second prediction screens framing, visibility and support for discretionary movement; an uncertain future view can withhold decoration without stopping otherwise valid current tracking. An unsafe decorative move retains its last accepted offset. Candidate evaluation runs at 5 Hz, with the selected camera and final presented pose checked each frame. A blocked blend cuts to its validated destination; a viewpoint change larger than 80 degrees also uses a validated cut instead of sweeping through the intervening arc. Physical failure can override dwell; if no valid picture remains, the existing bounded hold/recovery-slate path applies.

Foliage reduces picture quality rather than reporting missing world geometry. Scoring uses actual camera orientation, projection, near-plane clearance, primary visibility and weighted group visibility. Neither a low foliage score nor an outlying companion is a reason to teleport the observer.

## Cooperative groups and activity

Groups contain the primary and up to three nearby loaded players, plus up to two relevant loaded opponents. The observer is excluded, except when it is the explicit self-follow subject. Membership must persist before joining, retains existing companions preferentially and has a short separation grace. Entry/retention distance, maximum group spread, vertical separation, interior state and structural line of sight prevent chains of distant players or players across walls/floors from pulling the shot apart. Member weights ease in; the group focus stays near the primary. Four compact primary-only alternatives remain available when a group cannot fit safely. Group views use a smaller placement offset and bias the shared composition toward the primary player. Cinemachine determines bounded group lens size before the aim composer applies the placement guide, so a second group-centering rotation cannot overwrite the intended composition. If the solved view loses the primary or crowds its safe frame/headroom, the adapter relaxes placement toward a primary-centered view only as far as needed. It preserves position damping and holds the corrected composition for three seconds before easing back toward the preference.

Activity points have their own short-lived evidence. A relevant observed opponent can supply combat space; gathering uses the actual nearby tree/rock collider hit during an attack, not a guessed resource center. Competing points must persist before replacing the current point, and absent evidence expires. Local placement/crafting state can support a work label without inventing a work target. Remote build/craft intent remains unknown.

Activity labels describe available evidence:

- **Travel:** filtered sustained movement. **Idle:** no stronger activity evidence.
- **Combat:** relevant hostile activity, owner-valid AI targeting, or combined attack/health-loss proximity evidence. An enemy merely existing nearby does not establish a fight.
- **Work:** observed local placement/crafting state, or an attack aimed at a recognized tree/rock resource. Resource gathering is an inference. Remote build/craft intent is not assumed to be replicated.
- Health loss alone remains **cause unknown**. Replicated base value is only a **base-area hint**, not ownership. Unclassified actions stay explicitly uncertain in diagnostics.

Group framing does not automatically count everyone as watched. A player earns recent-coverage credit only after six continuous seconds of readable live footage. Held frames, slates, hidden members and failed poses earn none. Automatic scheduling can then avoid immediately revisiting a recently visible companion; manual next/previous and exact follow remain explicit controls.

## Reading camera diagnostics

`broadcast camera status` reports trajectory mode, `anchor lag`, planned anchor `speed`, `rendered camera` speed and `player` speed, plus the trajectory validation/correction reason. The anchor is a planned position reference; it is not the observer body or necessarily the final camera position. Rendered-camera speed is a smoothed estimate with a cap for cuts, not an isolated measurement of one camera component. Similar camera/player speeds during sustained travel are expected. Look at the response to starts, stops, reversals and shot development when checking whether movement is still being copied.

It also reports `placement requested`, `applied`, `primary focus`, `body height` and `safe`, followed by the placement and recovery reasons.

- **Requested** is the envelope-constrained policy destination. **Applied** is the eased guide currently sent to the aim composer, including a temporary corrective placement when needed.
- **Primary focus** is the selected player's measured focus point in the actual presented camera pose. It can differ from the guide because of damping, the dead zone, a blend, or a shared group anchor.
- These coordinates are fractions of image width/height relative to its center: `(0,0)` is center, positive X is right and positive Y is up. They are not pixel coordinates.
- **Body height** is the projected body envelope as a fraction of image height. **Safe** is the fraction of that envelope inside the safe frame. These measurements describe framing, not whether the world has loaded.

A centered-looking shot can therefore be deliberate, restrained by available space, or a temporary correction. The reported reason distinguishes these cases. A large difference between requested and measured positions is useful diagnostic evidence; it does not alone mean a tracking failure.

## Optional server context and compatibility

Observer admission remains **protocol 3**. Version 0.8.15 changes no server behavior or wire schema and requires no new server deployment. Existing protocol-3 servers can continue serving this client. Damping, local activity evidence and cooperative framing work without optional telemetry. Ordinary players still need no mod.

The companion added in 0.8.13 can supply observation ages, coarse velocity and recent replicated health-loss hints through a separate optional context exchange. `Observer.ContextEnabled` defaults to `true`; observer authorization and position-sharing policy still apply. It reads existing player ZDOs without activating remote creatures, generating terrain or assigning simulation ownership. Health loss has no damage-source attribution.

The client probes support after observer authorization. An older server that does not reply falls back to local context after six seconds. Freshness, character identity and packet bounds are checked; stale or invalid optional context does not revoke an approved observer role. The camera currently uses fresh primary health-loss hints as supporting evidence, not as sufficient proof of combat.

Install the client's three DLLs and included `ThirdParty` notices together, preserving configuration. A server still predating that optional context capability can be upgraded for those extra hints; if doing so, replace its server/core pair together during the operator's normal restart. See [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md) for deployment and authorization.

## Reused Cinemachine components

The mod compiles selected official **Cinemachine 3.1.7** runtime sources into its isolated assembly. It directly uses these upstream camera mechanics:

| Component | Use in this mod |
| --- | --- |
| [Orbital Follow](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineOrbitalFollow.html) | Damped position around the independently planned world-space anchor, with controlled orbital offsets |
| [Rotation Composer](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineRotationComposer.html) | Aim damping, dead zone and frame limits |
| [Target Group](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineTargetGroup.html) | Weighted subjects with radii, updated alongside virtual cameras |
| [Group Framing](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineGroupFraming.html) | Reused bounded lens-size calculation before aim; the adapter suppresses its competing centered rotation |
| [Deoccluder](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineDeoccluder.html) | Collider-based obstacle correction |

Cinemachine supplies geometric camera behavior; the mod adds Valheim scene evidence, foliage scoring, continuity validation, activity/pacing and observer constraints. Virtual cameras are solved manually; the existing main camera remains the only rendering camera. See [the dependency record](../third_party/Cinemachine/README.md) for source pinning, compatibility changes and notices.

Vegetation renderer bounds approximate leaves and transparency; custom vegetation can be missed. Physics line of sight is not semantic room detection. Activity inference cannot establish player intent or remote AI targets that are not replicated. Pure tests and compilation cannot establish composition quality, GPU cost, absence of visual flicker or multiplayer behavior.

## Runtime validation

**The 0.8.15 visual cases below are pending.** Use a disposable profile/world first. F10 / `broadcast self` needs no second player or server mod and exercises the actual camera; it does not validate replicated groups or opponents. Record build, game/dependency versions, mode, camera status, relevant diagnostics and a short local/RTMP clip when reporting a defect.

| Exercise | Expected result to verify |
| --- | --- |
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
