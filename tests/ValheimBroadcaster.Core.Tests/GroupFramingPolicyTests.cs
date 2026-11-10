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
