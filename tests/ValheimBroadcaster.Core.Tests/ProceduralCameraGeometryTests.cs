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
        yield return ("Foliage recovery adds compact lower viewpoints around all sides", RecoveryViews);
        yield return ("Foliage search does not alter loading reachability", RecoveryDoesNotAuthorizeTravel);
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

    private static void RecoveryViews()
    {
        Check(ProceduralCameraGeometry.TotalCandidateCount <= ProceduralShotPolicy.MaximumCandidates);
        bool below = false;
        for (int i = 0; i < ProceduralCameraGeometry.RecoveryCandidateCount; i++)
        {
            var view = ProceduralCameraGeometry.RecoveryOffset(i, 0);
            double radius = Math.Sqrt(view.X * view.X + view.Z * view.Z);
            Check(radius >= 3.19 && radius <= 4.81);
            Check(view.Y >= -.21 && view.Y <= 1.21); below |= view.Y < 0;
            var opposite = ProceduralCameraGeometry.RecoveryOffset(i, 180);
            Near(-view.X, opposite.X); Near(-view.Z, opposite.Z);
            var scaled = ProceduralCameraGeometry.RecoveryOffset(i, 0, .5);
            Near(view.X * .5, scaled.X); Near(view.Y * .5, scaled.Y);
        }
        Check(below);
    }

    private static void RecoveryDoesNotAuthorizeTravel()
    {
        // Extra picture-search angles do not enlarge the nominal acquisition
        // contract or change its meaning when a close view looks attractive.
        var recovery = ProceduralCameraGeometry.RecoveryOffset(0, 0);
        double targetX = -recovery.X, targetY = .5 - recovery.Y, targetZ = -recovery.Z;
        // This recovery endpoint would put the carrier exactly at the origin.
        Near(0, targetX + recovery.X); Near(0, targetY + 1.5 + recovery.Y - 2);
        Near(0, targetZ + recovery.Z);
        // Yet a configured 300m/100m acquisition still reports the nominal
        // supported candidate distance, rather than silently using that endpoint.
        Check(ProceduralCameraGeometry.RequiredDisplacement(targetX, targetY, targetZ,
            300, 100, 0, false) > CameraReachability.ImmediateLimit);
    }

    private static void InvalidGeometry()
    {
        Check(double.IsPositiveInfinity(ProceduralCameraGeometry.RequiredDisplacement(double.NaN, 0, 0, 18, 10, 90, false)));
        Check(double.IsPositiveInfinity(ProceduralCameraGeometry.RequiredDisplacement(0, 0, 0, -18, 10, 90, false)));
        Check(double.IsPositiveInfinity(ProceduralCameraGeometry.RequiredDisplacement(0, 0, 0, 18, 10, 90, false, double.PositiveInfinity)));
        Check(double.IsPositiveInfinity(ProceduralCameraGeometry.RequiredDisplacement(double.MaxValue, 0, double.MaxValue, 18, 10, 90, false)));
    }
}
