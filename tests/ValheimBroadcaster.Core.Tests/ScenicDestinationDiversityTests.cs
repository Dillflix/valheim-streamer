using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class ScenicDestinationDiversityTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Dense Black Forest landmarks cannot dominate the scenic programme", ImbalancedCatalogue);
        yield return ("Destination counts do not change source biome and landscape shares", DensityInvariant);
        yield return ("Scenic diversity credits sustained actual pictures and bounds stalled frames", FootageEvidence);
        yield return ("Acquisition attempts diversify retries without claiming useful scenery", FailedAttempts);
        yield return ("Unknown biome metadata remains one bounded editorial category", UnknownBiome);
        yield return ("Settlement areas share a family instead of inflating variety", FamilyNovelty);
        yield return ("A sole viable category remains available without impossible quotas", SoleCategory);
        yield return ("Destination history reset clears worlds and bounded editorial state", BoundedHistory);
        yield return ("Merged catalogues preserve source and biome diversity at capacity", BoundedMerge);
        yield return ("Classified natural records replace generic duplicate destinations", NaturalReplacement);
        yield return ("Diversity never overrides travel distance or destination cooldowns", EligibleOnly);
        yield return ("Player priority and held camera survive diverse destination inventories", PlayerAndHold);
        yield return ("Creature footage cannot credit a scenic destination", CreatureCredit);
        yield return ("Initial proximity does not credit structure coverage", InitialStructure);
        yield return ("Player takeover interrupts partial scenic viewing credit", PlayerInterrupt);
        yield return ("A failed scenic trip rotates source without successful-visit credit", FailedTravel);
    }
    private static ScenicRegion R(string id, ScenicDestinationKind kind = ScenicDestinationKind.Landscape,
        string biome = "Meadows", ScenicLandscape landscape = ScenicLandscape.Inland, float x = 1000, float preference = 0,
        string? family = null, double available = 0) => new ScenicRegion(id, id, new AutonomyPosition(x, 30, 0), preference, available, biome, landscape, kind, family);
    private static ScenicRegion Pick(ScenicDestinationDiversity d, IReadOnlyList<ScenicRegion> options) => d.Choose(options, default, _ => double.NegativeInfinity)!;
    private static void Complete(ScenicDestinationDiversity d, ScenicRegion r)
    { d.Attempt(r); d.Begin(r); for (int i = 0; i < 15; i++) d.Observe(1, true); }
    private static void Assert(bool c) { if (!c) throw new Exception("Assertion failed"); }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static ScenicRegion[] Catalogue(int landmarks)
    {
        var rows = new List<ScenicRegion>();
        for (int i = 0; i < landmarks; i++) rows.Add(R("ruin:" + i, ScenicDestinationKind.Landmark, "BlackForest", preference: 90));
        rows.Add(R("base", ScenicDestinationKind.Base, "Meadows", preference: 80));
        rows.Add(R("coast", biome: "Meadows", landscape: ScenicLandscape.Coast));
        rows.Add(R("plains", biome: "Plains"));
        rows.Add(R("mountain", biome: "Mountain", landscape: ScenicLandscape.Highland));
        return rows.ToArray();
    }
    private static void ImbalancedCatalogue()
    {
        var d = new ScenicDestinationDiversity(); var rows = Catalogue(900); var shots = new List<ScenicRegion>();
        for (int i = 0; i < 60; i++) { ScenicRegion r = Pick(d, rows); shots.Add(r); Complete(d, r); }
        int natural = shots.Count(r => r.Kind == ScenicDestinationKind.Landscape);
        Assert(natural >= 30 && natural <= 43);
        Assert(shots.Count(r => r.Kind == ScenicDestinationKind.Base) >= 8);
        Assert(shots.Count(r => r.Kind == ScenicDestinationKind.Landmark) >= 8);
        Assert(shots.Count(r => r.Landscape == ScenicLandscape.Coast) >= 12);
        Assert(shots.Select(r => r.Biome).Distinct().Count() >= 4);
        Assert(shots.Count(r => r.Biome == "BlackForest") < 20);
    }
    private static void DensityInvariant()
    {
        var sparse = new ScenicDestinationDiversity(); var dense = new ScenicDestinationDiversity();
        for (int i = 0; i < 60; i++)
        {
            ScenicRegion a = Pick(sparse, Catalogue(1)), b = Pick(dense, Catalogue(900));
            Eq(a.Kind, b.Kind); Eq(a.Biome, b.Biome); Eq(a.Landscape, b.Landscape);
            Complete(sparse, a); Complete(dense, b);
        }
    }
    private static void FootageEvidence()
    {
        var d = new ScenicDestinationDiversity(); d.Attempt(R("a")); d.Begin(R("a"));
        for (int i = 0; i < 30; i++) d.Observe(1, false); Eq(0, d.CompletedVisits);
        d.Observe(3600, true); Eq(0, d.CompletedVisits);
        for (int i = 0; i < 13; i++) d.Observe(1, true); Eq(0, d.CompletedVisits);
        d.Observe(1, true); Eq(1, d.CompletedVisits); Eq(0, d.RecentAttempts);
        for (int i = 0; i < 60; i++) d.Observe(1, true); Eq(1, d.CompletedVisits);
        d.Begin(R("b")); d.Observe(1, true); d.StopVisit(); d.Observe(100, true); Eq(1, d.CompletedVisits);
    }
    private static void FailedAttempts()
    {
        var d = new ScenicDestinationDiversity(); var kinds = new HashSet<ScenicDestinationKind>();
        var landscapes = new HashSet<ScenicLandscape>();
        for (int i = 0; i < 20; i++)
        { var r = Pick(d, Catalogue(900)); kinds.Add(r.Kind); landscapes.Add(r.Landscape); d.Attempt(r); }
        Eq(0, d.CompletedVisits); Eq(3, kinds.Count); Assert(landscapes.Contains(ScenicLandscape.Coast));
        Assert(d.RecentAttempts <= ScenicDestinationDiversity.AttemptLimit);
    }
    private static void UnknownBiome()
    {
        var d = new ScenicDestinationDiversity(); var rows = new[] { R("known", biome: "Meadows"), R("unknown", biome: "", preference: 100) };
        int known = 0, unknown = 0;
        for (int i = 0; i < 40; i++) { var r = Pick(d, rows); if (r.Biome.Length == 0) unknown++; else known++; Complete(d, r); }
        Assert(known >= 18 && unknown >= 18); Assert(known + unknown == 40);
        Eq("Meadows", new ScenicRegion("g", "Meadows", default).Biome);
        Eq("", new ScenicRegion("g", "GreydwarfCamp", default).Biome);
    }
    private static void FamilyNovelty()
    {
        var d = new ScenicDestinationDiversity();
        var rows = new[] { R("area:a", ScenicDestinationKind.Base, preference: 99, family: "base:a"),
            R("area:b", ScenicDestinationKind.Base, preference: 98, family: "base:a"), R("base:c", ScenicDestinationKind.Base, preference: 1) };
        Complete(d, Pick(d, rows)); Eq("base:c", Pick(d, rows).Id);
    }
    private static void SoleCategory()
    {
        var d = new ScenicDestinationDiversity(); var rows = new[] { R("a", ScenicDestinationKind.Landmark, "BlackForest") };
        for (int i = 0; i < 50; i++) { var r = Pick(d, rows); Eq("a", r.Id); Complete(d, r); }
        Eq<ScenicRegion?>(null, d.Choose(Array.Empty<ScenicRegion>(), default, _ => 0));
    }
    private static void BoundedHistory()
    {
        var d = new ScenicDestinationDiversity(); for (int i = 0; i < 100; i++) Complete(d, R("r:" + i));
        Eq(ScenicDestinationDiversity.HistoryLimit, d.CompletedVisits);
        for (int i = 0; i < 20; i++) d.Attempt(R("f:" + i)); Eq(ScenicDestinationDiversity.AttemptLimit, d.RecentAttempts);
        d.Reset(); Eq(0, d.CompletedVisits); Eq(0, d.RecentAttempts);
    }
    private static WorldInterestSite S(string id, WorldInterestKind kind, string biome, ScenicLandscape landscape, float priority = 80) =>
        new WorldInterestSite(id, "", id, kind, new ObserverPoint(1000, 30, 0), new ObserverPoint(10, 5, 10), new ObserverPoint(1024, 40, 0), 4, priority, biome: biome, landscape: landscape);
    private static void BoundedMerge()
    {
        var generated = Enumerable.Range(0, 1024).Select(i => R("g:" + i, biome: i == 0 ? "Plains" : "BlackForest")).ToArray();
        var sites = Enumerable.Range(0, 1024).Select(i => S("landmark:" + i, WorldInterestKind.Landmark, i == 0 ? "Meadows" : "BlackForest", ScenicLandscape.Inland)).ToArray();
        var merged = WorldDestinationPlanner.Merge(generated, sites, new DestinationFeedbackLedger(), 0);
        Eq(1024, merged.Count); Assert(merged.Count(r => r.Kind == ScenicDestinationKind.Landscape) >= 128);
        Assert(merged.Any(r => r.Biome == "Plains")); Assert(merged.Any(r => r.Biome == "Meadows"));
        Eq(1024, merged.Select(r => r.Id).Distinct().Count());
    }
    private static void NaturalReplacement()
    {
        var merged = WorldDestinationPlanner.Merge(new[] { new ScenicRegion("7:3", "Meadows", default) },
            new[] { S("7:3", WorldInterestKind.Natural, "Meadows", ScenicLandscape.Coast) }, new DestinationFeedbackLedger(), 0);
        Eq(1, merged.Count); Eq(ScenicLandscape.Coast, merged[0].Landscape); Eq(1024f, merged[0].Position.X);
        Eq(ScenicDestinationKind.Landscape, merged[0].Kind);
    }
    private static AutonomousDirector Director()
    { var d = new AutonomousDirector { RegionSeconds = 1, MinimumTravelSeconds = 1, MinimumScenicTravelDistance = 100, MaximumScenicTravelDistance = 2000 }; d.SetEnabled(true); return d; }
    private static void EligibleOnly()
    {
        var d = Director(); var rows = new[] { R("nearcoast", landscape: ScenicLandscape.Coast, x: 20, preference: 100),
            R("farcoast", landscape: ScenicLandscape.Coast, x: 4000, preference: 100), R("coolcoast", landscape: ScenicLandscape.Coast, available: 100, preference: 100), R("valid") };
        d.Tick(0, 1, Array.Empty<AutonomyPlayer>(), rows, default, null, true); Eq("valid", d.PendingTravel!.TargetId);
    }
    private static void PlayerAndHold()
    {
        var rows = Catalogue(900); var d = Director(); d.SetHolding(true);
        d.Tick(0, 10, Array.Empty<AutonomyPlayer>(), rows, default, null, true); Assert(d.PendingTravel == null);
        d.SetHolding(false);
        var player = new AutonomyPlayer("p", "Player", default(AutonomyPosition), 100, true);
        d.Tick(1, 1, new[] { player }, rows, default, "p", true); Eq(AutonomyState.Following, d.State); Assert(d.PendingTravel == null);
    }
    private static void CreatureCredit()
    {
        var d = Director(); d.RegionSeconds = 1000; var rows = new[] { R("local", x: 0) };
        for (int i = 0; i < 30; i++) d.Tick(i, 1, Array.Empty<AutonomyPlayer>(), rows, default, null, true, scenicFootageReady: false);
        Eq(0, d.SustainedScenicVisits);
        for (int i = 30; i < 45; i++) d.Tick(i, 1, Array.Empty<AutonomyPlayer>(), rows, default, null, true, scenicFootageReady: true);
        Eq(1, d.SustainedScenicVisits);
    }
    private static void InitialStructure()
    {
        var d = Director(); d.RegionSeconds = 1000; var rows = new[] { R("local-base", ScenicDestinationKind.Base, x: 0) };
        for (int i = 0; i < 30; i++) d.Tick(i, 1, Array.Empty<AutonomyPlayer>(), rows, default, null, true, scenicFootageReady: true);
        Eq(0, d.SustainedScenicVisits);
    }
    private static void PlayerInterrupt()
    {
        var d = Director(); d.RegionSeconds = 1000; var rows = new[] { R("local", x: 0) };
        for (int i = 0; i < 10; i++) d.Tick(i, 1, Array.Empty<AutonomyPlayer>(), rows, default, null, true, scenicFootageReady: true);
        var p = new AutonomyPlayer("p", "Player", default(AutonomyPosition), 100, true);
        d.Tick(10, 1, new[] { p }, rows, default, "p", false, scenicFootageReady: false);
        for (int i = 11; i < 20; i++) d.Tick(i, 1, Array.Empty<AutonomyPlayer>(), rows, default, null, true, scenicFootageReady: true);
        Eq(0, d.SustainedScenicVisits);
        for (int i = 20; i < 26; i++) d.Tick(i, 1, Array.Empty<AutonomyPlayer>(), rows, default, null, true, scenicFootageReady: true);
        Eq(1, d.SustainedScenicVisits);
    }
    private static void FailedTravel()
    {
        var d = Director(); var rows = Catalogue(900);
        d.Tick(0, 1, Array.Empty<AutonomyPlayer>(), rows, default, null, true); var first = d.PendingTravel!;
        Eq(AutonomyTravelKind.Scenic, first.Kind); d.TravelStarted(first.Token, 0); d.TravelCompleted(first.Token, false, 1, "No arrival");
        d.Tick(2, 1, Array.Empty<AutonomyPlayer>(), rows, default, null, true);
        Eq(0, d.SustainedScenicVisits); Assert(d.PendingTravel != null); Assert(!d.PendingTravel!.TargetId.StartsWith("ruin:"));
    }
}
