# Solo observer test (0.6.0 and later)

This tests the camera-relative body and native teleport/return path **without another player**. It is client-side code running in a closed local world. This procedure exercises the offline path. For the separately configured 0.7.0 companion, use [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md). These steps have not yet been run inside Valheim; record results rather than treating successful compilation as a pass.

## Setup

1. Download the successful appropriate development branch Actions artifact, or build that branch against matching real game assemblies. Check the startup log says the selected build version.
2. With Valheim closed, replace both `ValheimBroadcaster.dll` and `ValheimBroadcaster.Core.dll` in `BepInEx/plugins/ValheimBroadcaster/` inside the selected Thunderstore profile. Keep only one copy of each DLL in the profile. BepInEx and Jötunn remain installed through the profile. Launch modded as usual; add `-console` to the launch arguments for F5 commands.
3. Select a disposable local world and test character. Leave **Start Server** unchecked. Joining your dedicated server while alone does not qualify: observer mode will reject it. Stand on dry ground, away from vehicles, beds and combat.
4. Start without RTMP. Keep `Experimental.RemoteTargets=false`; observer mode does not use that reference-only path. After movement/restoration succeeds, repeat with RTMP enabled to check audio/video continuity.

## First verify local activation and restoration

Run:

```text
broadcast observer start
broadcast observer status
```

Close the console. Expected: changing scenic/creature coverage with `OFFLINE OBSERVER | Watching`, no local broadcaster model, and no ordinary movement/attack/pickup controls. The underlying body follows the camera, two metres below it. Inspect `broadcast observer status` for the **last camera gap** (expected approximately 2m after a valid shot) and distance from the activation point. Those numbers are diagnostics, not proof of invisibility or full scene fidelity.

Press **F8**, or run:

```text
broadcast observer return
```

Expected: `Returning` with a recovery slate, then ordinary view/controls at the original activation point. Native distant teleport intentionally takes several seconds even for a short return. Verify walking, jumping, gravity, collisions, HUD, renderers and ordinary camera behavior. `broadcast off` requests the same return. It is not an immediate physics release at the airborne camera position.

Stop here if restoration fails. Preserve the log and command status; do not proceed to distant travel or a shared world.

## Distant travel without another player

1. In ordinary gameplay, stand at destination **B** on dry ground and run `broadcast probe mark`. This command only saves a world-session coordinate; do **not** run `broadcast probe start` for this test.
2. Travel normally to a distant safe point **A**, far enough that B is unloaded (at least 200m is a useful starting point, not a guarantee). Stand still. This becomes the return position.
3. Run `broadcast observer start`, verify local coverage, then run `broadcast observer travel`.
4. Expected: `Travelling`, recovery slate while native teleport moves the character and scene loads, then `Watching` with fresh scouting around B. Status should show destination distance close to B and origin distance close to the A–B separation. Inspect terrain, buildings, actors and effects; readiness alone does not establish scene completeness.
5. Run `broadcast observer return` (or F8 / `broadcast off`). Expected: return to A, then restored ordinary controls/view. Compare the position to where you activated the observer, not where you marked B.

There is a 45-second **unpaused** travel/return budget. Menus that pause the game do not consume it. A timeout, retention failure or adapter exception enters `Suspended`, keeps input/physics protection, and displays a reason. Use `broadcast observer return` to try the bounded return. If that return also fails, it remains suspended; another return command retries it. Exiting the world is the fallback if recovery cannot complete.

The old `broadcast probe start` tests **reference-only acquisition without moving the body**. It retains the 0.5.1 abort guard and can legitimately abort because dual-region retention is unsupported. This observer test moves the body through native teleport; do not interpret the two paths as equivalent.

## Required edge cases — all pending in game

| Case | Expected result |
| --- | --- |
| `observer travel` without a marker or active observer | Useful rejection; ordinary controls/session preserved |
| Another travel request while travelling/returning | Rejected without interrupting the current trip |
| F8/off/return during outbound travel | Outbound teleport cancelled; bounded return begins |
| Repeated off/return while returning | Does not restart teleport or postpone the deadline |
| Timeout or scene-retention failure | Visible suspended reason; player not removed; return remains usable |
| Destination becomes unavailable | No invalid footage; bounded travel, then suspension and return option |
| Old reference probe or F10 while observer active | Rejected; no competing body/reference/camera ownership |
| Begin while airborne, swimming, attached, sleeping, attacking or dodging | Rejected before observer state is acquired |
| Activate on dedicated server or open hosted world, even alone | Rejected; no observer body changes |
| Open local world to multiplayer while observer active | Hosting call blocked with a message; return/stop first |
| Prior god/ghost or Rigidbody settings | Original flags restored after successful return, not blindly set to defaults |
| World exit/re-entry | No stale player/world references or destination marker reused |
| Normal F8 and F10 after observer test | Original modes still work; no remaining input suppression |
| RTMP active before travel | Same encoder session carries slate/footage and game audio; successful return stops output |
| Repeated start/travel/return (20 cycles) | No missing player, retained mask, orphan FFmpeg, duplicated hooks or accumulating errors |

World exit/plugin disable cannot complete an asynchronous return. Cleanup restores owned flags and references at the current location; do not assume it restores the original position or repairs a save taken while roaming. Return normally before leaving/disabling. Saved-position handling, autonomous region travel and long unattended sessions remain unvalidated.

Record source commit, DLL hashes, Valheim/BepInEx/Jötunn versions, configuration, exact commands, phase/reason/distance diagnostics and relevant `BepInEx/LogOutput.log` lines. If the screen blanks, note whether the recovery caption and F5 console remain visible, whether status says `Suspended`, and whether the log contains `Local player destroyed`.
