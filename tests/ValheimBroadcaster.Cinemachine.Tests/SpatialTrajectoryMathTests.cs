using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin;

internal static class SpatialTrajectoryMathTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Native spatial splines retain every measured turn including short corridor legs", MeasuredKnots);
        yield return ("Native spatial samples and Dolly distances agree over the actual curved route", NativeDistance);
        yield return ("Native spatial aim arrives at each paired viewpoint using actual segment lengths", SynchronizedAim);
        yield return ("Native spatial turn handles are forward and bounded by adjoining clear corridors", BoundedHandles);
        yield return ("Native measured spatial slices retain all interior turns and selected endpoint aims", MeasuredSlice);
        yield return ("Native spatial full slices preserve the same body and aim trajectory", FullSlice);
        yield return ("Spatial terrain fitting invalidates stale native geometry even with the same plan ID", RaisedCache);
        yield return ("Spatial dolly-off execution retains authored polyline ownership", AuthoredOwnership);
        yield return ("Native spatial previews are immutable and reset removes all retained path state", PreviewAndReset);
        yield return ("Spatial preflight waypoint times match both actual native and authored body ownership", WaypointTimes);
        yield return ("Spatial interval distance exposes hidden travel around a short returning corridor", HiddenArc);
        yield return ("Spatial native smoothing rejects out-of-world control geometry before sampling", WorldBoundControls);
    }

    private static AutonomyPosition P(float x, float y = 25, float z = 0) => new AutonomyPosition(x, y, z);
    private static Vector3 V(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
    private static AutonomyPosition P(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
    private static ScenicShotPlan Plan(bool native = true)
    {
        var feature = new ScenicFeature("ridge", ScenicFeatureKind.Ridge, P(8, 22, 25), P(1, 0), 12, 6, .9f);
        return ScenicShotPlan.Spatial(10, feature, ScenicShotRole.Traverse,
            new[] { P(0), P(8), P(8, 25, 1), P(8, 25, 9), P(16, 25, 9) },
            new[] { P(2, 22, 25), P(4, 23, 26), P(6, 24, 27), P(8, 24, 28), P(10, 25, 29) },
            53, .1f, -.04f, native);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(AutonomyPosition actual, AutonomyPosition expected, double tolerance = .003) =>
        Check(actual.Distance(expected) <= tolerance, "Position error " + actual.Distance(expected));
    private static float TimeAtDistance(double fraction, ScenicShotPlan plan)
    {
        double lo = 0, hi = 1;
        for (int i = 0; i < 40; i++)
        { double middle = (lo + hi) * .5; if (plan.MotionFraction(middle) < fraction) lo = middle; else hi = middle; }
        return (float)((lo + hi) * .5);
    }

    private static void MeasuredKnots()
    {
        var plan = Plan(); using var path = new ScenicSplinePath(); Check(path.Prepare(plan, 1), "Spatial native path failed preparation.");
        Check(path.Spline!.Count == plan.WaypointCount, "Generic re-sampling replaced measured corridor knots.");
        for (int i = 0; i < plan.WaypointCount; i++)
        {
            Near(P((Vector3)path.Spline[i].Position), plan.CameraWaypoint(i));
            float distance = path.Spline.CurveToSplineT(i);
            Near(path.Sample(TimeAtDistance(distance, path.PreparedPlan!)).CameraPosition, plan.CameraWaypoint(i));
        }
        Check(path.Length > 24 && path.Length < 28, "Native travel collapsed winding corridors into an endpoint chord.");
    }

    private static void NativeDistance()
    {
        using var path = new ScenicSplinePath(); path.Prepare(Plan(), 1);
        float previous = -1;
        for (int i = 0; i <= 200; i++)
        {
            float progress = i / 200f;
            float distance = path.Distance(progress);
            Check(distance >= previous && distance >= 0 && distance <= path.Length, "Native camera distance reversed or exceeded the route.");
            float normalized = path.Spline!.ConvertIndexUnit(distance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
            Near(path.Sample(progress).CameraPosition, P(path.Spline.EvaluatePosition(normalized)));
            previous = distance;
        }
        // The first turn is actually rounded. Evaluating only the original
        // polyline would miss this deviation, so admission must inspect this
        // same native evaluator, not accept the graph edge certificates alone.
        Vector3 rounded = path.Spline!.EvaluatePosition(path.Spline.CurveToSplineT(.5f));
        Check(Math.Abs(rounded.z) > .001, "Fixture does not exercise the curve-vs-polyline safety distinction.");
    }

    private static void SynchronizedAim()
    {
        var plan = Plan(); using var path = new ScenicSplinePath(); path.Prepare(plan, 1);
        for (int i = 0; i < plan.WaypointCount; i++)
        {
            float progress = TimeAtDistance(path.Spline!.CurveToSplineT(i), path.PreparedPlan!);
            Near(path.Sample(progress).CameraPosition, plan.CameraWaypoint(i));
            Near(path.Sample(progress).AimPosition, plan.AimWaypoint(i));
        }
        for (int i = 0; i < plan.WaypointCount - 1; i++)
        {
            double middleDistance = (path.Spline!.CurveToSplineT(i) + path.Spline.CurveToSplineT(i + 1)) * .5;
            var actual = path.Sample(TimeAtDistance(middleDistance, path.PreparedPlan!)).AimPosition;
            var expected = plan.RouteAimAt((plan.WaypointFraction(i) + plan.WaypointFraction(i + 1)) * .5);
            Near(actual, expected);
        }
    }

    private static void BoundedHandles()
    {
        var plan = Plan(); using var path = new ScenicSplinePath(); path.Prepare(plan, 1);
        for (int i = 1; i < plan.WaypointCount - 1; i++)
        {
            Vector3 incoming = V(plan.CameraWaypoint(i)) - V(plan.CameraWaypoint(i - 1));
            Vector3 outgoing = V(plan.CameraWaypoint(i + 1)) - V(plan.CameraWaypoint(i));
            Vector3 handle = (Vector3)path.Spline![i].TangentOut;
            Check(handle.magnitude <= Math.Min(incoming.magnitude, outgoing.magnitude) * .251f, "A tangent escaped the local spacing bound.");
            Check(Vector3.Dot(handle, incoming) >= 0 && Vector3.Dot(handle, outgoing) >= 0, "Unequal corridor spacing caused a backward native handle.");
        }
    }

    private static void MeasuredSlice()
    {
        var plan = Plan(); using var path = new ScenicSplinePath(); path.Prepare(plan, 1);
        float from = .08f, to = .92f;
        var first = path.Sample(from); var last = path.Sample(to);
        var slice = path.ProposeMeasuredSlice(from, to);
        Check(slice.IsSpatial && slice.WaypointCount == 5, "Measured interval collapsed a winding route to a three-point curve.");
        Near(slice.CameraWaypoint(0), first.CameraPosition); Near(slice.CameraWaypoint(4), last.CameraPosition);
        Near(slice.AimWaypoint(0), first.AimPosition); Near(slice.AimWaypoint(4), last.AimPosition);
        for (int i = 1; i < 4; i++)
        { Near(slice.CameraWaypoint(i), plan.CameraWaypoint(i)); Near(slice.AimWaypoint(i), plan.AimWaypoint(i)); }
        using var rebuilt = new ScenicSplinePath(); Check(rebuilt.Prepare(slice, 1), "Measured slice could not be re-prepared for complete validation.");
        Near(rebuilt.Sample(0).CameraPosition, first.CameraPosition); Near(rebuilt.Sample(1).CameraPosition, last.CameraPosition);
        Check(slice.Id == plan.Id && slice.FieldOfView == 53 && slice.UseNativeSpline, "Measured interval lost route identity or fixed lens.");
    }

    private static void FullSlice()
    {
        var plan = Plan(); using var path = new ScenicSplinePath(); path.Prepare(plan, 1);
        var slice = path.ProposeMeasuredSlice(0, 1); using var rebuilt = new ScenicSplinePath(); rebuilt.Prepare(slice, 1);
        Check(slice.WaypointCount == plan.WaypointCount && Math.Abs(path.Length - rebuilt.Length) < .001, "Full measured slice changed the route.");
        for (int i = 0; i <= 100; i++)
        {
            Near(path.Sample(i / 100f).CameraPosition, rebuilt.Sample(i / 100f).CameraPosition);
            Near(path.Sample(i / 100f).AimPosition, rebuilt.Sample(i / 100f).AimPosition);
        }
    }

    private static void RaisedCache()
    {
        var plan = Plan(); plan = plan.WithDuration(plan.DurationSeconds);
        var raised = plan.RaiseCamera(7);
        using var path = new ScenicSplinePath(); path.Prepare(plan, 1); var oldSpline = path.Spline;
        var before = path.Sample(.4f); float length = path.Length;
        Check(path.Prepare(raised, 1) && !ReferenceEquals(oldSpline, path.Spline), "Same-ID immutable replacement reused stale geometry.");
        var after = path.Sample(.4f);
        Near(after.CameraPosition, P(before.CameraPosition.X, before.CameraPosition.Y + 7, before.CameraPosition.Z));
        Near(after.AimPosition, before.AimPosition);
        Check(Math.Abs(path.Length - length) < .001, "Rigid terrain fitting changed native distance.");
        Check(path.Prepare(plan, .4f) && !ReferenceEquals(oldSpline, path.Spline), "Returning to an old plan reused a disposed spline identity.");
        Near(path.Sample(.4f).CameraPosition, before.CameraPosition);
    }

    private static void AuthoredOwnership()
    {
        var plan = Plan(false); using var path = new ScenicSplinePath(); path.Prepare(plan, 1);
        for (int i = 0; i <= 100; i++)
        {
            var expected = path.PreparedPlan!.Sample(i / 100f); var actual = path.Sample(i / 100f);
            Near(actual.CameraPosition, expected.CameraPosition); Near(actual.AimPosition, expected.AimPosition);
        }
        var slice = path.ProposeMeasuredSlice(.08f, .92f);
        Check(slice.IsSpatial && !slice.UseNativeSpline && slice.WaypointCount == 5, "Authored slice changed path engine or dropped bends.");
    }

    private static void PreviewAndReset()
    {
        var plan = Plan(); using var path = new ScenicSplinePath(); path.Prepare(plan, 1);
        var spline = path.Spline; var before = path.Sample(.35f);
        for (int i = 100; i >= 0; i--) path.Sample(i / 100f);
        Check(path.Prepare(plan, .2f) && ReferenceEquals(spline, path.Spline), "Live speed controls reshaped an accepted native corridor.");
        Near(path.Sample(.35f).CameraPosition, before.CameraPosition); Near(path.Sample(.35f).AimPosition, before.AimPosition);
        path.Reset(); Check(!path.Ready && path.Spline == null && path.Length == 0, "Reset retained geometry or distance state.");
        Check(path.Prepare(plan, 1), "A reset path could not be prepared again.");
        Near(path.Sample(.35f).AimPosition, before.AimPosition);
    }

    private static void WaypointTimes()
    {
        foreach (bool native in new[] { false, true })
        {
            var plan = Plan(native); using var path = new ScenicSplinePath(); path.Prepare(plan, 1);
            float previous = -1;
            for (int i = 0; i < plan.WaypointCount; i++)
            {
                float progress = path.WaypointProgress(i);
                Check(progress > previous && progress >= 0 && progress <= 1, "Preflight waypoint times are not ordered.");
                Near(path.Sample(progress).CameraPosition, plan.CameraWaypoint(i));
                Near(path.Sample(progress).AimPosition, plan.AimWaypoint(i));
                previous = progress;
            }
            double length = native ? path.Length : plan.TravelMetres;
            Check(Math.Abs(path.SpatialTravelBetween(0, 1) - length) < .0001, "Preflight arc distance uses the wrong route engine.");
        }
    }

    private static void HiddenArc()
    {
        // The short returning detour is only 0.4 m across, but travels more
        // than the evaluator's 0.75 m sampling limit. Chord-only refinement
        // can miss its intervening turns even though each is real camera space.
        var feature = new ScenicFeature("detour", ScenicFeatureKind.Ridge, P(10, 22, 25), P(1, 0), 12, 6, .9f);
        var body = new[] { P(0), P(10), P(10, 25, .3f), P(9.6f, 25, .3f), P(9.6f), P(20) };
        var aims = new AutonomyPosition[body.Length]; for (int i = 0; i < aims.Length; i++) aims[i] = P(10, 22, 25);
        foreach (bool native in new[] { false, true })
        {
            var plan = ScenicShotPlan.Spatial(11, feature, ScenicShotRole.Traverse, body, aims, 53, useNativeSpline: native);
            using var path = new ScenicSplinePath(); path.Prepare(plan, 1);
            float from = path.WaypointProgress(1), to = path.WaypointProgress(4);
            double chord = path.Sample(from).CameraPosition.Distance(path.Sample(to).CameraPosition);
            Check(chord < .5 && path.SpatialTravelBetween(from, to) > .9f,
                "Hidden detour would still appear safe to a chord-only preflight.");
            Check(path.SpatialTravelBetween(from, from) == 0, "Repeated preflight sampling fabricated travel.");
        }
    }

    private static void WorldBoundControls()
    {
        var aim = P(19990, 22, 20);
        var feature = new ScenicFeature("world-edge", ScenicFeatureKind.Ridge, aim, P(1, 0), 12, 6, .9f);
        var plan = ScenicShotPlan.Spatial(12, feature, ScenicShotRole.Traverse,
            new[] { P(19995), P(20000), P(20000, 25, 5) }, new[] { aim, aim, aim }, 53);
        using var path = new ScenicSplinePath();
        Check(!path.Prepare(plan, 1) && !path.Ready, "Native control overshoot can escape bounded world coordinates during a live sample.");
        var authored = ScenicShotPlan.Spatial(12, feature, ScenicShotRole.Traverse,
            new[] { P(19995), P(20000), P(20000, 25, 5) }, new[] { aim, aim, aim }, 53, useNativeSpline: false);
        Check(path.Prepare(authored, 1), "Unused native control geometry rejected a bounded authored route.");
        Near(path.Sample(1).CameraPosition, authored.CameraWaypoint(2));
        Check(path.Prepare(Plan(), 1), "An invalid proposal poisoned later valid path preparation.");
    }
}
