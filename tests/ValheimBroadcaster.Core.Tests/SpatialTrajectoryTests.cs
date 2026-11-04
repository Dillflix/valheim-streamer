using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class SpatialTrajectoryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Spatial routes preserve short turns and measure the full winding distance", WindingDistance);
        yield return ("Spatial shot trajectories copy input buffers and expose no mutable waypoint storage", ImmutableBuffers);
        yield return ("Spatial aim progression follows distance and settles smoothly at measured viewpoints", PairedAim);
        yield return ("Spatial routes retain shot scale and bounded meaningful duration", DurationAndLens);
        yield return ("Spatial terrain fitting translates every waypoint and preserves the subject path", RaisedRoute);
        yield return ("Spatial interval slices preserve all interior corners and endpoint composition", SliceCorners);
        yield return ("Spatial full slices preserve the original route and execution ownership", FullSlice);
        yield return ("Spatial proposals reject repeated or zero-length segments and unbounded geometry", InvalidGeometry);
        yield return ("Spatial proposals reject invalid paired aims and near-vertical sightlines", InvalidAim);
        yield return ("Spatial sampling never advances a path and preserves explicit hold behavior", ReadOnlySampling);
    }

    private static AutonomyPosition P(float x, float y = 25, float z = 0) => new AutonomyPosition(x, y, z);
    private static ScenicFeature Feature() => new ScenicFeature("ridge", ScenicFeatureKind.Ridge, P(8, 22, 25), P(1, 0), 12, 6, .9f);
    private static AutonomyPosition[] Body() => new[] { P(0), P(8), P(8, 25, 1), P(8, 25, 9), P(16, 25, 9) };
    private static AutonomyPosition[] Aims() => new[] { P(2, 22, 25), P(4, 23, 26), P(6, 24, 27), P(8, 24, 28), P(10, 25, 29) };
    private static ScenicShotPlan Plan(bool native = true) => ScenicShotPlan.Spatial(10, Feature(), ScenicShotRole.Traverse, Body(), Aims(), 53, .1f, -.04f, native);
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static void Near(AutonomyPosition actual, AutonomyPosition expected, double tolerance = .0001) =>
        Check(actual.Distance(expected) <= tolerance, "Position error " + actual.Distance(expected));
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Malformed spatial proposal was accepted."); }
    private static double TimeAtDistance(double fraction)
    {
        double lo = 0, hi = 1;
        for (int i = 0; i < 50; i++)
        { double middle = (lo + hi) * .5; if (ScenicMotionProfile.Progress(middle) < fraction) lo = middle; else hi = middle; }
        return (lo + hi) * .5;
    }

    private static void WindingDistance()
    {
        ScenicShotPlan plan = Plan();
        Check(plan.IsSpatial && plan.WaypointCount == 5 && Math.Abs(plan.TravelMetres - 25) < .0001, "Distance collapsed to a chord or lost the short turn.");
        for (int i = 0; i < plan.WaypointCount; i++)
            Near(plan.Sample(TimeAtDistance(plan.WaypointFraction(i))).CameraPosition, plan.CameraWaypoint(i));
        // Distance 8.5 is on the one-metre north leg, not a diagonal from the
        // long east leg toward the following viewpoints.
        Near(plan.Sample(TimeAtDistance(8.5 / 25)).CameraPosition, P(8, 25, .5f));
        Check(plan.TravelMetres > plan.CameraWaypoint(0).Distance(plan.CameraWaypoint(4)) + 5, "Fixture does not exercise a winding route.");
    }

    private static void ImmutableBuffers()
    {
        var body = Body(); var aims = Aims();
        var plan = ScenicShotPlan.Spatial(10, Feature(), ScenicShotRole.Reveal, body, aims, 53);
        var original = plan.Sample(.4); double distance = plan.TravelMetres;
        body[1] = P(180); aims[1] = P(-100, 0, 0);
        Near(plan.CameraWaypoint(1), P(8)); Near(plan.AimWaypoint(1), P(4, 23, 26));
        Near(plan.Sample(.4).CameraPosition, original.CameraPosition); Near(plan.Sample(.4).AimPosition, original.AimPosition);
        Check(plan.TravelMetres == distance, "Mutating search storage altered prepared distance metadata.");
        Reject(() => plan.CameraWaypoint(-1)); Reject(() => plan.AimWaypoint(5)); Reject(() => plan.WaypointFraction(5));
    }

    private static void PairedAim()
    {
        var plan = Plan();
        for (int i = 0; i < plan.WaypointCount; i++) Near(plan.RouteAimAt(plan.WaypointFraction(i)), plan.AimWaypoint(i));
        double at = plan.WaypointFraction(2), epsilon = .00001;
        Check(plan.RouteAimAt(at - epsilon).Distance(plan.RouteAimAt(at + epsilon)) < .00001, "Aim jumps or pans abruptly through a measured waypoint.");
        for (int i = 0; i < plan.WaypointCount - 1; i++)
        for (int step = 0; step <= 10; step++)
        {
            double from = plan.WaypointFraction(i), to = plan.WaypointFraction(i + 1);
            var aim = plan.RouteAimAt(from + (to - from) * step / 10);
            var a = plan.AimWaypoint(i); var b = plan.AimWaypoint(i + 1);
            Check(aim.X >= Math.Min(a.X, b.X) && aim.X <= Math.Max(a.X, b.X) &&
                aim.Y >= Math.Min(a.Y, b.Y) && aim.Y <= Math.Max(a.Y, b.Y) &&
                aim.Z >= Math.Min(a.Z, b.Z) && aim.Z <= Math.Max(a.Z, b.Z), "Aim overshot its measured endpoints.");
        }
        Reject(() => plan.RouteAimAt(double.NaN)); Reject(() => plan.RouteAimAt(1.01));
    }

    private static void DurationAndLens()
    {
        var plan = Plan();
        var longBody = new[] { P(-30), P(-10), P(10), P(30) };
        var longAims = new[] { P(0, 22, 30), P(0, 22, 30), P(0, 22, 30), P(0, 22, 30) };
        var longPlan = ScenicShotPlan.Spatial(11, Feature(), ScenicShotRole.Detail, longBody, longAims, 62);
        Check(plan.DurationSeconds >= 8 && plan.DurationSeconds <= 30 && longPlan.DurationSeconds == 30, "Spatial editing duration is not bounded by meaningful travel.");
        Check(plan.Sample(2 / plan.DurationSeconds).CameraPosition.Distance(plan.Sample(0).CameraPosition) > .5,
            "The spatial shot does not develop meaningful movement during its smooth entry.");
        for (int i = 0; i <= 100; i++)
        {
            var sample = plan.Sample(i / 100d);
            Check(plan.FieldOfView == 53 && sample.ScreenX == .1f && sample.ScreenY == -.04f, "Spatial execution introduced lens pumping or composition drift.");
        }
    }

    private static void RaisedRoute()
    {
        var plan = Plan(); var raised = plan.RaiseCamera(6);
        Check(raised.IsSpatial && raised.WaypointCount == plan.WaypointCount && raised.Id == plan.Id, "Terrain fitting replaced the winding trajectory.");
        for (int i = 0; i < plan.WaypointCount; i++)
        {
            var p = plan.CameraWaypoint(i); Near(raised.CameraWaypoint(i), P(p.X, p.Y + 6, p.Z));
            Near(raised.AimWaypoint(i), plan.AimWaypoint(i));
        }
        Near(plan.CameraWaypoint(0), P(0));
        Check(Math.Abs(raised.TravelMetres - plan.TravelMetres) < .0001 && ReferenceEquals(plan.RaiseCamera(0), plan), "Rigid fitting altered travel or zero-fit identity.");
    }

    private static void SliceCorners()
    {
        var plan = Plan(); double from = TimeAtDistance(2d / 25), to = TimeAtDistance(22d / 25);
        var slice = plan.Slice(from, to);
        Check(slice.IsSpatial && slice.WaypointCount == 5 && Math.Abs(slice.TravelMetres - 20) < .0001, "Slice collapsed the interior turns.");
        Near(slice.Sample(0).CameraPosition, plan.Sample(from).CameraPosition); Near(slice.Sample(1).CameraPosition, plan.Sample(to).CameraPosition);
        Near(slice.Sample(0).AimPosition, plan.Sample(from).AimPosition); Near(slice.Sample(1).AimPosition, plan.Sample(to).AimPosition);
        for (int i = 1; i < 4; i++) Near(slice.CameraWaypoint(i), plan.CameraWaypoint(i));
        Check(slice.FieldOfView == plan.FieldOfView && slice.Role == plan.Role && slice.UseNativeSpline == plan.UseNativeSpline, "Slice lost cinematic metadata.");
    }

    private static void FullSlice()
    {
        foreach (bool native in new[] { false, true })
        {
            var plan = Plan(native); var slice = plan.Slice(0, 1);
            Check(slice.IsSpatial && slice.UseNativeSpline == native && slice.WaypointCount == plan.WaypointCount, "Full slice changed geometry ownership.");
            for (int i = 0; i <= 100; i++)
            { Near(slice.Sample(i / 100d).CameraPosition, plan.Sample(i / 100d).CameraPosition); Near(slice.Sample(i / 100d).AimPosition, plan.Sample(i / 100d).AimPosition); }
        }
    }

    private static void InvalidGeometry()
    {
        Reject(() => ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, new[] { P(0) }, new[] { P(0, 22, 30) }, 50));
        Reject(() => ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, Body(), new[] { P(0, 22, 30) }, 50));
        Reject(() => ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, new[] { P(0), P(0) }, new[] { P(0, 22, 30), P(0, 22, 30) }, 50));
        Reject(() => ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, new[] { P(0), P(.01f), P(10) }, new[] { P(0, 22, 30), P(0, 22, 30), P(0, 22, 30) }, 50));
        Reject(() => ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, new[] { P(0), P(41) }, new[] { P(0, 22, 30), P(0, 22, 30) }, 50));
        var body = new AutonomyPosition[17]; var aims = new AutonomyPosition[17];
        for (int i = 0; i < body.Length; i++) { body[i] = P(i); aims[i] = P(i, 22, 30); }
        Reject(() => ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, body, aims, 50));
        Reject(() => ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, Body(), Aims(), float.NaN));
    }

    private static void InvalidAim()
    {
        Reject(() => ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, new[] { P(0), P(5) }, new[] { P(0, 40), P(5, 40) }, 50));
        // Endpoint directions are valid, but the center crosses a vertical look
        // direction. A waypoint-only check would miss this degenerate passage.
        Reject(() => ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, new[] { P(-5), P(5) }, new[] { P(5, 40), P(-5, 40) }, 50));
        Reject(() => ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, new[] { P(0), P(5) }, new[] { P(300), P(300) }, 50));
    }

    private static void ReadOnlySampling()
    {
        var plan = Plan(); var before = plan.Sample(.35);
        for (int i = 100; i >= 0; i--) plan.Sample(i / 100d);
        Near(before.CameraPosition, plan.Sample(.35).CameraPosition); Near(before.AimPosition, plan.Sample(.35).AimPosition);
        Near(plan.Sample(1, 0).CameraPosition, plan.Sample(0).CameraPosition);
        Near(plan.Sample(1, dynamic: false).AimPosition, plan.Sample(0).AimPosition);
        Near(plan.Sample(.75, .2f).CameraPosition, plan.Sample(.75, 1.5f).CameraPosition);
    }
}
