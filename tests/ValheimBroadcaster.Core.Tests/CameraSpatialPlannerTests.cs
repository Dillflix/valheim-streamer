using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class CameraSpatialPlannerTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Spatial planner finds measured movement from an arbitrary clear start", AnyStart);
        yield return ("Spatial planner cannot invent an edge between clear viewpoints", MissingCorridor);
        yield return ("Unknown and blocked spatial viewpoints cannot enter a route", UnknownNodes);
        yield return ("Unknown and blocked corridor evidence cannot connect viewpoints", UnknownEdges);
        yield return ("Spatial planning rejects stale viewpoint and corridor evidence", StaleEvidence);
        yield return ("Future observations and mismatched spatial epochs are not proof", EpochAndFuture);
        yield return ("Spatial routes retain oldest node and corridor observation", RouteFreshness);
        yield return ("Spatial graph copies and canonicalizes caller observations", ImmutableCanonical);
        yield return ("Spatial graph rejects ambiguous identities and malformed bounds", InvalidGraph);
        yield return ("Spatial request validates finite ordered movement objectives", InvalidRequest);
        yield return ("Spatial planner honours a required actual-camera start", FixedStart);
        yield return ("Spatial route follows measured corners without inventing shortcuts", AroundCorner);
        yield return ("Spatial movement rejects reversals and repeated-point cycles", NoPingPong);
        yield return ("Spatial movement cannot manufacture duration from a short clear patch", ShortPatch);
        yield return ("Spatial role objectives distinguish lateral and rising shots", PurposefulRoles);
        yield return ("Spatial quality and clearance thresholds apply to entire route", WholeRouteThresholds);
        yield return ("Spatial route ranking uses observed novelty with equal geometry", Novelty);
        yield return ("Spatial search gives later starts a chance before beam narrowing", AllStarts);
        yield return ("Spatial search is deterministic across frame budgets and input order", Deterministic);
        yield return ("Dense spatial search obeys total and per-step effort bounds", SearchBudget);
        yield return ("Spatial reset removes old-world routes and pending state", Reset);
    }

    private static CameraSpatialNode N(int id, float x, float z = 10, float y = 3, float quality = .9f,
        float clearance = 1, float novelty = .5f, double at = 10, long epoch = 1,
        SpatialEvidence evidence = SpatialEvidence.Clear) =>
        new CameraSpatialNode(id, new AutonomyPosition(x, y, z), new AutonomyPosition(0, 3, 0),
            quality, clearance, novelty, at, epoch, evidence);
    private static CameraSpatialEdge E(int a, int b, float quality = .9f, float clearance = 1,
        double at = 10, long epoch = 1, SpatialEvidence evidence = SpatialEvidence.Clear) =>
        new CameraSpatialEdge(a, b, quality, clearance, at, epoch, evidence);
    private static CameraSpatialRoute? Plan(CameraSpatialGraph graph, CameraSpatialRequest? request = null, int budget = 64)
    {
        var planner = new CameraSpatialPlanner(); planner.Begin(graph, request ?? new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse));
        int ticks = 0;
        while (!planner.Complete && ticks++ < CameraSpatialPlanner.MaximumExpansions + 10)
        {
            int before = planner.Expansions; planner.Step(budget);
            Check(planner.Expansions - before <= budget, "Per-step operation budget was exceeded.");
        }
        Check(planner.Complete, "Spatial planner did not terminate.");
        Check(planner.Expansions <= CameraSpatialPlanner.MaximumExpansions, "Total operation budget was exceeded.");
        return planner.Result;
    }
    private static CameraSpatialGraph Graph(CameraSpatialNode[] nodes, params CameraSpatialEdge[] edges) => new CameraSpatialGraph(1, nodes, edges);
    private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
    private static void Throws(Action action)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Malformed spatial input was accepted.");
    }
    private static string Ids(CameraSpatialRoute? route) => route == null ? "none" : string.Join(",", route.Nodes.Select(n => n.Id));

    private static void AnyStart()
    {
        CameraSpatialRoute? route = Plan(Graph(new[] { N(0, -20, evidence: SpatialEvidence.Blocked), N(1, -6), N(2, 0), N(3, 6) }, E(0, 1), E(1, 2), E(2, 3)));
        Check(route != null && route.Nodes[0].Id == 1 && route.Distance >= 5, "Did not start from measured clear space after a blocked region.");
    }
    private static void MissingCorridor()
    { Check(Plan(Graph(new[] { N(0, -6), N(1, 6) })) == null, "Endpoint visibility fabricated a corridor."); }
    private static void UnknownNodes()
    {
        foreach (SpatialEvidence state in new[] { SpatialEvidence.Unknown, SpatialEvidence.Blocked })
            Check(Plan(Graph(new[] { N(0, -6), N(1, 0, evidence: state), N(2, 6) }, E(0, 1), E(1, 2))) == null, "An unproven intermediate view was traversed.");
    }
    private static void UnknownEdges()
    {
        foreach (SpatialEvidence state in new[] { SpatialEvidence.Unknown, SpatialEvidence.Blocked })
            Check(Plan(Graph(new[] { N(0, -6), N(1, 6) }, E(0, 1, evidence: state))) == null, "An unproven corridor was traversed.");
    }
    private static void StaleEvidence()
    {
        Check(Plan(Graph(new[] { N(0, -6, at: 4), N(1, 6) }, E(0, 1))) == null, "Old view remained eligible.");
        Check(Plan(Graph(new[] { N(0, -6), N(1, 6) }, E(0, 1, at: 4))) == null, "Old corridor remained eligible.");
        Check(Plan(Graph(new[] { N(0, -6, at: 5), N(1, 6) }, E(0, 1, at: 5))) != null, "Exact freshness boundary was incorrectly rejected.");
    }
    private static void EpochAndFuture()
    {
        Check(Plan(Graph(new[] { N(0, -6, epoch: 2), N(1, 6) }, E(0, 1))) == null, "Foreign-world node used.");
        Check(Plan(Graph(new[] { N(0, -6), N(1, 6) }, E(0, 1, epoch: 2))) == null, "Foreign-world edge used.");
        Check(Plan(new CameraSpatialGraph(2, new[] { N(0, -6, epoch: 2), N(1, 6, epoch: 2) }, new[] { E(0, 1, epoch: 2) })) == null, "Foreign graph accepted.");
        Check(Plan(Graph(new[] { N(0, -6, at: 10.1), N(1, 6) }, E(0, 1))) == null, "Future view treated as fresh.");
        Check(Plan(Graph(new[] { N(0, -6), N(1, 6) }, E(0, 1, at: 10.1))) == null, "Future corridor treated as fresh.");
    }
    private static void RouteFreshness()
    {
        CameraSpatialRoute route = Plan(Graph(new[] { N(0, -6, at: 9), N(1, 6) }, E(0, 1, at: 8))) ?? throw new Exception("Fixture route absent.");
        Check(route.OldestObservation == 8 && route.IsFresh(13, 1, 5), "Lost oldest corridor evidence.");
        Check(!route.IsFresh(13.01, 1, 5) && !route.IsFresh(10, 2, 5) && !route.IsFresh(double.NaN, 1, 5), "Expired/foreign route remained valid.");
        Check(!route.IsFresh(9, 1, 5), "A backwards clock made future route evidence appear fresh.");
    }
    private static void ImmutableCanonical()
    {
        var nodes = new[] { N(2, 6), N(0, -6), N(1, 0) }; var edges = new[] { E(1, 2), E(0, 1) };
        CameraSpatialGraph graph = Graph(nodes, edges); nodes[0] = N(4, 100); edges[0] = E(4, 1);
        Check(graph.Nodes[0].Id == 0 && graph.Nodes[2].Id == 2 && graph.Edges[0].FromId == 0, "Caller mutation changed immutable graph.");
        Check(Plan(graph) != null, "Canonical copied graph no longer plans.");
    }
    private static void InvalidGraph()
    {
        Throws(() => Graph(new[] { N(0, 0), N(0, 6) }));
        Throws(() => Graph(new[] { N(0, 0), N(1, 6) }, E(0, 1), E(0, 1)));
        Throws(() => Graph(new[] { N(0, 0) }, E(0, 2)));
        Throws(() => new CameraSpatialGraph(0, Array.Empty<CameraSpatialNode>(), Array.Empty<CameraSpatialEdge>()));
        Throws(() => Graph(Enumerable.Range(0, CameraSpatialGraph.MaximumNodes + 1).Select(i => N(i, i)).ToArray()));
        Throws(() => N(0, 0, quality: float.NaN)); Throws(() => N(0, 0, clearance: -1));
        Throws(() => N(0, 0, novelty: 1.1f)); Throws(() => N(0, 0, at: double.PositiveInfinity));
        Throws(() => N(0, 0, evidence: (SpatialEvidence)99)); Throws(() => E(0, 0));
    }
    private static void InvalidRequest()
    {
        Throws(() => new CameraSpatialRequest(double.NaN, 1, ScenicShotRole.Traverse));
        Throws(() => new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, 6, 5));
        Throws(() => new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, 5, 8, 7));
        Throws(() => new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, maxDistance: 121));
        Throws(() => new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, maxAge: 0));
        Throws(() => new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, startId: -1));
    }
    private static void FixedStart()
    {
        CameraSpatialGraph graph = Graph(new[] { N(0, -6), N(1, 0), N(2, 6) }, E(0, 1), E(1, 2));
        Check(Ids(Plan(graph, new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, startId: 1))) == "1,2", "Required source camera was ignored.");
        Check(Plan(graph, new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, startId: 7)) == null, "Absent start silently became another camera.");
    }
    private static void AroundCorner()
    {
        CameraSpatialGraph graph = Graph(new[] { N(0, 0, 10), N(1, 6, 10), N(2, 6, 16) }, E(0, 1), E(1, 2));
        CameraSpatialRoute? route = Plan(graph, new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, 10, 12, 15, startId: 0));
        Check(Ids(route) == "0,1,2" && route!.Distance == 12, "Planner shortcut through unmeasured corner.");
    }
    private static void NoPingPong()
    {
        CameraSpatialGraph reverse = Graph(new[] { N(0, 0, 10), N(1, 6, 10), N(2, 2, 10.1f) }, E(0, 1), E(1, 0), E(1, 2));
        Check(Plan(reverse, new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, 8, 10, 15, startId: 0)) == null, "Reversal fabricated a developed shot.");
    }
    private static void ShortPatch()
    {
        Check(Plan(Graph(new[] { N(0, 0), N(1, 2), N(2, 4) }, E(0, 1), E(1, 2), E(2, 0))) == null, "Repeated patch travel passed minimum motion.");
    }
    private static void PurposefulRoles()
    {
        CameraSpatialGraph graph = Graph(new[] { N(0, -6), N(1, 6), N(2, -6, y: 15) }, E(0, 1), E(0, 2));
        var traverse = Plan(graph, new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, 5, 12, 15, startId: 0));
        var reveal = Plan(graph, new CameraSpatialRequest(10, 1, ScenicShotRole.Reveal, 5, 12, 15, startId: 0));
        Check(Ids(traverse) == "0,1" && Ids(reveal) == "0,2", "Role did not affect measured movement selection.");
    }
    private static void WholeRouteThresholds()
    {
        Check(Plan(Graph(new[] { N(0, -6), N(1, 6, quality: .49f) }, E(0, 1))) == null, "Low endpoint quality admitted.");
        Check(Plan(Graph(new[] { N(0, -6), N(1, 6) }, E(0, 1, quality: .49f))) == null, "Low corridor quality admitted.");
        Check(Plan(Graph(new[] { N(0, -6), N(1, 6) }, E(0, 1, clearance: .24f))) == null, "Insufficient corridor clearance admitted.");
        CameraSpatialRoute? route = Plan(Graph(new[] { N(0, -6, quality: .8f), N(1, 6) }, E(0, 1, quality: .7f)));
        Check(route != null && Math.Abs(route.MinimumQuality - .7f) < .0001, "Worst corridor visibility lost in route summary.");
    }
    private static void Novelty()
    {
        CameraSpatialGraph graph = Graph(new[] { N(0, -6), N(1, 6, novelty: 0), N(2, 6, novelty: 1) }, E(0, 1), E(0, 2));
        Check(Ids(Plan(graph, new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse, startId: 0))) == "0,2", "Observed novelty was ignored.");
    }
    private static void AllStarts()
    {
        var nodes = new List<CameraSpatialNode>(); var edges = new List<CameraSpatialEdge>();
        for (int i = 0; i < 100; i += 2)
        { nodes.Add(N(i, i, quality: 1)); nodes.Add(N(i + 1, i + .1f, quality: 1)); edges.Add(E(i, i + 1, quality: 1)); }
        nodes.Add(N(100, -6, quality: .6f)); nodes.Add(N(101, 6, quality: .6f)); edges.Add(E(100, 101, quality: .6f));
        Check(Ids(Plan(new CameraSpatialGraph(1, nodes, edges))) == "100,101", "Beam seed limit hid the only useful start.");
    }
    private static void Deterministic()
    {
        CameraSpatialNode[] nodes = { N(0, -6), N(1, 0), N(2, 6), N(3, 0, 16), N(4, 6, 16) };
        CameraSpatialEdge[] edges = { E(0, 1), E(1, 2), E(0, 3), E(3, 4), E(1, 4), E(4, 2) };
        var graph = Graph(nodes, edges); string expected = Ids(Plan(graph, budget: 1));
        Array.Reverse(nodes); Array.Reverse(edges);
        Check(expected != "none" && Ids(Plan(Graph(nodes, edges), budget: 1024)) == expected, "Frame budget/enumeration changed search result.");
    }
    private static void SearchBudget()
    {
        var nodes = new List<CameraSpatialNode>(); var edges = new List<CameraSpatialEdge>();
        for (int i = 0; i < 64; i++) nodes.Add(N(i, i % 8 * 2, 10 + i / 8 * 2));
        for (int i = 0; i < nodes.Count; i++)
        for (int j = 1; j <= 8; j++) edges.Add(E(i, (i + j) % nodes.Count));
        var planner = new CameraSpatialPlanner(); planner.Begin(new CameraSpatialGraph(1, nodes, edges), new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse));
        while (!planner.Complete)
        {
            int before = planner.Expansions; planner.Step(7);
            Check(planner.Expansions - before <= 7 && planner.Expansions <= CameraSpatialPlanner.MaximumExpansions, "Dense graph exceeded bounded search effort.");
        }
        Check(planner.Result != null && planner.Result.Nodes.Count <= CameraSpatialPlanner.MaximumWaypoints, "Dense graph did not retain a bounded useful proposal.");
    }
    private static void Reset()
    {
        var planner = new CameraSpatialPlanner(); planner.Begin(Graph(new[] { N(0, -6), N(1, 6) }, E(0, 1)), new CameraSpatialRequest(10, 1, ScenicShotRole.Traverse));
        planner.Step(); Check(planner.Result != null, "Fixture route absent."); planner.Reset();
        Check(planner.Result == null && !planner.Complete && planner.Expansions == 0 && !planner.BudgetExhausted, "Reset retained old route proof.");
        planner.Begin(new CameraSpatialGraph(2, Array.Empty<CameraSpatialNode>(), Array.Empty<CameraSpatialEdge>()), new CameraSpatialRequest(10, 2, ScenicShotRole.Detail));
        Check(planner.Complete && planner.Result == null, "Empty new world inherited route.");
    }
}
