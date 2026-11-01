# Player camera director (0.8.13)

The procedural player camera now combines separate position/aim damping, screen-space freedom, deliberate shot movement, persistent nearby groups and activity-aware pacing. It builds on 0.8.12's Cinemachine and foliage evaluator. The selected player remains the required subject; composition changes do not authorize observer travel. Scenic/creature idle coverage remains a separate director.

This implementation compiles against the real Valheim 1.0.21 references, and the combined core suite passes **183/183 cases**. **The new camera behavior has not been run in Unity/Valheim here.** The user's positive visibility feedback concerns 0.8.12; new damping, group compositions, visual transitions and runtime cost still need the matrix below.

## Controls and configuration

Use `-console` when launching Valheim, then F5. Close the console to observe the picture. The following commands update the saved client configuration; no server restart is needed for camera changes.

| Command | Effect |
| --- | --- |
| `broadcast camera status` | Show scene/activity evidence, selected shot, quality, available space, movement/settling reason and camera settings |
| `broadcast camera dynamic` | Enable timed fresh compositions and deliberate movement when space permits |
| `broadcast camera steady` | Disable that editorial layer; retain procedural visibility selection, damping and group framing |
| `broadcast camera groups on` / `off` | Enable/disable cooperative and opponent group compositions |
| `broadcast camera motion 0` | Stop decorative movement while retaining tracking, damping and dynamic shot selection |
| `broadcast camera motion 1` | Restore default decorative movement strength; accepted range is 0–1.5 |
| `broadcast camera damping 0.8 1` | Set position and aim damping in seconds; each accepts 0.1–3 |
| `broadcast camera procedural` / `fixed` | Select the procedural rig or the legacy fixed-angle comparison camera |
| `broadcast diagnostics` | Include observer/discovery/output state and optional server-context status |

`steady` keeps the Cinemachine rig active. `fixed` selects the older camera. `motion 0` can still produce a new composition after sufficient usable footage. `broadcast hold` pauses player rotation; it does not freeze camera movement.

| `Camera` key | Default | Meaning |
| --- | --- | --- |
| `ProceduralPlayers` | `true` | Use the procedural player camera |
| `DynamicShots` | `true` | Enable activity/space-aware pacing and deliberate movement |
| `GroupFraming` | `true` | Consider persistent companions and relevant opponents |
| `PositionDamping` | `0.8` | Seconds; increasing it softens position response |
| `AimDamping` | `1` | Seconds; increasing it softens aim response |
| `DeadZone` | `0.12` | Screen fraction allowing subject movement before recentering; range 0–0.3 |
| `ShotMotion` | `1` | Decorative movement strength, range 0–1.5 |

`Distance`, `Height`, `Azimuth` and `FieldOfView` remain composition preferences. Procedural views can adapt them within limits. `Smoothing` governs legacy fixed/scenic response; use the two damping settings for procedural player cameras. `Director.DwellSeconds` controls player rotation, independently of a composition's duration.

## Movement, framing and pacing

The player can move within the aim dead zone instead of being pinned to one image coordinate. Filtered movement supplies limited prediction and gradual lead room. Sustained travel can slowly shift the preferred heading; sudden turns do not instantly swing the camera. Position and aim have separate damping. The rig preserves solved camera orientation during transitions rather than replacing every frame with a hard look-at rotation.

Readable shots develop through bounded left/right arcs, approaches, reveals or rises. Movement eases in and out and ends at rest. Open-space composition durations are approximately 16–21 seconds for idle, 20–25 for travel and 19–24 for work; these are usable-footage targets, not guaranteed cut intervals. Selection retains recent shot/movement history and requires a readable, near-best alternative. Combat suppresses discretionary cuts and movement, while visibility corrections remain available.

Interiors lengthen dwell and greatly reduce decorative motion. Cramped passages, few readable alternatives and repeated obstruction corrections favor holding the viable view. A spacious hall can still change composition when several good views exist. Corrections impose at least five seconds of settling; repeated corrections within ten seconds suppress extra camera activity. This prevents obstruction-driven changes from being followed immediately by an unnecessary planned move.

The current move and an approximately 0.8-second prediction are screened for clearance, subject framing and existing scene support. An unsafe decorative move retains its last accepted offset. Candidate evaluation runs at 5 Hz, with the selected camera and final presented pose checked each frame. A blocked blend cuts to its validated destination. Physical failure can override dwell; if no valid picture remains, the existing bounded hold/recovery-slate path applies.

Foliage reduces picture quality rather than reporting missing world geometry. Scoring uses actual camera orientation, projection, near-plane clearance, primary visibility and weighted group visibility. Neither a low foliage score nor an outlying companion is a reason to teleport the observer.

## Cooperative groups and activity

Groups contain the primary and up to three nearby loaded players, plus up to two relevant loaded opponents. The observer is excluded, except when it is the explicit self-follow subject. Membership must persist before joining, retains existing companions preferentially and has a short separation grace. Entry/retention distance, maximum group spread, vertical separation, interior state and structural line of sight prevent chains of distant players or players across walls/floors from pulling the shot apart. Member weights ease in; the group focus stays near the primary. Four compact primary-only alternatives remain available when a group cannot fit safely.

Activity labels describe available evidence:

- **Travel:** filtered sustained movement. **Idle:** no stronger activity evidence.
- **Combat:** relevant hostile activity, owner-valid AI targeting, or combined attack/health-loss proximity evidence. An enemy merely existing nearby does not establish a fight.
- **Work:** observed local placement/crafting state, or an attack aimed at a recognized tree/rock resource. Resource gathering is an inference. Remote build/craft intent is not assumed to be replicated.
- Health loss alone remains **cause unknown**. Replicated base value is only a **base-area hint**, not ownership. Unclassified actions stay explicitly uncertain in diagnostics.

Group framing does not automatically count everyone as watched. A player earns recent-coverage credit only after six continuous seconds of readable live footage. Held frames, slates, hidden members and failed poses earn none. Automatic scheduling can then avoid immediately revisiting a recently visible companion; manual next/previous and exact follow remain explicit controls.

## Optional server context and compatibility

Observer admission remains **protocol 3**. Existing protocol-3 servers can continue serving this client. Damping, local activity evidence and cooperative framing work without the new optional telemetry. Ordinary players still need no mod.

The updated companion can supply observation ages, coarse velocity and recent replicated health-loss hints through a separate optional context exchange. `Observer.ContextEnabled` defaults to `true`; observer authorization and position-sharing policy still apply. It reads existing player ZDOs without activating remote creatures, generating terrain or assigning simulation ownership. Health loss has no damage-source attribution.

The client probes support after observer authorization. An older server that does not reply falls back to local context after six seconds. Freshness, character identity and packet bounds are checked; stale or invalid optional context does not revoke an approved observer role. The camera currently uses fresh primary health-loss hints as supporting evidence, not as sufficient proof of combat.

Install the client's three DLLs and included `ThirdParty` notices together, preserving configuration. The server update is optional for these extra hints; if used, replace its server/core pair together during the operator's normal restart. See [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md) for deployment and authorization.

## Reused Cinemachine components

The mod compiles selected official **Cinemachine 3.1.7** runtime sources into its isolated assembly. It directly uses these upstream camera mechanics:

| Component | Use in this mod |
| --- | --- |
| [Orbital Follow](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineOrbitalFollow.html) | Damped world-space position with controlled orbital offsets |
| [Rotation Composer](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineRotationComposer.html) | Aim damping, dead zone and frame limits |
| [Target Group](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineTargetGroup.html) | Weighted subjects with radii, updated alongside virtual cameras |
| [Group Framing](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineGroupFraming.html) | Damped bounded FOV/rotation adjustment for group views |
| [Deoccluder](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineDeoccluder.html) | Collider-based obstacle correction |

Cinemachine supplies geometric camera behavior; the mod adds Valheim scene evidence, foliage scoring, continuity validation, activity/pacing and observer constraints. Virtual cameras are solved manually; the existing main camera remains the only rendering camera. See [the dependency record](../third_party/Cinemachine/README.md) for source pinning, compatibility changes and notices.

Vegetation renderer bounds approximate leaves and transparency; custom vegetation can be missed. Physics line of sight is not semantic room detection. Activity inference cannot establish player intent or remote AI targets that are not replicated. Pure tests and compilation cannot establish composition quality, GPU cost, absence of visual flicker or multiplayer behavior.

## Runtime validation

**All 0.8.13 cases below are pending.** Use a disposable profile/world first. F10 / `broadcast self` needs no second player or server mod and exercises the actual camera; it does not validate replicated groups or opponents. Record build, game/dependency versions, mode, camera status, relevant diagnostics and a short local/RTMP clip when reporting a defect.

| Exercise | Expected result to verify |
| --- | --- |
| Solo: stand, walk, sprint continuously, stop, reverse and turn | Subject can move inside the frame; tracking settles gently without excessive lag, sudden spins or travel |
| Remain idle in open terrain for a minute; compare `dynamic`, `steady`, `motion 0`, then restore `motion 1` | Deliberate movement and occasional new readable compositions only in the enabled modes |
| Dense forest, narrow hallway, stairs and doorway; then spacious interior | Limited space reduces motion/churn; roomy interiors can still vary; primary remains readable when a valid option exists |
| Two to four players gather, travel together, separate, cross different floors, then rejoin | Stable join/split, bounded framing, no distant outlier pulling the camera away; visible companions affect later automatic rotation |
| Fight near allies; gather wood/ore; stand near a passive enemy | Relevant opponents only; work/combat hints match observed evidence; unknown damage is not automatically combat |
| Updated server with context enabled/disabled, then existing protocol-3 server | Observer authorization and local camera work in all cases; optional-context status/fallback is truthful |
| Target portal travel/respawn, cycle targets, off/on, return/park/resume and world exit/rejoin | Fresh bindings and normal recovery; no stale group, motion or camera state |
| `broadcast framehold test`, real loading gap and travel while comparing local display/RTMP | Upright held/live transitions and correct slate; no pre-travel scene resurrection |
| Long crowded/forested session with RTMP enabled | Profile frame time and allocation cost; no accumulating helper objects or repeated camera exceptions |

Full validation evidence and older regression procedures remain in [RUNTIME-TESTS.md](RUNTIME-TESTS.md).
