using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class WorldInterestTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Gravestones expire by the saved death time on the same world clock", GraveAge);
        yield return ("Gravestones keep the newest marker in each zone within a global bound", GraveZones);
        yield return ("Gravestone areas survive v2, receive occasional Scenic coverage and disappear on removal", GraveCoverage);
        yield return ("World interests round-trip full bounded pages and immutable subject extents", PageRoundTrip);
        yield return ("World interests reject malformed and truncated wire payloads", PacketValidation);
        yield return ("World interests reject invalid identities extents counts and flags", SubjectValidation);
        yield return ("Destination feedback validates sustained output separately from failure", FeedbackWire);
        yield return ("Interest cycles publish atomically and remove deleted destinations", AtomicCycles);
        yield return ("Interest pages preserve source age across receipts and expire", SourceFreshness);
        yield return ("Interest caches reject mixed cycles duplicate IDs and wrong worlds", CacheValidation);
        yield return ("Interest capability disable and session reset clear stale evidence", CacheReset);
        yield return ("Small construction clusters distinguish discarded pieces from functional camps", Camps);
        yield return ("Base grouping crosses negative grid boundaries without combining distant sites", BaseClustering);
        yield return ("Sprawling construction supplies bounded area subjects and local arrival hints", Sprawling);
        yield return ("Base indexing separates vertically unrelated construction and caps global memory", IndexBounds);
        yield return ("Base IDs and output remain deterministic across scan order", DeterministicBases);
        yield return ("Destination history persists per world and rejects corruption atomically", PersistedHistory);
        yield return ("Destination feedback applies distinct bounded cooldowns and decays", FeedbackCooldown);
        yield return ("Destination history bounds capacity and survives wall-clock correction", HistoryBounds);
        yield return ("Destination visits credit actual footage and only one outcome", VisitEvidence);
        yield return ("Destination interruptions reset visits without inventing failure", VisitInterruption);
        yield return ("Destination success requires the selected structure to be shown", SubjectVisit);
        yield return ("Destination merge preserves generated fallback and stale evidence exclusion", MergeFallback);
        yield return ("Destination merge respects independent local and remote cooldowns", MergeCooldown);
        yield return ("Scenic selection prefers eligible server subjects without weakening travel bounds", PrioritySelection);
        yield return ("Destination cooldowns yield to generated scenery and player priority", PriorityFallback);
    }
    private static void GraveAge()
    {
        long death = TimeSpan.FromHours(16).Ticks;
        Eq(7200d, RecentGravestones<int>.Remaining(death, death, 120));
        Eq(1d, RecentGravestones<int>.Remaining(death + TimeSpan.FromSeconds(7199).Ticks, death, 120));
        Eq(0d, RecentGravestones<int>.Remaining(death + TimeSpan.FromHours(2).Ticks, death, 120));
        Eq(0d, RecentGravestones<int>.Remaining(death - 1, death, 120));
        Eq(0d, RecentGravestones<int>.Remaining(death, 0, 120));
        Eq(0d, RecentGravestones<int>.Remaining(death, death, 0));
    }
    private static void GraveZones()
    {
        var index = new RecentGravestones<int>();
        for (int i = 60; i >= 1; i--) index.Observe(-i, 3, default, i, i);
        Eq(16, index.Count);
        Assert(index.Markers.Min(m => m.DeathTicks) == 45);
        index.Observe(-59, 3, new ObserverPoint(2, 5, 3), 89, 129);
        index.Observe(-59, 3, default, 58, 100);
        Eq(129, index.Find("grave:-59:3")!.Handle);
        Eq(16, index.Count);
        index.Clear(); Eq(0, index.Count);
    }
    private static void GraveCoverage()
    {
        var area = new WorldInterestSite("grave:-5:3", "", "Recent gravestone area", WorldInterestKind.Natural,
            new ObserverPoint(-310, 5, 203), default, new ObserverPoint(-300, 10, 215), 0, 24, biome: "Swamp");
        var cache = new WorldInterestCache();
        var page = P(1, 1, sites: new[] { area });
        cache.Accept(WorldInterestProtocol.DecodeV2(WorldInterestProtocol.EncodeV2(page)), 0);
        var land = new ScenicRegion("land", "Coast", new AutonomyPosition(3, 4, 500), landscape: ScenicLandscape.Coast);
        var regions = WorldDestinationPlanner.Merge(new[] { land }, cache.Available(1), new DestinationFeedbackLedger(), 1);
        Eq(ScenicDestinationKind.Gravestone, regions.Single(r => r.Id == area.Id).Kind);
        var diversity = new ScenicDestinationDiversity();
        int visits = 0;
        for (int i = 0; i < 24; i++)
        {
            var next = diversity.Choose(regions, default, _ => 0)!;
            if (next.Kind == ScenicDestinationKind.Gravestone) visits++;
            diversity.Begin(next);
            for (int sec = 0; sec < 15; sec++) diversity.Observe(1, true);
        }
        Assert(visits > 0 && visits < 6);
        cache.Accept(P(2, 2), 4);
        Assert(!WorldDestinationPlanner.Merge(new[] { land }, cache.Available(4), new DestinationFeedbackLedger(), 4).Any(r => r.Id == area.Id));
    }
    private static WorldInterestSite S(string id = "base:0:0:0", float x = 500, float age = 0, float retry = 0, float priority = 60) =>
        new WorldInterestSite(id, "", "A base", WorldInterestKind.Base, new ObserverPoint(x, 30, 0), new ObserverPoint(10, 5, 10), new ObserverPoint(x + 24, 40, 0), 20, priority, age, retry);
    private static WorldInterestPage P(long seq, long cycle, int page = 0, int pages = 1, params WorldInterestSite[] sites) =>
        new WorldInterestPage(7, seq, cycle, page, pages, true, true, sites);
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static void Bad(Action a) { try { a(); } catch (ArgumentException) { return; } catch (EndOfStreamException) { return; } throw new Exception("Expected invalid input to be rejected"); }
    private static void PageRoundTrip()
    {
        var sites = Enumerable.Range(0, 64).Select(i => S("base:" + i)).ToArray(); var p = P(1, 1, 0, 1, sites);
        sites[0] = S("changed"); Eq("base:0", p.Sites[0].Id);
        byte[] bytes = WorldInterestProtocol.Encode(p); Assert(bytes.Length < WorldInterestProtocol.MaxPacketBytes);
        var d = WorldInterestProtocol.Decode(bytes); Eq(64, d.Sites.Count); Eq(7L, d.World); Eq(1L, d.Cycle);
        Eq(10f, d.Sites[0].Extents.X); Eq(524f, d.Sites[0].Arrival.X); Eq(20, d.Sites[0].Pieces);
        var child = new WorldInterestSite("area:1", "base:1", "Area", WorldInterestKind.BaseArea, default, new ObserverPoint(2, 3, 4), default, 4, 42);
        Eq("base:1", WorldInterestProtocol.Decode(WorldInterestProtocol.Encode(P(2, 2, sites: new[] { child }))).Sites[0].ParentId);
    }
    private static void PacketValidation()
    {
        byte[] good = WorldInterestProtocol.Encode(P(1, 1, sites: new[] { S() }));
        for (int n = 0; n < good.Length; n++) { byte[] cut = good.Take(n).ToArray(); Bad(() => WorldInterestProtocol.Decode(cut)); }
        Bad(() => WorldInterestProtocol.Decode(good.Concat(new byte[] { 1 }).ToArray()));
        Bad(() => WorldInterestProtocol.Decode(new byte[WorldInterestProtocol.MaxPacketBytes + 1]));
        foreach (int offset in new[] { 0, 4, 40, 41, 42 }) { byte[] bad = (byte[])good.Clone(); bad[offset] = 255; Bad(() => WorldInterestProtocol.Decode(bad)); }
        byte[] huge = (byte[])good.Clone(); huge[44] = 255; huge[45] = 255; Bad(() => WorldInterestProtocol.Decode(huge));
        byte[] utf = (byte[])good.Clone(); utf[46] = 255; Bad(() => WorldInterestProtocol.Decode(utf));
    }
    private static void SubjectValidation()
    {
        foreach (string id in new[] { "", "../base", "base\n", new string('b', 65), "base🔥" }) Bad(() => S(id));
        Bad(() => S(priority: float.NaN)); Bad(() => S(age: 90000)); Bad(() => S(retry: -1));
        Bad(() => new WorldInterestSite("a", "a", "a", WorldInterestKind.Base, default, default, default, 0, 0));
        Bad(() => new WorldInterestSite("a", "", "a", (WorldInterestKind)99, default, default, default, 0, 0));
        Bad(() => new WorldInterestSite("a", "", "a", WorldInterestKind.Base, default, new ObserverPoint(-1, 0, 0), default, 0, 0));
        Bad(() => P(1, 1, sites: new[] { S(), S() })); Bad(() => P(1, 1, 16, 16));
        Bad(() => new WorldInterestPage(7, 1, 1, 0, 1, false, false, new[] { S() }));
    }
    private static void FeedbackWire()
    {
        foreach (DestinationOutcome outcome in Enum.GetValues(typeof(DestinationOutcome)))
        {
            var f = new DestinationFeedback(7, 8, "base:1", outcome, outcome == DestinationOutcome.UsableFootage ? 15 : 0);
            byte[] bytes = WorldInterestProtocol.Encode(f); var d = WorldInterestProtocol.DecodeFeedback(bytes);
            Eq(outcome, d.Outcome); Eq(8L, d.Sequence); Eq(f.FootageSeconds, d.FootageSeconds);
            for (int n = 0; n < bytes.Length; n++) { byte[] cut = bytes.Take(n).ToArray(); Bad(() => WorldInterestProtocol.DecodeFeedback(cut)); }
        }
        Bad(() => new DestinationFeedback(1, 1, "a", DestinationOutcome.UsableFootage, 14.9f));
        Bad(() => new DestinationFeedback(1, 1, "a", DestinationOutcome.TravelFailed, 1));
    }
    private static void AtomicCycles()
    {
        var c = new WorldInterestCache(); c.Accept(P(1, 1, sites: new[] { S("old") }), 0);
        c.Accept(P(2, 2, 0, 2, S("new-a")), 1); Eq("old", c.Available(1)[0].Id);
        c.Accept(P(3, 2, 1, 2, S("new-b")), 2); Eq(2, c.Count); Assert(c.Available(2).All(s => s.Id != "old"));
        Assert(!c.Accept(P(2, 1), 3)); Eq(2, c.Count);
        c.Accept(P(4, 3), 4); Eq(0, c.Count);
    }
    private static void SourceFreshness()
    {
        var c = new WorldInterestCache(); c.Accept(P(1, 1, 0, 2, S("a", age: 590, retry: 20)), 0);
        c.Accept(P(2, 1, 1, 2, S("b")), 5);
        Eq(595f, c.Available(5)[0].Age); Eq(15f, c.Available(5)[0].RetryAfter);
        Eq(1, c.Available(10).Count); Eq(0, c.Available(50).Count);
        c.Accept(P(3, 2, sites: new[] { S("a", age: 600) }), 51); Eq(0, c.Available(51).Count);
    }
    private static void CacheValidation()
    {
        var c = new WorldInterestCache(); c.Accept(P(1, 1, 0, 2, S()), 0);
        Bad(() => c.Accept(P(2, 1, 1, 2, S()), 1)); Eq(0, c.Count);
        c.Reset(); c.Accept(P(1, 1, 0, 2, S()), 0); Bad(() => c.Accept(P(2, 1, 1, 3, S("b")), 1));
        c.Reset(); c.Accept(P(1, 1), 2); Bad(() => c.Accept(new WorldInterestPage(8, 2, 2, 0, 1, true, true, Array.Empty<WorldInterestSite>()), 3));
        Bad(() => c.Accept(P(2, 2), 1));
    }
    private static void CacheReset()
    {
        var c = new WorldInterestCache(); c.Accept(P(1, 1, sites: new[] { S() }), 0);
        c.Accept(new WorldInterestPage(7, 2, 2, 0, 1, false, false, Array.Empty<WorldInterestSite>()), 1); Eq(0, c.Count); Assert(!c.Fresh(1));
        c.Accept(P(3, 3, sites: new[] { S() }), 2); Eq(1, c.Count);
        c.Reset(); Assert(!c.Fresh(2)); c.Accept(new WorldInterestPage(8, 1, 1, 0, 1, true, true, new[] { S() }), 0); Eq(1, c.Count);
    }
    private static void Pieces(WorldBaseIndex b, float x, float y = 30, int count = 4) { for (int i = 0; i < count; i++) b.Add(new ObserverPoint(x + i % 2, y, i / 2 % 2), false); }
    private static void Camps()
    {
        var b = new WorldBaseIndex(); Pieces(b, 0, count: 3); Eq(0, b.Build().Count);
        b.Add(new ObserverPoint(0, 30, 0), false); Eq(4, b.Build()[0].Pieces);
        b.Clear(); b.Add(new ObserverPoint(0, 30, 0), true); Eq(1, b.Build().Count);
    }
    private static void BaseClustering()
    {
        var b = new WorldBaseIndex(); Pieces(b, -2); Pieces(b, 1); Pieces(b, 500);
        var sites = b.Build(); Eq(2, sites.Count); Eq(8, sites[0].Pieces); Assert(sites.Any(s => s.Center.X > 400));
    }
    private static void Sprawling()
    {
        var b = new WorldBaseIndex(); for (int x = 0; x <= 240; x += 16) Pieces(b, x);
        var sites = b.Build(); var root = sites.Single(s => s.Kind == WorldInterestKind.Base);
        Assert(root.Extents.X > 100); Assert(Math.Abs(root.Arrival.X - root.Center.X) > 50);
        var areas = sites.Where(s => s.Kind == WorldInterestKind.BaseArea).ToArray(); Assert(areas.Length > 1 && areas.Length <= 12);
        Assert(areas.All(a => a.ParentId == root.Id && a.Extents.X < root.Extents.X));
    }
    private static void IndexBounds()
    {
        var b = new WorldBaseIndex(); Pieces(b, 0, 20); Pieces(b, 0, 150); Eq(2, b.Build().Count);
        b.Clear(); b.Add(new ObserverPoint(0, 5000, 0), true); Eq(0, b.Count);
        for (int x = 0; x < 65; x++) for (int z = 0; z < 65; z++) b.Add(new ObserverPoint(x * 64, 30, z * 64), true);
        Eq(WorldBaseIndex.MaxCells, b.Count); Assert(b.Truncated); Assert(b.Build().Count <= WorldBaseIndex.MaxCells);
        b.Clear(); Eq(0, b.Count); Assert(!b.Truncated);
    }
    private static void DeterministicBases()
    {
        var a = new WorldBaseIndex(); var b = new WorldBaseIndex();
        for (int x = -128; x <= 128; x += 16) Pieces(a, x);
        for (int x = 128; x >= -128; x -= 16) Pieces(b, x);
        Eq(string.Join(",", a.Build().Select(s => s.Id)), string.Join(",", b.Build().Select(s => s.Id)));
    }
    private static void PersistedHistory()
    {
        var h = new DestinationFeedbackLedger(); h.Record("base:1", DestinationOutcome.PictureUnavailable, 100);
        byte[] bytes = h.Save(7); var restored = new DestinationFeedbackLedger(); restored.Load(bytes, 7); Eq(290f, restored.RetryAfter("base:1", 110));
        Bad(() => restored.Load(bytes, 8)); Eq(1, restored.Count);
        for (int n = 0; n < bytes.Length; n++) { byte[] cut = bytes.Take(n).ToArray(); Bad(() => restored.Load(cut, 7)); Eq(1, restored.Count); }
        Bad(() => restored.Load(bytes.Concat(new byte[] { 0 }).ToArray(), 7));
    }
    private static void FeedbackCooldown()
    {
        var h = new DestinationFeedbackLedger(); h.Record("a", DestinationOutcome.TravelFailed, 10); Eq(120f, h.RetryAfter("a", 10));
        h.Record("b", DestinationOutcome.PictureUnavailable, 10); Eq(300f, h.RetryAfter("b", 10));
        for (int i = 0; i < 10; i++) h.Record("b", DestinationOutcome.PictureUnavailable, 20 + i);
        Eq(1800f, h.RetryAfter("b", 29)); Assert(h.Adjustment("b", 29) < 0);
        h.Record("b", DestinationOutcome.UsableFootage, 30); Eq(0f, h.RetryAfter("b", 30));
        Eq(0f, h.Adjustment("b", 90000)); h.Record("b", DestinationOutcome.UsableFootage, 90000); Assert(h.Adjustment("b", 90000) > 0);
    }
    private static void HistoryBounds()
    {
        var h = new DestinationFeedbackLedger(); for (int i = 0; i < 1100; i++) h.Record("id:" + i, DestinationOutcome.TravelFailed, i + 1);
        Eq(1024, h.Count); Assert(h.Save(7).Length < 100000);
        h.Record("x", DestinationOutcome.PictureUnavailable, 5000); Eq(0f, h.RetryAfter("x", 1));
        h.Record("x", DestinationOutcome.TravelFailed, 2); Eq(120f, h.RetryAfter("x", 2));
    }
    private static void VisitEvidence()
    {
        var v = new DestinationVisit(); v.Begin("a");
        for (int i = 0; i < 20; i++) Eq<DestinationOutcome?>(null, v.Observe(1, false, false)); Eq(0f, v.FootageSeconds);
        Eq<DestinationOutcome?>(null, v.Observe(100, true, false)); Eq(1f, v.FootageSeconds);
        for (int i = 0; i < 13; i++) Eq<DestinationOutcome?>(null, v.Observe(1, true, false));
        Eq<DestinationOutcome?>(DestinationOutcome.UsableFootage, v.Observe(1, true, false));
        Eq<DestinationOutcome?>(null, v.Observe(1, false, true));
        v.Begin("b"); Eq<DestinationOutcome?>(DestinationOutcome.PictureUnavailable, v.Observe(1, false, true));
        Eq<DestinationOutcome?>(null, v.Observe(1, false, true));
    }
    private static void SubjectVisit()
    {
        var v = new DestinationVisit(); v.Begin("ruins", true);
        for (int i = 0; i < 30; i++) Eq<DestinationOutcome?>(null, v.Observe(1, true, false));
        Eq(0f, v.FootageSeconds);
        Eq<DestinationOutcome?>(DestinationOutcome.SubjectUnavailable, v.Observe(1, true, false, false, true));
        Eq<DestinationOutcome?>(null, v.Observe(1, false, true));
        v.Begin("base", true);
        for (int i = 0; i < 14; i++) Eq<DestinationOutcome?>(null, v.Observe(1, true, false, true));
        Eq<DestinationOutcome?>(null, v.Observe(1, true, false, true));
        Eq<DestinationOutcome?>(DestinationOutcome.UsableFootage, v.Observe(0, true, false, true, subjectSatisfied: true));
        v.Begin("landscape");
        for (int i = 0; i < 14; i++) v.Observe(1, true, false);
        Eq<DestinationOutcome?>(DestinationOutcome.UsableFootage, v.Observe(1, true, false));
    }
    private static void VisitInterruption()
    {
        var v = new DestinationVisit(); v.Begin("a"); v.Observe(1, true, false); v.Reset();
        Eq<DestinationOutcome?>(null, v.Observe(1, false, true)); v.Begin("b"); Eq(0f, v.FootageSeconds);
        Bad(() => v.Observe(float.NaN, true, false));
    }
    private static ScenicRegion G(string id = "g", float x = 300) => new ScenicRegion(id, "Generated", new AutonomyPosition(x, 40, 0));
    private static void MergeFallback()
    {
        var generic = Enumerable.Range(0, 1024).Select(i => G("g:" + i, 300 + i)).ToArray();
        var sites = Enumerable.Range(0, 1024).Select(i => S("base:" + i)).ToArray();
        var h = new DestinationFeedbackLedger(); var merged = WorldDestinationPlanner.Merge(generic, sites, h, 0);
        Eq(1024, merged.Count); Assert(merged.Count(r => r.Id.StartsWith("g:")) >= 128);
        Eq(1024, WorldDestinationPlanner.Merge(generic, Array.Empty<WorldInterestSite>(), h, 1).Count);
        Eq(1, WorldDestinationPlanner.Merge(new[] { G() }, new[] { S(age: 600) }, h, 0).Count);
    }
    private static void MergeCooldown()
    {
        var h = new DestinationFeedbackLedger(); h.Record("a", DestinationOutcome.PictureUnavailable, 100);
        var m = WorldDestinationPlanner.Merge(Array.Empty<ScenicRegion>(), new[] { S("a", retry: 20), S("b", retry: 40) }, h, 110);
        Eq(400d, m.Single(r => r.Id == "a").AvailableAfter); Eq(150d, m.Single(r => r.Id == "b").AvailableAfter);
    }
    private static AutonomousDirector Director() { var d = new AutonomousDirector { RegionSeconds = 1, MinimumScenicTravelDistance = 100, MaximumScenicTravelDistance = 1000, MinimumTravelSeconds = 1 }; d.SetEnabled(true); return d; }
    private static void Tick(AutonomousDirector d, double now, IReadOnlyList<ScenicRegion> regions, AutonomyPlayer[]? players = null, string? shown = null) =>
        d.Tick(now, 1, players ?? Array.Empty<AutonomyPlayer>(), regions, default, shown, true);
    private static void PrioritySelection()
    {
        var h = new DestinationFeedbackLedger(); var sites = new[] { S("near", x: 0, priority: 100), S("far", x: 2000, priority: 100), S("base", x: 800) };
        var rows = WorldDestinationPlanner.Merge(new[] { G() }, sites, h, 0); var d = Director(); Tick(d, 0, rows);
        Eq("base", d.PendingTravel!.TargetId); Eq(AutonomyTravelKind.Scenic, d.PendingTravel.Kind);
        long token = d.PendingTravel.Token; d.TravelStarted(token, 0); d.TravelCompleted(token, true, 1);
        d.Tick(2, 1, Array.Empty<AutonomyPlayer>(), rows, d.CurrentRegionId == "base" ? new AutonomyPosition(824, 40, 0) : default, null, true);
        Assert(d.PendingTravel?.TargetId != "base");
    }
    private static void PriorityFallback()
    {
        var rows = WorldDestinationPlanner.Merge(new[] { G() }, new[] { S(retry: 120) }, new DestinationFeedbackLedger(), 0);
        var d = Director(); Tick(d, 0, rows); Eq("g", d.PendingTravel!.TargetId);
        d = Director(); var p = new AutonomyPlayer("player", "Player", default(AutonomyPosition), 100, true);
        Tick(d, 0, rows, new[] { p }, "player"); Eq(AutonomyState.Following, d.State); Assert(d.PendingTravel == null);
    }
}
