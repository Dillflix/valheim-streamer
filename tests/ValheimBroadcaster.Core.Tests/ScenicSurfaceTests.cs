using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicSurfaceTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Loaded terrain still rejects a camera wholly underneath its surface", UnderGround);
        yield return ("Surface validation respects negative world heights", NegativeHeight);
        yield return ("A dry camera retains its exact measured clearance", DryClearance);
        yield return ("Loaded water above terrain controls camera clearance", WaterAboveGround);
        yield return ("Changing wave height can invalidate the same authored pose", WaveChange);
        yield return ("Missing terrain or malformed water cannot certify a scenic shot", UnknownSurface);
        yield return ("Actual near-plane corners are measured when its centre is clear", NearCorner);
        yield return ("Downward composition rotates the footprint below the camera body", DownwardNearPlane);
        yield return ("Aspect and lens changes expand the validated camera footprint", WideFootprint);
        yield return ("An unknown terrain tile under one corner rejects the entire footprint", UnknownCorner);
        yield return ("A ridge between route endpoints requires lift at the interior sample", RidgeInterior);
        yield return ("Scenic footprint generation rejects invalid lens and orientation", InvalidFootprint);
    }

    private static AutonomyPosition P(float x = 0, float y = 0, float z = 0) => new AutonomyPosition(x, y, z);
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static void Near(float actual, float expected) => Check(Math.Abs(actual - expected) < .001,
        "Expected " + expected + " but received " + actual);
    private static ScenicSurfacePolicy.Result Ground(float y, float ground, bool body = true) =>
        ScenicSurfacePolicy.EvaluateSample(y, true, ground, false, float.NaN, body);
    private static AutonomyPosition[] Footprint(AutonomyPosition position, float fov = 90, float aspect = 1, float nearClip = 1,
        AutonomyPosition? forward = null, AutonomyPosition? up = null)
    {
        var points = new AutonomyPosition[ScenicSurfacePolicy.SampleCount];
        Check(ScenicSurfacePolicy.TryFootprint(position, forward ?? P(0, 0, 1), P(1), up ?? P(0, 1),
            fov, aspect, nearClip, points), "A valid camera footprint was rejected.");
        return points;
    }
    private static void UnderGround()
    {
        // The former overlap-only test can report clear when the body sits
        // several metres below a one-sided terrain mesh. Measured Y cannot.
        var result = Ground(30, 42);
        Check(result.Known && !result.Clear && result.Constraint == ScenicSurfacePolicy.SurfaceConstraint.Ground,
            "A loaded heightmap was confused with an above-ground camera.");
        Near(result.RequiredLift, 14);
    }
    private static void NegativeHeight()
    {
        Check(Ground(-7, -10).Clear, "Negative sea-level coordinates were clamped to zero.");
        Near(Ground(-14, -10).RequiredLift, 6);
    }
    private static void DryClearance()
    {
        Check(Ground(22, 20).Clear, "A body exactly at the required clearance was rejected.");
        Near(Ground(21.75f, 20).RequiredLift, .25f);
        Check(Ground(20.5f, 20, false).Clear, "Near-plane margin was replaced with body margin.");
        Near(Ground(20.25f, 20, false).RequiredLift, .25f);
    }
    private static void WaterAboveGround()
    {
        var body = ScenicSurfacePolicy.EvaluateSample(5, true, -20, true, 8, true);
        Check(body.Known && !body.Clear && body.Constraint == ScenicSurfacePolicy.SurfaceConstraint.Water,
            "An underwater camera passed because its terrain floor was distant.");
        Near(body.RequiredLift, 5);
        Near(ScenicSurfacePolicy.EvaluateSample(8.5f, true, -20, true, 8, false).RequiredLift, .5f);
        var groundWins = ScenicSurfacePolicy.EvaluateSample(9, true, 10, true, 3, true);
        Check(groundWins.Constraint == ScenicSurfacePolicy.SurfaceConstraint.Ground, "Lower water hid a higher terrain floor.");
        Near(groundWins.RequiredLift, 3);
    }
    private static void WaveChange()
    {
        Check(ScenicSurfacePolicy.EvaluateSample(12, true, 0, true, 9, true).Clear, "A clear initial water pose failed.");
        Near(ScenicSurfacePolicy.EvaluateSample(12, true, 0, true, 11, true).RequiredLift, 1);
    }
    private static void UnknownSurface()
    {
        Check(!ScenicSurfacePolicy.EvaluateSample(20, false, 0, false, 0, true).Known, "Unknown terrain became sea level.");
        Check(!ScenicSurfacePolicy.EvaluateSample(20, true, float.NaN, false, 0, true).Known, "NaN terrain certified the camera.");
        Check(!ScenicSurfacePolicy.EvaluateSample(20, true, 0, true, float.NaN, true).Known, "Unknown loaded water was ignored.");
        Check(!ScenicSurfacePolicy.EvaluateSample(float.PositiveInfinity, true, 0, false, 0, true).Known,
            "Invalid camera height certified the camera.");
    }
    private static void NearCorner()
    {
        AutonomyPosition[] points = Footprint(P(0, 3));
        Check(Ground(points[0].Y, 0).Clear && Ground(points[1].Y, 0, false).Clear, "Fixture centre was not clear.");
        // The right side of the image faces a raised bank; body/centre-only
        // screening misses it even though every height measurement is known.
        float lift = 0;
        for (int i = 0; i < points.Length; i++)
        {
            float floor = points[i].X > .5f ? 2.5f : 0;
            lift = Math.Max(lift, Ground(points[i].Y, floor, i == 0).RequiredLift);
        }
        Near(lift, 1);
    }
    private static void DownwardNearPlane()
    {
        float s = (float)Math.Sqrt(.5);
        AutonomyPosition[] points = Footprint(P(0, 2.1f), nearClip: 2, forward: P(0, -s, s), up: P(0, s, s));
        Check(Ground(points[0].Y, 0).Clear, "Fixture body was not above its floor.");
        float lowest = float.MaxValue;
        for (int i = 1; i < points.Length; i++) lowest = Math.Min(lowest, points[i].Y);
        Check(lowest < 0 && Ground(lowest, 0, false).RequiredLift > 1,
            "Pitch was ignored when placing the actual near plane.");
    }
    private static void WideFootprint()
    {
        AutonomyPosition[] narrow = Footprint(P(0, 5), 60, 1, .3f);
        AutonomyPosition[] wide = Footprint(P(0, 5), 90, 16f / 9f, .3f);
        Check(Math.Abs(wide[2].X) > Math.Abs(narrow[2].X) * 2,
            "Lens/aspect did not change the horizontal samples.");
        Near(wide[1].Z, .3f);
        Near(wide[2].Y, 4.7f);
    }
    private static void UnknownCorner()
    {
        AutonomyPosition[] points = Footprint(P(0, 5));
        bool known = true;
        for (int i = 0; i < points.Length; i++)
            known &= ScenicSurfacePolicy.EvaluateSample(points[i].Y, points[i].X < .5f, 0, false, 0, i == 0).Known;
        Check(!known, "A missing adjacent tile under the image corner was ignored.");
    }
    private static void RidgeInterior()
    {
        Check(Ground(5, 0).Clear, "Route endpoints are intentionally clear.");
        float lift = 0;
        foreach (float floor in new[] { 0f, 2f, 9f, 2f, 0f }) lift = Math.Max(lift, Ground(5, floor).RequiredLift);
        Near(lift, 6);
    }
    private static void InvalidFootprint()
    {
        var points = new AutonomyPosition[6];
        Check(!ScenicSurfacePolicy.TryFootprint(P(), P(0, 0, 1), P(1), P(0, 1), float.NaN, 1, 1, points), "NaN lens passed.");
        Check(!ScenicSurfacePolicy.TryFootprint(P(), P(0, 0, 1), P(1), P(0, 1), 60, 1, 0, points), "Zero near clip passed.");
        Check(!ScenicSurfacePolicy.TryFootprint(P(), P(0, 0, 1), P(1), P(1), 60, 1, 1, points), "Parallel axes passed.");
        Check(!ScenicSurfacePolicy.TryFootprint(P(), P(0, 0, 2), P(1), P(0, 1), 60, 1, 1, points), "Scaled camera axes passed.");
        Check(!ScenicSurfacePolicy.TryFootprint(P(20000, 0, 0), P(0, 0, 1), P(1), P(0, 1), 60, 1, 1, points),
            "An out-of-world near-plane corner passed.");
    }
}
