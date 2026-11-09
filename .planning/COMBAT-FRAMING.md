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
   current target only if current is dead/invalid/absent OR challenger is
   >= 20% + 1m closer.
   - **The forward cone is an ACQUISITION filter ONLY (flee retention rule, added
     2026-11-09).** Live symptom it fixed: when the player walked/ran AWAY from
     the engaged enemy, the enemy fell behind, left the 80-degree cone, the policy
     starved, released after 2.5s, and native recentering swung the camera to the
     stay-behind view — losing the enemy. Rule now:
     - A NEW target (engage from Solo, challenger steal, or promotion after a
       death) must be InFront — new targets never acquire from behind.
     - The CURRENT target is RETAINED regardless of InFront while it is alive and
       present in the candidate list. The adapter enforces CombatRange by dropping
       out-of-range enemies from the list, so retention ends naturally at range
       (the target simply goes absent). While retained-behind, the orbit azimuth
       is solved for opposite-thirds framing exactly as in front — the geometry
       does not care about the player's facing, so the camera looks BACK at the
       enemy instead of recentering.
     - Challenger hysteresis unchanged (a challenger must be InFront + 20%+1m
       closer). A behind candidate can neither steal nor be promoted.
     - Release timing unchanged: the starvation gate begins only when the current
       target is dead/absent AND no InFront candidate exists.
     - Adapter: `SenseHostiles` already adds behind-the-player enemies (within
       range, alive, hostile) with `InFront=false`; no adapter change was needed —
       verified the candidate list includes them so the policy can retain.
3. **Framing mechanism** (Plugin, SimpleFollowCamera):
   - Follow/LookAt anchor stays the PLAYER (lookPoint at 0.8x height). Never-out-of-
     frame guarantee (hard limits 0.6/0.55, deadzone, deoccluder) unchanged.
   - Player thirds: composer ScreenPosition.X eases from solo -0.08 to +/-1/6
     (0.167). Y stays 0.05.
   - Enemy opposite thirds: steer orbit.HorizontalAxis so the horizontal angle
     between cam->player and cam->enemy equals the thirds separation angle:
     sep = 2 * atan(2 * 0.167 * tan(hFov/2)), hFov from the BLENDED lens fov + aspect.
     Solve desired orbit azimuth from enemy bearing (from player), enemy distance d,
     orbit radius r: angle(delta) = atan2(d*sin(delta), r + d*cos(delta)); invert for
     delta, clamp to best-achievable when infeasible (enemy too close/far). This
     solve is PURE MATH in Core (testable), the Plugin feeds live values.
   - **Azimuth convention (CORRECTED — was a 180-degree bug):** the orbit
     HorizontalAxis value h IS the cam->player azimuth. The camera offset is
     Euler(elev,h,0)*(0,0,-r), so the planar cam->player direction is dir(h). The
     solved axis is therefore `axis = normalize(enemyBearing - side*delta)` with NO
     +180 flip, and the sticky side is chosen from `DeltaAngle(h, enemyBearing)`
     directly (not against h+180). The earlier code added 180 in both the side pick
     (Engage) and the solve (SolveOrbitAzimuth), which framed the enemy on the SAME
     side as the player on live footage. Invariant now regression-tested: feeding the
     solved axis back through apparent = atan2(d*sin(bearing-axis), r+d*cos(bearing-axis))
     yields sign(apparent) == side and |apparent| == separation.
   - Writing HorizontalAxis cancels native recentering that frame (proven Layer 1
     pattern, SimpleFollowCamera.cs:114-147); on exit we stop writing and native
     stay-behind recentering resumes — continuous by construction.
   - **Lens model — solo 50 mm / combat 75 mm (updated 2026-11-09, was 50-deg
     solo / 35 mm combat):** full-frame vertical fov = 2*atan(12/focalMm) deg.
     Solo = 50 mm -> **26.9915 deg**. Combat = 75 mm -> **18.1805 deg** (longer,
     (WIDER, so it holds both actors). Combat is now wider than solo, not tighter.
     - `SimpleFollowCamera.LensFov(mm) = 2*atan(12/max(1,mm))*Rad2Deg`;
       `CombatLensMm = 24f`. soloFov = LensFov(clamp(settings.FollowLensMm, 12, 135)),
       clamped to [10,100] as a non-finite backstop (the [12,135] mm clamp already
       bounds fov to ~[10.1, 90]). combatFovTarget = LensFov(75) = 18.1805.
     - One eased 0..1 blend (combatBlend, SmoothDamp ~0.8s toward the policy
       Weight) drives BOTH the thirds screen-X slide AND the lens change together:
       fov = Lerp(soloFov, combatFovTarget, combatBlend); screenX = Lerp(SoloScreenX,
       -side*1/6, combatBlend). The thirds separation is solved from this SAME
       blended fov (passed in, never read back from the lens), clamped [10,100], so
       the enemy stays pinned on its thirds line as the lens widens. combatFov is
       stored in a field; the TrySolve lens assignment reads it (seeded to soloFov
       on create/reset/out-of-combat). Eases out in lockstep on release; no snaps.
     - The TrySolve failure-path `fov` out-param default is now the solo lens fov
       (LensFov(FollowLensMm)), the simple camera's own lens — not the dormant
       directed knob.
   - **`Camera.FollowLensMm` is a NEW config key (migration, same reason as
     FollowFieldOfView):** the deployed broadcast machine's BepInEx cfg has
     persisted values under old keys (Config.Bind keeps saved values), so a new
     key is required to force the lens default to actually apply. Default 50f,
     range [12,135] mm. `Camera.FollowFieldOfView` is LEFT BOUND unchanged — it
     feeds the dormant directed/scenic system (CameraRig/CinemachineShotRig/
     ScenicProduction); the simple-follow camera no longer reads it.
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
   - **FollowFieldOfView rename:** the follow-fov config key was renamed from
     `Camera.FieldOfView` to `Camera.FollowFieldOfView` (default 50, range 30-110).
     The deployed broadcast machine's BepInEx cfg had persisted FieldOfView=75 from
     the old default, so Config.Bind kept the saved 75 and the new 50 default never
     applied. Renaming the key forces a fresh entry at the correct default. The C#
     property name `FieldOfView` is unchanged (every other file references it).
     Solo follow uses this fov; combat eases from it to the 35 mm combat lens.
8. **Diagnostics**: Simple-follow line gains "; combat: <targetName> <d>m, enemy <L|R>
   thirds" while in Combat (0.25s cadence line already exists).
9. Wildlife cutaways already suppressed in simple mode; directed system untouched.
10. **Full-body fit floor (locked 2026-11-09):** the mm lens targets (solo 50 /
    combat 75) become AESTHETIC targets; the PRESENTED vertical fov takes a
    widening-only floor solved from each actor's REAL body span. Never narrows
    below the lens target; never exceeds an 85-deg fisheye cap.
    - **Rule 1 (hard): the player's feet are ALWAYS in frame.** A pure static
      `FrameFitPolicy.RequiredVerticalFovDegrees(aimScreenY, belowAimMetres,
      aboveAimMetres, planarDistanceMetres)` solves (by bisection in [1,120] deg
      to 5e-4) the minimal vfov that fits a segment below+above the AIM POINT when
      the aim is pinned at screen fraction aimScreenY (Cinemachine Y-down, 0.05 =
      aim below centre, so phi_aim = atan(2*aimScreenY*tan(vfov/2)) biases room to
      the head and tightens the feet). Player spans: belowAim = aimHeight + 0.25
      (feet margin), aboveAim = (h - aimHeight) + 0.35 (head margin), aimHeight =
      0.8*h. Real size via GetHeight()*lossyScale.y.
    - **Rule 2 (best-effort, capped 85): the engaged enemy's body fits too.** An
      overload `RequiredVerticalFovFromAnglesDegrees(aimScreenY, angleBelowAimDeg,
      angleAboveAimDeg)` takes the enemy's bottom (feet - 0.15) and top (head +
      0.25) as angles RELATIVE TO THE AIM DIRECTION (handles a tall troll beside a
      small player). The metre overload delegates to it after atan-ing. The live
      enemy Character is reached via `combatTargetCharacter` (set each frame from
      this frame's candidates, never retained, reset with combat).
    - **Floor = max(playerNeed, min(enemyNeed, 85)); playerNeed is NOT capped by
      the enemy cap.** finalFovTarget = clamp(max(lensBlendedFov, floor), 10, 85).
      A pathological playerNeed above 85 (deoccluder jammed against the player)
      still clamps to 85 — documented, accepted.
    - **Distances use the PRESENTED camera pose** (last frame's actual position,
      incl. deoccluder displacement; falls back to the ideal orbit distance before
      the first presented frame). The floor reacts to where the shot really is.
    - **Ease:** `fitFov` SmoothDamps toward finalFovTarget over ~0.5s (within the
      user's 1-2s feet-loss allowance — fast enough to rescue feet, slow enough not
      to pump). The LENS reads fitFov each frame (replaces the direct combatFov
      assignment; combatFov now carries the mm-lens blend ONLY). The thirds solve
      reads the SAME fitFov the lens carries this frame (no read-back). Seeded to
      soloFov on create/reset/rebind; reconverges in ~0.5s.
    - **Diagnostics:** the simple-follow line appends "; fit +N.N°"
      (fitFov - lensBlendedFov) only when the floor is actually widening (>0.3 deg)
      so footage review can see the constraint working.
    - **HONEST NOTE — the floor dominates 75mm at the default 6m orbit.** For a
      1.9m player at d=6: belowAim=1.77m => angle_down=atan(1.77/6)=16.43 deg, which
      ALONE already exceeds half of the 75mm 18.18 deg vfov (9.09). With the 0.05
      below-centre aim pin reducing down-space further, the solved playerNeed lands
      at ~36.7 deg (verified in test). So the 75mm aesthetic survives mainly at
      longer deoccluder-free distances or if the operator raises SimpleFollowDistance;
      at the default 6m orbit the feet floor correctly wins. This is expected, not a
      bug — rule 1 (feet) outranks the lens look by design.
    - Untouched: deadzone, hard limits, deoccluder, Layer 1 steering, state machine,
      screen-X blend.

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
  - Flee retention (added 2026-11-09): engage an InFront target then flip InFront
    to false — policy stays Combat on the SAME target, azimuth still solved for
    opposite thirds (exact value asserted), no release past 2.5s while it lives,
    release only once it leaves the list. Acquisition still gated: a behind-only
    candidate never engages from Solo however long sustained. A behind challenger
    cannot steal; after the front target dies a behind-only survivor is not
    promoted and the policy starves/releases after 2.5s.
- All three suites + Core/CM/Plugin builds green before commit.
- Runtime footage verification outstanding (user reviews live broadcast).
