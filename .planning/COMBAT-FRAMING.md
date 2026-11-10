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
10. **FOV-floor fit (2026-11-09) — REJECTED by the operator, PR #43 closed unmerged.**
    The approach widened the lens (a fov floor) to fit the player's feet. The
    operator's rule: "that's not how lens choices work. if we say 50, we go 50.
    if we say 75, we go 75mm." The lens is INVIOLATE. This decision never reached
    main; the fitFov path does not exist on this branch. Superseded by #11.
11. **Dolly group framing (2026-11-09) — the fit is absorbed by CAMERA DISTANCE,
    never the lens.** Cinemachine's native GroupFraming in `DollyOnly` mode
    (obj/upstream/Runtime/Behaviours/CinemachineGroupFraming.cs: SizeAdjustment=
    DollyOnly moves the camera along its boom at a fixed fov; Damping ~2 eases the
    move) is MIRRORED in our player-anchored rig as pure math — we do NOT attach a
    CinemachineTargetGroup / CinemachineGroupFraming. Native group framing needs
    LookAt=group, which would move the rule-1 hard-limit protection off the PLAYER
    onto a group centroid; our composer must keep pinning the player, so the
    semantics are reimplemented instead of delegated.
    - **New Core pure math `GroupFramingPolicy`** (static, ArgumentOutOfRangeException
      contracts, house style):
      - `RequiredDistanceForVerticalSpan(fixedVfovDeg, aimScreenY, belowAim,
        aboveAim)` — CLOSED FORM. The fixed lens makes phi_aim = atan(2*aimScreenY*
        tan(vfov/2)) a constant; available half-angles are down = vfov/2 - phi_aim
        (Y-down positive aimScreenY, aim below centre, toward the feet/bottom edge)
        and up = vfov/2 + phi_aim (toward the head). Required d = max(below/tan(down),
        above/tan(up)). Both extremes land exactly on their frame edge at the result;
        verified by forward projection in tests (feet reach screenY +0.5, d-0.05
        crops). Contracts: vfov in (1,170), aimScreenY in [-0.45,0.45] with both
        angles > 1 deg, spans >= 0.
      - `GroupWeight(separation, full, zero)` — the SPARSE GATE. Smoothstep 1->0 as
        the planar player-enemy separation grows from full (10 m, weight 1) to zero
        (16 m, weight 0), clamped outside. A far enemy never drags the camera back
        into a sparse two-shot (rule 4) even though combat state / thirds continue.
      - `RequiredDistanceForEnemy(...)` — enemy on the player ground plane at an
        axial depth offset along the camera forward; conservative, treats the
        enemy's extent at planar distance (d_player + axialOffset) so d_player =
        span fit - axialOffset, floored at 1 m (axialOffset negative when the enemy
        is nearer the camera than the player, pushing the camera further out).
      - `HeadBandBelowSpan(enemyAboveAimExtent, headKeep)` — the oversized-enemy
        fallback: when the full body cannot fit within the dolly cap, keep the top
        headKeep band (feet may crop, rule 3). Returns the reduced below-aim span.
    - **Plugin (SimpleFollowCamera) wiring** — everything else (lens blend 50<->75,
      thirds solve, Layer 1 priority, flee retention) is UNCHANGED:
      - Margins: player feet 0.25 / head 0.35, enemy feet 0.15 / head 0.25.
        GroupFull 10 m, GroupZero 16 m, DollyMax 18 m, headKeep = max(0.8, 30% of
        enemy height), ease ~0.7 s SmoothDamp (within the 1-2 s loss allowance).
      - Each frame (solo AND combat): playerNeed from the live blended lens fov at
        aimScreenY 0.05. In combat with a live target and weight > 0: compute the
        enemy's axial depth along the current camera forward (orbit axis), solve the
        enemy full-body need; if it exceeds DollyMax, recompute with the head band;
        effective = max(playerNeed, Lerp(playerNeed, enemyNeed, weight)).
      - desiredRadius = clamp(max(SimpleFollowDistance, playerNeed, weightedEnemy),
        1, DollyMax); a SmoothDamp'd dollyRadius field eases toward it and becomes
        orbit.Radius (replacing the raw SimpleFollowDistance assignment). dollyRadius
        is seeded to SimpleFollowDistance on create/reset/rebind/warp. The thirds
        SolveOrbitAzimuth and the Layer 1 occlusion probes both receive the LIVE
        dollyRadius so the two-shot stays exact while dollying.
      - Diagnostics append "; dolly N.Nm" when dollyRadius exceeds SimpleFollowDistance
        by > 0.2 m, plus " (head)" when the head-band fallback is active.
    - **Real numbers (verified, player h=1.9, aimHeight 1.52, aimY 0.05, margins
      0.25/0.35 => belowAim 1.77, aboveAim 0.73):**
      - 50 mm solo (26.9915 deg vFov): playerNeed ~= **8.24 m** — ABOVE the 6 m
        default, so solo dollies out ~2.2 m to keep the feet in frame. EXPECTED.
      - 75 mm combat (18.1805 deg vFov): playerNeed ~= **12.32 m** — comfortably
        UNDER the DollyMax 18 m cap (~5.7 m of headroom). The operator's worry that
        75 mm might blow the cap does not materialise: a 1.9 m player's feet fit at
        ~12.3 m, so **DollyMax stays 18 m** (chosen from the math, not raised).
    - Distance-law note: dollying OUT never parks the camera; FollowDistanceInvariant
      and the deoccluder are untouched. The lens is INVIOLATE throughout (#10's
      rejected fov-floor is gone).
12. **Framing FILL (2026-11-09) — supersedes #11's fit-only minimum + fixed shoulder
    pin.** Live symptom after #44: the player read TOO SMALL and the camera sat too
    far (~8.24 m solo). Root cause: #11's `RequiredDistanceForVerticalSpan` guarantees
    the body FITS but not that it FILLS, and the fixed composer pin (shoulder at
    screen Y +0.05) wasted the frame top — feet at the bottom edge, head barely past
    centre, ~40% empty sky. The operator's rule for solo: 50 mm lens, the character
    FILLS the frame (full-frame body) on the LEFT THIRDS.
    - **New Core pure solver `GroupFramingPolicy.FramingFill(fixedVfovDeg,
      belowAimMetres, aboveAimMetres, framingSize, topGapShare)` -> readonly struct
      `FramingFillResult { DistanceMetres, AimScreenY }`** (Cinemachine GroupFraming
      FramingSize concept): given the body span below/above the AIM POINT (margins
      folded by the caller) and a fixed vfov, solve BOTH the camera distance AND the
      aim-pin's screen Y so the body spans exactly `framingSize` (fraction of frame
      height): feet land at screenY = +0.5 - bottomGap, head at -0.5 + topGap, where
      topGap = topGapShare*(1-framingSize), bottomGap = (1-topGapShare)*(1-framingSize).
      Exact perspective projection (matches the repo tests): a point at signed angle
      phi below forward projects to screenY = tan(phi)/(2*tan(vfov/2)); aim at
      phi_aim = atan(2*aimScreenY*tan(vfov/2)); feet at phi_aim + atan(below/d), head
      at phi_aim - atan(above/d). Solved by bisection on phi_aim: d_feet =
      below/tan(phiFeet - phi_aim), d_head = above/tan(phi_aim - phiHead); g(phi_aim) =
      d_feet - d_head is strictly increasing (monotone — documented), one root where
      the two agree. Contracts: vfov (1,170), spans > 0, framingSize (0.1,1],
      topGapShare [0,1]; ArgumentOutOfRangeException. Degenerate narrow bands guarded
      by clamping the aim search to the composer's [-0.45,0.45] limit minus a 0.5 deg
      guard. #11's `RequiredDistanceForVerticalSpan`/`RequiredDistanceForEnemy`/
      `GroupWeight`/`HeadBandBelowSpan` all STAY — the enemy/sparse/head-band machinery
      is unchanged; only the player's own fit minimum + fixed pin are replaced by the
      fill. `FramingSizeConst = 0.8`, `TopGapShare = 0.6` (more headroom than foot-room).
    - **Plugin (SimpleFollowCamera):** solo screen X moves from -0.08 to **-(1/6)**
      (LEFT THIRDS). The fixed `SoloScreenY=0.05` pin is replaced by a live `aimScreenY`
      field eased (SmoothDamp ~0.8 s) toward the solved fill aim-Y; the composer
      ScreenPosition becomes (easedX, easedAimY). Each frame `SolvePlayerDolly` calls
      `FramingFill` at the current blended lens fov (belowAim = aimHeight+0.25,
      aboveAim = (h-aimHeight)+0.35) and returns both the fill distance and the aim-Y.
      Distance target: `desired = clamp(max(fillDistance, weightedEnemyNeed), 2, DollyMax)`
      — **SimpleFollowDistance is REMOVED from the max (demoted to a seed/fallback).**
      The #44 enemy-need solves now receive the live eased aim-Y instead of the 0.05
      constant. Diagnostics gained "; fill d=N.Nm aimY=-0.NN". In combat the enemy may
      still pull the camera farther (priority 2), shrinking the player below full-frame
      — accepted; sparse gate + head-band unchanged.
    - **SimpleFollowDistance demoted to SEED:** config stays bound (default 6, 2-30) and
      seeds dollyRadius on create/reset/warp and when spans are unavailable; its
      description now documents that the live distance is solved to FILL and may come
      CLOSER than the configured value. The config is NOT deleted.
    - **Solved real cases (verified, player h=1.9, aimHeight 1.52, margins 0.25/0.35
      => belowAim 1.77, aboveAim 0.73, framingSize 0.8, topGapShare 0.6):**
      - 50 mm solo (26.9915 deg vFov): **distance 6.4707 m, aimScreenY -0.1420**
        (aim ABOVE frame centre — the fill lifts the aim so the body fills top-to-bottom
        instead of parking feet at the bottom edge). Down from #11's ~8.24 m.
      - 75 mm combat baseline (18.1805 deg vFov): **distance 9.7387 m, aimScreenY
        -0.1444** (player-only fill; enemy fit may pull farther in a live two-shot).
    - Tests: +6 Core cases — forward-projection exactness (feet/head on their target
      edges within 1e-3 across vfov/span/framingSize/topShare combos incl. both real
      cases), monotonicity in framing size, symmetric body centres the aim, the two
      real distance bands + aim sign, full-frame (size 1) edge-to-edge, and contracts.

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
