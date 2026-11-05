using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin;

/// <summary>Production graph/gaze policy and actual native spline mathematics
/// against an explicitly analytic world. No Unity scene, physics, foliage,
/// loading or actual rendered-picture acceptance is simulated by this test.</summary>
internal static class NativeNavigationTests
{
    private const double PhysicalEnvelope = .85, PlanningEnvelope = 1.30;
    private const int DenseSamples = 2048;
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Connected search and bounded gaze produce a native travelling curve through staggered analytic obstacles", StaggeredNativeRoute);
        yield return ("Native smoothing can invalidate clear graph edges without erasing the connected search frontier", UnsafeSmoothing);
        yield return ("Connected navigation refuses a backward exact entry before native path admission", InvalidEntry);
        yield return ("Native temporal gaze verification includes Journey ramps exactly once and respects later retiming", RampedPeakRate);
    }
    private static AutonomyPosition P(double x, double z = 0, double y = 20) => new AutonomyPosition((float)x, (float)y, (float)z);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static AutonomyPosition Add(AutonomyPosition a, AutonomyPosition b, double scale = 1) =>
        new AutonomyPosition((float)(a.X + b.X * scale), (float)(a.Y + b.Y * scale), (float)(a.Z + b.Z * scale));
    private static AutonomyPosition Difference(AutonomyPosition a, AutonomyPosition b) =>
        new AutonomyPosition(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private readonly struct Box
    {
        private readonly double x0, x1, z0, z1;
        internal Box(double minX, double maxX, double minZ, double maxZ)
        { x0 = minX; x1 = maxX; z0 = minZ; z1 = maxZ; }
        internal bool Hits(AutonomyPosition a, AutonomyPosition b, double radius)
        {
            double lo = 0, hi = 1;
            return Axis(a.X, b.X - a.X, x0 - radius, x1 + radius, ref lo, ref hi) &&
                Axis(a.Z, b.Z - a.Z, z0 - radius, z1 + radius, ref lo, ref hi);
        }
        private static bool Axis(double value, double delta, double min, double max, ref double lo, ref double hi)
        {
            if (Math.Abs(delta) < .0000001) return value >= min && value <= max;
            double a = (min - value) / delta, b = (max - value) / delta;
            lo = Math.Max(lo, Math.Min(a, b)); hi = Math.Min(hi, Math.Max(a, b)); return lo <= hi;
        }
    }

    private sealed class World
    {
        internal readonly Box[] Solids = { new Box(8, 10, -30, 1), new Box(18, 20, -1, 30) };
        internal AutonomyPosition Goal = P(32);
        internal double Speed = 2;
        internal bool Clear(AutonomyPosition from, AutonomyPosition to, double radius)
        {
            if (Math.Abs(to.Y - 20) > .001 || to.X < -12 || to.X > 72 || Math.Abs(to.Z) > 12) return false;
            foreach (Box box in Solids) if (box.Hits(from, to, radius)) return false;
            return true;
        }
        internal JourneyNavigationObservation Observe(JourneyNavigationProbe probe)
        {
            bool clear = Clear(probe.From.Position, probe.Position, PlanningEnvelope);
            AutonomyPosition aim = default;
            if (probe.RequiredAim.HasValue)
            {
                aim = probe.RequiredAim.Value;
                clear &= probe.IsStart || JourneyNavigationGazePolicy.ForwardCompatible(probe.From.Position, probe.Position, aim);
            }
            else
            {
                bool view = false;
                for (int option = 0; option < 3 && !view; option++)
                    view = JourneyNavigationGazePolicy.TryAim(probe.From, probe.Position, Goal, Speed, option, out aim);
                clear &= view;
            }
            return new JourneyNavigationObservation(clear ? JourneyNavigationEvidence.Clear : JourneyNavigationEvidence.Blocked,
                new JourneyNavigationPose(probe.Position, aim), clear ? .9 : 0, 3, 0);
        }
    }

    private static JourneyNavigationRequest Request(JourneyNavigationPose? start = null, JourneyNavigationPose? entry = null,
        AutonomyPosition? goal = null, double target = 56) => new JourneyNavigationRequest(7, 3,
            start ?? new JourneyNavigationPose(P(0), P(28)), entry ?? new JourneyNavigationPose(P(2), P(30)),
            goal ?? P(32), start?.Position ?? P(0), target, 64, 85, 4, 512, 8192);

    private static JourneyNavigationSearch Search(World world, JourneyNavigationRequest request)
    {
        var search = new JourneyNavigationSearch(); search.Begin(request, 0);
        Advance(search, world);
        return search;
    }
    private static void Advance(JourneyNavigationSearch search, World world)
    {
        for (int i = 0; i < 20000 && (search.Status == JourneyNavigationStatus.Searching ||
            search.Status == JourneyNavigationStatus.WaitingForObservations); i++) search.Step(16, 0, 3, world.Observe);
    }
    private static ScenicShotPlan Plan(JourneyNavigationPath route, long id = 22, long predecessor = 21)
    {
        var body = new AutonomyPosition[route.Poses.Count]; var aims = new AutonomyPosition[body.Length];
        for (int i = 0; i < body.Length; i++) { body[i] = route.Poses[i].Position; aims[i] = route.Poses[i].Aim; }
        var feature = new ScenicFeature("analytic-navigation", ScenicFeatureKind.Landscape, aims[0], P(1, 0, 0), 12, 5, 1);
        return ScenicShotPlan.Spatial(id, feature, ScenicShotRole.Traverse, body, aims, 61, .1f, -.04f)
            .AsJourney(7, predecessor, route.Length / 2).WithJourneyRamps(false, false).WithJourneyCruiseSpeed(2);
    }
    private static bool ClearNative(ScenicSplinePath native, World world)
    {
        AutonomyPosition prior = native.Sample(0).CameraPosition;
        for (int i = 0; i <= DenseSamples; i++)
        {
            AutonomyPosition next = native.Sample(i / (float)DenseSamples).CameraPosition;
            if (!world.Clear(prior, next, PhysicalEnvelope)) return false;
            prior = next;
        }
        return true;
    }
    private static (JourneyNavigationSearch Search, JourneyNavigationPath Route, ScenicShotPlan Plan) NativeReady(World world,
        JourneyNavigationRequest request, long id = 22, long predecessor = 21)
    {
        var search = Search(world, request);
        for (int candidate = 0; candidate < 40 && search.Status == JourneyNavigationStatus.Ready; candidate++)
        {
            var route = search.Result!; var plan = Plan(route, id, predecessor);
            using var native = new ScenicSplinePath();
            if (native.Prepare(plan, 1) && ClearNative(native, world) && NativeJourneyGaze.WithinRate(native, native.Length / world.Speed))
                return (search, route, plan);
            search.RejectCandidate(); Advance(search, world);
        }
        throw new Exception("No native-safe connected proposal: " + search.Status + "; " + search.Diagnostics);
    }

    private static void StaggeredNativeRoute()
    {
        var world = new World(); var request = Request(); var accepted = NativeReady(world, request);
        bool positive = false, negative = false;
        foreach (JourneyNavigationPose pose in accepted.Route.Poses)
        { positive |= pose.Position.Z > 2.3; negative |= pose.Position.Z < -2.3; }
        Check(accepted.Route.AtGoal && positive && negative, "Native test bypassed the alternating-obstacle requirement");
        using var native = new ScenicSplinePath(); Check(native.Prepare(accepted.Plan, 1), "Native path preparation failed");
        Check(ClearNative(native, world), "Rounded native camera/near-plane envelope intersected an analytic solid");
        // .85m encloses a61°/16:9/.5m near rectangle and camera body at all
        // orientations. It is a deliberately conservative analytic surrogate,
        // not the plugin's Unity BoxCast or a claim about the actual game world.
        double cornerRadius = Math.Sqrt(Math.Pow(.5 * Math.Tan(61 * Math.PI / 360) * 16 / 9, 2) +
            Math.Pow(.5 * Math.Tan(61 * Math.PI / 360), 2) + .5 * .5);
        Check(cornerRadius < PhysicalEnvelope, "Analytic camera envelope no longer contains the tested near plane");
        Check(native.Sample(0).CameraPosition.Distance(request.Start.Position) < .0001 &&
            native.Sample(0).AimPosition.Distance(request.Start.Aim) < .0001 &&
            accepted.Plan.CameraWaypoint(1).Distance(request.Entry.Position) < .0001 &&
            accepted.Plan.AimWaypoint(1).Distance(request.Entry.Aim) < .0001, "Native preparation changed the exact first two knots");
        DenseGaze(native, world.Speed);

        int last = accepted.Plan.WaypointCount - 1;
        ScenicShotSample end = native.Sample(1);
        AutonomyPosition delta = Difference(accepted.Plan.CameraWaypoint(last), accepted.Plan.CameraWaypoint(last - 1));
        double length = delta.Distance(new AutonomyPosition(0, 0, 0));
        AutonomyPosition entryBody = Add(end.CameraPosition, delta, 2 / length);
        AutonomyPosition entryAim = Add(end.AimPosition,
            Difference(accepted.Plan.AimWaypoint(last), accepted.Plan.AimWaypoint(last - 1)), 2 / length);
        world.Goal = P(60);
        var continuation = NativeReady(world, Request(new JourneyNavigationPose(end.CameraPosition, end.AimPosition),
            new JourneyNavigationPose(entryBody, entryAim), world.Goal, 24), 23, 22);
        using var next = new ScenicSplinePath(); Check(next.Prepare(continuation.Plan, 1), "Continuation preparation failed");
        ScenicShotSample first = next.Sample(0);
        Check(first.CameraPosition.Distance(end.CameraPosition) < .0001 && first.AimPosition.Distance(end.AimPosition) < .0001 &&
            first.ScreenX == end.ScreenX && first.ScreenY == end.ScreenY && continuation.Plan.FieldOfView == accepted.Plan.FieldOfView,
            "Graph regeneration lost the exact native seam view");
        DenseGaze(next, world.Speed);
    }

    private static void UnsafeSmoothing()
    {
        var world = new World(); var accepted = NativeReady(world, Request());
        using var native = new ScenicSplinePath(); Check(native.Prepare(accepted.Plan, 1), "Native path preparation failed");
        AutonomyPosition? obstacle = null; const double tinyRadius = .002;
        for (int i = 1; i < DenseSamples && !obstacle.HasValue; i++)
        {
            AutonomyPosition point = native.Sample(i / (float)DenseSamples).CameraPosition;
            AutonomyPosition nearest = ClosestPolyline(accepted.Route, point, out double separation);
            if (separation <= .06) continue;
            AutonomyPosition center = Add(point, Difference(point, nearest), (PhysicalEnvelope - .01) / separation);
            ClosestPolyline(accepted.Route, center, out double edgeDistance);
            if (edgeDistance > PhysicalEnvelope + tinyRadius + .005) obstacle = center;
        }
        Check(obstacle.HasValue, "Actual native rounding never departed enough from the graph edges for the negative fixture");
        // Place one analytic pin outside every certified straight-edge volume
        // but inside the rounded native volume. This explicitly demonstrates
        // why graph acceptance alone cannot authorize a spline.
        ClosestPolyline(accepted.Route, obstacle!.Value, out double distance);
        Check(distance > PhysicalEnvelope + tinyRadius, "Negative obstacle already intersects a graph edge");
        bool nativeCollision = false;
        for (int i = 0; i <= DenseSamples; i++)
            nativeCollision |= native.Sample(i / (float)DenseSamples).CameraPosition.Distance(obstacle.Value) < PhysicalEnvelope + tinyRadius;
        Check(nativeCollision, "Dense native preflight failed to detect the newly constructed rounded-curve collision");
        int nodes = accepted.Search.Nodes, edges = accepted.Search.Edges;
        accepted.Search.RejectCandidate();
        Check(accepted.Search.Status == JourneyNavigationStatus.Searching && accepted.Search.Result == null &&
            accepted.Search.Nodes == nodes && accepted.Search.Edges == edges && accepted.Search.RejectedCandidates > 0,
            "Native rejection erased the measured graph or left its unsafe proposal publishable");
    }

    private static void InvalidEntry()
    {
        var world = new World(); var request = Request(entry: new JourneyNavigationPose(P(2), P(-26)));
        var search = Search(world, request);
        Check(search.Status == JourneyNavigationStatus.Exhausted && search.Result == null && search.Probes <= 2,
            "A backward fixed entry passed the same production gaze predicate used for observed nodes");
    }

    private static void RampedPeakRate()
    {
        var body = new AutonomyPosition[11]; var aims = new AutonomyPosition[body.Length];
        for (int i = 0; i < body.Length; i++)
        {
            body[i] = P(i * 4); double angle = i / 10d * 30.4 * Math.PI / 180;
            aims[i] = P(body[i].X + 28 * Math.Cos(angle), 28 * Math.Sin(angle));
        }
        var feature = new ScenicFeature("ramped-gaze", ScenicFeatureKind.Landscape, aims[0], P(1, 0, 0), 12, 5, 1);
        var flat = ScenicShotPlan.Spatial(50, feature, ScenicShotRole.Traverse, body, aims, 61)
            .AsJourney(7, 49, 10).WithJourneyRamps(false, false);
        using var steady = new ScenicSplinePath(); Check(steady.Prepare(flat, 1), "Steady native view could not prepare");
        Check(NativeJourneyGaze.WithinRate(steady, 4), "Constructed7.6degree/second view is not below the8degree limit");
        using var ramped = new ScenicSplinePath(); Check(ramped.Prepare(flat.WithJourneyRamps(true, true), 1), "Ramped native view could not prepare");
        Check(!NativeJourneyGaze.WithinRate(ramped, 4), "Uniform-time sampling failed to notice the faster middle of the ramped view");
        double retimed = 4 / ScenicJourneyMotion.CruiseFraction(true, true);
        Check(NativeJourneyGaze.WithinRate(ramped, retimed),
            "Ramp compensation was applied twice or verifier ignored the separately supplied final duration");
        double max = 0; ScenicShotSample prior = ramped.Sample(0);
        for (int i = 1; i <= DenseSamples; i++)
        {
            ScenicShotSample next = ramped.Sample(i / (float)DenseSamples);
            max = Math.Max(max, Angle(Difference(prior.AimPosition, prior.CameraPosition),
                Difference(next.AimPosition, next.CameraPosition)) * DenseSamples / retimed);
            prior = next;
        }
        Check(max > 7.4 && max < 7.9, "Dense actual temporal samples disagree with bounded retimed cruise: " + max);
    }

    private static void DenseGaze(ScenicSplinePath native, double speed)
    {
        double duration = native.Length / speed; ScenicShotSample prior = native.Sample(0);
        Check(NativeJourneyGaze.WithinRate(native, duration), "Production native gaze verifier rejected the selected cruise");
        for (int i = 1; i <= DenseSamples; i++)
        {
            ScenicShotSample next = native.Sample(i / (float)DenseSamples);
            double rate = Angle(Difference(prior.AimPosition, prior.CameraPosition), Difference(next.AimPosition, next.CameraPosition)) * DenseSamples / duration;
            Check(rate <= JourneyGazePolicy.NativeDegreesPerSecondLimit + .08, "Dense native gaze exceeds the permitted rate: " + rate);
            prior = next;
        }
    }
    private static double Angle(AutonomyPosition a, AutonomyPosition b)
    {
        double lengths = Math.Sqrt((a.X * (double)a.X + a.Y * (double)a.Y + a.Z * (double)a.Z) *
            (b.X * (double)b.X + b.Y * (double)b.Y + b.Z * (double)b.Z));
        return Math.Acos(Math.Max(-1, Math.Min(1, (a.X * (double)b.X + a.Y * (double)b.Y + a.Z * (double)b.Z) / lengths))) * 180 / Math.PI;
    }
    private static AutonomyPosition ClosestPolyline(JourneyNavigationPath route, AutonomyPosition point, out double distance)
    {
        AutonomyPosition nearest = route.Poses[0].Position; distance = point.Distance(nearest);
        for (int i = 1; i < route.Poses.Count; i++)
        {
            AutonomyPosition a = route.Poses[i - 1].Position, b = route.Poses[i].Position;
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double t = Math.Max(0, Math.Min(1, ((point.X - a.X) * dx + (point.Y - a.Y) * dy + (point.Z - a.Z) * dz) /
                (dx * dx + dy * dy + dz * dz)));
            AutonomyPosition p = P(a.X + dx * t, a.Z + dz * t, a.Y + dy * t); double d = point.Distance(p);
            if (d < distance) { distance = d; nearest = p; }
        }
        return nearest;
    }
}
