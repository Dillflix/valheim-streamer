using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class JourneyOpeningScheduleTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey origins retain independent graph progress across deferred work and twelve-second preparation slices", PersistentGraphs);
        yield return ("Journey opening discovery and known origins receive bounded interleaved service", DiscoveryFairness);
        yield return ("Journey opening removal and physical context reset preserve scheduling identity", RemovalAndReset);
        yield return ("Journey proof priority completes an actually aged connected route without extending freshness or starving neighbours", AgedProofPriority);
        yield return ("Journey proof priority yields immediately when its selected observation becomes unknown", UnknownProofYields);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static AutonomyPosition P(float x) => new AutonomyPosition(x, 0, 0);
    private static JourneyNavigationRequest Request(long id) => new JourneyNavigationRequest(id + 1, 3,
        new JourneyNavigationPose(P(0), P(12)), new JourneyNavigationPose(P(2), P(14)), P(1000), P(0),
        targetMetres: 56, maximumMetres: 64, maximumPending: 1);
    private static void PersistentGraphs()
    {
        var schedule = new JourneyOpeningSchedule();
        var graphs = new Dictionary<long, JourneyNavigationSearch>();
        for (long id = 0; id < 3; id++)
        {
            var graph = new JourneyNavigationSearch(); graph.Begin(Request(id), 0);
            graphs.Add(id, graph); Check(schedule.Register(id), "Could not register independent origin");
        }
        var counts = new int[3]; var probeIds = new HashSet<long>();
        // These elapsed times cross several former12s deadlines. Only origin0
        // remains unavailable throughout; origin2 becomes available after24s.
        // Available origins use a fully specified empty analytic world. The
        // production graphs still choose their paths; no route is preselected.
        for (int step = 0; step < 180; step++)
        {
            Check(schedule.TryNext(out long id), "Deferred origin removed its neighbours"); counts[id]++;
            double now = step / 4d;
            graphs[id].Step(4, now, 3, probe =>
            {
                if (id == 0) { probeIds.Add(probe.Id); return new JourneyNavigationObservation(JourneyNavigationEvidence.Unknown, default, 0, 3, now); }
                if (id == 2 && now < 24) return new JourneyNavigationObservation(JourneyNavigationEvidence.Unknown, default, 0, 3, now);
                var aim = probe.RequiredAim ?? new AutonomyPosition(probe.Position.X + 12, probe.Position.Y, probe.Position.Z);
                return new JourneyNavigationObservation(JourneyNavigationEvidence.Clear,
                    new JourneyNavigationPose(probe.Position, aim), 1, 3, now);
            });
        }
        Check(counts.All(n => n == 60) && schedule.Count == 3 && probeIds.Count == 1,
            "Service slices restarted pending observations or starved another origin");
        Check(graphs[0].Status == JourneyNavigationStatus.WaitingForObservations &&
            graphs[1].Result != null && graphs[2].Result != null &&
            graphs[1].Result!.Length >= 56 && graphs[2].Result!.Length >= 56,
            "A blocked first origin prevented another actual connected graph from reaching a useful proposal");
    }
    private static void DiscoveryFairness()
    {
        var schedule = new JourneyOpeningSchedule();
        int discovery = 0, origin = 0; var visits = new Dictionary<long, int>();
        for (int tick = 0; tick < 300; tick++)
        {
            if (schedule.TakeDiscoveryTurn(pendingOpening: true, catalogueExhausted: false))
            {
                discovery++;
                // Two service slices are needed per new observation. Neither
                // the partial observation nor existing origins is restarted.
                if (discovery % 2 == 0 && origin < 8) { Check(schedule.Register(origin), "Discovery lost a free slot"); visits[origin++] = 0; }
            }
            else if (schedule.TryNext(out long id)) visits[id]++;
        }
        Check(schedule.Count == 8 && discovery == 16 && visits.Values.All(n => n > 8),
            "Pending discovery monopolized work or known origins prevented discovering alternatives");
        Check(!schedule.Register(8) && !schedule.TakeDiscoveryTurn(true, false), "Opening count exceeded its bounded capacity");
        Check(schedule.Remove(0), "Completed origin could not release its slot");
        for (int i = 0; i < 4; i++) schedule.TryNext(out _);
        Check(schedule.TakeDiscoveryTurn(false, false), "Freed origin capacity never resumed discovery");
        Check(!schedule.TakeDiscoveryTurn(false, true), "Exhausted discovery consumed more service turns");
    }
    private static void RemovalAndReset()
    {
        var schedule = new JourneyOpeningSchedule();
        schedule.Register(0); schedule.Register(1); schedule.Register(2);
        schedule.TryNext(out long first); Check(first == 0, "Opening identity0 was treated as missing");
        schedule.Remove(0); schedule.TryNext(out long next); Check(next == 1, "Removal skipped the next retained origin");
        Check(!schedule.Register(1) && !schedule.Remove(99), "Duplicate/unowned identities modified scheduling");
        schedule.Reset(); Check(schedule.Count == 0 && !schedule.TryNext(out _) && schedule.TakeDiscoveryTurn(false, false),
            "Physical context reset retained graph identities or discovery pacing");
    }
    private static JourneyNavigationSearch PreparedGraph()
    {
        var graph = new JourneyNavigationSearch(); graph.Begin(Request(0), 0);
        for (int i = 0; i < 100 && graph.Result == null; i++) graph.Step(16, 0, 3, p => Clear(p, 0));
        Check(graph.Result != null && graph.Result.OnwardPose.HasValue, "Could not prepare the production graph freshness fixture");
        graph.ResumeCandidateValidation(); return graph;
    }
    private static JourneyNavigationObservation Clear(JourneyNavigationProbe probe, double now) =>
        new JourneyNavigationObservation(JourneyNavigationEvidence.Clear,
            new JourneyNavigationPose(probe.Position, probe.RequiredAim ?? new AutonomyPosition(probe.Position.X + 12, probe.Position.Y, probe.Position.Z)),
            1, 3, now);

    private static void AgedProofPriority()
    {
        (JourneyNavigationSearch Graph, int[] Visits, int LongestRun, double Now) Run(bool prioritize)
        {
            var graph = PreparedGraph(); var schedule = new JourneyOpeningSchedule();
            for (int id = 0; id < 8; id++) schedule.Register(id);
            var visits = new int[8]; int run = 0, longest = 0; long previous = -1; double now = 6;
            for (int step = 0; step < 240 && graph.Result == null; step++)
            {
                now = 6 + step * .15; schedule.TryNext(out long id); visits[id]++;
                run = previous == id ? run + 1 : 1; previous = id; longest = Math.Max(longest, run);
                if (id != 0) { if (prioritize) schedule.ReportProofProgress(id, false, false, true); continue; }
                long refreshed = graph.ProofRefreshes;
                graph.Step(1, now, 3, p => Clear(p, now));
                if (prioritize) schedule.ReportProofProgress(id, graph.SelectedProofPending,
                    graph.ProofRefreshes > refreshed, graph.Status == JourneyNavigationStatus.WaitingForObservations);
            }
            return (graph, visits, longest, now);
        }
        var ordinary = Run(false); var priority = Run(true);
        Check(ordinary.Graph.Result == null && ordinary.Graph.ProofRefreshes > 10,
            "Round-robin counterfactual did not actually reproduce repeated aging of a selected path");
        Check(priority.Graph.Result != null && priority.Graph.Result.OldestEvidence >= 6 &&
            priority.Now - priority.Graph.Result.OldestEvidence <= JourneyNavigationSearch.MaximumEvidenceAge &&
            priority.Graph.Result.OnwardObservedAt >= priority.Graph.Result.OldestEvidence,
            "Bounded proof priority fabricated or relaxed the selected route's five-second evidence window");
        Check(priority.LongestRun <= 4 && priority.Visits.Skip(1).Any(n => n > 0),
            "Selected-proof priority monopolized all service instead of yielding between bounded bursts");
    }
    private static void UnknownProofYields()
    {
        var graph = PreparedGraph(); var schedule = new JourneyOpeningSchedule();
        schedule.Register(0); schedule.Register(3); schedule.Register(7);
        var selected = new List<long>(); bool unknown = false;
        for (int step = 0; step < 20; step++)
        {
            double now = 6 + step * .05; schedule.TryNext(out long id); selected.Add(id);
            if (id != 0) { schedule.ReportProofProgress(id, false, false, true); continue; }
            long refreshed = graph.ProofRefreshes;
            graph.Step(1, now, 3, p =>
            {
                if (!p.IsStart) { unknown = true; return new JourneyNavigationObservation(JourneyNavigationEvidence.Unknown, default, 0, 3, now); }
                return Clear(p, now);
            });
            schedule.ReportProofProgress(id, graph.SelectedProofPending, graph.ProofRefreshes > refreshed,
                graph.Status == JourneyNavigationStatus.WaitingForObservations);
        }
        Check(unknown && graph.SelectedProofPending && graph.Result == null && selected[0] == 0 && selected[1] == 0 && selected[2] == 3,
            "An unknown selected observation retained priority or lost its unresolved proof state");
        Check(selected.Count(id => id == 3) >= 5 && selected.Count(id => id == 7) >= 5,
            "A repeatedly unknown selected origin starved the other observed origins");
    }
}
