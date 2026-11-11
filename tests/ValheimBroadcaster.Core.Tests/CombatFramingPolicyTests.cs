using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class CombatFramingPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("A short hostile blip never leaves Solo", ShortBlipStaysSolo);
        yield return ("A sustained qualifying enemy engages Combat", SustainedEngages);
        yield return ("The release gate bridges staggered kills without flicker", ReleaseBridgesKills);
        yield return ("A closer-but-not-20% challenger cannot steal the target", WeakChallengerHeld);
        yield return ("A 20%+1m closer challenger steals the target", StrongChallengerSteals);
        yield return ("A dead target promotes the next closest qualifying enemy", DeathPromotesNext);
        yield return ("Out-of-cone hostiles never qualify", ForwardConeFilter);
        yield return ("A fleeing player retains the enemy behind them", FleeingRetainsBehindTarget);
        yield return ("A behind-only candidate never acquires from Solo", BehindNeverAcquires);
        yield return ("A behind challenger cannot steal or be promoted", BehindNeverStealsOrPromotes);
        yield return ("The enemy screen side is fixed per target and never flips", SideStickiness);
        yield return ("Thirds separation matches the lens exactly", ThirdsSeparationExact);
        yield return ("The orbit azimuth solve is exact on both sides", OrbitSolveExact);
        yield return ("The solved axis frames the enemy on the requested side at separation", OrbitSolveInvariant);
        yield return ("An infeasibly close enemy clamps to the achievable maximum", OrbitSolveClamp);
        yield return ("A clock rollback resets the state machine", ClockRollbackResets);
        yield return ("The policy rejects non-finite and out-of-range inputs", ContractValidation);
        yield return ("An aimed enemy steals from a closer non-aimed after the sustain", AimedStealsCloser);
        yield return ("Sweeping the aim across two enemies never steals", AimSweepNeverSteals);
        yield return ("An invalid aim (180) preserves every legacy behavior", AimInvalidIsLegacy);
        yield return ("An aimed enemy outside the forward cone still acquires", AimedOutsideConeAcquires);
        yield return ("An aimed target is retained when the aim moves off", AimedRetainedWhenAimLeaves);
        yield return ("While aimed, distance hysteresis is disabled", AimDisablesDistanceHysteresis);
        yield return ("The aim-offset field is contract-validated", AimOffsetContract);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float expected, float actual, string message, float tol = 1e-3f)
    { if (Math.Abs(expected - actual) > tol) throw new Exception($"{message}: expected {expected}, got {actual}."); }

    private static CombatCandidate E(int id, float bearing, float distance, bool inFront = true, bool alive = true) =>
        new CombatCandidate(id, bearing, distance, inFront, alive);

    // An enemy with an explicit aim offset (180 = aim invalid / not looking at it).
    private static CombatCandidate A(int id, float bearing, float distance, float aimOffset,
        bool inFront = true, bool alive = true) =>
        new CombatCandidate(id, bearing, distance, inFront, alive, aimOffset);

    private const double Step = 1.0 / 30;
    private const float Radius = 6f;
    private const float Sep = 31f; // A representative live separation; exact values are pinned in the math cases.

    // Drive the policy with a fixed candidate set for a duration at a fixed
    // camera azimuth (the orbit axis the adapter would be presenting).
    private static CombatFramingDecision Drive(CombatFramingPolicy policy, ref double now, double seconds,
        float cameraAzimuth, IReadOnlyList<CombatCandidate>? candidates)
    {
        CombatFramingDecision decision = default;
        for (double end = now + seconds; now < end; now += Step)
            decision = policy.Observe(now, cameraAzimuth, Sep, Radius, candidates);
        return decision;
    }

    private static void ShortBlipStaysSolo()
    {
        var policy = new CombatFramingPolicy();
        var enemy = new[] { E(1, 0f, 8f) };
        double now = 0;
        // 0.3s under the 0.35s engage gate, then clear: never engages.
        var during = Drive(policy, ref now, 0.3, 0f, enemy);
        Check(during.Phase == CombatFramingPhase.Solo, "A sub-gate hostile engaged Combat.");
        var after = Drive(policy, ref now, 0.5, 0f, Array.Empty<CombatCandidate>());
        Check(after.Phase == CombatFramingPhase.Solo && after.TargetId == 0, "Policy did not settle back to Solo.");
    }

    private static void SustainedEngages()
    {
        var policy = new CombatFramingPolicy();
        var enemy = new[] { E(7, 0f, 8f) };
        double now = 0;
        var decision = Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f, enemy);
        Check(decision.Phase == CombatFramingPhase.Combat, "Sustained enemy never engaged.");
        Check(decision.TargetId == 7, "Engaged the wrong target.");
        Check(policy.Phase == CombatFramingPhase.Combat, "Phase property disagrees with the decision.");
        // Weight eases up over time, never snapping to 1 instantly.
        Check(decision.Weight > 0f && decision.Weight <= 1f, "Weight is out of band at engage.");
    }

    private static void ReleaseBridgesKills()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f, new[] { E(1, 0f, 8f) });
        Check(policy.Phase == CombatFramingPhase.Combat, "Setup failed to engage.");
        // The only enemy dies; a brief starve under the release window holds Combat.
        var brief = Drive(policy, ref now, CombatFramingPolicy.ReleaseSustainSeconds - 0.3, 0f,
            Array.Empty<CombatCandidate>());
        Check(brief.Phase == CombatFramingPhase.Combat, "Released before the 2.5s release gate.");
        // A fresh qualifying enemy appears inside the window: Combat continues
        // and retargets, no flicker to Solo.
        var bridged = Drive(policy, ref now, 0.2, 0f, new[] { E(2, 0f, 10f) });
        Check(bridged.Phase == CombatFramingPhase.Combat && bridged.TargetId == 2,
            "Release window failed to bridge to the next enemy.");
        // Now a sustained starve past the gate releases to Solo.
        var released = Drive(policy, ref now, CombatFramingPolicy.ReleaseSustainSeconds + 0.2, 0f,
            Array.Empty<CombatCandidate>());
        Check(released.Phase == CombatFramingPhase.Solo && released.TargetId == 0,
            "Never released after a sustained starve.");
    }

    private static void WeakChallengerHeld()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f, new[] { E(1, 0f, 10f) });
        Check(policy.TargetId == 1, "Setup failed to engage the first enemy.");
        // Challenger at 8.5m is 15% closer (needs 20%) and only 1.5m closer:
        // fails the fraction test, so the target is held.
        var held = Drive(policy, ref now, 0.5, 0f, new[] { E(1, 0f, 10f), E(2, 0f, 8.5f) });
        Check(held.TargetId == 1, "A sub-threshold challenger stole the target.");
    }

    private static void StrongChallengerSteals()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f, new[] { E(1, 0f, 10f) });
        Check(policy.TargetId == 1, "Setup failed to engage the first enemy.");
        // 7m is 30% closer AND 3m closer: clears both margins and steals.
        var stolen = Drive(policy, ref now, 0.2, 0f, new[] { E(1, 0f, 10f), E(2, 0f, 7f) });
        Check(stolen.TargetId == 2, "A qualifying challenger failed to steal the target.");
        // A challenger that is 20% closer in fraction but under the 1m absolute
        // floor (close range) must not thrash: 4m vs 5m is 20% but exactly 1m -
        // the <= keeps it a steal; 4.1m vs 5m is 18% and held.
        var p2 = new CombatFramingPolicy();
        now = 0;
        Drive(p2, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f, new[] { E(1, 0f, 5f) });
        var narrow = Drive(p2, ref now, 0.2, 0f, new[] { E(1, 0f, 5f), E(2, 0f, 4.1f) });
        Check(narrow.TargetId == 1, "An 18% challenger stole at close range.");
    }

    private static void DeathPromotesNext()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        // Engage the near enemy with a farther one also present.
        Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f,
            new[] { E(1, 0f, 6f), E(2, 20f, 12f) });
        Check(policy.TargetId == 1, "Setup engaged the wrong target.");
        // The current target dies; the farther survivor is promoted immediately
        // (current invalid bypasses challenger hysteresis).
        var promoted = Drive(policy, ref now, 0.2, 0f,
            new[] { E(1, 0f, 6f, true, false), E(2, 20f, 12f) });
        Check(promoted.Phase == CombatFramingPhase.Combat && promoted.TargetId == 2,
            "A dead target did not promote the next qualifying enemy.");
    }

    private static void ForwardConeFilter()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        // A close enemy that is out of the forward cone cannot engage; only the
        // farther in-cone enemy qualifies.
        var decision = Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f,
            new[] { E(1, 160f, 4f, false, true), E(2, 0f, 11f, true, true) });
        Check(decision.Phase == CombatFramingPhase.Combat && decision.TargetId == 2,
            "The forward-cone filter admitted a behind-the-player hostile.");
        // With only an out-of-cone hostile, the policy stays Solo.
        var p2 = new CombatFramingPolicy(); now = 0;
        var solo = Drive(p2, ref now, 1.0, 0f, new[] { E(1, 160f, 4f, false, true) });
        Check(solo.Phase == CombatFramingPhase.Solo, "An out-of-cone hostile engaged Combat.");
    }

    // The forward cone is an ACQUISITION filter only: once engaged, the current
    // target is retained while alive even after it falls behind the player (the
    // player flees), and its opposite-thirds azimuth keeps being solved from the
    // live geometry - the camera looks back instead of starving and recentering.
    private static void FleeingRetainsBehindTarget()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        // Engage an in-front enemy dead ahead at 8 m (camera azimuth 0 -> side +1).
        var engaged = Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f,
            new[] { E(1, 0f, 8f, true, true) });
        Check(engaged.Phase == CombatFramingPhase.Combat && engaged.TargetId == 1, "Setup failed to engage.");
        int side = engaged.PlayerScreenSide;
        // The player turns and flees: the SAME enemy is now behind the cone
        // (InFront=false) but still alive and in range. Hold Combat well past the
        // 2.5s release gate - retention never starves while the target lives.
        var fleeing = Drive(policy, ref now, CombatFramingPolicy.ReleaseSustainSeconds + 1.0, 0f,
            new[] { E(1, 0f, 8f, false, true) });
        Check(fleeing.Phase == CombatFramingPhase.Combat && fleeing.TargetId == 1,
            "A behind-the-player (fleeing) target was released instead of retained.");
        Check(fleeing.PlayerScreenSide == side, "The retained target's screen side flipped while behind.");
        // The azimuth is still solved for opposite-thirds framing from the live
        // geometry, exactly as if the enemy were in front.
        float expected = CombatFramingPolicy.SolveOrbitAzimuth(0f, 8f, Radius, Sep, side);
        Near(expected, fleeing.TargetAzimuthDegrees, "retained-behind azimuth");
        // And once the enemy finally leaves range (the adapter drops it from the
        // list), with nothing in front, the release gate fires.
        var released = Drive(policy, ref now, CombatFramingPolicy.ReleaseSustainSeconds + 0.2, 0f,
            Array.Empty<CombatCandidate>());
        Check(released.Phase == CombatFramingPhase.Solo && released.TargetId == 0,
            "An absent target never released after retention ended.");
    }

    // Acquisition stays gated: from Solo a behind-only hostile never engages, no
    // matter how long it is sustained - the cone still guards new targets.
    private static void BehindNeverAcquires()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        var solo = Drive(policy, ref now, CombatFramingPolicy.ReleaseSustainSeconds + 2.0, 0f,
            new[] { E(1, 180f, 6f, false, true) });
        Check(solo.Phase == CombatFramingPhase.Solo && solo.TargetId == 0,
            "A sustained behind-only hostile engaged Combat from Solo.");
    }

    // A behind candidate can neither steal from a live front target nor be
    // promoted after that target dies: promotion requires InFront. With no front
    // candidate left, the policy starves and releases after the 2.5s gate.
    private static void BehindNeverStealsOrPromotes()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        // Engage a front target at 10 m; a much closer enemy sits behind (6 m).
        Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f,
            new[] { E(1, 0f, 10f, true, true), E(2, 180f, 6f, false, true) });
        Check(policy.TargetId == 1, "Setup failed to engage the front target.");
        // The behind enemy is 40% + 4 m closer but out of cone: it must NOT steal.
        var held = Drive(policy, ref now, 0.5, 0f,
            new[] { E(1, 0f, 10f, true, true), E(2, 180f, 6f, false, true) });
        Check(held.TargetId == 1, "A behind challenger stole the live front target.");
        // The front target dies; only the behind enemy remains. It cannot be
        // promoted (acquisition is InFront-gated), so Combat bridges then releases.
        var brief = Drive(policy, ref now, CombatFramingPolicy.ReleaseSustainSeconds - 0.3, 0f,
            new[] { E(1, 0f, 10f, true, false), E(2, 180f, 6f, false, true) });
        Check(brief.Phase == CombatFramingPhase.Combat && brief.TargetId != 2,
            "A behind-only survivor was promoted after the front target died.");
        var released = Drive(policy, ref now, 0.6, 0f,
            new[] { E(1, 0f, 10f, true, false), E(2, 180f, 6f, false, true) });
        Check(released.Phase == CombatFramingPhase.Solo && released.TargetId == 0,
            "The policy never released with only a behind-only survivor.");
    }

    private static void SideStickiness()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        // Camera azimuth 0 -> cam->player forward is 0 (the axis value IS the
        // cam->player azimuth). An enemy bearing 170 is to one side of forward
        // (signed +170 -> side +1); engage fixes that side.
        var engaged = Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f,
            new[] { E(1, 170f, 8f) });
        Check(engaged.Phase == CombatFramingPhase.Combat, "Setup failed to engage.");
        int side = engaged.PlayerScreenSide;
        Check(side == 1, "Enemy at signed +170 must take side +1.");
        // The same target drifts to the other side of forward (bearing 190,
        // signed DeltaAngle(0,190) = -170); the side must NOT flip mid-target.
        var drifted = Drive(policy, ref now, 0.5, 0f, new[] { E(1, 190f, 8f) });
        Check(drifted.TargetId == 1 && drifted.PlayerScreenSide == side,
            "The screen side flipped mid-target.");
        // A retarget re-evaluates the side for the new enemy.
        var retargeted = Drive(policy, ref now, 0.2, 0f,
            new[] { E(1, 190f, 8f), E(2, 200f, 5f) });
        Check(retargeted.TargetId == 2, "Setup failed to retarget.");
    }

    private static void ThirdsSeparationExact()
    {
        // hFov = 2*atan(tan(25)*16/9); sep = 2*atan((1/3)*tan(hFov/2)).
        Near(30.894195f, CombatFramingPolicy.ThirdsSeparationDegrees(50f, 16f / 9f), "sep(50,16:9)");
        Near(21.786789f, CombatFramingPolicy.ThirdsSeparationDegrees(60f, 1f), "sep(60,1)");
        Throws(() => CombatFramingPolicy.ThirdsSeparationDegrees(0f, 1.6f));
        Throws(() => CombatFramingPolicy.ThirdsSeparationDegrees(50f, 0f));
        Throws(() => CombatFramingPolicy.ThirdsSeparationDegrees(50f, 10f));
    }

    private static void OrbitSolveExact()
    {
        float sep = CombatFramingPolicy.ThirdsSeparationDegrees(50f, 16f / 9f); // 30.894195
        // Enemy dead ahead (bearing 0), d=10, r=6. delta = sep + asin((r/d)sin sep)
        // = 48.837309. The axis IS the cam->player azimuth: axis = bearing - side*delta.
        float right = CombatFramingPolicy.SolveOrbitAzimuth(0f, 10f, 6f, sep, +1);
        float left = CombatFramingPolicy.SolveOrbitAzimuth(0f, 10f, 6f, sep, -1);
        Near(-48.837309f, right, "axis side+1");
        Near(48.837309f, left, "axis side-1");
        // The two sides mirror across the cam->player baseline (bearing 0).
        Near(right, -left, "sides mirror");
        // A bearing offset rotates the solution rigidly.
        float offset = CombatFramingPolicy.SolveOrbitAzimuth(90f, 10f, 6f, sep, +1);
        Near(CombatFramingPolicy.DeltaAngle(right, offset), 90f, "bearing offset rotates the solve");
    }

    // Regression invariant for the 180-degree sign bug: feeding the SOLVED axis
    // back through the cam-apparent angle must put the enemy on the requested
    // side at exactly the separation. Violated by the old +180 flip, which
    // framed the enemy on the SAME side as the player.
    private static void OrbitSolveInvariant()
    {
        float sep = CombatFramingPolicy.ThirdsSeparationDegrees(50f, 16f / 9f); // 30.894195
        const float d = 10f, r = 6f;
        foreach (float bearing in new[] { 0f, 90f, -120f, 175f })
            foreach (int side in new[] { +1, -1 })
            {
                float axis = CombatFramingPolicy.SolveOrbitAzimuth(bearing, d, r, sep, side);
                // apparent = atan2(d*sin(bearing-axis), r + d*cos(bearing-axis)):
                // the cam-apparent player->enemy angle. Its sign is the enemy's
                // screen side; its magnitude is the achieved separation.
                double delta = (bearing - axis) * Math.PI / 180.0;
                double apparent = Math.Atan2(d * Math.Sin(delta), r + d * Math.Cos(delta)) * 180.0 / Math.PI;
                Check(Math.Sign(apparent) == side, $"Enemy landed on the wrong side (bearing {bearing}, side {side}): apparent {apparent}.");
                Near(sep, (float)Math.Abs(apparent), $"separation off (bearing {bearing}, side {side})");
            }
    }

    private static void OrbitSolveClamp()
    {
        // d=2 < r=6, sep=30: (r/d)sin30 = 1.5 >= 1 -> infeasible. delta clamps to
        // acos(-d/r) = 109.47122, the tangent realising asin(d/r) = 19.47122.
        // axis = bearing - side*delta = 0 - 109.47122 = -109.47122.
        float az = CombatFramingPolicy.SolveOrbitAzimuth(0f, 2f, 6f, 30f, +1);
        Near(-109.471221f, az, "clamped axis");
        // Degenerate zero distance parks behind the player: delta = 180,
        // axis = 0 - 180 = -180 (the cam->player azimuth directly behind).
        float onTop = CombatFramingPolicy.SolveOrbitAzimuth(0f, 0f, 6f, 30f, +1);
        Near(-180f, onTop, "zero-distance parks behind the player");
        Throws(() => CombatFramingPolicy.SolveOrbitAzimuth(0f, -1f, 6f, 30f, +1));
        Throws(() => CombatFramingPolicy.SolveOrbitAzimuth(0f, 10f, 0f, 30f, +1));
        Throws(() => CombatFramingPolicy.SolveOrbitAzimuth(0f, 10f, 6f, 30f, 0));
        Throws(() => CombatFramingPolicy.SolveOrbitAzimuth(0f, 10f, 6f, 180f, +1));
    }

    private static void ClockRollbackResets()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f, new[] { E(1, 0f, 8f) });
        Check(policy.Phase == CombatFramingPhase.Combat, "Setup failed to engage.");
        // World reload: the clock goes backwards. The machine restarts; the
        // engage gate applies anew rather than steering from stale state.
        var reset = policy.Observe(0.1, 0f, Sep, Radius, new[] { E(1, 0f, 8f) });
        Check(reset.Phase == CombatFramingPhase.Solo, "Combat survived a clock rollback.");
        Check(reset.TargetId == 0, "Rollback kept a stale target.");
    }

    private static void ContractValidation()
    {
        var policy = new CombatFramingPolicy();
        var ok = new[] { E(1, 0f, 8f) };
        foreach (var (n, az, sep, r) in new (double, float, float, float)[]
        {
            (double.NaN, 0f, Sep, Radius), (double.PositiveInfinity, 0f, Sep, Radius), (-0.1, 0f, Sep, Radius),
            (1, float.NaN, Sep, Radius), (1, float.PositiveInfinity, Sep, Radius),
            (1, 0f, 0f, Radius), (1, 0f, 180f, Radius), (1, 0f, float.NaN, Radius),
            (1, 0f, Sep, 0f), (1, 0f, Sep, -1f), (1, 0f, Sep, float.NaN)
        })
        {
            bool threw = false;
            try { policy.Observe(n, az, sep, r, ok); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, $"Accepted invalid inputs ({n}, {az}, {sep}, {r}).");
        }
        foreach (var candidate in new[] { E(1, float.NaN, 8f), E(1, 0f, float.NaN), E(1, 0f, -1f) })
        {
            bool threw = false;
            try { policy.Observe(1, 0f, Sep, Radius, new[] { candidate }); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, "Accepted an invalid candidate.");
        }
        // Null and empty candidate lists are legal.
        policy.Observe(1, 0f, Sep, Radius, null);
        policy.Observe(2, 0f, Sep, Radius, Array.Empty<CombatCandidate>());
    }

    // An aimed enemy (offset within the 15-deg cone) STEALS from a closer
    // non-aimed target after AimStealSustainSeconds of being the consistent aimed
    // candidate - aim outranks distance.
    private static void AimedStealsCloser()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        // Engage the near enemy (6 m); nothing is aimed at yet (both offset 180).
        Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f,
            new[] { E(1, 0f, 6f), A(2, 20f, 12f, 180f) });
        Check(policy.TargetId == 1, "Setup engaged the wrong target.");
        // Under the steal sustain the closer non-aimed target is still held.
        var early = Drive(policy, ref now, CombatFramingPolicy.AimStealSustainSeconds - 0.15, 0f,
            new[] { E(1, 0f, 6f), A(2, 20f, 12f, 3f) });
        Check(early.TargetId == 1, "The aimed enemy stole before its sustain elapsed.");
        // Past the sustain it steals despite being farther.
        var stolen = Drive(policy, ref now, 0.3, 0f,
            new[] { E(1, 0f, 6f), A(2, 20f, 12f, 3f) });
        Check(stolen.TargetId == 2 && policy.TargetByAim,
            "The aimed enemy failed to steal after its sustain.");
    }

    // Sweeping the aim across two enemies (each aimed for < the steal sustain)
    // never completes a steal - prevents flicker as the aim crosses a crowd.
    private static void AimSweepNeverSteals()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f,
            new[] { E(1, 0f, 6f), E(2, 20f, 7f), E(3, 40f, 8f) });
        Check(policy.TargetId == 1, "Setup engaged the wrong target.");
        // Aim lands briefly on enemy 2, then enemy 3, each under the 0.35s sustain.
        for (int i = 0; i < 6; i++)
        {
            int aimedId = (i % 2 == 0) ? 2 : 3;
            Drive(policy, ref now, CombatFramingPolicy.AimStealSustainSeconds - 0.1, 0f,
                new[] { E(1, 0f, 6f),
                    A(2, 20f, 7f, aimedId == 2 ? 3f : 180f),
                    A(3, 40f, 8f, aimedId == 3 ? 3f : 180f) });
        }
        Check(policy.TargetId == 1, "A sweeping aim stole the target without a sustained hold.");
    }

    // Aim invalid (offset 180 on every candidate) must preserve legacy behavior
    // byte-for-byte: run the StrongChallengerSteals scenario both with the legacy
    // E() factory and with explicit 180 offsets and assert identical outcomes.
    private static void AimInvalidIsLegacy()
    {
        CombatFramingDecision RunLegacy()
        {
            var p = new CombatFramingPolicy(); double n = 0;
            Drive(p, ref n, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f, new[] { E(1, 0f, 10f) });
            return Drive(p, ref n, 0.2, 0f, new[] { E(1, 0f, 10f), E(2, 0f, 7f) });
        }
        CombatFramingDecision RunAimInvalid()
        {
            var p = new CombatFramingPolicy(); double n = 0;
            Drive(p, ref n, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f, new[] { A(1, 0f, 10f, 180f) });
            return Drive(p, ref n, 0.2, 0f, new[] { A(1, 0f, 10f, 180f), A(2, 0f, 7f, 180f) });
        }
        var legacy = RunLegacy();
        var invalid = RunAimInvalid();
        Check(legacy.TargetId == invalid.TargetId && legacy.TargetId == 2,
            "Aim-invalid diverged from legacy challenger-steal selection.");
        Check(legacy.PlayerScreenSide == invalid.PlayerScreenSide, "Aim-invalid diverged on side.");
        Near(legacy.TargetAzimuthDegrees, invalid.TargetAzimuthDegrees, "aim-invalid azimuth parity");
    }

    // An aimed enemy BEHIND the forward cone (InFront=false) still acquires from
    // Solo after the engage sustain - the player is literally looking at it.
    private static void AimedOutsideConeAcquires()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        var decision = Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f,
            new[] { A(1, 150f, 8f, 4f, inFront: false, alive: true) });
        Check(decision.Phase == CombatFramingPhase.Combat && decision.TargetId == 1,
            "An aimed behind-cone enemy failed to acquire from Solo.");
        Check(policy.TargetByAim, "An aim-acquired target did not report TargetByAim.");
    }

    // An aimed target, once engaged, is not instantly dropped when the aim moves
    // off it - it falls back to normal retention (held while alive and present).
    private static void AimedRetainedWhenAimLeaves()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f,
            new[] { A(1, 0f, 8f, 3f) });
        Check(policy.TargetId == 1 && policy.TargetByAim, "Setup failed to aim-acquire.");
        // Aim moves off (offset 180); no other candidate. The target is retained by
        // normal rules, not dropped, and TargetByAim clears.
        var held = Drive(policy, ref now, 1.0, 0f, new[] { A(1, 0f, 8f, 180f) });
        Check(held.Phase == CombatFramingPhase.Combat && held.TargetId == 1,
            "The target was dropped when the aim moved off instead of retained.");
        Check(!policy.TargetByAim, "TargetByAim did not clear once the aim left.");
    }

    // While an aimed candidate holds the slot, a closer in-front non-aimed
    // challenger that WOULD clear distance hysteresis cannot steal (aim outranks).
    private static void AimDisablesDistanceHysteresis()
    {
        var policy = new CombatFramingPolicy();
        double now = 0;
        // Engage enemy 1 while aiming at it; a much closer non-aimed enemy 2 sits
        // in front at 5 m (would otherwise be a 50%+5m steal).
        Drive(policy, ref now, CombatFramingPolicy.EngageSustainSeconds + 0.1, 0f,
            new[] { A(1, 0f, 10f, 3f), E(2, 0f, 5f) });
        Check(policy.TargetId == 1 && policy.TargetByAim, "Setup failed to aim-engage enemy 1.");
        var held = Drive(policy, ref now, 1.0, 0f, new[] { A(1, 0f, 10f, 3f), E(2, 0f, 5f) });
        Check(held.TargetId == 1, "A closer non-aimed challenger stole while the target was aimed.");
    }

    private static void AimOffsetContract()
    {
        var policy = new CombatFramingPolicy();
        foreach (float bad in new[] { -1f, 181f, float.NaN, float.PositiveInfinity })
        {
            bool threw = false;
            try { policy.Observe(1, 0f, Sep, Radius, new[] { A(1, 0f, 8f, bad) }); }
            catch (ArgumentOutOfRangeException) { threw = true; }
            Check(threw, $"Accepted an invalid aim offset {bad}.");
        }
        // 0 and 180 are the valid extremes.
        policy.Observe(1, 0f, Sep, Radius, new[] { A(1, 0f, 8f, 0f) });
        policy.Observe(2, 0f, Sep, Radius, new[] { A(1, 0f, 8f, 180f) });
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new Exception("Expected ArgumentOutOfRangeException.");
    }
}
