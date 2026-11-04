# Encounter coverage — 0.8.35

This client update builds directly on 0.8.34 (`0df83a4798b3d40e8124e14fe440dba97aad4fdc`). It extends local encounter observation, camera composition and actor visibility. Observer admission remains protocol 3; the existing compatible companion can remain installed. No extra ordinary-player mod is required. These observations do not activate creatures, change AI, take ownership or claim remote private input.

Replace the three owned client DLLs and included Cinemachine notices together, preserving configuration. Build/policy results and actual Valheim footage are separate evidence; the runtime scenarios below are not represented as completed by compilation.

## Presence, developing pressure and engagement

The local encounter survey keeps hostile presence separate from evidence of combat. An ordinary hostile can matter before attacking. Nearby numbers, reliable closing motion, prominence and the distribution of hostiles around the primary contribute to developing pressure. Confirmed local action evidence can establish engagement; proximity, motion and coarse surrounding geometry alone cannot establish aggro, an exact victim or a predicted outcome.

Situation analysis uses the bounded loaded-actor shortlist independently of the smaller cast used by a camera. Its eight directional sectors describe observed geometry around the primary, not tactical awareness. Structural separation and dungeon-layer checks keep unrelated spaces apart. Missing/dead/unloaded actors leave the observation, and pressure promotions must persist rather than changing on every small movement.

The selected key opponent remains useful for exchange angles, hunting direction and complementary views. It does not replace the broader situation. Nearby tame creatures and passive wildlife can visually obstruct the view without becoming hostile participants. Hunting retains its existing distinction between an inferred body-facing target and private exact aim.

## Observed cast and each shot's subjects

The former universal three-opponent/three-companion framing rule is removed. A bounded observation cast can retain up to 16 participants including the primary. It reserves the principal relationship, retains both allies and opponents, and considers spatial representation. This is a processing limit on detailed cast observations, not a requirement to show 16 actors or proof that every actor in a larger encounter is represented.

Each camera chooses subjects appropriate to its purpose and measured extent. Wider coverage and a focused exchange have different requirements; a distant outlier should not force the primary into a tiny silhouette. Native Cinemachine group framing continues to own the selected group's framing mechanics. The director still retains player-only coverage when a safe useful encounter composition is unavailable.

An exchange requires readable primary and key opponent coverage. An overview additionally checks that several relevant opponents, any present companion population, and a sufficient share of observed directions are represented. These are bounded operational criteria, not proof that the entire encounter is on screen. Satisfying the requested purpose changes editorial preference among readable candidates; it does not grant a physically invalid camera permission to render, and an unavailable purpose does not invalidate the remaining useful picture. Actual presented coverage, rather than background candidate solves, advances the overview/sequence evidence. A protected action can briefly delay a newly informative view, but persistent missing opponent coverage has a three-second grace before a supported improvement can proceed through normal cut persistence. A useful existing exchange remains protected from ordinary overview novelty. Closing or surrounding hostiles also inhibit optional opponent portraits.

## Foreground actors and picture visibility

Actor visibility now considers other loaded characters between camera and subject. The sampler observes up to 128 nearby characters from the game's existing registry, refreshes membership at most four times per second, and updates retained body positions once per render frame. It uses a supported root capsule/sphere collider's inner body shape only when an enabled mesh renderer represents that body. Character-wide renderer boxes, attack triggers, wings and weapon extents are not treated as solid opaque volumes.

The measured subject excludes itself by instance identity; the broadcaster's local character is excluded. Dead, inactive and unsupported bodies do not fabricate an obstruction. Foreground actor coverage contributes to the same body samples used to evaluate readable footage, including head/torso requirements where applicable, and is reported separately from static geometry and foliage.

This remains a body-shape approximation. It does not perform pixel segmentation, reproduce every animated limb, or guarantee detection of arbitrary modded collider/renderer arrangements. Its bounded shortlist can omit actors in extremely crowded scenes. Actor visibility does not change static camera clearance, world readiness or permission to travel.

## Capture an encounter

Use the existing automatic synchronized capture described in [EDITORIAL-CAPTURE.md](EDITORIAL-CAPTURE.md). Before a test, check `broadcast capture status` and `broadcast camera status`. Preserve the paired footage/JSONL segments, their parent `scenic-*.jsonl` trace files and the matching `BepInEx/LogOutput.log` from the broadcaster's active installation. For the production setup, that is the sandboxed Windows client, not another Steam or Thunderstore profile.

The synchronized picture context includes `encounter` (observed pressure), `coverage` (the displayed camera's purpose result), `hostiles`, `participants`, `framed`, `visibleOpponents` and `actorOcclusion`. These counts differ deliberately: the hostile survey can exceed the observation cast; a camera can frame a subset; and a framed opponent can still be obscured. Actor occlusion is a sampled body-coverage fraction, not an exact percentage of pixels. The operator's camera diagnostics also describe closing actors, observed sectors and the currently evaluated cast. Held footage retains the metadata of its original source picture.

Capture comparable approaches and fights from the same location where practical. Let the camera establish a scene before changing the situation; distinguish a necessary visibility correction from an unnecessary editorial cut. Stop capture or exit normally and copy the evidence before its bounded ring overwrites it. Diagnostic video samples 12 frames per second and can miss a one-frame flash; retain a higher-rate recording when investigating such a defect.

## Runtime acceptance

Use disposable test characters/worlds for staged combat. Ordinary remote players should remain unmodded; local self-follow is useful for camera checks but does not validate replicated multiplayer evidence.

| Scenario | What must be observed |
| --- | --- |
| One ordinary hostile nearby, then several approaching before an attack | Nearby hostile presence contributes to direction before combat. Developing pressure reflects observed positions/movement without asserting an AI target or aggression that was not observed. |
| Hostiles on one side, then on opposite sides and around the player | Overview choices communicate the observed arrangement. Brief movement does not flip the action axis or cause a cascade of cuts. Walls/floors prevent unrelated rooms from being treated as one surrounding encounter. |
| Four or more cooperative players with several opponents | The camera can cover a coherent larger group, then concentrate on a relevant exchange. Participant changes do not continually reset the scene, and invisible players earn no viewing credit. |
| A compact pack versus two widely separated opponents | Framing responds to measured extent and readability. A distant outlier does not force the primary to become tiny; a compact larger group is not excluded merely by the former three-opponent cap. |
| Small mob, player, then large mob crossing between camera and primary | Foreground actors affect visibility and trigger appropriate bounded correction. The subject itself and the invisible observer do not shadow the subject. Actors behind the subject do not falsely obstruct it. |
| Large actor alongside the subject, with gaps between limbs or broad appendages | Geometry approximations do not treat every point inside a large renderer box as opaque. Record both missed occlusion and false rejection; the system is not pixel-level actor segmentation. |
| Close combat in a room, doorway, dense forest or narrow path | Safe readable fallback remains available when an overview or exchange cannot be composed. Unfulfilled encounter coverage does not become a scene-loading claim or authorize teleport travel. |
| Bow draw toward distant quarry; two ambiguous quarry; cancel/release | Distant interest preserves useful direction without forcing every actor into one group. Ambiguity and unconfirmed release/hit remain explicit. Important observed player moments survive routine edit boundaries. |
| Mob death, unload, teleport, separation and player respawn | Removed actors stop influencing composition promptly; no stale target/occlusion/participant data crosses a character or world boundary. |
| Tamed creatures, passive wildlife, unrelated mobs across a wall | Their visual bodies may obscure the picture, but they do not automatically become hostile encounter participants. |
| A useful player view while every proposed enemy-inclusive view fails | The useful picture stays available with the unmet purpose described honestly. No rapid shot churn or recovery slate solely because optional encounter framing failed. |
| Encounter ends; players resume travel, then Scenic/Creature coverage | Threat/action context expires cleanly. Existing ordinary tracking, wildlife eligibility, scenic direction, output orientation and game audio remain functional. |
| Dense multi-actor scene held for several minutes | Record CPU/GPU frame time, cut frequency and capture health. Computational bounds remain effective and the broader cast does not make rendering/camera work progressively slower. |

Evaluate the footage for sustained actor obscuration, useful overview/exchange duration, clarity of who is near whom, unnecessary cuts, movement/FOV discontinuities, recovery time and frame cost. A favorable numeric score is not a substitute for the visible result. Exact hit attribution, private aiming and complete remote AI intent remain outside the observer's evidence.
