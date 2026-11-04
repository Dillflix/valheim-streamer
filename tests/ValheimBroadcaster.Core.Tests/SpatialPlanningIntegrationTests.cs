using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

/// <summary>Pure handoff fixtures. Clear observations stand in for measured
/// adapter output; these tests exercise graph admission, authored route and
/// editorial use together, without claiming to validate Unity rendering.</summary>
internal static class SpatialPlanningIntegrationTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Measured scenic graph becomes a developed composed multiwaypoint shot", DevelopedShot);
        yield return ("Spatial trajectory retains measured winding instead of cutting the endpoint chord", PreserveWinding);
        yield return ("Unknown intermediate scenery cannot become a renderable route proposal", UnknownHandoff);
        yield return ("Fresh scenic endpoints cannot bridge an expired or missing corridor", CorridorHandoff);
        yield return ("A spatial scenic route expires while waiting for trajectory admission", AdmissionFreshness);
        yield return ("A new world cannot reuse the previous scenic route or measured edges", WorldHandoff);
        yield return ("Editing survey buffers cannot reshape a prepared scenic trajectory", ImmutableHandoff);
        yield return ("Shown settlement coverage develops into its measured child before unrelated coverage", ParentSequence);
        yield return ("An unshown or merely staged settlement earns no child sequence boost", UnshownParent);
        yield return ("Aggregate places never progress into an invented close detail scope", AggregateSequence);
        yield return ("Partial place scope remains explicit through spatial trajectory and editorial ranking", PartialScope);
        yield return ("Spatial search visits every subject before repeating a subject at another scale", SubjectInterleave);
        yield return ("Spatial scheduling spends one applicable role pass without redundant azimuth passes", SingleRolePass);
        yield return ("Settlement and district schedules exclude detail even when it was preferred", AggregateRoles);
        yield return ("Spatial subject scheduling handles empty single and bounded large surveys", BoundedSchedule);
    }

    private static AutonomyPosition P(float x, float y = 26, float z = -20) => new AutonomyPosition(x, y, z);
    private static readonly AutonomyPosition Aim = P(0, 20, 0);
    private static readonly AutonomyPosition[] Winding =
        { P(-12), P(-6), P(0, 26, -17), P(6, 27, -17), P(12, 27, -13) };
    private static ScenicFeature Feature(string id = "coast", string? parent = null, string? scope = null, bool partial = false) =>
        new ScenicFeature(id, scope == null ? ScenicFeatureKind.Coastline : ScenicFeatureKind.Structure,
            Aim, P(1, 0, 0), 12, 8, .8f, parent, scope, partial);

    private static CameraSpatialGraph Graph(long epoch = 1, double at = 10,
        Func<int, SpatialEvidence>? nodeEvidence = null, Func<int, SpatialEvidence>? edgeEvidence = null,
        Func<int, double>? edgeTime = null, int missingEdge = -1, long? edgeEpoch = null, bool rotate = false)
    {
        var nodes = Winding.Select((point, i) => new CameraSpatialNode(i,
            rotate ? P(-point.Z, point.Y, point.X) : point,
            Aim, .85f, 1.2f, .9f, at, epoch, nodeEvidence?.Invoke(i) ?? SpatialEvidence.Clear)).ToArray();
        var edges = Enumerable.Range(0, nodes.Length - 1).Where(i => i != missingEdge).Select(i =>
            new CameraSpatialEdge(i, i + 1, .83f, .9f, edgeTime?.Invoke(i) ?? at,
                edgeEpoch ?? epoch, edgeEvidence?.Invoke(i) ?? SpatialEvidence.Clear)).ToArray();
        return new CameraSpatialGraph(epoch, nodes, edges);
    }
    private static CameraSpatialRoute? Search(CameraSpatialGraph graph, double now = 10, long epoch = 1)
    {
        var planner = new CameraSpatialPlanner();
        planner.Begin(graph, new CameraSpatialRequest(now, epoch, ScenicShotRole.Traverse, 24, 26, 30, startId: 0));
        int frames = 0;
        while (!planner.Step(3)) Check(++frames < 3000, "Finite fixture did not terminate within bounded work.");
        return planner.Result;
    }
    private static ScenicShotPlan Shot(CameraSpatialRoute route, ScenicFeature? feature = null,
        ScenicShotRole role = ScenicShotRole.Traverse, long id = 1, bool native = true) =>
        ScenicShotPlan.Spatial(id, feature ?? Feature(), role, route.Nodes.Select(n => n.Position).ToArray(),
            route.Nodes.Select(n => n.Aim).ToArray(), 58, .08f, -.04f, native);
    private static ScenicShotPlan ShotFor(ScenicFeature feature, long id = 1,
        ScenicShotRole role = ScenicShotRole.Establish, bool rotate = false) =>
        Shot(Search(Graph(rotate: rotate)) ?? throw new Exception("Clear fixture did not plan."), feature, role, id);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(double a, double b, string reason) => Check(Math.Abs(a - b) < .0001, reason);
    private static void Invalid(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid spatial schedule was accepted."); }

    private static void DevelopedShot()
    {
        CameraSpatialRoute route = Search(Graph()) ?? throw new Exception("No connected route.");
        ScenicShotPlan shot = Shot(route);
        Check(shot.IsDirected && shot.IsSpatial && shot.UseNativeSpline && shot.WaypointCount == 5,
            "The graph was reduced to a legacy endpoint/template shot.");
        Near(shot.TravelMetres, route.Distance, "Measured corridor length changed at the trajectory handoff.");
        Check(shot.DurationSeconds >= 12 && shot.DurationSeconds <= 30 && shot.VisualChange >= .2,
            "A developed readable route became a one-second or perceptually static shot.");
        Check(shot.Sample(2 / shot.DurationSeconds).CameraPosition.Distance(shot.Sample(0).CameraPosition) > .5,
            "A useful route gained a prolonged static opening.");
        for (int i = 0; i <= 100; i++)
        {
            ScenicShotSample view = shot.Sample(i / 100d);
            Check(view.CameraPosition.Distance(view.AimPosition) > 2 && view.CameraPosition.HorizontalDistance(view.AimPosition) > .75,
                "Paired measured viewpoints lost a usable environmental look direction.");
            Near(view.ScreenX, .08f, "Authored off-center composition was discarded.");
            Near(view.ScreenY, -.04f, "Authored vertical composition was discarded.");
        }
    }
    private static void PreserveWinding()
    {
        CameraSpatialRoute route = Search(Graph()) ?? throw new Exception("Winding corridor did not plan.");
        ScenicShotPlan shot = Shot(route, native: false);
        Check(shot.TravelMetres > shot.Sample(0).CameraPosition.Distance(shot.Sample(1).CameraPosition) + .5,
            "Fixture must distinguish its measured route from its endpoint chord.");
        for (int i = 0; i < route.Nodes.Count; i++)
        {
            Check(shot.CameraWaypoint(i).Distance(route.Nodes[i].Position) < .001,
                "The prepared proposal dropped or reordered a measured bend.");
            Check(shot.RouteAimAt(shot.WaypointFraction(i)).Distance(route.Nodes[i].Aim) < .001,
                "A viewpoint no longer meets its paired authored aim.");
        }
        for (int sample = 0; sample <= 300; sample++)
        {
            AutonomyPosition camera = shot.Sample(sample / 300d).CameraPosition;
            double nearest = double.PositiveInfinity;
            for (int i = 1; i < route.Nodes.Count; i++) nearest = Math.Min(nearest,
                SegmentDistance(camera, route.Nodes[i - 1].Position, route.Nodes[i].Position));
            Check(nearest < .001, "Non-native playback crossed space that was never a measured corridor.");
        }
    }
    private static double SegmentDistance(AutonomyPosition p, AutonomyPosition a, AutonomyPosition b)
    {
        double x = b.X - a.X, y = b.Y - a.Y, z = b.Z - a.Z;
        double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * x + (p.Y - a.Y) * y + (p.Z - a.Z) * z) / (x * x + y * y + z * z)));
        return p.Distance(P((float)(a.X + t * x), (float)(a.Y + t * y), (float)(a.Z + t * z)));
    }
    private static void UnknownHandoff()
    {
        foreach (SpatialEvidence evidence in new[] { SpatialEvidence.Unknown, SpatialEvidence.Blocked })
        {
            Check(Search(Graph(nodeEvidence: i => i == 2 ? evidence : SpatialEvidence.Clear)) == null,
                "An unobserved/blocked intermediate viewpoint was promoted into a complete scenic path.");
            Check(Search(Graph(edgeEvidence: i => i == 1 ? evidence : SpatialEvidence.Clear)) == null,
                "Clear endpoints manufactured a traversable intervening camera corridor.");
        }
    }
    private static void CorridorHandoff()
    {
        Check(Search(Graph(edgeTime: i => i == 1 ? 4.9 : 10)) == null,
            "Fresh endpoints laundered an expired intervening corridor into a scenic proposal.");
        Check(Search(Graph(missingEdge: 1)) == null,
            "Disconnected view clusters were joined through unobserved space.");
        Check(Search(Graph(edgeTime: i => 5)) != null,
            "A corridor at the defined age boundary was incorrectly unavailable.");
    }
    private static void AdmissionFreshness()
    {
        CameraSpatialRoute route = Search(Graph(edgeTime: i => i == 2 ? 7 : 10)) ?? throw new Exception("Fresh route missing.");
        Check(route.IsFresh(12, 1, 5), "Fresh proposal lost its admission window.");
        Check(!route.IsFresh(12.01, 1, 5), "A pending native trajectory could outlive its oldest corridor observation.");
        Check(!route.IsFresh(9.9, 1, 5), "A reversed clock renewed an old planning request.");
        Near(route.OldestObservation, 7, "The handoff forgot the corridor's actual observation age.");
    }
    private static void WorldHandoff()
    {
        CameraSpatialRoute old = Search(Graph()) ?? throw new Exception("Initial route missing.");
        Check(!old.IsFresh(10, 2, 5), "Prior-world scenery retained admission proof.");
        Check(Search(Graph(epoch: 2, at: 12, edgeEpoch: 1), 12, 2) == null,
            "New-world viewpoint IDs connected using old-world corridors.");
        CameraSpatialRoute fresh = Search(Graph(epoch: 2, at: 12), 12, 2) ?? throw new Exception("Fresh world could not plan.");
        Check(fresh.Epoch == 2 && fresh.IsFresh(12, 2, 5) && fresh.Nodes.All(n => n.Epoch == 2),
            "World replacement mixed previous spatial evidence into its trajectory.");
        Check(Shot(fresh, id: 2).Id != Shot(old).Id, "New fixture reused prior presentation identity.");
    }
    private static void ImmutableHandoff()
    {
        CameraSpatialRoute route = Search(Graph()) ?? throw new Exception("Fixture route absent.");
        AutonomyPosition[] body = route.Nodes.Select(n => n.Position).ToArray(), aims = route.Nodes.Select(n => n.Aim).ToArray();
        ScenicShotPlan shot = ScenicShotPlan.Spatial(1, Feature(), ScenicShotRole.Traverse, body, aims, 58);
        ScenicShotSample before = shot.Sample(.4); body[1] = P(999, -99, 999); aims[2] = body[1];
        ScenicShotSample after = shot.Sample(.4);
        Check(before.CameraPosition.Distance(after.CameraPosition) == 0 && before.AimPosition.Distance(after.AimPosition) == 0,
            "The next scene survey mutated an already-prepared on-air trajectory.");
    }
    private static double Show(ScenicDirector director, ScenicShotPlan shot, double at)
    {
        director.Accept(shot, at);
        for (int i = 0; i <= 22; i++) director.Observe(shot.Id, at + i / 10d, i / 220d, true, true, false);
        return at + 2.3;
    }
    private static void ParentSequence()
    {
        var director = new ScenicDirector();
        ScenicFeature parent = Feature("settlement", scope: "Settlement");
        Show(director, ShotFor(parent), 0);
        ScenicShotPlan child = ShotFor(Feature("building", parent.Id, "Building"), 2);
        ScenicShotPlan unrelated = ShotFor(Feature("unrelated", scope: "Building"), 3);
        Check(director.Rank(child, .8) > director.Rank(unrelated, .8) + .2,
            "An established settlement did not develop into an equally readable measured child.");
    }
    private static void UnshownParent()
    {
        var director = new ScenicDirector();
        ScenicFeature parent = Feature("settlement", scope: "Settlement");
        ScenicShotPlan child = ShotFor(Feature("building", parent.Id, "Building"), 2);
        ScenicShotPlan unrelated = ShotFor(Feature("unrelated", scope: "Building"), 3);
        director.Stage(ShotFor(parent));
        Near(director.Rank(child, .8), director.Rank(unrelated, .8), "Staging a parent consumed its establishing beat.");
        director.Accept(director.TakePrepared()!, 0);
        director.Observe(1, 0, 0, false, false, false);
        director.Observe(1, .1, 0, false, false, false);
        Near(director.Rank(child, .8), director.Rank(unrelated, .8), "An accepted but unshown parent earned a child continuity bonus.");
    }
    private static void AggregateSequence()
    {
        foreach (string scope in new[] { "Settlement", "District" })
        {
            var director = new ScenicDirector(); ScenicFeature feature = Feature(scope, scope: scope);
            double at = Show(director, ShotFor(feature), 0);
            Show(director, ShotFor(feature, 2, ScenicShotRole.Traverse, true), at);
            Check(director.PreferredRole(feature) != ScenicShotRole.Detail,
                "A sprawling aggregate progressed into building-detail framing without narrowing its subject.");
        }
    }
    private static void PartialScope()
    {
        ScenicFeature partial = Feature("partial", scope: "Settlement", partial: true);
        ScenicFeature complete = Feature("complete", scope: "Settlement");
        ScenicShotPlan local = ShotFor(partial), whole = ShotFor(complete, 2);
        Check(local.Feature!.IsPartial && local.Feature.ScopeLabel == "Settlement", "Spatial trajectory dropped the observed scope limitation.");
        var director = new ScenicDirector();
        Check(director.Rank(local, .8) < director.Rank(whole, .8), "Partial settlement coverage ranked as complete-base coverage.");
    }
    private static ScenicFeature[] Subjects() => new[]
        { Feature("settlement", scope: "Settlement"), Feature("building", "settlement", "Building"), Feature("coast") };
    private static void SubjectInterleave()
    {
        ScenicFeature[] subjects = Subjects();
        for (int role = 0; role < 3; role++)
        {
            var round = Enumerable.Range(role * 3, 3).Select(i => ScenicCandidateSchedule.SelectSpatial(subjects, 1, 0, i)).ToArray();
            Check(round[0].FeatureIndex == 1 && round.Select(c => c.FeatureIndex).Distinct().Count() == 3 && round.All(c => c.RoleIndex == role),
                "A failed first subject monopolized the bounded spatial survey before other subjects received a turn.");
        }
        var final = ScenicCandidateSchedule.SelectSpatial(subjects, 1, 0, 9);
        Check(final.FeatureIndex == 1 && final.RoleIndex == 3, "The final detail search was assigned to an ineligible scope.");
    }
    private static void SingleRolePass()
    {
        ScenicFeature[] subjects = Subjects(); int count = ScenicCandidateSchedule.SpatialAttemptLimit(subjects, -1, 2);
        Check(count == 10, "Spatial search repeated azimuth passes or lost a legitimate role.");
        var candidates = Enumerable.Range(0, count).Select(i => ScenicCandidateSchedule.SelectSpatial(subjects, -1, 2, i)).ToArray();
        Check(candidates.Select(c => c.FeatureIndex + "/" + c.RoleIndex).Distinct().Count() == count,
            "A bounded spatial search repeated an already-surveyed subject and role.");
        Invalid(() => ScenicCandidateSchedule.SelectSpatial(subjects, -1, 2, count));
    }
    private static void AggregateRoles()
    {
        foreach (string scope in new[] { "Settlement", "District" })
        foreach (ScenicShotRole preferred in Enum.GetValues(typeof(ScenicShotRole)))
        {
            ScenicFeature aggregate = Feature(scope, scope: scope);
            var roles = Enumerable.Range(0, ScenicCandidateSchedule.RoleCount(aggregate)).Select(i => ScenicCandidateSchedule.Role(aggregate, preferred, i)).ToArray();
            Check(roles.Length == 3 && roles.Distinct().Count() == 3 && !roles.Contains(ScenicShotRole.Detail),
                "Aggregate search allowed a misleading detail scope or duplicated another role.");
            Invalid(() => ScenicCandidateSchedule.Role(aggregate, preferred, 3));
        }
        ScenicFeature building = Feature("building", scope: "Building");
        Check(ScenicCandidateSchedule.RoleCount(building) == 4 &&
            ScenicCandidateSchedule.Role(building, ScenicShotRole.Detail, 0) == ScenicShotRole.Detail,
            "Protecting aggregates accidentally removed legitimate building detail.");
    }
    private static void BoundedSchedule()
    {
        Check(ScenicCandidateSchedule.SpatialAttemptLimit(Array.Empty<ScenicFeature>(), -1, 0) == 0, "Empty survey fabricated work.");
        Invalid(() => ScenicCandidateSchedule.SelectSpatial(Array.Empty<ScenicFeature>(), -1, 0, 0));
        var one = new[] { Feature("one") };
        for (int i = 0; i < 3; i++) Check(ScenicCandidateSchedule.SelectSpatial(one, 0, 7, i).FeatureIndex == 0,
            "Single current subject could not be scheduled.");
        var many = Enumerable.Range(0, 32).Select(i => Feature("f" + i, scope: "Building")).ToArray();
        var visited = new HashSet<int>();
        for (int round = 0; round < 2; round++)
        {
            int count = ScenicCandidateSchedule.SpatialAttemptLimit(many, 5, round);
            Check(count <= ScenicCandidateSchedule.MaximumAttempts, "Large spatial survey exceeded its bounded attempt budget.");
            var candidates = Enumerable.Range(0, count).Select(i => ScenicCandidateSchedule.SelectSpatial(many, 5, round, i)).ToArray();
            Check(candidates.Select(c => c.FeatureIndex + "/" + c.RoleIndex).Distinct().Count() == count,
                "Large survey repeated one subject/role inside a bounded pass.");
            foreach (var candidate in candidates) visited.Add(candidate.FeatureIndex);
        }
        Check(visited.Count == many.Length, "Repeated bounded surveys permanently starved later subjects.");
    }
}
