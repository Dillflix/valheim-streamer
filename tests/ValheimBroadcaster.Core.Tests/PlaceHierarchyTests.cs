using System;
using System.Collections.Generic;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class PlaceHierarchyTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Place model connects actual touching pieces into one building", Connected);
        yield return ("Place model retains neighbouring separate buildings within a district", Neighbours);
        yield return ("Sprawling settlement has bounded districts and actual full extent", Sprawling);
        yield return ("Building identity survives ordering and connected additions", StableAddition);
        yield return ("Construction removal updates extent and revision", Removal);
        yield return ("Building split preserves major identity without duplicate IDs", Split);
        yield return ("Building merge retains strongest overlapping identity", Merge);
        yield return ("Incomplete place survey cannot claim complete bases", Partial);
        yield return ("Place boundary contact marks a truncated building", Boundary);
        yield return ("Place ownership is never inferred from structural geometry", Ownership);
        yield return ("Qualified room and opening attach to observed building", Spaces);
        yield return ("Observed courtyard attaches to district without invented rooms", Courtyard);
        yield return ("Unknown geometry does not invent rooms or construction targets", Unknown);
        yield return ("Place snapshots are immutable across construction updates", Immutable);
        yield return ("Place evidence expires and resets at world boundaries", Freshness);
        yield return ("Place updates reject duplicate or over-budget input atomically", Invalid);
        yield return ("Dense huge construction stays bounded and explicitly partial", Dense);
        yield return ("Scenic export retains hierarchy within 16 subjects for 32 buildings", ScenicHierarchy);
        yield return ("Scenic export retains preferred children and skips oversized parents", ScenicPreferred);
    }
    private static PlaceBounds B(float x, float z = 0, float width = 4, float height = 4) =>
        new PlaceBounds(new AutonomyPosition(x, 0, z), new AutonomyPosition(x + width, height, z + 4));
    private static PlacePieceObservation P(string id, float x, float z = 0, bool built = false, bool door = false) => new PlacePieceObservation(id, B(x, z), built, door);
    private static PlaceSnapshot Update(PlaceHierarchy h, PlacePieceObservation[] pieces, double now = 1, bool complete = true,
        PlaceSpaceObservation[]? spaces = null, float radius = 112) => h.Update(pieces, spaces ?? Array.Empty<PlaceSpaceObservation>(), new AutonomyPosition(0, 0, 0), radius, complete, now);
    private static List<PlaceNode> Kind(PlaceSnapshot s, PlaceKind kind) => s.Nodes.Where(n => n.Kind == kind).ToList();
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Throws(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid observation rejection"); }
    private static void Connected()
    {
        var s = Update(new PlaceHierarchy(), new[] { P("a", 0), P("b", 4), P("c", 8) });
        var b = Kind(s, PlaceKind.Building); Check(b.Count == 1 && b[0].PieceCount == 3 && b[0].Bounds.Width == 12, "Touching pieces were split");
        Check(s.Find(b[0].ParentId)?.Kind == PlaceKind.District && s.Find(s.Find(b[0].ParentId)!.ParentId)?.Kind == PlaceKind.Settlement, "Hierarchy missing");
    }
    private static void Neighbours()
    {
        var s = Update(new PlaceHierarchy(), new[] { P("a", 0), P("b", 12) });
        Check(Kind(s, PlaceKind.Building).Count == 2 && Kind(s, PlaceKind.District).Count == 1, "Buildings merged by distance alone");
    }
    private static void Sprawling()
    {
        var pieces = new[] { P("a", -90), P("b", -60), P("c", -30), P("d", 0), P("e", 30), P("f", 60), P("g", 90) };
        var s = Update(new PlaceHierarchy(), pieces, radius: 150);
        Check(Kind(s, PlaceKind.Building).Count == 7 && Kind(s, PlaceKind.District).Count > 1, "Sprawling base collapsed to one building/district");
        var settlement = Kind(s, PlaceKind.Settlement);
        Check(settlement.Count == 1 && settlement[0].Bounds.Width == 184 && settlement[0].Bounds.Radius > 24, "Settlement extent was clamped or disconnected");
        Check(Kind(s, PlaceKind.District).All(d => d.Bounds.Width <= 70 && d.Bounds.Depth <= 70), "District span exceeded budget");
    }
    private static void StableAddition()
    {
        var h = new PlaceHierarchy(); var first = Update(h, new[] { P("b", 4), P("c", 8) });
        string id = Kind(first, PlaceKind.Building)[0].Id;
        var reordered = Update(h, new[] { P("c", 8), P("b", 4) }, 2);
        Check(reordered.Revision == first.Revision && Kind(reordered, PlaceKind.Building)[0].Id == id, "Order changed place identity");
        var extended = Update(h, new[] { P("a", 0), P("b", 4), P("c", 8) }, 3);
        Check(Kind(extended, PlaceKind.Building)[0].Id == id && extended.Revision > reordered.Revision, "New lowest piece replaced stable identity");
    }
    private static void Removal()
    {
        var h = new PlaceHierarchy(); var a = Update(h, new[] { P("a", 0), P("b", 4), P("c", 8) });
        var b = Update(h, new[] { P("a", 0), P("b", 4) }, 2);
        Check(b.Revision > a.Revision && Kind(b, PlaceKind.Building)[0].Bounds.Width == 8, "Removed geometry persisted");
    }
    private static void Split()
    {
        var h = new PlaceHierarchy(); var a = Update(h, new[] { P("a", 0), P("bridge", 4), P("b", 8), P("c", 12) });
        string id = Kind(a, PlaceKind.Building)[0].Id;
        var b = Update(h, new[] { P("a", 0), P("b", 8), P("c", 12) }, 2);
        var buildings = Kind(b, PlaceKind.Building);
        Check(buildings.Count == 2 && buildings.First(n => n.PieceCount == 2).Id == id, "Largest split lost continuity");
        Check(b.Nodes.Select(n => n.Id).Distinct().Count() == b.Nodes.Count, "Split duplicated identities");
    }
    private static void Merge()
    {
        var h = new PlaceHierarchy(); var a = Update(h, new[] { P("a", 0), P("b", 8), P("c", 12) });
        string id = Kind(a, PlaceKind.Building).First(n => n.PieceCount == 2).Id;
        var b = Update(h, new[] { P("a", 0), P("bridge", 4), P("b", 8), P("c", 12) }, 2);
        Check(Kind(b, PlaceKind.Building).Single().Id == id, "Merge ignored strongest history overlap");
    }
    private static void Partial()
    {
        var s = Update(new PlaceHierarchy(), new[] { P("a", 0), P("b", 20) }, complete: false);
        Check(!s.CoverageComplete && s.Nodes.All(n => n.IsPartial && n.Confidence < .8f), "Partial observation claimed full base");
    }
    private static void Boundary()
    {
        var s = Update(new PlaceHierarchy(), new[] { P("a", 106) });
        Check(Kind(s, PlaceKind.Building).Single().IsPartial, "Edge-truncated building claimed complete footprint");
    }
    private static void Ownership()
    {
        var s = Update(new PlaceHierarchy(), new[] { P("a", 0), P("b", 20, built: true) });
        Check(!Kind(s, PlaceKind.Building).First(n => n.Bounds.Minimum.X == 0).HasPlayerConstruction, "Ruins/unknown pieces labelled player-owned");
        Check(Kind(s, PlaceKind.Building).First(n => n.Bounds.Minimum.X == 20).HasPlayerConstruction, "Verified construction evidence lost");
    }
    private static void Spaces()
    {
        var room = new PlaceSpaceObservation("room1", PlaceKind.Room, new PlaceBounds(new AutonomyPosition(1, 0, 1), new AutonomyPosition(3, 3, 3)), .8f);
        var s = Update(new PlaceHierarchy(), new[] { P("a", 0, door: true) }, spaces: new[] { room });
        Check(Kind(s, PlaceKind.Room).Count == 1 && Kind(s, PlaceKind.Opening).Count == 1, "Measured room/opening absent");
        Check(s.FindContext(new AutonomyPosition(2, 1, 2))?.Kind == PlaceKind.Room, "Small measured room lost context to whole settlement");
        Check(s.Find(Kind(s, PlaceKind.Room)[0].ParentId)?.Kind == PlaceKind.Building, "Room detached from building");
    }
    private static void Courtyard()
    {
        var yard = new PlaceSpaceObservation("yard", PlaceKind.Courtyard, B(7, width: 3, height: 2), .75f);
        var s = Update(new PlaceHierarchy(), new[] { P("a", 0), P("b", 14) }, spaces: new[] { yard });
        var node = Kind(s, PlaceKind.Courtyard).Single();
        Check(s.Find(node.ParentId)?.Kind == PlaceKind.District && Kind(s, PlaceKind.Room).Count == 0, "Open yard became an invented interior");
    }
    private static void Unknown()
    {
        var s = Update(new PlaceHierarchy(), new[] { P("a", 0), P("b", 14) });
        Check(!s.Nodes.Any(n => n.Kind == PlaceKind.Room || n.Kind == PlaceKind.Courtyard), "Geometry extent invented semantic spaces");
        var distant = new PlaceSpaceObservation("unknown", PlaceKind.Room, B(80), .8f);
        var b = Update(new PlaceHierarchy(), new[] { P("a", 0) }, spaces: new[] { distant });
        Check(Kind(b, PlaceKind.Room).Count == 0, "Unrelated room attached to arbitrary building");
    }
    private static void Immutable()
    {
        var h = new PlaceHierarchy(); var a = Update(h, new[] { P("a", 0) });
        string id = a.Nodes[0].Id; int count = a.Nodes.Count; long revision = a.Revision;
        Update(h, new[] { P("b", 40), P("c", 44) }, 2);
        Check(a.Nodes.Count == count && a.Nodes[0].Id == id && a.Revision == revision, "Old snapshot changed under reader");
    }
    private static void Freshness()
    {
        var h = new PlaceHierarchy(); var a = Update(h, new[] { P("a", 0) });
        Check(a.Fresh(5) && !a.Fresh(20) && !a.Fresh(0), "Stale/future snapshot treated as fresh");
        h.Reset(); Check(h.Snapshot.Nodes.Count == 0 && h.Snapshot.Revision == 0, "World reset retained old places");
    }
    private static PlaceSnapshot ExportFixture()
    {
        var pieces = new PlacePieceObservation[32];
        for (int i = 0; i < pieces.Length; i++) pieces[i] = P("base:" + i, i % 8 * 20 - 72, i / 8 * 20 - 32);
        var yard = new PlaceSpaceObservation("yard", PlaceKind.Courtyard, B(-71, -31, 2, 2), .75f);
        return Update(new PlaceHierarchy(), pieces, spaces: new[] { yard });
    }
    private static void ScenicHierarchy()
    {
        var snapshot = ExportFixture();
        Check(Kind(snapshot, PlaceKind.Building).Count == 32, "Regression fixture must contain 32 buildings");
        var selected = PlaceScenicSelection.Select(snapshot, default, 80);
        var prefix = selected.Take(PlaceScenicSelection.MaximumFeatures).ToArray();
        Check(selected.Count <= 24 && prefix.Length == 16, "Scenic export attempt/feature limits were lost");
        Check(prefix.Any(n => n.Kind == PlaceKind.Settlement) && prefix.Count(n => n.Kind == PlaceKind.District) >= 2 &&
            prefix.Count(n => n.Kind == PlaceKind.Building) >= 2, "Buildings starved aggregate or child subjects");
        Check(prefix[0].Kind == PlaceKind.Settlement, "Establishing parent must precede its children");
        Check(selected.Select(n => n.Id).Distinct().Count() == selected.Count, "Export duplicated hierarchy nodes");
        var again = PlaceScenicSelection.Select(snapshot, default, 80);
        Check(selected.Select(n => n.Id).SequenceEqual(again.Select(n => n.Id)), "Unchanged export ordering was unstable");
    }
    private static void ScenicPreferred()
    {
        var snapshot = ExportFixture();
        PlaceNode yard = Kind(snapshot, PlaceKind.Courtyard).Single();
        var preferred = PlaceScenicSelection.Select(snapshot, default, 80, yard.ParentId).Take(16).ToArray();
        Check(preferred.Any(n => n.Id == yard.Id) && preferred.Any(n => n.Id == yard.ParentId), "Preferred building lost its courtyard child");
        Check(Array.FindIndex(preferred, n => n.Id == yard.ParentId) < Array.FindIndex(preferred, n => n.Id == yard.Id), "Detail preceded its parent");
        var wide = Update(new PlaceHierarchy(), new[] { P("a", -135), P("b", -90), P("c", -45), P("d", 0), P("e", 45), P("f", 90), P("g", 135) }, radius: 180);
        Check(Kind(wide, PlaceKind.Settlement).Single().Bounds.Radius > 120, "Oversized-parent fixture was too small");
        var local = PlaceScenicSelection.Select(wide, default, 160);
        Check(local.Any(n => n.Kind == PlaceKind.Building) && local.All(n => n.Bounds.Radius <= 120), "Oversized aggregate excluded its observable children");
    }
    private static void Dense()
    {
        var pieces = new PlacePieceObservation[PlaceHierarchy.MaximumPieces];
        for (int i = 0; i < pieces.Length; i++) pieces[i] = P("piece:" + i, (i % 24) * 6 - 72, (i / 24) * 6 - 66);
        var h = new PlaceHierarchy(); var first = Update(h, pieces, radius: 180);
        Check(Kind(first, PlaceKind.Building).Count == PlaceHierarchy.MaximumBuildings, "Building work was not bounded");
        Check(!first.CoverageComplete && first.Nodes.All(n => n.IsPartial), "Dropped observations claimed complete coverage");
        Array.Reverse(pieces); var second = Update(h, pieces, 2, radius: 180);
        Check(first.Revision == second.Revision && first.Nodes.Select(n => n.Id).SequenceEqual(second.Nodes.Select(n => n.Id)), "Dense input ordering destabilized history");
    }
    private static void Invalid()
    {
        var h = new PlaceHierarchy(); var a = Update(h, new[] { P("a", 0) });
        Throws(() => Update(h, new[] { P("a", 0), P("a", 4) }, 2));
        Throws(() => Update(h, new PlacePieceObservation[PlaceHierarchy.MaximumPieces + 1], 2));
        Throws(() => Update(h, new[] { P("a", 0) }, 2, spaces: new[] { default(PlaceSpaceObservation) }));
        Check(ReferenceEquals(h.Snapshot, a), "Invalid input replaced current evidence");
    }
}
