using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class GroupFramingPolicyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Fixed-lens span distance lands both extremes on the frame edges", ClosedFormProjects);
        yield return ("The span distance is minimal: one metre closer crops an extreme", DistanceIsMinimal);
        yield return ("Both aim-sign directions project correctly", AimSignBothWays);
        yield return ("The sparse gate is 1 near, 0 far, smoothstep between, and clamps", SparseGate);
        yield return ("Enemy depth offset shifts the required player distance", EnemyDepthOffset);
        yield return ("The oversized head-band reduces the below-aim span to crop feet", HeadBandReduction);
        yield return ("The fit at the realistic 50mm and 75mm lenses matches the spec", LensSanity);
        yield return ("The fill solve lands feet and head exactly on their target screen edges", FillProjectsExactly);
        yield return ("The fill solve is monotone: a smaller framing size pushes the camera in", FillMonotone);
        yield return ("A symmetric body with symmetric gaps centres the aim on the span", FillSymmetricCentres);
        yield return ("The real solo and combat fill cases land in their expected distance bands", FillRealCases);
        yield return ("A full-frame fill (size 1) packs the body edge-to-edge with no gap", FillFullFrame);
        yield return ("Combining group spans: w=0 is player identity, w=1 is element-wise max", CombineGroupSpansEndpoints);
        yield return ("Combining group spans lerps the midpoint between identity and max", CombineGroupSpansMidpoint);
        yield return ("A small low creature at w=1 grows the group below span and lifts the fill aim toward the head", GroupFillSmallCreatureTiltsDown);
        yield return ("The fill solve rejects non-finite and out-of-range inputs", FillContracts);
        yield return ("Combining group spans rejects non-finite and out-of-range inputs", CombineGroupSpansContracts);
        yield return ("The policy rejects non-finite and out-of-range inputs", ContractValidation);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double expected, double actual, string message, double tol = 1e-3)
    { if (Math.Abs(expected - actual) > tol) throw new Exception($"{message}: expected {expected}, got {actual}."); }

    // Forward model: a fixed lens looks horizontally at the aim-pin. The aim sits
    // at phi below the optical axis (Y-down positive aimScreenY). A point
    // metresFromAim away at distance d is at angle atan(metres/d) from the aim ray;
    // sign +1 drops it toward the feet (below the aim), -1 lifts it toward the head.
    // Its screen Y (Y-down, +/-0.5 at the edges) follows the tangent ratio against
    // the half-fov. This is the independent oracle for the closed form.
    private static double ProjectScreenY(double vfovDeg, double aimScreenY, double metresFromAim, double d, int sign)
    {
        double half = vfovDeg * 0.5 * Math.PI / 180.0;
        double tanHalf = Math.Tan(half);
        double phi = Math.Atan(2.0 * aimScreenY * tanHalf);
        double ang = Math.Atan(metresFromAim / d);
        double angleBelowAxis = phi + sign * ang;
        return 0.5 * Math.Tan(angleBelowAxis) / tanHalf;
    }

    private static void ClosedFormProjects()
    {
        const float vfov = 27f, aimY = 0.05f, below = 1.77f, above = 0.73f;
        float d = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, below, above);
        // Feet (below) must reach the bottom edge (+0.5); head (above) the top (-0.5).
        // Below dominates with a positive aimScreenY, so feet land exactly on +0.5 and
        // the head stays safely inside the top edge (|screenY| < 0.5).
        double feet = ProjectScreenY(vfov, aimY, below, d, +1);
        double head = ProjectScreenY(vfov, aimY, above, d, -1);
        Near(0.5, feet, "feet land on the bottom edge", 5e-3);
        Check(head > -0.5 && head < 0.5, $"head left the frame: screenY {head}.");
        // The dominant extreme is within |0.5|; verify the non-dominant one did not
        // secretly require more distance (max() picked the right axis).
        float dBelowOnly = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, below, 0f);
        float dAboveOnly = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, 0f, above);
        Near(Math.Max(dBelowOnly, dAboveOnly), d, "distance is the max of the two single-axis fits", 1e-3);
    }

    private static void DistanceIsMinimal()
    {
        const float vfov = 27f, aimY = 0.05f, below = 1.77f, above = 0.73f;
        float d = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, below, above);
        // One probe-step closer must push the dominant extreme past its frame edge.
        double feet = ProjectScreenY(vfov, aimY, below, d - 0.05, +1);
        double head = ProjectScreenY(vfov, aimY, above, d - 0.05, -1);
        Check(Math.Abs(feet) > 0.5 || Math.Abs(head) > 0.5,
            $"d-0.05 still fit both extremes (feet {feet}, head {head}) - distance is not minimal.");
    }

    private static void AimSignBothWays()
    {
        // Positive aimScreenY pins the aim below centre: more headroom, feet-limited.
        // Negative pins it above centre: more legroom, head-limited. Verify the
        // dominant extreme lands on its edge in BOTH conventions.
        foreach (float aimY in new[] { 0.05f, -0.05f })
        {
            float d = GroupFramingPolicy.RequiredDistanceForVerticalSpan(30f, aimY, 1.2f, 1.2f);
            double feet = ProjectScreenY(30f, aimY, 1.2f, d, +1);
            double head = ProjectScreenY(30f, aimY, 1.2f, d, -1);
            // Symmetric spans: the side nearer its edge is the dominant one; it must
            // reach +/-0.5 and the other stays inside.
            double dominant = Math.Max(Math.Abs(feet), Math.Abs(head));
            double other = Math.Min(Math.Abs(feet), Math.Abs(head));
            Near(0.5, dominant, $"dominant extreme on edge (aimY {aimY})", 5e-3);
            Check(other <= 0.5 + 1e-6, $"both extremes exceeded the frame (aimY {aimY}).");
        }
    }

    private static void SparseGate()
    {
        Near(1f, GroupFramingPolicy.GroupWeight(5f, 10f, 16f), "weight near (clamped 1)");
        Near(1f, GroupFramingPolicy.GroupWeight(10f, 10f, 16f), "weight at full endpoint");
        Near(0f, GroupFramingPolicy.GroupWeight(16f, 10f, 16f), "weight at zero endpoint");
        Near(0f, GroupFramingPolicy.GroupWeight(20f, 10f, 16f), "weight far (clamped 0)");
        // Midpoint of a smoothstep inverted is exactly 0.5.
        Near(0.5f, GroupFramingPolicy.GroupWeight(13f, 10f, 16f), "weight at midpoint");
        // Monotonic decreasing across the band.
        float a = GroupFramingPolicy.GroupWeight(11f, 10f, 16f);
        float b = GroupFramingPolicy.GroupWeight(14f, 10f, 16f);
        Check(a > b && a < 1f && b > 0f, "weight is not strictly decreasing inside the band.");
    }

    private static void EnemyDepthOffset()
    {
        const float vfov = 18.1805f, aimY = 0.05f, below = 1.67f, above = 2.25f;
        float baseSpan = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, below, above);
        // Zero offset: the player distance equals the raw span fit.
        Near(baseSpan, GroupFramingPolicy.RequiredDistanceForEnemy(vfov, aimY, below, above, 0f),
            "zero-offset enemy need equals the span fit", 1e-3);
        // Enemy FARTHER than the player (positive offset): the player can sit closer.
        float far = GroupFramingPolicy.RequiredDistanceForEnemy(vfov, aimY, below, above, 3f);
        Near(baseSpan - 3f, far, "positive offset pulls the player in", 1e-3);
        // Enemy NEARER than the player (negative offset): the player must dolly back.
        float near = GroupFramingPolicy.RequiredDistanceForEnemy(vfov, aimY, below, above, -2f);
        Near(baseSpan + 2f, near, "negative offset pushes the player out", 1e-3);
        // The 1 m floor holds even when a large positive offset would go negative.
        Near(1f, GroupFramingPolicy.RequiredDistanceForEnemy(vfov, aimY, below, above, 100f),
            "player distance floors at 1 m", 1e-6);
    }

    private static void HeadBandReduction()
    {
        // A 4 m enemy, aim at 1.52 m. Head top is 4 - 1.52 = 2.48 m above the aim.
        // headKeep = max(0.8, 0.3*4) = 1.2 m. The band bottom (2.48 - 1.2 = 1.28 m)
        // is still ABOVE the aim, so nothing below the aim must fit: reduced = 0.
        Near(0f, GroupFramingPolicy.HeadBandBelowSpan(2.48f, 1.2f), "head band entirely above aim -> 0 below", 1e-4);
        // A shorter enemy whose head band straddles the aim: head top 1.0 m above,
        // headKeep 1.4 m -> band bottom 0.4 m BELOW the aim -> reduced below = 0.4.
        Near(0.4f, GroupFramingPolicy.HeadBandBelowSpan(1.0f, 1.4f), "head band straddles aim", 1e-4);
        // The reduction genuinely shrinks the fit: head-band need <= full-body need.
        const float vfov = 18.1805f, aimY = 0.05f;
        float fullBelow = 1.52f + 0.15f;           // aim down to feet + margin
        float aboveSpan = 2.48f + 0.25f;           // head top above aim + margin
        float fullNeed = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, fullBelow, aboveSpan);
        float reducedBelow = GroupFramingPolicy.HeadBandBelowSpan(2.48f, 1.2f);
        float bandNeed = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, reducedBelow, aboveSpan);
        Check(bandNeed <= fullNeed + 1e-4f, "The head band did not reduce (or held) the required distance.");
    }

    private static void LensSanity()
    {
        // The two locked lenses, player h=1.9, aimHeight=1.52, aimY=0.05,
        // margins feet 0.25 / head 0.35: belowAim 1.77, aboveAim 0.73.
        const float aimY = 0.05f, below = 1.77f, above = 0.73f;
        float solo = GroupFramingPolicy.RequiredDistanceForVerticalSpan(26.9915f, aimY, below, above);
        float combat = GroupFramingPolicy.RequiredDistanceForVerticalSpan(18.1805f, aimY, below, above);
        // Solo dollies out past the 6 m default; combat sits well under the 18 m cap.
        Near(8.24f, solo, "50 mm player need", 0.05);
        Near(12.32f, combat, "75 mm player need", 0.05);
        Check(solo > 6f, "50 mm player need did not exceed the 6 m default.");
        Check(combat < 18f, "75 mm player need exceeded the 18 m dolly cap.");
    }

    // Forward-project a point metresFromAim away given the SOLVED aim screen Y, for
    // the fill oracle (same tangent model as ProjectScreenY but parameterised by the
    // solved aim rather than a fixed pin).
    private static void FillProjectsExactly()
    {
        // A spread of fov / span / framingSize / topGapShare combos, including the two
        // real locked cases. For each, the feet must land on +0.5 - bottomGap and the
        // head on -0.5 + topGap to 1e-3.
        (float vfov, float below, float above, float size, float top)[] cases =
        {
            (26.9915f, 1.77f, 0.73f, 0.80f, 0.60f),   // solo locked case
            (18.1805f, 1.77f, 0.73f, 0.80f, 0.60f),   // combat baseline case
            (30f,      1.20f, 1.20f, 0.90f, 0.50f),
            (45f,      2.00f, 0.50f, 0.70f, 0.30f),
            (18.1805f, 1.67f, 2.25f, 0.85f, 0.60f),   // enemy-ish spans
            (60f,      1.00f, 1.00f, 0.60f, 0.80f),
        };
        foreach (var c in cases)
        {
            FramingFillResult r = GroupFramingPolicy.FramingFill(c.vfov, c.below, c.above, c.size, c.top);
            double gap = 1.0 - c.size;
            double topGap = c.top * gap;
            double bottomGap = (1.0 - c.top) * gap;
            double feetTarget = 0.5 - bottomGap;
            double headTarget = -0.5 + topGap;
            double feet = ProjectScreenY(c.vfov, r.AimScreenY, c.below, r.DistanceMetres, +1);
            double head = ProjectScreenY(c.vfov, r.AimScreenY, c.above, r.DistanceMetres, -1);
            Near(feetTarget, feet, $"feet land on target (vfov {c.vfov}, size {c.size})", 1e-3);
            Near(headTarget, head, $"head lands on target (vfov {c.vfov}, size {c.size})", 1e-3);
            // The spanned fraction (feet - head) must equal the framing size.
            Near(c.size, feet - head, $"body spans the framing size (vfov {c.vfov})", 2e-3);
        }
    }

    private static void FillMonotone()
    {
        // Shrinking the framing size (body fills LESS of the frame) must move the
        // camera FARTHER out; growing it must pull in. Distance is monotone in size.
        float dBig = GroupFramingPolicy.FramingFill(26.9915f, 1.77f, 0.73f, 0.9f, 0.6f).DistanceMetres;
        float dMid = GroupFramingPolicy.FramingFill(26.9915f, 1.77f, 0.73f, 0.8f, 0.6f).DistanceMetres;
        float dSmall = GroupFramingPolicy.FramingFill(26.9915f, 1.77f, 0.73f, 0.6f, 0.6f).DistanceMetres;
        Check(dBig < dMid && dMid < dSmall, $"distance not monotone in framing size: {dBig}, {dMid}, {dSmall}.");
    }

    private static void FillSymmetricCentres()
    {
        // Symmetric body (equal below/above) with a symmetric gap split (topGapShare
        // 0.5) must centre the aim: aimScreenY ~ 0 and feet/head symmetric about 0.
        FramingFillResult r = GroupFramingPolicy.FramingFill(30f, 1.2f, 1.2f, 0.8f, 0.5f);
        Near(0.0, r.AimScreenY, "symmetric body centres the aim", 1e-3);
        double feet = ProjectScreenY(30f, r.AimScreenY, 1.2f, r.DistanceMetres, +1);
        double head = ProjectScreenY(30f, r.AimScreenY, 1.2f, r.DistanceMetres, -1);
        Near(0.4, feet, "symmetric feet on +0.4", 1e-3);   // size 0.8 => gap 0.2, half each side
        Near(-0.4, head, "symmetric head on -0.4", 1e-3);
    }

    private static void FillRealCases()
    {
        // The two locked cases. Spec expects solo ~6.3-6.8 m with a NEGATIVE aim
        // (aim above centre), combat ~9.5-10.2 m. Assert the bands and the aim sign.
        FramingFillResult solo = GroupFramingPolicy.FramingFill(26.9915f, 1.77f, 0.73f, 0.8f, 0.6f);
        FramingFillResult combat = GroupFramingPolicy.FramingFill(18.1805f, 1.77f, 0.73f, 0.8f, 0.6f);
        Check(solo.DistanceMetres > 6.0f && solo.DistanceMetres < 7.0f,
            $"solo fill distance {solo.DistanceMetres} outside the expected ~6.3-6.8 m band.");
        Check(solo.AimScreenY < 0f, $"solo fill aim should be negative (above centre), got {solo.AimScreenY}.");
        Check(combat.DistanceMetres > 9.3f && combat.DistanceMetres < 10.4f,
            $"combat fill distance {combat.DistanceMetres} outside the expected ~9.5-10.2 m band.");
    }

    private static void FillFullFrame()
    {
        // framingSize = 1 means zero gap: the feet must reach exactly +0.5 and the
        // head exactly -0.5 (edge to edge), whatever the gap split.
        FramingFillResult r = GroupFramingPolicy.FramingFill(26.9915f, 1.77f, 0.73f, 1f, 0.6f);
        double feet = ProjectScreenY(26.9915f, r.AimScreenY, 1.77f, r.DistanceMetres, +1);
        double head = ProjectScreenY(26.9915f, r.AimScreenY, 0.73f, r.DistanceMetres, -1);
        Near(0.5, feet, "full-frame feet on the bottom edge", 1e-3);
        Near(-0.5, head, "full-frame head on the top edge", 1e-3);
    }

    private static void CombineGroupSpansEndpoints()
    {
        // w = 0: the group is exactly the player's own spans (solo identity).
        var id = GroupFramingPolicy.CombineGroupSpans(1.77f, 0.73f, 2.6f, 0.4f, 0f);
        Near(1.77f, id.Below, "w=0 below is the player span");
        Near(0.73f, id.Above, "w=0 above is the player span");
        // w = 1: element-wise max (the group must contain both actors).
        var max = GroupFramingPolicy.CombineGroupSpans(1.77f, 0.73f, 2.6f, 0.4f, 1f);
        Near(2.6f, max.Below, "w=1 below is the max of player and enemy");
        Near(0.73f, max.Above, "w=1 above keeps the larger player span (enemy shorter)");
        // A taller enemy grows the above span at w=1.
        var tall = GroupFramingPolicy.CombineGroupSpans(1.77f, 0.73f, 0.5f, 2.25f, 1f);
        Near(1.77f, tall.Below, "w=1 below keeps the larger player span (enemy low feet)");
        Near(2.25f, tall.Above, "w=1 above is the max for a tall enemy");
    }

    private static void CombineGroupSpansMidpoint()
    {
        // At w = 0.5 each span is halfway between the player value and the max.
        var mid = GroupFramingPolicy.CombineGroupSpans(1.77f, 0.73f, 2.6f, 1.0f, 0.5f);
        Near(1.77f + 0.5f * (2.6f - 1.77f), mid.Below, "below lerps to the midpoint");
        Near(0.73f + 0.5f * (1.0f - 0.73f), mid.Above, "above lerps to the midpoint");
    }

    private static void GroupFillSmallCreatureTiltsDown()
    {
        // Player spans (below 1.77, above 0.73). A small low creature projects to an
        // equivalent 2.6 m below the player's aim line (its body sits low in frame).
        // At w = 1 the group below span grows to 2.6 while the above stays 0.73.
        const float vfov = 18.1805f; // combat lens
        var player = GroupFramingPolicy.FramingFill(vfov, 1.77f, 0.73f, 0.9f, 0.6f);
        var group = GroupFramingPolicy.CombineGroupSpans(1.77f, 0.73f, 2.6f, 0.4f, 1f);
        var grouped = GroupFramingPolicy.FramingFill(vfov, group.Below, group.Above, 0.9f, 0.6f);
        // Projection-verified direction: growing the BELOW span (the small creature
        // sits low, far below the player's aim line) forces the solved aim to move UP
        // toward the head to keep the now-lower feet on their fixed screen target.
        // Screen Y is Y-down, so "up" is MORE NEGATIVE. The grouped aim is therefore
        // LESS than (above) the player-only aim. Downstream this means the camera
        // framing lifts so the low creature's extent is captured instead of empty sky.
        Check(grouped.AimScreenY < player.AimScreenY,
            $"group aim did not lift toward the head: player {player.AimScreenY}, group {grouped.AimScreenY}.");
        // Forward-projection exactness (same oracle as the fill tests): the combined
        // feet/head land on their target edges for the grouped spans.
        double gap = 1.0 - 0.9, topGap = 0.6 * gap, bottomGap = 0.4 * gap;
        double feet = ProjectScreenY(vfov, grouped.AimScreenY, group.Below, grouped.DistanceMetres, +1);
        double head = ProjectScreenY(vfov, grouped.AimScreenY, group.Above, grouped.DistanceMetres, -1);
        Near(0.5 - bottomGap, feet, "grouped feet land on target", 1e-3);
        Near(-0.5 + topGap, head, "grouped head lands on target", 1e-3);
    }

    private static void CombineGroupSpansContracts()
    {
        Throws(() => GroupFramingPolicy.CombineGroupSpans(-1f, 0.7f, 1f, 1f, 0.5f));  // playerBelow < 0
        Throws(() => GroupFramingPolicy.CombineGroupSpans(1f, -1f, 1f, 1f, 0.5f));    // playerAbove < 0
        Throws(() => GroupFramingPolicy.CombineGroupSpans(1f, 1f, -1f, 1f, 0.5f));    // enemyBelowEq < 0
        Throws(() => GroupFramingPolicy.CombineGroupSpans(1f, 1f, 1f, -1f, 0.5f));    // enemyAboveEq < 0
        Throws(() => GroupFramingPolicy.CombineGroupSpans(1f, 1f, 1f, 1f, -0.01f));   // weight < 0
        Throws(() => GroupFramingPolicy.CombineGroupSpans(1f, 1f, 1f, 1f, 1.01f));    // weight > 1
        Throws(() => GroupFramingPolicy.CombineGroupSpans(float.NaN, 1f, 1f, 1f, 0.5f));
        Throws(() => GroupFramingPolicy.CombineGroupSpans(1f, 1f, 1f, 1f, float.NaN));
    }

    private static void FillContracts()
    {
        Throws(() => GroupFramingPolicy.FramingFill(1f, 1f, 1f, 0.8f, 0.6f));            // fov <= 1
        Throws(() => GroupFramingPolicy.FramingFill(170f, 1f, 1f, 0.8f, 0.6f));          // fov >= 170
        Throws(() => GroupFramingPolicy.FramingFill(float.NaN, 1f, 1f, 0.8f, 0.6f));
        Throws(() => GroupFramingPolicy.FramingFill(30f, 0f, 1f, 0.8f, 0.6f));           // below <= 0
        Throws(() => GroupFramingPolicy.FramingFill(30f, 1f, 0f, 0.8f, 0.6f));           // above <= 0
        Throws(() => GroupFramingPolicy.FramingFill(30f, 1f, 1f, 0.1f, 0.6f));           // size <= 0.1
        Throws(() => GroupFramingPolicy.FramingFill(30f, 1f, 1f, 1.01f, 0.6f));          // size > 1
        Throws(() => GroupFramingPolicy.FramingFill(30f, 1f, 1f, 0.8f, -0.01f));         // topShare < 0
        Throws(() => GroupFramingPolicy.FramingFill(30f, 1f, 1f, 0.8f, 1.01f));          // topShare > 1
        Throws(() => GroupFramingPolicy.FramingFill(30f, 1f, 1f, float.NaN, 0.6f));
    }

    private static void ContractValidation()
    {
        // RequiredDistanceForVerticalSpan contracts.
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(1f, 0.05f, 1f, 1f));   // fov <= 1
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(170f, 0.05f, 1f, 1f)); // fov >= 170
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(float.NaN, 0.05f, 1f, 1f));
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(30f, 0.5f, 1f, 1f));   // aimY > 0.45
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(30f, -0.5f, 1f, 1f));  // aimY < -0.45
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(30f, 0.05f, -1f, 1f)); // span < 0
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(30f, 0.05f, 1f, float.PositiveInfinity));
        // An aim that closes one available angle below 1 deg must throw. At a narrow
        // fov a near-edge aim collapses the up/down room.
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(5f, 0.45f, 1f, 1f));

        // GroupWeight contracts.
        Throws(() => GroupFramingPolicy.GroupWeight(-1f, 10f, 16f));
        Throws(() => GroupFramingPolicy.GroupWeight(5f, 16f, 10f)); // zero <= full
        Throws(() => GroupFramingPolicy.GroupWeight(5f, 10f, 10f)); // zero == full
        Throws(() => GroupFramingPolicy.GroupWeight(float.NaN, 10f, 16f));

        // RequiredDistanceForEnemy contracts (offset + delegated span contracts).
        Throws(() => GroupFramingPolicy.RequiredDistanceForEnemy(18f, 0.05f, 1f, 1f, float.NaN));
        Throws(() => GroupFramingPolicy.RequiredDistanceForEnemy(0.5f, 0.05f, 1f, 1f, 0f)); // bad fov delegated

        // HeadBandBelowSpan contracts.
        Throws(() => GroupFramingPolicy.HeadBandBelowSpan(-1f, 1f));
        Throws(() => GroupFramingPolicy.HeadBandBelowSpan(1f, 0f));
        Throws(() => GroupFramingPolicy.HeadBandBelowSpan(float.NaN, 1f));
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new Exception("Expected ArgumentOutOfRangeException.");
    }
}
