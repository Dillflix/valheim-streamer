# Combat two-shot framing — player states (pre-Layer-2)

User spec (2026-11-09, verbatim intent): before Layer 2 (shot-scale variety), build
player STATES based on action. Two states first: player by themself (solo follow,
current baseline) vs attacking an enemy (combat two-shot). In combat: identify the
closest enemy IN FRONT of the character (player forward vector); frame the enemy in
the right thirds and the main character in the left thirds, or vice versa — the enemy
is ALWAYS in the opposite thirds from the player. When an enemy dies and the next
closest is identified, the camera rotation must ease to it — "nice and smooth and
weighted feeling". No cuts, ever (locked editorial rule 1).

## Locked design decisions

1. **States**: Solo ↔ Combat, owned by a NEW pure Core policy `CombatFramingPolicy`
   (house style: sealed, explicit clock `Observe(double now, ...)`, pure, no Unity
   types, ArgumentOutOfRangeException contracts — model on Core/OcclusionRecoveryPolicy.cs).
   - Enter Combat: a qualifying enemy sustained >= 0.35s (EngageSustainSeconds).
   - Exit Combat: NO qualifying enemy sustained >= 2.5s (ReleaseSustainSeconds) —
     bridges the gap between kills so multi-enemy fights never flicker to Solo.
   - Backwards clock resets state (house pattern).
2. **Qualifying enemy** (sensed by Plugin adapter, passed as candidate list):
   alive, hostile (`ai is MonsterAI && BaseAI.IsEnemy(player, actor)` — exact pattern
   at PlayerSceneContext.cs:289), within CombatRange (config, default 30m), within
   the player's forward cone (dot(playerForward, toEnemy) >= cos(80 deg)).
   Selection = closest qualifying. Retarget hysteresis: a challenger replaces the
   current target only if current is dead/invalid/out-of-cone OR challenger is
   >= 20% + 1m closer.
3. **Framing mechanism** (Plugin, SimpleFollowCamera):
   - Follow/LookAt anchor stays the PLAYER (lookPoint at 0.8x height). Never-out-of-
     frame guarantee (hard limits 0.6/0.55, deadzone, deoccluder) unchanged.
   - Player thirds: composer ScreenPosition.X eases from solo -0.08 to +/-1/6
     (0.167) via critically-damped smoothing (~0.8s settle). Y stays 0.05.
   - Enemy opposite thirds: steer orbit.HorizontalAxis so the horizontal angle
     between cam->player and cam->enemy equals the thirds separation angle:
     sep = 2 * atan(2 * 0.167 * tan(hFov/2)), hFov from live lens fov + aspect.
     Solve desired orbit azimuth from enemy bearing (from player), enemy distance d,
     orbit radius r: angle(delta) = atan2(d*sin(delta), r + d*cos(delta)); invert for
     delta, clamp to best-achievable when infeasible (enemy too close/far). This
     solve is PURE MATH in Core (testable), the Plugin feeds live values.
   - Writing HorizontalAxis cancels native recentering that frame (proven Layer 1
     pattern, SimpleFollowCamera.cs:114-147); on exit we stop writing and native
     stay-behind recentering resumes — continuous by construction.
4. **Side rule**: enemy takes the screen side it is ALREADY nearer (sign of current
   signed bearing of enemy relative to camera forward at engage/retarget time);
   player gets the opposite. Side is sticky per target: re-evaluated only on
   retarget, never mid-target (no flip-flop).
5. **Easing / weighted feel**: azimuth slew speed = clamp(|remaining| * 1.5, 8, 45)
   deg/s (gentler than Layer 1 recovery's 20-90) -> slow start, brisk middle,
   soft settle. Same easing for engage, retarget-after-kill, and release.
6. **Priority**: Layer 1 occlusion recovery (Steering phase) OVERRIDES combat
   steering while engaged — survival beats composition. Combat framing resumes
   after release. Combat state machine keeps running (time still flows) while
   suppressed.
7. **Config** (BroadcastSettings, Camera section): CombatFraming bool default TRUE;
   CombatRange float default 30 (5-80). Nothing else configurable in v1.
8. **Diagnostics**: Simple-follow line gains "; combat: <targetName> <d>m, enemy <L|R>
   thirds" while in Combat (0.25s cadence line already exists).
9. Wildlife cutaways already suppressed in simple mode; directed system untouched.

## Which editorial rules this serves/touches
- Serves rule 7 (one proven layer at a time) — this is the state framework Layer 2+
  will hang off.
- Rule 4 amended BY USER for combat state: player moves from center-left to a
  thirds position while fighting; still hard-pinned, never out of frame.
- Rules 1,2,3,5,6 untouched: no cuts, occlusion still the only vantage-RECOVERY
  trigger (combat steering is composition, explicitly user-authorized), distance
  law, shoulder height, camera not solid.

## Verification
- Core tests for CombatFramingPolicy: enter/exit hysteresis, kill->retarget easing
  target, challenger hysteresis, cone filter, side stickiness, azimuth solve math
  (exact angles both sides, infeasible clamp), separation-angle math vs fov/aspect,
  clock rollback, contracts.
- All three suites + Core/CM/Plugin builds green before commit.
- Runtime footage verification outstanding (user reviews live broadcast).
