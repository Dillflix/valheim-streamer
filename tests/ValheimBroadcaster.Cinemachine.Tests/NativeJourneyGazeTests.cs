using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin;

internal static class NativeJourneyGazeTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Native journey gaze develops through a bending corridor at the selected cruise", BendingCorridor);
        yield return ("Native gaze rejects a fast turn even when its endpoint aims are valid", RejectFastTurn);
        yield return ("Native journey arrival framing resolves a nearby observed subject", Arrival);
        yield return ("Native evolving journey windows retain exact gaze, position and composition at their seam", Seam);
        yield return ("Native terminal approach reaches a stand-off view without passing through its destination", StandOffApproach);
    }
    private static AutonomyPosition P(double x, double y = 12, double z = 0) => new AutonomyPosition((float)x, (float)y, (float)z);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static ScenicFeature Feature() => new ScenicFeature("gaze-native", ScenicFeatureKind.Coastline, P(20, 2), P(1, 0), 12, 5, 1);
    private static AutonomyPosition[] Curve(AutonomyPosition start, double initialAngle = 0)
    {
        var body = new AutonomyPosition[13]; double x = start.X, z = start.Z;
        for (int i = 0; i < body.Length; i++)
        {
            body[i] = P(x, start.Y, z);
            double angle = (initialAngle + i * 3) * Math.PI / 180;
            x += 4 * Math.Cos(angle); z += 4 * Math.Sin(angle);
        }
        return body;
    }
    private static ScenicShotPlan Build(long id, AutonomyPosition[] body, AutonomyPosition firstAim,
        double speed, bool entry, AutonomyPosition? subject = null)
    {
        Check(JourneyGazePolicy.TryBuild(body, firstAim, speed, null, subject, out var aims, out var reason), reason);
        return ScenicShotPlan.Spatial(id, Feature(), ScenicShotRole.Traverse, body, aims, 61, .1f, -.04f)
            .AsJourney(19, id - 1, 48 / speed / ScenicJourneyMotion.CruiseFraction(entry, false))
            .WithJourneyRamps(entry, false);
    }
    private static double Angle(ScenicShotSample before, ScenicShotSample after)
    {
        double ax = before.AimPosition.X - before.CameraPosition.X, ay = before.AimPosition.Y - before.CameraPosition.Y,
            az = before.AimPosition.Z - before.CameraPosition.Z;
        double bx = after.AimPosition.X - after.CameraPosition.X, by = after.AimPosition.Y - after.CameraPosition.Y,
            bz = after.AimPosition.Z - after.CameraPosition.Z;
        return Math.Acos(Math.Max(-1, Math.Min(1, (ax * bx + ay * by + az * bz) /
            Math.Sqrt((ax * ax + ay * ay + az * az) * (bx * bx + by * by + bz * bz))))) * 180 / Math.PI;
    }
    private static void BendingCorridor()
    {
        foreach (double speed in new[] { 2d, 6d, 12d })
        {
            var body = Curve(P(0)); var plan = Build(22, body, P(24, 10), speed, true);
            using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Native curve failed");
            double duration = native.Length / speed / ScenicJourneyMotion.CruiseFraction(true, false);
            Check(NativeJourneyGaze.WithinRate(native, duration), "Developing native gaze exceeded its angular rate at " + speed + "m/s");
            Check(Angle(native.Sample(0), native.Sample(1)) > 15, "Native execution restored constant inherited gaze");
            Check(native.Sample(0).AimPosition.Distance(plan.Sample(0).AimPosition) < .0001, "Native departure moved its first aim");
            for (int i = 0; i <= 256; i++)
            {
                var frame = native.Sample(i / 256f);
                Check(frame.AimPosition.X > frame.CameraPosition.X && frame.AimPosition.Z >= frame.CameraPosition.Z - .001,
                    "Developing gaze looked away from the forward corridor");
                Check(frame.ScreenX == .1f && frame.ScreenY == -.04f && native.PreparedPlan!.FieldOfView == 61,
                    "Independent gaze changed the admitted lens/composition");
            }
        }
    }
    private static void RejectFastTurn()
    {
        var body = new[] { P(0), P(4), P(8), P(12) };
        var aims = new[] { P(20, 10), P(24, 10), P(8, 10, 20), P(12, 10, 20) };
        var plan = ScenicShotPlan.Spatial(22, Feature(), ScenicShotRole.Traverse, body, aims, 61).AsJourney(19, 21, 2);
        using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Fast-turn test route failed");
        Check(!NativeJourneyGaze.WithinRate(native, 2), "Native interpolation admitted a 90-degree snap at cruise");
    }
    private static void Arrival()
    {
        var body = new AutonomyPosition[13]; for (int i = 0; i < body.Length; i++) body[i] = P(i * 4);
        var subject = P(66, 7, 10); var plan = Build(22, body, P(24, 10), 6, false, subject);
        using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Arrival route failed");
        Check(NativeJourneyGaze.WithinRate(native, native.Length / 6), "Native reveal exceeds its bounded angular rate");
        var end = native.Sample(1);
        Check(Angle(end, end.WithAimPosition(subject)) < 2, "Native arrival retained inherited landscape gaze instead of the observed subject");
    }
    private static void Seam()
    {
        var first = Build(22, Curve(P(-1620, 42, 602)), P(-1596, 40, 602), 6, true);
        using var a = new ScenicSplinePath(); Check(a.Prepare(first, 1), "First native section failed");
        var end = a.Sample(1);
        var next = Build(23, Curve(end.CameraPosition, 33), end.AimPosition, 6, false);
        using var b = new ScenicSplinePath(); Check(b.Prepare(next, 1), "Second native section failed");
        var start = b.Sample(0);
        Check(start.CameraPosition.Distance(end.CameraPosition) < .0001 && start.AimPosition.Distance(end.AimPosition) < .0001 &&
            start.ScreenX == end.ScreenX && start.ScreenY == end.ScreenY && next.FieldOfView == first.FieldOfView,
            "Continuation changed the admitted full endpoint view");
        Check(NativeJourneyGaze.WithinRate(b, b.Length / 6), "Continuation gaze cannot develop at cruise");
        Check(Angle(end, b.Sample(.01f)) < 1, "A section boundary introduces an immediate viewing jump");
    }
    private static void StandOffApproach()
    {
        var destination = P(52, 2, 4); var subject = P(52, 6, 4);
        Check(JourneyArrivalGeometry.TryBuild(P(0), P(1, 0), destination, 16, subject, 64, 0,
            out var body, out var reason), reason);
        var plan = Build(22, body, P(24, 10), 6, false, subject);
        using var native = new ScenicSplinePath(); Check(native.Prepare(plan, 1), "Native arrival approach failed");
        Check(NativeJourneyGaze.WithinRate(native, native.Length / 6), "Stand-off approach cannot develop its gaze at cruise");
        var last = native.Sample(1);
        Check(last.CameraPosition.HorizontalDistance(destination) >= 8 && last.CameraPosition.HorizontalDistance(destination) <= 16 &&
            last.CameraPosition.Distance(body[body.Length - 1]) < .001 && Angle(last, last.WithAimPosition(subject)) < 1,
            "Native approach lost its authored arrival stand-off or subject");
        double previous = 1000;
        for (int i = 0; i <= 256; i++)
        {
            double distance = native.Sample(i / 256f).CameraPosition.HorizontalDistance(destination);
            Check(distance <= previous + .001 && distance >= 7.99, "Native approach passed its destination or retreated from it");
            previous = distance;
        }
    }
}
