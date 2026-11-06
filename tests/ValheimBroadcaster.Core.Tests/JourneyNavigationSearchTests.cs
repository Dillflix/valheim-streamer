using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class JourneyNavigationSearchTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("A guide for native preflight preserves historic node ages and renews its exact onward edge", NativeGuideRenewal);
        yield return ("Connected Journey search develops production-scale clear routes without exhausting node breadth", ClearLongRoutes);
        yield return ("Connected Journey search goes around a measured obstacle with an exact entry seam", SingleObstacle);
        yield return ("Connected Journey search composes opposite turns through offset trees instead of a fixed route fan", AlternatingDogleg);
        yield return ("Connected Journey search resolves a narrow swept-body gap", NarrowGap);
        yield return ("Connected Journey search finds an offset wall opening", WallGap);
        yield return ("Connected Journey search allows a local retreat before making destination progress", WrongWayDetour);
        yield return ("Connected Journey search can climb over a physical obstacle and descend", VerticalRoute);
        yield return ("Connected Journey search retains exact unknown frontier probes and rejects stale scene evidence", UnknownAndInvalidation);
        yield return ("Connected Journey search distinguishes enclosed observed space from bounded incomplete search", EnclosedAndBounded);
        yield return ("Connected Journey search is deterministic across interrupted work budgets and resumes after native rejection", SchedulingAndRejection);
        yield return ("Connected Journey search requires a clear onward witness beyond a local prefix", OnwardWitness);
        yield return ("Connected Journey search revalidates a retained route and its onward witness across preparation batches", RetainedCandidateFreshness);
        yield return ("Connected Journey search bounds the observed neighbourhood without treating synthetic aim as loaded geometry", PhysicalViewHorizon);
        yield return ("Connected Journey search requires the next exact native seam rather than a clear sideways exit", ExactOnwardSeam);
        yield return ("Connected Journey search completes slow partial observations within the adapter capacity and lifetime", SlowObservationCapacity);
        yield return ("Connected Journey search waits at full unknown observation capacity without dropping or relabelling work", FullUnknownCapacity);
        yield return ("Connected Journey search continues a narrow tortuous route at the native waypoint bound without lowering its nominal target", WaypointBoundContinuation);
    }
    private static AutonomyPosition P(double x, double z = 0, double y = 0) => new AutonomyPosition((float)x, (float)y, (float)z);
    private static JourneyNavigationPose Pose(double x) => new JourneyNavigationPose(P(x), P(x + 12));
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static JourneyNavigationRequest Request(double goal = 40, double target = 48, int nodes = 512, int edges = 8192) =>
        new JourneyNavigationRequest(7, 3, Pose(0), Pose(2), P(goal), P(0), target, 80, 85, 4, nodes, edges);

    // An analytic camera-volume world, not mocked successful route output and
    // not Valheim/Unity physics. Every proposed point and swept edge is tested
    // against inflated solids. The real production search chooses all paths.
    private readonly struct Box
    {
        internal readonly double X0, X1, Z0, Z1, Y0, Y1;
        internal Box(double x0, double x1, double z0, double z1, double y0 = -100, double y1 = 100)
        { X0 = x0; X1 = x1; Z0 = z0; Z1 = z1; Y0 = y0; Y1 = y1; }
        internal bool Hits(AutonomyPosition a, AutonomyPosition b)
        {
            double low = 0, high = 1;
            return Axis(a.X, b.X - a.X, X0 - .45, X1 + .45, ref low, ref high) &&
                Axis(a.Y, b.Y - a.Y, Y0 - .45, Y1 + .45, ref low, ref high) &&
                Axis(a.Z, b.Z - a.Z, Z0 - .45, Z1 + .45, ref low, ref high);
        }
        private static bool Axis(double a, double d, double lo, double hi, ref double low, ref double high)
        {
            if (Math.Abs(d) < .000001) return a >= lo && a <= hi;
            double p = (lo - a) / d, q = (hi - a) / d;
            low = Math.Max(low, Math.Min(p, q)); high = Math.Min(high, Math.Max(p, q)); return low <= high;
        }
    }
    private sealed class Scene
    {
        internal readonly List<Box> Solids = new List<Box>();
        internal readonly Dictionary<long, int> CallsById = new Dictionary<long, int>();
        internal bool Vertical, Unknown, Stale;
        internal long Epoch = 3;
        internal double Now;
        internal int Calls;
        internal double MaxZ = 24, MinimumX = -24, MaximumX = 80;
        internal Func<JourneyNavigationProbe, bool>? ExtraBlock;
        internal Func<AutonomyPosition, AutonomyPosition, bool>? Corridor;
        internal AutonomyPosition? FixedAimOffset;
        internal bool Clear(AutonomyPosition from, AutonomyPosition to)
        {
            if (to.X < MinimumX || to.X > MaximumX || Math.Abs(to.Z) > MaxZ ||
                (!Vertical && Math.Abs(to.Y) > .001) || Vertical && (to.Y < -.001 || to.Y > 16)) return false;
            foreach (Box solid in Solids) if (solid.Hits(from, to)) return false;
            return Corridor == null || Corridor(from, to);
        }
        internal JourneyNavigationObservation Observe(JourneyNavigationProbe probe)
        {
            Calls++; CallsById.TryGetValue(probe.Id, out int count); CallsById[probe.Id] = count + 1;
            if (Unknown && !probe.IsStart && !probe.IsEntry)
                return new JourneyNavigationObservation(JourneyNavigationEvidence.Unknown, default, 0, Epoch, Now);
            bool clear = Clear(probe.From.Position, probe.Position) && !(ExtraBlock?.Invoke(probe) ?? false);
            double dx = probe.Position.X - probe.From.Position.X, dz = probe.Position.Z - probe.From.Position.Z;
            double n = Math.Sqrt(dx * dx + dz * dz);
            var aim = probe.RequiredAim ?? (FixedAimOffset.HasValue ?
                new AutonomyPosition(probe.Position.X + FixedAimOffset.Value.X, probe.Position.Y + FixedAimOffset.Value.Y, probe.Position.Z + FixedAimOffset.Value.Z) :
                new AutonomyPosition(probe.Position.X + (float)(dx / Math.Max(.001, n) * 12),
                    probe.Position.Y, probe.Position.Z + (float)(dz / Math.Max(.001, n) * 12)));
            return new JourneyNavigationObservation(clear ? JourneyNavigationEvidence.Clear : JourneyNavigationEvidence.Blocked,
                new JourneyNavigationPose(probe.Position, aim), clear ? .9 : 0, Epoch, Stale ? 0 : Now);
        }
    }
    private static JourneyNavigationSearch Run(Scene scene, JourneyNavigationRequest request, int budget = 11, int limit = 20000)
    {
        var search = new JourneyNavigationSearch(); search.Begin(request, scene.Now);
        for (int i = 0; i < limit; i++)
        {
            int before = scene.Calls;
            search.Step(budget, scene.Now, request.Epoch, scene.Observe);
            Check(scene.Calls - before <= budget, "A search tick bypassed its work budget");
            if (search.Status == JourneyNavigationStatus.Ready || search.Status == JourneyNavigationStatus.Exhausted ||
                search.Status == JourneyNavigationStatus.BudgetExhausted || search.Status == JourneyNavigationStatus.Invalidated) return search;
            scene.Now += .01;
        }
        throw new Exception("Connected search did not reach a bounded outcome: " + search.Diagnostics);
    }
    private static JourneyNavigationPath Ready(JourneyNavigationSearch search, Scene scene, JourneyNavigationRequest request)
    {
        Check(search.Status == JourneyNavigationStatus.Ready && search.Result != null,
            $"No connected route: {search.Status}; {search.Diagnostics}; nodes {search.Nodes}; edges {search.Edges}; probes {search.Probes}");
        JourneyNavigationPath path = search.Result!;
        Check(path.Poses.Count >= 3 && path.Poses.Count <= 16 && path.Length <= 80 && path.Length >= 8,
            "Route bypassed the native path size/distance contract");
        Check(path.Poses[0].Position.Distance(request.Start.Position) < .00001 && path.Poses[0].Aim.Distance(request.Start.Aim) < .00001 &&
            path.Poses[1].Position.Distance(request.Entry.Position) < .00001 && path.Poses[1].Aim.Distance(request.Entry.Aim) < .00001,
            "Search changed the exact incoming body or aim tangent");
        for (int i = 1; i < path.Poses.Count; i++)
            Check(scene.Clear(path.Poses[i - 1].Position, path.Poses[i].Position), "Returned path crossed an analytic physical solid");
        Check(path.AtGoal || path.OnwardPose.HasValue && scene.Clear(path.Poses[path.Poses.Count - 1].Position, path.OnwardPose.Value.Position),
            "Local prefix had no physically observed onward edge");
        if (path.OnwardPose.HasValue)
        {
            var last = path.Poses[path.Poses.Count - 1]; var prior = path.Poses[path.Poses.Count - 2];
            double scale = 2 / prior.Position.Distance(last.Position);
            var expectedBody = Extend(prior.Position, last.Position, scale);
            var expectedAim = Extend(prior.Aim, last.Aim, scale);
            Check(path.OnwardPose.Value.Position.Distance(expectedBody) < .0001 &&
                path.OnwardPose.Value.Aim.Distance(expectedAim) < .0001,
                "Onward evidence did not certify the exact two-metre native body/aim continuation");
        }
        Check(path.Epoch == request.Epoch && scene.Now - path.OldestEvidence <= JourneyNavigationSearch.MaximumEvidenceAge + .00001,
            "Returned path reused stale physical scene evidence");
        return path;
    }

    private static void NativeGuideRenewal()
    {
        var scene = new Scene();
        var request = new JourneyNavigationRequest(7, 3, Pose(0), Pose(2), P(200), P(0),
            targetMetres: 24, forNativePreflight: true);
        JourneyNavigationSearch search = Run(scene, request);
        var path = Ready(search, scene, request);
        double oldest = path.OldestEvidence;
        scene.Now += 10;
        search.ResumeCandidateValidation();
        int renewals = 0;
        search.Step(4, scene.Now, 3, probe =>
        {
            Check(probe.IsOnward, "Historical guide edges must go through native curve validation");
            renewals++;
            return scene.Observe(probe);
        });
        Check(renewals == 1 && search.Result != null && search.Result.OldestEvidence == oldest
            && search.Result.OnwardObservedAt == scene.Now, "Guide age or refreshed onward identity lost");
        scene.Now += 10;
        search.ResumeCandidateValidation();
        search.Step(1, scene.Now, 3, probe => new JourneyNavigationObservation(
            JourneyNavigationEvidence.Blocked, default, 0, 3, scene.Now));
        Check(search.Result == null, "Guide was returned despite blocked onward renewal");
    }

    private static void ClearLongRoutes()
    {
        foreach (double target in new[] { 55d, 64d })
        {
            var scene = new Scene(); var request = Request(1000, target);
            var search = Run(scene, request, 4); var result = Ready(search, scene, request);
            Check(result.Length >= target && result.GoalProgress >= target - 2 && search.Nodes < 100 && search.Probes < 100,
                $"Clear {target}m path spent its budget on breadth: {search.Nodes} nodes / {search.Probes} probes");
        }
    }
    private static void SingleObstacle()
    {
        var scene = new Scene(); scene.Solids.Add(new Box(8, 14, -3, 3));
        var request = Request(32, 64); var path = Ready(Run(scene, request), scene, request);
        Check(path.AtGoal && path.Poses.Any(p => Math.Abs(p.Position.Z) > 3.45), "Search did not actually go around the obstacle");
        // Start's synthetic aim points through this solid. Camera-space
        // navigation must not demand that empty-space focus ray be visible.
        Check(scene.Solids[0].Hits(request.Start.Position, request.Start.Aim), "Focus-veto regression geometry was not represented");
    }
    private static void AlternatingDogleg()
    {
        var scene = new Scene { MaxZ = 10 };
        scene.Solids.Add(new Box(8, 10, -30, 1)); scene.Solids.Add(new Box(18, 20, -1, 30));
        var request = Request(32, 64, 2048, 32768); var path = Ready(Run(scene, request), scene, request);
        bool left = false, right = false;
        for (int i = 2; i < path.Poses.Count; i++)
        {
            var a = path.Poses[i - 2].Position; var b = path.Poses[i - 1].Position; var c = path.Poses[i].Position;
            double turn = (b.X - a.X) * (c.Z - b.Z) - (b.Z - a.Z) * (c.X - b.X);
            left |= turn > .1; right |= turn < -.1;
        }
        Check(path.AtGoal && left && right && path.Poses.Any(p => p.Position.Z > 1.45) && path.Poses.Any(p => p.Position.Z < -1.45),
            "Fixed-side or single-turn route passed the alternating obstacle regression");
    }
    private static void NarrowGap()
    {
        var scene = new Scene { MaxZ = 12 };
        scene.Solids.Add(new Box(10, 14, -30, 1)); scene.Solids.Add(new Box(10, 14, 4, 30));
        var request = Request(28, 60, 2048, 32768); var path = Ready(Run(scene, request), scene, request);
        Check(path.AtGoal, "Narrow-gap route stopped on its near side");
    }
    private static void WallGap()
    {
        var scene = new Scene { MaxZ = 12 };
        scene.Solids.Add(new Box(14, 16, -30, -7)); scene.Solids.Add(new Box(14, 16, -3, 30));
        var request = Request(34, 64, 2048, 32768); var path = Ready(Run(scene, request), scene, request);
        Check(path.AtGoal && path.Poses.Any(p => p.Position.Z < -3.45), "Search never found the observed offset wall gap");
    }
    private static void WrongWayDetour()
    {
        var scene = new Scene { MaxZ = 16 };
        scene.Solids.Add(new Box(6, 8, -6, 6));
        scene.Solids.Add(new Box(-8, 8, -7, -5)); scene.Solids.Add(new Box(-8, 8, 5, 7));
        var request = Request(24, 72, 4096, 65536); var path = Ready(Run(scene, request), scene, request);
        Check(path.AtGoal && path.Poses.Any(p => p.Position.X < -8.45),
            "Local movement away from the destination was forbidden by a greedy progress gate");
    }
    private static void VerticalRoute()
    {
        var scene = new Scene { Vertical = true, MaxZ = 10 };
        scene.Solids.Add(new Box(14, 16, -30, 30, -5, 3));
        var request = Request(30, 64, 2048, 32768); var path = Ready(Run(scene, request), scene, request);
        Check(path.AtGoal && path.Poses.Any(p => p.Position.Y > 3.45), "Search had no useful vertical navigation option");
    }
    private static void UnknownAndInvalidation()
    {
        var scene = new Scene { Unknown = true }; var request = Request(30, 40); var search = new JourneyNavigationSearch(); search.Begin(request, 0);
        for (int i = 0; i < 80; i++) { scene.Now = i * .05; search.Step(4, scene.Now, 3, scene.Observe); }
        Check(search.Result == null && search.Status == JourneyNavigationStatus.WaitingForObservations && search.PendingCount > 0 &&
            scene.CallsById.Values.Any(n => n > 1), "Unknown map edges were dropped or silently labelled physically blocked");
        scene.Unknown = false;
        for (int i = 0; i < 3000 && search.Result == null; i++)
        { scene.Now += .01; search.Step(8, scene.Now, 3, scene.Observe); }
        Ready(search, scene, request);
        search.Invalidate(); Check(search.Result == null && search.Status == JourneyNavigationStatus.Invalidated, "Invalidated physical proof remained usable");
        var stale = new Scene { Now = 10, Stale = true }; var waiting = new JourneyNavigationSearch(); waiting.Begin(request, 10);
        waiting.Step(8, 10, 3, stale.Observe);
        Check(waiting.Status == JourneyNavigationStatus.WaitingForObservations && waiting.Result == null,
            "Old clear evidence was accepted as a current scene proof");
        waiting.Step(8, 10, 4, stale.Observe);
        Check(waiting.Status == JourneyNavigationStatus.Invalidated, "Scene identity changed without invalidating the search");
    }
    private static void EnclosedAndBounded()
    {
        var enclosed = new Scene { ExtraBlock = p => !p.IsStart && !p.IsEntry };
        var exhausted = Run(enclosed, Request());
        Check(exhausted.Status == JourneyNavigationStatus.Exhausted && exhausted.Result == null,
            "Observed enclosed space was reported as an available route");
        var limited = new Scene(); var bounded = Run(limited, Request(1000, 64, 16, 32));
        Check(bounded.Status == JourneyNavigationStatus.BudgetExhausted && bounded.Nodes <= 16 && bounded.Edges <= 32,
            "Bounded incomplete search overran work or claimed exhaustive world failure");
    }
    private static void SchedulingAndRejection()
    {
        var firstScene = new Scene(); firstScene.Solids.Add(new Box(8, 14, -3, 3));
        var secondScene = new Scene(); secondScene.Solids.Add(new Box(8, 14, -3, 3));
        var request = Request(32, 64);
        var a = Run(firstScene, request, 1); var b = Run(secondScene, request, 31);
        JourneyNavigationPath left = Ready(a, firstScene, request), right = Ready(b, secondScene, request);
        Check(Signature(left) == Signature(right), "Changing per-tick work budget changed deterministic graph decisions");
        int nodes = a.Nodes; string old = Signature(left); a.RejectCandidate();
        for (int i = 0; i < 10000 && a.Result == null && a.Status != JourneyNavigationStatus.Exhausted && a.Status != JourneyNavigationStatus.BudgetExhausted; i++)
        { firstScene.Now += .01; a.Step(8, firstScene.Now, 3, firstScene.Observe); }
        var next = Ready(a, firstScene, request);
        Check(a.Nodes >= nodes && a.RejectedCandidates >= 1 && Signature(next) != old,
            "Native rejection restarted the search or immediately returned the same path");
    }
    private static void OnwardWitness()
    {
        var scene = new Scene { ExtraBlock = p => p.IsOnward };
        var search = Run(scene, Request(1000, 16, 64, 512));
        Check(search.Result == null && search.Status != JourneyNavigationStatus.Ready,
            "An endpoint before blocked/unknown onward space was advertised as continuing travel");
    }
    private static void RetainedCandidateFreshness()
    {
        var scene = new Scene(); var request = Request(1000, 32);
        var search = Run(scene, request, 4); var original = Ready(search, scene, request);
        int nodes = search.Nodes, probes = search.Probes;
        scene.Now += 6; search.ResumeCandidateValidation();
        Check(search.Result == null && search.Status == JourneyNavigationStatus.Searching,
            "A retained ready result remained publishable before its evidence was refreshed");
        for (int i = 0; i < 100 && search.Result == null; i++)
        {
            int before = scene.Calls; search.Step(1, scene.Now, 3, scene.Observe);
            Check(scene.Calls - before <= 1, "Retained route refresh bypassed the observation budget");
            scene.Now += .01;
        }
        var refreshed = Ready(search, scene, request);
        Check(Signature(refreshed) == Signature(original) && search.Nodes == nodes &&
            search.Probes - probes >= original.Poses.Count + 1 && refreshed.OldestEvidence > original.OldestEvidence + 5,
            "Retained proposal lost its frontier or reused stale route/onward evidence");
        scene.Now += 6; scene.ExtraBlock = p => p.IsOnward; search.ResumeCandidateValidation();
        search.Step(16, scene.Now, 3, scene.Observe);
        Check(search.Result == null && search.RejectedCandidates > 0,
            "A newly obstructed onward witness was copied from the earlier preparation batch");
        var readyScene = new Scene(); var ready = Run(readyScene, request);
        ready.Step(1, readyScene.Now, 4, readyScene.Observe);
        Check(ready.Status == JourneyNavigationStatus.Invalidated && ready.Result == null,
            "A previously ready route survived a physical scene epoch change");
    }
    private static void PhysicalViewHorizon()
    {
        var scene = new Scene();
        var request = new JourneyNavigationRequest(7, 3,
            new JourneyNavigationPose(P(0), P(100)), new JourneyNavigationPose(P(2), P(102)),
            P(1000), P(0), 55, 80, 85, 4, 512, 8192, 12);
        var result = Ready(Run(scene, request), scene, request);
        Check(result.Poses[0].Aim.Distance(request.Carrier) > request.ReachMetres &&
            result.Poses.All(p => p.Position.Distance(request.Carrier) + request.ViewDepth <= request.ReachMetres) &&
            result.OnwardPose.HasValue && result.OnwardPose.Value.Position.Distance(request.Carrier) + request.ViewDepth <= request.ReachMetres,
            "Loading reach either followed a synthetic focus ray or omitted the actual view neighbourhood");
    }
    private static void ExactOnwardSeam()
    {
        var scene = new Scene(); scene.Solids.Add(new Box(19.5, 20.5, -.25, .25));
        Check(!scene.Clear(P(18), P(20)) && scene.Clear(P(18), P(19, -Math.Sqrt(3))),
            "Regression geometry must block the straight seam while leaving a turning exit clear");
        var request = Request(1000, 16); var search = Run(scene, request); var path = Ready(search, scene, request);
        Check(path.Poses[path.Poses.Count - 1].Position.Distance(P(18)) > .01,
            "A clear sideways exit admitted an endpoint whose mandatory native continuation is blocked");
        var open = new Scene(); var straight = Ready(Run(open, request), open, request);
        Check(straight.Poses[straight.Poses.Count - 1].Position.Distance(P(18)) < .01 &&
            straight.OnwardPose.HasValue && straight.OnwardPose.Value.Position.Distance(P(20)) < .01,
            "The same endpoint with an actually clear next seam was not accepted");
        var renewed = JourneyNavigationProbe.ExactContinuation(1_000_000_000,
            straight.Poses[straight.Poses.Count - 1], straight.OnwardPose!.Value);
        Check(renewed.IsOnward && !renewed.IsStart && !renewed.IsEntry && renewed.RequiredAim.HasValue &&
            renewed.Position.Distance(straight.OnwardPose.Value.Position) < .0001 &&
            renewed.RequiredAim.Value.Distance(straight.OnwardPose.Value.Aim) < .0001 &&
            renewed.From.Position.Distance(straight.Poses[straight.Poses.Count - 1].Position) < .0001 &&
            straight.OnwardObservedAt >= straight.OldestEvidence,
            "External finalization renewal lost the exact known seam or its original timestamp");
    }
    private static AutonomyPosition Extend(AutonomyPosition prior, AutonomyPosition last, double scale) =>
        new AutonomyPosition((float)(last.X + (last.X - prior.X) * scale),
            (float)(last.Y + (last.Y - prior.Y) * scale), (float)(last.Z + (last.Z - prior.Z) * scale));
    private static JourneyNavigationRequest CapacityRequest() => new JourneyNavigationRequest(7, 3, Pose(0), Pose(2),
        P(1000), P(0), targetMetres: 55, maximumMetres: 80, maximumPending: 8);
    private static void SlowObservationCapacity()
    {
        var scene = new Scene(); var request = CapacityRequest(); var search = new JourneyNavigationSearch(); search.Begin(request, 0);
        var partials = new Dictionary<long, (int Visits, double Started)>();
        int expired = 0, peak = 0, completed = 0;
        JourneyNavigationObservation Observe(JourneyNavigationProbe probe)
        {
            var physical = scene.Observe(probe);
            if (probe.IsStart || probe.IsEntry) return physical;
            if (!partials.TryGetValue(probe.Id, out var work)) work = (0, scene.Now);
            // Mirrors a bounded multi-pass observation lifetime, not Unity's
            // implementation: each new edge needs four visits within two seconds.
            if (scene.Now - work.Started > 2) { expired++; work = (0, scene.Now); }
            work.Visits++; partials[probe.Id] = work; peak = Math.Max(peak, partials.Count);
            if (work.Visits < 4)
                return new JourneyNavigationObservation(JourneyNavigationEvidence.Unknown, default, 0, 3, scene.Now);
            partials.Remove(probe.Id); completed++; return physical;
        }
        int peakPending = 0;
        for (int frame = 0; frame < 6000 && search.Result == null; frame++)
        {
            scene.Now = frame / 30d; int before = scene.Calls;
            search.Step(1, scene.Now, 3, Observe);
            Check(scene.Calls - before <= 1, "Partial-observation retries bypassed the per-tick budget");
            peakPending = Math.Max(peakPending, search.PendingCount);
            Check(search.PendingCount <= 8, "Search exceeded the adapter's partial-observation capacity");
            if (search.Status == JourneyNavigationStatus.Exhausted || search.Status == JourneyNavigationStatus.BudgetExhausted) break;
        }
        Ready(search, scene, request);
        // One selected-route refresh can coexist with retained graph frontiers.
        Check(expired == 0 && peak <= 9 && peakPending == 8 && completed > 8 && search.Nodes > 8,
            $"Slow observations were restarted/starved: expired {expired}, peak partials {peak}, peak pending {peakPending}, completed {completed}");
    }
    private static void FullUnknownCapacity()
    {
        var scene = new Scene { Unknown = true }; var request = CapacityRequest();
        var search = new JourneyNavigationSearch(); search.Begin(request, 0); search.Step(64, 0, 3, scene.Observe);
        Check(search.PendingCount == 8 && search.Status == JourneyNavigationStatus.WaitingForObservations && search.Nodes == 2,
            "Unknown observations did not backpressure graph expansion at the adapter capacity");
        long[] ids = scene.CallsById.Keys.OrderBy(x => x).ToArray(); int calls = scene.Calls;
        search.Step(64, 0, 3, scene.Observe);
        Check(scene.Calls == calls && search.PendingCount == 8, "Undue pending work was spun or replaced instead of waiting");
        for (int i = 1; i <= 12; i++)
        {
            scene.Now = i * .06; search.Step(64, scene.Now, 3, scene.Observe);
            Check(search.PendingCount == 8 && search.Status == JourneyNavigationStatus.WaitingForObservations &&
                ids.SequenceEqual(scene.CallsById.Keys.OrderBy(x => x)),
                "Genuinely unknown world observations were discarded, relabelled blocked, or replaced with new work");
        }
        scene.Unknown = false;
        for (int i = 0; i < 3000 && search.Result == null; i++) { scene.Now += .02; search.Step(4, scene.Now, 3, scene.Observe); }
        Ready(search, scene, request);
    }
    private static void WaypointBoundContinuation()
    {
        // Constructed narrow corridor: the0.8.51 capture does not contain graph
        // parent/depth chains or complete scene geometry. This specifically
        // proves the production eligibility defect, not a captured world route.
        var centreline = new List<AutonomyPosition> { P(0), P(2) };
        for (int i = 0; i < 36; i++)
        {
            double yaw = i % 2 == 0 ? Math.PI / 6 : -Math.PI / 6;
            if (i == 14) yaw = -Math.PI / 6; // next exact2m continuation after the16-knot boundary
            AutonomyPosition prior = centreline[centreline.Count - 1];
            centreline.Add(P(prior.X + 2 * Math.Cos(yaw), prior.Z + 2 * Math.Sin(yaw)));
        }
        bool Corridor(AutonomyPosition from, AutonomyPosition to)
        {
            int samples = Math.Max(1, (int)Math.Ceiling(from.Distance(to) / .05));
            for (int i = 0; i <= samples; i++)
            {
                double t = i / (double)samples;
                var point = P(from.X + (to.X - from.X) * t, from.Z + (to.Z - from.Z) * t);
                double nearest = double.PositiveInfinity;
                for (int k = 1; k < centreline.Count; k++) nearest = Math.Min(nearest, SegmentDistance(point, centreline[k - 1], centreline[k]));
                // The distance-to-polyline function is1-Lipschitz: <=.30m at
                // <=.05m spacing certifies a continuous <=.325m centre envelope.
                if (nearest > .30) return false;
            }
            return true;
        }
        var scene = new Scene { Corridor = Corridor, FixedAimOffset = P(12) };
        var request = new JourneyNavigationRequest(7, 3, Pose(0), Pose(2), P(1000), P(0),
            targetMetres: 56, maximumMetres: 64, maximumNodes: 512, maximumEdges: 8192);
        var result = Ready(Run(scene, request), scene, request);
        Check(result.Poses.Count == JourneyNavigationSearch.MaximumWaypoints && result.Length > 8 && result.Length < 56 &&
            result.GoalProgress >= 8 && result.OnwardPose.HasValue && !result.AtGoal && request.TargetMetres == 56 && request.MaximumMetres == 64,
            "The waypoint-bound continuation was discarded, became an arrival, or lowered the global window target");
        for (int i = 1; i <= 12; i++)
            Check(Math.Abs(result.Poses[i - 1].Position.Distance(result.Poses[i].Position) - 2) < .001,
                "The alternating corridor admitted a shortcut before the deliberately straight continuation seam");
        var blockedEnding = new Scene { Corridor = Corridor, FixedAimOffset = P(12), ExtraBlock = probe => probe.IsOnward };
        var blocked = Run(blockedEnding, request);
        Check(blocked.Result == null, "A short internal section was accepted without its exact fresh onward seam");
    }
    private static double SegmentDistance(AutonomyPosition point, AutonomyPosition from, AutonomyPosition to)
    {
        double dx = to.X - from.X, dz = to.Z - from.Z;
        double t = Math.Max(0, Math.Min(1, ((point.X - from.X) * dx + (point.Z - from.Z) * dz) / (dx * dx + dz * dz)));
        double x = point.X - from.X - t * dx, z = point.Z - from.Z - t * dz;
        return Math.Sqrt(x * x + z * z);
    }
    private static string Signature(JourneyNavigationPath path) => string.Join(";", path.Poses.Select(p =>
        p.Position.X.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," +
        p.Position.Y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "," +
        p.Position.Z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)));
}
