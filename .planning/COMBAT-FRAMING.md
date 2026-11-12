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

13. **Screen-space composition layer (2026-11-09) — three operator-ordered behaviors
    on the live simple-follow camera. No cuts, everything eased, feet always framed,
    lenses INVIOLATE (solo 50 / combat 75 mm), all of #1-#12 intact.**
    - **New pure Core policy `ScreenCompositionPolicy`** (sealed, explicit double clock,
      ArgumentOutOfRangeException contracts, zero per-frame allocations, house style).
      One `Observe(now, lateralScreenVelocity)` drives two coupled outputs:
      - **A. Solo side follows motion (lead room).** The player TRAILS its lateral
        screen-space motion onto a thirds line so the empty two-thirds ahead is lead
        room. Output `PlayerScreenX` = -1/6 (LEFT) or +1/6 (RIGHT). Sign rule: moving
        RIGHT -> player on the LEFT thirds (room opens right), moving LEFT -> RIGHT
        thirds. The side only flips when sustained OPPOSITE lateral motion holds past
        `SustainSeconds = 1.5s`; brief spurts (0.8s) and oscillation keep the current
        side; stationary (|v| < `MotionThresholdMetresPerSecond = 1.2`) keeps it.
        Initial/default side = LEFT (operator's standing preference). Backwards clock
        resets. A `Reason` token ("stationary"/"leading motion"/"confirming side flip").
      - **B. Combat lead-room bias (small, shared).** Second output `LeadOffsetX` in
        [-0.05, +0.05], biased AWAY from the sustained motion (moving right -> negative),
        ramping 0..1 over the same 1.5s sustain window, zero when stationary/oscillating.
        `LeadOffsetMagnitude = 0.05`.
    - **Plugin (SimpleFollowCamera) wiring:**
      - **Lateral velocity** = player world position delta / dt, projected onto the
        PRESENTED camera-right axis (from the orbit horizontal axis: cam->player
        forward = (sin h, 0, cos h), right = (cos h, 0, -sin h)), low-passed over
        `VelocitySmoothSeconds = 0.3s` (exponential 1-exp(-dt/tau)). Warp-sized deltas
        and non-finite values zeroed; `hasVelocitySample` re-seeds cleanly after a warp.
      - **Solo X** eased with `Mathf.SmoothDamp` `SoloXEaseSeconds = 1.0s` (slow weighted
        slide through centre on a flip, never a snap). The composer target is
        `soloScreenX + leadOffsetX`. Combat X flow UNCHANGED otherwise: the combat side
        still comes from `CombatFramingPolicy` stickiness; combat X = `Lerp(soloScreenX +
        leadOffsetX, -side*(1/6) + leadOffsetX, combatBlend)`. On release the blend eases
        back to the solo policy's side. `leadOffsetX` eased `LeadEaseSeconds = 0.8s`, only
        non-zero while the combat phase is engaged (zero target in solo).
      - **C. Group vertical balance.** New pure Core `GroupFramingPolicy.CombineGroupSpans(
        playerBelow, playerAbove, enemyBelowEq, enemyAboveEq, weight)` ->
        (below, above): groupBelow = Lerp(playerBelow, max(playerBelow, enemyBelowEq), w);
        groupAbove = Lerp(playerAbove, max(playerAbove, enemyAboveEq), w). The max keeps
        the player always framed (never cropped to fit a smaller enemy). `SolvePlayerDolly`
        now feeds these GROUP spans to `FramingFill` whenever combat has a live framed
        enemy with sparse weight > 0 (player-only solve when solo or w == 0). The enemy's
        vertical extents are projected to equivalent metres at the player's planar
        distance in the Plugin (`TryEnemyGroupSpans`): equivalentMetres = extent *
        dPlayer / dEnemy (dEnemy = orbit radius + axial offset along cam forward), clamped
        [0, 6]. The angle/pose math stays in the Plugin (needs the live camera pose); the
        span combination is pure Core (testable).
      - **Projection-verified aim direction (the spec's open question):** a small low
        creature grows the group BELOW span; to keep the lowered feet on their fixed
        screen target the solved aimScreenY moves MORE NEGATIVE (UP toward the head,
        Y-down), and the dolly pulls back — the framing lifts so the creature's extent
        is captured instead of empty sky. (Player-only aimY -0.174 -> grouped -0.239 at
        the combat lens, verified by forward projection in the test suite.)
      - Diagnostics line extended: "; side R" when the solo side is RIGHT, "; lead +0.NN"
        when |leadX| > 0.01. The "; fill d=.. aimY=.." tail is unchanged.
      - Reset/rebind/warp: `composition.Reset()` (side -> LEFT, lead 0), soloScreenX ->
        SoloScreenX, leadOffsetX 0, velocity sample voided. Enemy hard visibility (the
        dolly fit / head-band) is UNAFFECTED by the lead bias.
    - **Tests: +12 Core (1500 -> 1512).** ScreenCompositionPolicy (8): default LEFT;
      sustained opposite motion >= 1.5s flips; 0.8s spurt never flips; oscillation keeps
      side; stationary keeps side; lead offset sign/magnitude/clamp/zero-when-stationary;
      clock rollback reset; contracts. GroupFramingPolicy CombineGroupSpans (4): w=0
      identity, w=1 max, midpoint lerp, group-fill small-creature aim direction +
      projection exactness, contracts. All three suites green (Core 1512, CM 128, Hooks
      45), 0 warn / 0 err. Runtime footage verification outstanding.

14. **Tight-space shot ladder (2026-11-09) — in caves/buildings the camera moves
    CLOSER while the lens stays INVIOLATE and the thirds composition is MAINTAINED;
    the shot degrades gracefully full shot -> medium close -> close-up as the space
    shrinks. The head is ALWAYS kept with its headroom; the body crops feet-up.**
    - **New Core pure solver `GroupFramingPolicy.CroppedFraming(fixedVfovDeg,
      availableDistanceMetres, aboveAimMetres, belowAimFullMetres, topGapFraction)`
      -> readonly struct `CroppedFramingResult { AimScreenY, VisibleBelowAimMetres,
      VisibleBodyFraction }`.** Given the camera CANNOT reach the full-fill distance
      and must sit at `availableDistance`, it PINS THE HEAD (head point = aim + above
      projects to the fixed top target screenY = -0.5 + topGap) and lets the bottom
      frame edge CROP the body. Closed form: htheta = atan(2*(-0.5+topGap)*tan(vfov/2))
      (negative, up); phi_aim = htheta + atan(above/d); aimScreenY = tan(phi_aim)/
      (2*tan(vfov/2)), clamped [-0.45, 0.45]; bottom edge btheta = atan(2*0.5*
      tan(vfov/2)); visibleBelow = d*tan(btheta - phi_aim) (clamp >= 0);
      VisibleBodyFraction = clamp01((visibleBelow + above)/(belowAimFull + above)).
      Contracts: finite, distance > 0.5, spans > 0, vfov (1,170), topGap [0, 0.4];
      ArgumentOutOfRangeException. At the full-fill distance the fraction is ~1 and the
      aim reproduces the FramingFill composition (the two solvers agree at the boundary).
    - **Measured 50 mm ladder (26.9915 deg, above 0.73, belowFull 1.77, topGap 0.06):**
      - d = 5.81 m: aimY = -0.1736, visibleBelow = 1.917, **fraction = 1.000 — "full"**
        (at the exact fill distance 5.742 the cropped aim = the fill aim -0.1706).
      - d = 3.5 m:  aimY = -0.0052, visibleBelow = 0.849, **fraction = 0.632 — "medium"**.
      - d = 2.0 m:  aimY = +0.2975, visibleBelow = 0.188, **fraction = 0.367 — "close-up"**.
      Diagnostic bands: fraction >= 0.85 "full", 0.45-0.85 "medium", < 0.45 "close-up".
    - **Plugin (SimpleFollowCamera) wiring — lens, thirds, lead, Layer 1 all UNCHANGED:**
      - **Boom-room probe (each frame, solo AND combat):** Physics.SphereCastNonAlloc
        from the player aim point along the CURRENT ideal boom direction (unit vector
        toward the ideal orbit position at the live azimuth/elevation — the same rig
        Layer 1 uses), sphere radius 0.35 m, max distance = desiredFillDistance + 0.5 m,
        same solid mask (Default/static_solid/piece/terrain), QueryTriggerInteraction.
        Ignore, Solid() filter (no triggers/Characters, skip zero-distance embeds).
        available = nearest solid hit ? hitDistance - 0.15 : fillDistance. Non-alloc
        buffer (boomHits[8]).
      - **Hysteresis (so doorways do not pump):** a smoothed `availableDistance` field
        shrinks FAST (SmoothDamp 0.25 s when target < current) and recovers SLOW
        (SmoothDamp 1.5 s when target > current). Floored at 1.3 m (deoccluder min
        distance 1 + margin). Re-seeds to the fill distance on reset/rebind/warp.
      - **Radius cap:** desiredDollyRadius = min(existing desired [fill / enemy chain],
        availableDistance), floor 1.3 m (below the 2 m fill minimum), cap DollyMax 18 m.
        The enemy need is capped by availableDistance with a min() AFTER the existing
        max chain, before the clamp (physical space wins).
      - **Tight mode:** when availableDistance < fillDistance - 0.1 m, the aim-Y target
        comes from `CroppedFraming(lensFov, availableDistance, aboveAim, belowAim,
        topGap = TopGapShare*(1-FramingSizeConst) = 0.06)` instead of FramingFill; the
        existing 0.8 s aimScreenY ease carries it. In combat the aim uses the player-only
        cropped solve (enemy fit stays best-effort; the enemy may still be larger than
        the crop — accepted, same as the sparse/head-band best-effort elsewhere).
      - **Diagnostics:** append "; tight d=N.Nm frame=<full|medium|close-up> NN%" when
        tight mode is active (bands above). The "; fill d=.. aimY=.." tail is unchanged.
      - **Layer 1 unchanged:** it arcs for occlusion (azimuth); this handles radial
        cramping (radius). They compose — tight mode caps the radius, recovery picks the
        azimuth, and the probe follows the recovered azimuth each frame.
    - **Tests: +6 Core (1512 -> 1518).** CroppedFraming: head pinned exactly on its top
      target edge across fov/distance/span/topGap combos (projection oracle); bottom
      edge crops the body feet-up where predicted; full-fill distance reproduces the fill
      aim + fraction ~1; fraction strictly decreasing as the camera is forced closer; the
      real 50 mm ladder lands in its full/medium/close-up bands; contracts. All three
      suites green (Core 1518, CM 128, Hooks 45), 0 warn / 0 err. Runtime footage
      verification outstanding.

15. **Eyeline-on-top-third doctrine (2026-11-10) — head safety margins ERADICATED;
    the player's EYELINE owns the whole vertical composition. Operator order: "head
    safety margins never should have been added." Supersedes the VERTICAL parts of
    #12 (FramingFill) and #14 (CroppedFraming); their dolly cap / boom probe / sparse
    gate / head-band / Layer 1 / thirds / lead / lens machinery all REMAIN.**
    - **The doctrine:** the player's eyeline sits on the frame TOP-THIRD line (screen
      Y = -1/6 in Cinemachine Y-down fractions). The camera DISTANCE is solved so the
      FEET land exactly on the bottom edge. Only character height is available, so
      eyeline height = `EyelineFraction` (0.93, tunable from footage) * height. NO head
      margin / NO headroom gap anywhere. Feet margins STAY (feet-in-frame is rule 1).
      Works identically outdoors (feet on the bottom edge) and in a tight space (the
      boom cap pulls the camera in -> feet crop automatically, the eye NEVER leaves the
      top-third line). The eyeline invariance under crop is THE point of the doctrine.
    - **New Core pure solver `EyelineFraming`** (sibling of GroupFramingPolicy in
      GroupFramingPolicy.cs; static, ArgumentOutOfRangeException contracts, house style):
      - `AimYForEyeline(fixedVfovDeg, eyelineScreenY, aimToEyeMetres, distanceMetres)`
        — CLOSED FORM. T=tan(vfov/2); eyeAngle=atan(2*eyelineScreenY*T) (negative = above
        centre); phi_aim = eyeAngle + atan(aimToEye/distance) (the aim sits aimToEye BELOW
        the eye in world); aimScreenY = tan(phi_aim)/(2*T), clamped [-0.45,0.45]. Contracts:
        vfov (1,170), eyelineScreenY [-0.45,0.45], aimToEye >= 0 finite, distance > 0.3.
      - `DistanceForFeetAtEyeline(fixedVfovDeg, eyelineScreenY, aimToEyeMetres, belowAimMetres)`
        — bisection on d in [0.5,60], tol 1e-4, in ANGLE space: g(d) = phi_feet(d) - half
        where phi_feet(d) = eyeAngle + atan(aimToEye/d) + atan(belowAim/d). Both atan terms
        shrink with d, so g is STRICTLY DECREASING (documented) — a single root where the
        feet sit on the bottom edge angle (half). Angle space is used (not screenY) because
        screenY wraps through tan's pole when phi_feet > 90 deg at tiny d. Contracts as
        above, belowAim > 0.
    - **Mechanisms REMOVED (every head-margin / headroom mechanism found):**
      - `FramingFill` + `FramingFillResult` (Core) — the fill solved a framingSize/topGap
        composition that parked headroom above the head. DELETED (+ its 6 tests).
      - `CroppedFraming` + `CroppedFramingResult` (Core) — pinned head+margin at a topGap
        from the frame top. DELETED (+ its 6 tests).
      - `CombineGroupSpans` (Core) — fed group AIM blending; the eyeline owns vertical now,
        so the aim blend is gone. DELETED (+ its 4 tests). (Enemy DISTANCE needs stay.)
      - Plugin consts `FramingSizeConst` (0.9), `TopGapShare` (0.6), `TightTopGapMin` (0.01),
        `PlayerHeadMargin` (0.35), `EnemyHeadMargin` (0.25). DELETED.
      - Plugin `ResolveTightAim` (CroppedFraming path + the #52 head-margin taper
        `aboveTight = aboveAim - nearness*PlayerHeadMargin`) -> replaced by `ResolveEyelineAim`.
      - Plugin `TryEnemyGroupSpans` (group vertical balance projection) -> DELETED.
      - Enemy ABOVE-span lost its margin in BOTH the group projection (deleted) and the
        `RequiredDistanceForEnemy` call site: enemyAbove = max(0.01, enemyHeight - aimHeight),
        NO margin. HeadBandBelowSpan oversized-enemy rule unchanged (content rule, not safety).
    - **New Plugin consts:** `EyelineFraction = 0.93f`, `EyelineScreenY = -(1f/6f)`.
    - **Plugin flow (SimpleFollowCamera, solo AND combat):** eyeHeight = EyelineFraction*h
      (same scale clamps as aimHeight); aimToEye = max(0.01, eyeHeight - aimHeight);
      fillDistance = DistanceForFeetAtEyeline(lensFov, EyelineScreenY, aimToEye, aimHeight +
      PlayerFeetMargin). desiredDolly = clamp(max(fillDistance, enemy-need chain), TightFloor,
      DollyMax) then min with boom-room availableDistance (tight cap) exactly as before. aimY
      = AimYForEyeline(lensFov, EyelineScreenY, aimToEye, LIVE boom-capped radius) so aim and
      boom agree; the existing 0.8 s aimScreenY SmoothDamp carries it. tight diagnostics:
      visible fraction = (visibleBelowAim + headTopAboveAim)/fullBody, head top = h - aimHeight
      (no margin). Diagnostic tail changed "; fill d=.." -> "; eyeline d=N.Nm aimY=-0.NN".
    - **Exact solved distances (h=1.9, aim=0.8h=1.52, eye=0.93h=1.767, aimToEye=0.247,
      belowAim=1.77, eyelineY=-1/6):**
      - 50 mm (26.9915 deg): **d = 6.2520 m, aimY = -0.0841** (slightly below -1/6, as the
        aim is below the eye in world). Eye lands on -0.16667, feet on +0.50000.
      - 75 mm (18.1805 deg): **d = 9.4205 m, aimY = -0.0846.** Eye on -0.16667, feet +0.50000.
    - **Tests: +8 EyelineFraming (projection oracle), -16 deleted (FramingFill 6 + CroppedFraming
      6 + CombineGroupSpans/group-fill 4) => NET -8. Core 1518 -> 1510.** Cases: eye pin exact
      across lens/distance combos; feet on the bottom edge at the solved distance; minimality
      (d-0.05 crops feet); eyeline invariance under crop (the doctrine); real 50/75mm bands +
      aim between eyeline and centre; tiny aimToEye collapses aim onto the eyeline; both
      contract sets. All three suites green (Core 1510, CM 128, Hooks 45), 0 warn / 0 err.
      Runtime footage verification outstanding.

16. **Look-target doctrine (2026-11-11) — the camera follows the player's GAZE.**
    Operator order: (1) point the camera at the player's look target so whatever
    they are looking at is in frame; (2) the enemy the look-target points at is THE
    framing priority (overrides closest-first); (3) the player's thirds side gives
    LOOK ROOM toward the look target. House rules intact: no cuts, everything eased,
    lenses INVIOLATE (solo 50 / combat 75 mm), eyeline-on-top-third doctrine (#15)
    untouched, Layer 1 occlusion recovery outranks everything, zero per-frame allocs.
    - **Aim signal = the REPLICATED `ZDOVars.s_lookTarget`** (PLAYER-ACTION-API §3a):
      the owner writes `eyePos + GetLookDir()*100` to the ZDO every 0.2 s (~5 Hz);
      every client reads `zdo.GetVec3(ZDOVars.s_lookTarget, Vector3.zero)` (zero =
      unset sentinel). `GetLookDir()`/`m_lookDir` are OWNER-ONLY and are NOT used.
      Aim ray = `normalize(lookTarget - GetEyePoint())`; `GetEyePoint()` is public and
      remote-safe. ZDO via `GetComponent<ZNetView>().GetZDO()`, cached per binding.
    - **Plugin aim sensing (SimpleFollowCamera.UpdateAimSensing):** read per frame;
      valid iff `sqrMagnitude > 0.01` AND finite. SMOOTH the raw direction with a
      SLERP low-pass, time constant `AimSmoothTimeConstant = 0.25 s` (the 5 Hz steps
      must not jolt the camera — the slerp eases across each 0.2 s refresh so the
      azimuth is continuous between updates). Keep `aimAzimuthDegrees`
      (atan2(x,z) — SAME convention as candidate bearings), `aimValid`, and a 1.0 s
      grace (`AimGraceSeconds`) so a brief zero/invalid read holds the last smoothed
      aim before declaring invalid. Rebind/reset/warp clears the cached ZNetView and
      all aim smoothing/validity.
    - **Aim-priority selection (CombatFramingPolicy):** `CombatCandidate` gains
      `AimOffsetDegrees` (caller-computed `|DeltaAngle(aimAzimuth, bearing)|`, 180 when
      aim invalid; contract [0,180] finite). AIMED TARGET RULE: among alive candidates
      within `AimConeDegrees = 15f`, the smallest-offset one is desired; it STEALS the
      current target after `AimStealSustainSeconds = 0.35 s` of being the consistent
      aimed candidate (an aim sweep across a crowd never completes the sustain). While
      an aimed candidate holds the slot (`TargetByAim`), distance challenger hysteresis
      is DISABLED (aim outranks distance). An aimed enemy also satisfies ACQUISITION
      from Solo even OUTSIDE the 80-deg front cone (the player is literally looking at
      it), still honouring the 0.35 s engage sustain. Aim moving off a held target
      falls back to normal retention (no instant drop). Aim invalid (180) preserves
      every legacy behavior byte-for-byte (regression-tested both ways).
    - **Solo behind-gaze steering (SoloLookSteer):** when NOT combat-steering and NOT
      Layer 1 steering and aim valid, gently slew `orbit.HorizontalAxis` toward
      `aimAzimuth` so the camera settles BEHIND the player's gaze (look target lands
      center-depth). Speed `clamp(|remaining|*1, 4, 20)` deg/s — the GENTLEST writer in
      the stack. Aim invalid → write nothing (native stay-behind recentering resumes).
    - **Axis-writer precedence (documented in code):** Layer 1 recovery > combat
      two-shot > solo look steer > native recentering.
    - **Look-room side (ScreenCompositionPolicy):** `Observe` gains optional
      `lookOffsetDegrees` (caller: `DeltaAngle(cameraAzimuth, aimAzimuth)`; 0 when
      invalid). When `|lookOffset| >= LookRoomThresholdDegrees = 10f` sustained >= the
      SAME `SustainSeconds = 2.0`, the player sits on the thirds OPPOSITE the look
      (looking right → player left) — LOOK ROOM OUTRANKS motion-trailing. One arbiter:
      the look want and the motion want feed the single existing pending-side timer;
      the look want wins conflicts and is the sole want while past threshold, else the
      motion rule stays in charge. Invalid look (0) = pure legacy motion behavior.
    - **Diagnostics:** "; aim <az>deg off <lookOffset>deg" when valid; the combat
      target is marked "(aimed)" when it holds via `TargetByAim`.
    - **Tests: +11 Core (1510 -> 1521).** CombatFramingPolicy +7 (aimed steal after
      0.35 s; sweep never steals; aim-invalid legacy parity run both ways;
      aimed-outside-cone acquires; aimed retained when aim leaves; aim disables
      distance hysteresis; aim-offset contract). ScreenCompositionPolicy +4 (look flips
      side; sub-threshold defers to motion; look wins a conflict; invalid-look legacy
      parity). All three suites green (Core 1521, CM 128, Hooks 45), 0 warn / 0 err.
      Runtime footage verification outstanding.

17. **Bone-eyeline doctrine (2026-11-11) — the character's EYE BONE is the single
    measured quantity for ALL framing spans; the capsule is demoted to FALLBACK.**
    Operator order: constrain/weight everything against bones — "that's how we can
    tell the eyelines of every enemy as well. make sure the eye bones are now used
    for everything, main character and enemies."
    - **Why the capsule had to go:** NOTHING in the game assembly ever writes
      `CapsuleCollider.height` — the capsule never resizes on crouch/sit/sneak, so a
      crouched player got a full standing-height box and the eyeline pin aimed above
      the real eyes. `GetHeight()` also returns `2*radius` for squat/wide creatures
      (boar, neck). `Character.GetEyePoint()` (IL: `return m_eye.position;`, public,
      remote-safe, the REAL animated eye transform) replaces it. `m_eye` is a public
      Transform with NO null guard inside GetEyePoint — every call site guards it.
    - **The measurement:** `eyeHeight = GetEyePoint().y - transform.position.y`
      (world-space Y delta — carries scale, NO lossyScale multiply on the bone path;
      clamp [0.3, 14] player / [0.3, 24] enemy; non-finite or <= 0.05 m rejected).
      Everything derives from it:
      - `aimHeight = AimFromEyeFraction * eyeHeight`, const `0.8602f` (= 0.8/0.93 —
        preserves the standing aim point exactly; THE tunable).
      - `aimToEye = eyeHeight - aimHeight` (= 0.1398 * eyeHeight, ~0.247 m standing).
      - head top (tight visible-fraction + enemy above-span + occlusion ray spread)
        = `HeadAboveEyeFactor * eyeHeight`, const `1.0753f` (= 1/0.93, crown ~7.5%
        above the eyes). The HEAD bone is deliberately NOT used — one bone, fewer
        reads; m_head sits at neck/skull-base anyway.
      - Enemy: same measurement from `enemy.GetEyePoint()`; `enemyAbove =
        HeadAboveEyeFactor*enemyEyeHeight - aimHeight`; the oversized head-band path
        keeps its 30%-band rule with `height = HeadAboveEyeFactor*enemyEyeHeight`.
        REAL enemy eyelines, posture-true, every creature.
    - **FALLBACK (missing/invalid m_eye):** exactly the pre-bone capsule math —
      `Clamp(GetHeight()*EyelineFraction, ...)*lossyScale.y`; `EyelineFraction`
      (0.93) survives fallback-only. `AimHeightFraction` (0.8) deleted (subsumed by
      AimFromEyeFraction applied to the fallback eye).
    - **Smoothing (critical):** the eye bone bobs with the run cycle; raw per-frame
      eyeHeight would pump the dolly solve and aim. Exponential low-pass, tau
      `EyeHeightSmoothSeconds = 0.35f` (lateralScreenVelocity pattern). Player
      reseeds instantly on rebind/warp/Reset (zeroed in ResetRecovery); enemy
      smoothed per engaged target, reseeded on combat target change (ResetCombat).
      CROUCH/SIT is exactly what we want to track: the low-pass passes sustained
      posture through in ~1 s while killing per-step bob. Zero per-frame allocs.
    - **Aim-sensing consistency:** UpdateAimSensing's eye read now guards `m_eye`
      and falls back to the smoothed eye height (was a hardcoded `up*1.6`).
    - **Standing invariant (eye 1.767 m):** aim 1.52000, aimToEye 0.24703, belowAim
      1.76997 — at 50 mm **d = 6.2520 m, aimY = -0.0841**, identical to #15's
      capsule numbers to 4 decimals. Core untouched (EyelineFraming inputs are
      metre spans; signatures unchanged) — this is a Plugin-only change.
    - **Diagnostics:** "; eye h=1.77" (" (capsule)" suffix on fallback) + enemy
      ", eye 2.10" token in the combat segment for footage review of creature bones.
    - **Tests:** Core 1521 / CM 128 unchanged; Hooks 45 -> 48 (+3 BoneEyelineAudits:
      GetEyePoint public-instance Vector3 reader; IL reads exactly m_eye
      Transform.position with no internal guard; m_eye public instance Transform).
      0 warn / 0 err. Runtime footage verification outstanding.

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

18. **Eyeline containment + absolute frame backstop (2026-11-11) — the player's
    eye STAYS in its third, and NEVER leaves frame.** Operator defect: "when the
    character is walking around and the camera is trying to frame up the character
    and the enemy ... sometimes it's getting way out of frame and we're letting
    them go too much"; standing rule "Don't let the player make it out of frame.
    Ever."
    - **Diagnosis (ranked, projection math at 75 mm / 16:9, tan(vfov/2)=0.16,
      tan(hfov/2)=0.2844):**
      1. Composer hard limits too loose: 0.6 x 0.55 rect centered on
         ScreenPosition let the AIM point drift ±0.30/±0.275 from its composed
         target — player third 1/6 + 0.30 = x 0.467, essentially AT the frame
         edge, before the undamped pass bit. The eye rides the same excursion.
      2. Lookahead (0.25 s, IgnoreY): the damped pass tracks the PREDICTED point,
         so a 7 m/s run displaces the real player 1.75 m ≈ 0.38 screen fraction
         toward the trailing edge at 8 m — pinned against the loose hard rect.
      3. Composer damping lag: 0.15 s tau at a 50 deg/s subject rate (7 m/s at
         8 m, or the 45 deg/s azimuth slew) = ~7.5 deg ≈ 0.22 fraction transient.
      4. Combat aim-Y solved at desiredDollyRadius but presented at the eased
         dollyRadius: ~0.85 deg ≈ 0.05 fraction vertical error through every
         dolly transition (engage 6.25→10 m).
      5. Azimuth slew cap 45 deg/s lagged close strafes (4 m/s at 4 m = 57 deg/s
         bearing rate) — mostly enemy-side thirds error.
    - **LAYER A — thirds containment:** upstream HardLimitsRect is CENTERED on
      ScreenPosition (Offset 0), i.e. on the live composition target (eased
      thirds X + lead, eyeline aim Y) — sizing the rect IS the band. Combat
      HardLimits.Size = 0.22 x 0.22 (aim excursion ≤ ±0.11), lerped from the solo
      0.6 x 0.55 by combatBlend (tighten/release shares the engage ease, no
      snaps). Eye == aim excursion: AimYForEyeline makes solved aimY = eyeline +
      the fixed aim→eye angle, so clamping the aim at ±0.11 holds the eye within
      0.12 of its line (proven over the full lens/distance envelope by the new
      Core test). Band sized so WALK stays damped (2 m/s at 8 m: 14 deg/s·0.15 s
      = 2.1 deg = 0.065 fraction; walk lookahead lead 0.5 m = 0.11, exactly the
      band) while RUN is contained undamped at the band edge. The hard pass runs
      every frame (LookAtTargetAttachment is reset to 1 by CinemachineCamera;
      nothing in this rig lowers it) and clamps ReferenceLookAt (the REAL target,
      not the lookahead point).
    - **LAYER B — absolute backstop:** Core FinalFrameClamp applied to the player
      EYE POINT (GetEyePoint, m_eye-guarded, smoothed-eye fallback) as the LAST
      pose mutation in TrySolve, every frame, solo + combat, default band
      0.42/0.38. Mirrors the directed rig byte-for-byte: ShotProjection.Measure
      projection (centred Y-up fractions), minimal yaw about world up / pitch
      about camera right, re-project + full look-at fallback on residual or
      behind-camera. Undamped, fires only outside the band — zero cost when
      framing is healthy; catches deoccluder displacement and any composer-bypass
      transient. Rotations only — never a cut. Diagnostics "; frame clamp"
      (1 s latch).
    - **Cheap confirmed fixes:** combat AND solo aim-Y now solved at the LIVE
      eased dollyRadius (new solveDistance param on ResolveEyelineAim;
      fillDistance keeps tight-space detection semantics). Combat azimuth slew
      ceiling 45 → 60 deg/s (covers the 57 deg/s close-strafe case; floor 8 and
      gain 1.5 unchanged — 60 only engages past a 40 deg arc, feel preserved).
    - **Tests:** FinalFrameClampTests +3 (backstop zero-cost on the tightened
      band; eye-within-0.12 containment invariant across 50/65/75 mm and
      4–14 m; combat-lens band restore). Core 1524, CM 128, Hooks 48; 0 warn /
      0 err. Runtime footage verification outstanding.
