using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ProceduralCameraGeometryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Procedural wide view stays reachable without a recent-pose exemption", WideOffset);
        yield return ("Procedural endpoint reachability follows sustained player motion", SustainedMovement);
        yield return ("Procedural geometry uses actual lead and rejects distant players", LeadAndDistance);
        yield return ("Procedural interior poses and reachability share the same clamps", InteriorOffsets);
        yield return ("Procedural reachability rejects nonfinite geometry", InvalidGeometry);
    }

    private static void Check(bool value) { if (!value) throw new Exception("Procedural geometry assertion failed."); }
    private static void Near(double expected, double actual)
    { if (Math.Abs(expected - actual) > .001) throw new Exception($"Expected {expected}, got {actual}."); }

    private static void WideOffset()
    {
        // Player(-80,-39.5,0), configured offset(+80,+40,0), +1.5m focus,
        // -2m carrier: the actual desired carrier is exactly at its current origin.
        // There is no remembered pose or age input: this remains true after gaps.
        Near(0, ProceduralCameraGeometry.RequiredDisplacement(-80, -39.5, 0, 80, 40, 90, false));
        Check(ProceduralCameraGeometry.RequiredDisplacement(-80, -39.5, 0, 80, 40, 90, false) < CameraReachability.ImmediateLimit);
    }

    private static void SustainedMovement()
    {
        double targetX = -80, carrierX = 0;
        for (int tick = 0; tick < 3000; tick++)
        {
            double step = tick % 100 < 50 ? .6 : 2;
            targetX += step;
            double lead = tick % 100 < 50 ? .9 : 3;
            double expectedCarrierX = targetX + 80 + lead;
            double required = ProceduralCameraGeometry.RequiredDisplacement(targetX - carrierX, -39.5, 0,
                80, 40, 90, false, lead);
            Near(Math.Abs(expectedCarrierX - carrierX), required);
            Check(required < CameraReachability.ImmediateLimit);
            carrierX = expectedCarrierX;
        }
        Check(targetX > 3000);
        // Stopping drops actual look-ahead, requiring only its previous3m offset.
        Near(3, ProceduralCameraGeometry.RequiredDisplacement(targetX - carrierX, -39.5, 0, 80, 40, 90, false));
    }

    private static void LeadAndDistance()
    {
        Near(3, ProceduralCameraGeometry.RequiredDisplacement(-77, -39.5, 0, 80, 40, 90, false));
        Near(6, ProceduralCameraGeometry.RequiredDisplacement(-77, -39.5, 0, 80, 40, 90, false, 3));
        Check(ProceduralCameraGeometry.RequiredDisplacement(1000, 0, 1000, 80, 40, 90, false) > CameraReachability.ImmediateLimit);
    }

    private static void InteriorOffsets()
    {
        AutonomyPosition wide = ProceduralCameraGeometry.Offset(0, 80, 40, 90, true);
        Near(6, wide.X); Near(1.8, wide.Y); Near(0, wide.Z);
        AutonomyPosition close = ProceduralCameraGeometry.Offset(6, 80, 40, 90, true);
        Near(3, close.X); Near(.5, close.Y); Near(0, close.Z);
        Near(0, ProceduralCameraGeometry.RequiredDisplacement(-6, -1.3, 0, 80, 40, 90, true));
    }

    private static void InvalidGeometry()
    {
        Check(double.IsPositiveInfinity(ProceduralCameraGeometry.RequiredDisplacement(double.NaN, 0, 0, 18, 10, 90, false)));
        Check(double.IsPositiveInfinity(ProceduralCameraGeometry.RequiredDisplacement(0, 0, 0, -18, 10, 90, false)));
        Check(double.IsPositiveInfinity(ProceduralCameraGeometry.RequiredDisplacement(0, 0, 0, 18, 10, 90, false, double.PositiveInfinity)));
        Check(double.IsPositiveInfinity(ProceduralCameraGeometry.RequiredDisplacement(double.MaxValue, 0, double.MaxValue, 18, 10, 90, false)));
    }
}
