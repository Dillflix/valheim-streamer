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
        // Eyeline-on-top-third doctrine.
        yield return ("The eye lands exactly on the eyeline across lens/distance combos", EyelineEyePinExact);
        yield return ("The feet land exactly on the bottom edge at the solved distance", EyelineFeetOnEdge);
        yield return ("The solved distance is minimal: 0.05 m closer crops the feet", EyelineDistanceMinimal);
        yield return ("Under a crop the eye NEVER leaves the top-third line (the doctrine)", EyelineInvariantUnderCrop);
        yield return ("The real 50mm and 75mm eyeline distances land in their bands", EyelineRealCases);
        yield return ("A tiny aim-to-eye collapses the aim onto the eyeline", EyelineTinyAimToEye);
        yield return ("The eyeline aim solve rejects non-finite and out-of-range inputs", EyelineAimContracts);
        yield return ("The eyeline distance solve rejects non-finite and out-of-range inputs", EyelineDistanceContracts);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double expected, double actual, string message, double tol = 1e-3)
    { if (Math.Abs(expected - actual) > tol) throw new Exception($"{message}: expected {expected}, got {actual}."); }

    // Forward model: a fixed lens looks horizontally at the aim-pin. The aim sits
    // at phi below the optical axis (Y-down positive aimScreenY). A point
    // metresFromAim away at distance d is at angle atan(metres/d) from the aim ray;
    // sign +1 drops it toward the feet (below the aim), -1 lifts it toward the head.
    // Its screen Y (Y-down, +/-0.5 at the edges) follows the tangent ratio against
    // the half-fov. This is the independent oracle for the closed forms.
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
        float dBelowOnly = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, below, 0f);
        float dAboveOnly = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, 0f, above);
        Near(Math.Max(dBelowOnly, dAboveOnly), d, "distance is the max of the two single-axis fits", 1e-3);
    }

    private static void DistanceIsMinimal()
    {
        const float vfov = 27f, aimY = 0.05f, below = 1.77f, above = 0.73f;
        float d = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, below, above);
        double feet = ProjectScreenY(vfov, aimY, below, d - 0.05, +1);
        double head = ProjectScreenY(vfov, aimY, above, d - 0.05, -1);
        Check(Math.Abs(feet) > 0.5 || Math.Abs(head) > 0.5,
            $"d-0.05 still fit both extremes (feet {feet}, head {head}) - distance is not minimal.");
    }

    private static void AimSignBothWays()
    {
        foreach (float aimY in new[] { 0.05f, -0.05f })
        {
            float d = GroupFramingPolicy.RequiredDistanceForVerticalSpan(30f, aimY, 1.2f, 1.2f);
            double feet = ProjectScreenY(30f, aimY, 1.2f, d, +1);
            double head = ProjectScreenY(30f, aimY, 1.2f, d, -1);
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
        Near(0.5f, GroupFramingPolicy.GroupWeight(13f, 10f, 16f), "weight at midpoint");
        float a = GroupFramingPolicy.GroupWeight(11f, 10f, 16f);
        float b = GroupFramingPolicy.GroupWeight(14f, 10f, 16f);
        Check(a > b && a < 1f && b > 0f, "weight is not strictly decreasing inside the band.");
    }

    private static void EnemyDepthOffset()
    {
        const float vfov = 18.1805f, aimY = 0.05f, below = 1.67f, above = 2.25f;
        float baseSpan = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, below, above);
        Near(baseSpan, GroupFramingPolicy.RequiredDistanceForEnemy(vfov, aimY, below, above, 0f),
            "zero-offset enemy need equals the span fit", 1e-3);
        float far = GroupFramingPolicy.RequiredDistanceForEnemy(vfov, aimY, below, above, 3f);
        Near(baseSpan - 3f, far, "positive offset pulls the player in", 1e-3);
        float near = GroupFramingPolicy.RequiredDistanceForEnemy(vfov, aimY, below, above, -2f);
        Near(baseSpan + 2f, near, "negative offset pushes the player out", 1e-3);
        Near(1f, GroupFramingPolicy.RequiredDistanceForEnemy(vfov, aimY, below, above, 100f),
            "player distance floors at 1 m", 1e-6);
    }

    private static void HeadBandReduction()
    {
        Near(0f, GroupFramingPolicy.HeadBandBelowSpan(2.48f, 1.2f), "head band entirely above aim -> 0 below", 1e-4);
        Near(0.4f, GroupFramingPolicy.HeadBandBelowSpan(1.0f, 1.4f), "head band straddles aim", 1e-4);
        const float vfov = 18.1805f, aimY = 0.05f;
        float fullBelow = 1.52f + 0.15f;
        float aboveSpan = 2.48f + 0.25f;
        float fullNeed = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, fullBelow, aboveSpan);
        float reducedBelow = GroupFramingPolicy.HeadBandBelowSpan(2.48f, 1.2f);
        float bandNeed = GroupFramingPolicy.RequiredDistanceForVerticalSpan(vfov, aimY, reducedBelow, aboveSpan);
        Check(bandNeed <= fullNeed + 1e-4f, "The head band did not reduce (or held) the required distance.");
    }

    private static void LensSanity()
    {
        const float aimY = 0.05f, below = 1.77f, above = 0.73f;
        float solo = GroupFramingPolicy.RequiredDistanceForVerticalSpan(26.9915f, aimY, below, above);
        float combat = GroupFramingPolicy.RequiredDistanceForVerticalSpan(18.1805f, aimY, below, above);
        Near(8.24f, solo, "50 mm player need", 0.05);
        Near(12.32f, combat, "75 mm player need", 0.05);
        Check(solo > 6f, "50 mm player need did not exceed the 6 m default.");
        Check(combat < 18f, "75 mm player need exceeded the 18 m dolly cap.");
    }

    // Project the eye (aimToEye ABOVE the aim, sign -1) and the feet (belowAim below
    // the aim, sign +1) through the solved aim screen Y, using the same oracle.
    private static void EyelineEyePinExact()
    {
        // A spread of lens / distance / aim-to-eye / eyeline combos. For each, the eye
        // must land EXACTLY on the requested eyeline screen Y to 1e-3.
        const float eyeline = -(1f / 6f);
        (float vfov, float a2e, float d, float eyeY)[] cases =
        {
            (26.9915f, 0.247f, 6.25f, eyeline),  // real 50mm
            (18.1805f, 0.247f, 9.42f, eyeline),  // real 75mm
            (30f,      0.30f,  5.0f,  eyeline),
            (40f,      0.50f,  8.0f, -0.10f),
            (22f,      0.20f,  7.0f,  eyeline),
            (18.1805f, 0.40f,  4.0f,  eyeline),  // close / cropped
        };
        foreach (var c in cases)
        {
            float aimY = EyelineFraming.AimYForEyeline(c.vfov, c.eyeY, c.a2e, c.d);
            double eye = ProjectScreenY(c.vfov, aimY, c.a2e, c.d, -1);
            Near(c.eyeY, eye, $"eye on the eyeline (vfov {c.vfov}, d {c.d})", 1e-3);
        }
    }

    private static void EyelineFeetOnEdge()
    {
        // At the solved distance the feet (belowAim below the aim) must land exactly on
        // the bottom edge (+0.5), with the eye pinned on the eyeline.
        const float eyeline = -(1f / 6f), a2e = 0.247f, below = 1.77f;
        foreach (float vfov in new[] { 26.9915f, 18.1805f, 30f, 22f })
        {
            float d = EyelineFraming.DistanceForFeetAtEyeline(vfov, eyeline, a2e, below);
            float aimY = EyelineFraming.AimYForEyeline(vfov, eyeline, a2e, d);
            double feet = ProjectScreenY(vfov, aimY, below, d, +1);
            double eye = ProjectScreenY(vfov, aimY, a2e, d, -1);
            Near(0.5, feet, $"feet on the bottom edge (vfov {vfov})", 2e-3);
            Near(eyeline, eye, $"eye still on the eyeline at the solved distance (vfov {vfov})", 2e-3);
        }
    }

    private static void EyelineDistanceMinimal()
    {
        // 0.05 m closer than the solved distance crops the feet (feet screenY > 0.5),
        // the eyeline re-pinned at the closer distance.
        const float vfov = 26.9915f, eyeline = -(1f / 6f), a2e = 0.247f, below = 1.77f;
        float d = EyelineFraming.DistanceForFeetAtEyeline(vfov, eyeline, a2e, below);
        float aimY = EyelineFraming.AimYForEyeline(vfov, eyeline, a2e, d - 0.05f);
        double feet = ProjectScreenY(vfov, aimY, below, d - 0.05, +1);
        Check(feet > 0.5, $"d-0.05 did not crop the feet (feet {feet}) - distance is not minimal.");
    }

    private static void EyelineInvariantUnderCrop()
    {
        // THE point of the doctrine: at ANY distance closer than the solved one, the
        // eye still sits exactly on the top-third line - the crop eats the feet, never
        // the eyeline. Sweep several tight distances and assert the eye never moves.
        const float vfov = 26.9915f, eyeline = -(1f / 6f), a2e = 0.247f, below = 1.77f;
        float solved = EyelineFraming.DistanceForFeetAtEyeline(vfov, eyeline, a2e, below);
        foreach (float d in new[] { solved - 1f, solved - 2f, 3.0f, 2.0f })
        {
            if (d <= 0.3f) continue;
            float aimY = EyelineFraming.AimYForEyeline(vfov, eyeline, a2e, d);
            double eye = ProjectScreenY(vfov, aimY, a2e, d, -1);
            Near(eyeline, eye, $"eye left the eyeline under crop at d {d}", 1e-3);
        }
    }

    private static void EyelineRealCases()
    {
        // The locked sanity numbers: h=1.9, aim=1.52, eye=1.767, aimToEye=0.247,
        // belowAim=1.77, eyeline=-1/6. 50mm ~6.0-6.6 m, 75mm ~9-10 m.
        const float eyeline = -(1f / 6f), a2e = 0.247f, below = 1.77f;
        float solo = EyelineFraming.DistanceForFeetAtEyeline(26.9915f, eyeline, a2e, below);
        float combat = EyelineFraming.DistanceForFeetAtEyeline(18.1805f, eyeline, a2e, below);
        Check(solo > 6.0f && solo < 6.6f, $"50mm eyeline distance {solo} outside the ~6.0-6.6 m band.");
        Check(combat > 9.0f && combat < 10.0f, $"75mm eyeline distance {combat} outside the ~9-10 m band.");
        // Aim sits slightly BELOW the eyeline line (less negative), since the aim is
        // below the eye in the world.
        float soloAim = EyelineFraming.AimYForEyeline(26.9915f, eyeline, a2e, solo);
        Check(soloAim > eyeline && soloAim < 0f, $"50mm aim {soloAim} not between the eyeline and centre.");
    }

    private static void EyelineTinyAimToEye()
    {
        // With the aim-to-eye collapsing to near-zero, the aim pin coincides with the
        // eyeline (the aim IS the eye).
        const float vfov = 26.9915f, eyeline = -(1f / 6f);
        float aimY = EyelineFraming.AimYForEyeline(vfov, eyeline, 0.0001f, 6.0f);
        Near(eyeline, aimY, "tiny aim-to-eye collapses the aim onto the eyeline", 2e-3);
    }

    private static void EyelineAimContracts()
    {
        Throws(() => EyelineFraming.AimYForEyeline(1f, -0.1f, 0.2f, 6f));            // vfov <= 1
        Throws(() => EyelineFraming.AimYForEyeline(170f, -0.1f, 0.2f, 6f));          // vfov >= 170
        Throws(() => EyelineFraming.AimYForEyeline(float.NaN, -0.1f, 0.2f, 6f));
        Throws(() => EyelineFraming.AimYForEyeline(27f, 0.5f, 0.2f, 6f));            // eyelineY > 0.45
        Throws(() => EyelineFraming.AimYForEyeline(27f, -0.5f, 0.2f, 6f));           // eyelineY < -0.45
        Throws(() => EyelineFraming.AimYForEyeline(27f, -0.1f, -0.1f, 6f));          // aimToEye < 0
        Throws(() => EyelineFraming.AimYForEyeline(27f, -0.1f, 0.2f, 0.3f));         // distance <= 0.3
        Throws(() => EyelineFraming.AimYForEyeline(27f, -0.1f, 0.2f, float.PositiveInfinity));
    }

    private static void EyelineDistanceContracts()
    {
        Throws(() => EyelineFraming.DistanceForFeetAtEyeline(1f, -0.1f, 0.2f, 1.7f));        // vfov <= 1
        Throws(() => EyelineFraming.DistanceForFeetAtEyeline(170f, -0.1f, 0.2f, 1.7f));      // vfov >= 170
        Throws(() => EyelineFraming.DistanceForFeetAtEyeline(float.NaN, -0.1f, 0.2f, 1.7f));
        Throws(() => EyelineFraming.DistanceForFeetAtEyeline(27f, 0.5f, 0.2f, 1.7f));        // eyelineY > 0.45
        Throws(() => EyelineFraming.DistanceForFeetAtEyeline(27f, -0.5f, 0.2f, 1.7f));       // eyelineY < -0.45
        Throws(() => EyelineFraming.DistanceForFeetAtEyeline(27f, -0.1f, -0.1f, 1.7f));      // aimToEye < 0
        Throws(() => EyelineFraming.DistanceForFeetAtEyeline(27f, -0.1f, 0.2f, 0f));         // belowAim <= 0
        Throws(() => EyelineFraming.DistanceForFeetAtEyeline(27f, -0.1f, 0.2f, float.NaN));
    }

    private static void ContractValidation()
    {
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(1f, 0.05f, 1f, 1f));
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(170f, 0.05f, 1f, 1f));
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(float.NaN, 0.05f, 1f, 1f));
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(30f, 0.5f, 1f, 1f));
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(30f, -0.5f, 1f, 1f));
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(30f, 0.05f, -1f, 1f));
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(30f, 0.05f, 1f, float.PositiveInfinity));
        Throws(() => GroupFramingPolicy.RequiredDistanceForVerticalSpan(5f, 0.45f, 1f, 1f));

        Throws(() => GroupFramingPolicy.GroupWeight(-1f, 10f, 16f));
        Throws(() => GroupFramingPolicy.GroupWeight(5f, 16f, 10f));
        Throws(() => GroupFramingPolicy.GroupWeight(5f, 10f, 10f));
        Throws(() => GroupFramingPolicy.GroupWeight(float.NaN, 10f, 16f));

        Throws(() => GroupFramingPolicy.RequiredDistanceForEnemy(18f, 0.05f, 1f, 1f, float.NaN));
        Throws(() => GroupFramingPolicy.RequiredDistanceForEnemy(0.5f, 0.05f, 1f, 1f, 0f));

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
