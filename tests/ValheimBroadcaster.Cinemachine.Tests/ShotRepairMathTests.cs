using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin;

// Executes the production lens-band and arc-probe math. Unity component
// lifetime, scene raycasts and native CameraState interpolation still
// require the game runtime; only pure camera math runs here.
internal static class ShotRepairMathTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Lens banding opens wide shots and tightens medium shots around the baseline", BandedLens);
        yield return ("Lens banding clamps to the photographically usable range and survives a non-finite baseline", BandedLensBounds);
        yield return ("Scale-distinct lens bands exceed the jump-cut distinctness threshold", BandDistinctness);
        yield return ("Arc probes preserve height and distance while rotating about the focus", ArcGeometry);
        yield return ("Arc probe offsets match the rig's sine/cosine heading convention", ArcHeadingConvention);
        yield return ("Arc steering step bounds the damped heading rate to sixty degrees per second", ArcSteerRate);
    }

    private static void Near(float a, float b, float tolerance = .0001f)
    { if (float.IsNaN(a) || Math.Abs(a - b) > tolerance) throw new Exception("Expected " + b + ", actual " + a); }
    private static void Near(Vector3 a, Vector3 b, float tolerance = .001f)
    { if (Vector3.Distance(a, b) > tolerance) throw new Exception("Expected " + b + ", actual " + a); }

    private static void BandedLens()
    {
        Near(ShotRepairMath.ScaleBandFov(ShotScale.Wide, 50f), 62f);
        Near(ShotRepairMath.ScaleBandFov(ShotScale.Full, 50f), 50f);
        Near(ShotRepairMath.ScaleBandFov(ShotScale.Medium, 50f), 38f);
    }

    private static void BandedLensBounds()
    {
        Near(ShotRepairMath.ScaleBandFov(ShotScale.Wide, 65f), 70f);
        Near(ShotRepairMath.ScaleBandFov(ShotScale.Medium, 35f), 28f);
        Near(ShotRepairMath.ScaleBandFov(ShotScale.Full, float.NaN), 50f);
        Near(ShotRepairMath.ScaleBandFov(ShotScale.Wide, float.PositiveInfinity), 50f);
    }

    private static void BandDistinctness()
    {
        // Wide and medium bands around any baseline differ by the full
        // 24 degrees unless clamped; even clamped bands stay at or above
        // the ShotTransitionPolicy distinctness threshold for 28..58 bases.
        for (float baseline = 28f; baseline <= 58f; baseline += 2f)
        {
            float wide = ShotRepairMath.ScaleBandFov(ShotScale.Wide, baseline);
            float medium = ShotRepairMath.ScaleBandFov(ShotScale.Medium, baseline);
            if (wide - medium < ShotTransitionPolicy.DistinctFovDegrees)
                throw new Exception("Bands collapse below the jump-cut threshold at baseline " + baseline);
        }
    }

    private static void ArcGeometry()
    {
        Vector3 focus = new Vector3(10f, 4f, -3f);
        Vector3 camera = focus + new Vector3(6f, 2.5f, 8f);
        foreach (float offset in new[] { 15f, -15f, 30f, -30f, 45f, -45f })
        {
            Vector3 probe = ShotRepairMath.ArcProbePosition(focus, camera, offset);
            Near(probe.y, camera.y);
            Near(Vector3.Distance(probe, focus), Vector3.Distance(camera, focus));
            Near(Vector3.Angle(Vector3.ProjectOnPlane(camera - focus, Vector3.up),
                Vector3.ProjectOnPlane(probe - focus, Vector3.up)), Math.Abs(offset), .01f);
        }
    }

    private static void ArcHeadingConvention()
    {
        // The rig's offsets place the camera at (sin(heading), cos(heading));
        // a positive probe offset must land where heading+offset would.
        const float heading = 40f, offset = 30f, radius = 9f, height = 3f;
        Vector3 focus = new Vector3(2f, 1f, 5f);
        Vector3 At(float degrees) => focus + new Vector3(
            Mathf.Sin(degrees * Mathf.Deg2Rad) * radius, height, Mathf.Cos(degrees * Mathf.Deg2Rad) * radius);
        Near(ShotRepairMath.ArcProbePosition(focus, At(heading), offset), At(heading + offset));
        Near(ShotRepairMath.ArcProbePosition(focus, At(heading), -offset), At(heading - offset));
    }

    private static void ArcSteerRate()
    {
        // SmoothMotion damps heading toward approvedHeading with rate 5/s;
        // the peak angular speed of that lerp is 5 x the remaining distance.
        Near(ShotRepairMath.ArcSteerStepDegrees * 5f, ShotRepairMath.ArcDegreesPerSecond);
        if (ShotRepairMath.ArcSteerStepDegrees <= 0) throw new Exception("Steering step must advance the heading");
    }

}
