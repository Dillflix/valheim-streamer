using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class DestinationSubjectCoverageTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Destination intent requires measured structures within the nominated footprint", MatchEvidence);
        yield return ("Tiny ruin fragments cannot satisfy a broad destination visit", MeaningfulScale);
        yield return ("Destination subject coverage counts actual matching live output", ActualCoverage);
        yield return ("Destination intent survives same-ID refresh and clears on world/subject reset", IntentLifetime);
        yield return ("Subject search is bounded and fallback footage cannot fulfil it", BoundedFallback);
        yield return ("Subject timing excludes held/off-air intervals and caps discontinuities", PresentationTiming);
        yield return ("Satisfied structural coverage releases editorial priority", CompletedSequence);
        yield return ("Close-up details cannot substitute for an establishing view of a destination", ContextBeforeDetail);
        yield return ("Broad settlement visits identify local observed coverage without claiming the whole settlement", LocalSettlementScope);
        yield return ("Coastal destination intent requires an actually observed coastline", ObservedCoast);
        yield return ("Nearby inland scenery cannot fulfil a coastal visit", CoastFallback);
    }
    private static WorldInterestSite Site(string id = "landmark:1", float x = 0, float extent = 30) =>
        new WorldInterestSite(id, "", "Ruins", WorldInterestKind.Landmark,
            new ObserverPoint(x, 10, 0), new ObserverPoint(extent, 8, extent), new ObserverPoint(x + 24, 15, 0), 0, 50);
    private static ScenicFeature Feature(string id = "ruin", ScenicFeatureKind kind = ScenicFeatureKind.Structure,
        float x = 0, float radius = 12, float y = 10, string? scope = "Building", bool partial = false) =>
        new ScenicFeature(id, kind, new AutonomyPosition(x, y, 0), new AutonomyPosition(1, 0, 0), radius, 10, .8f,
            scopeLabel: scope, isPartial: partial);
    private static void Eq<T>(T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) throw new Exception($"Expected {a}, got {b}"); }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static void Feed(DestinationSubjectCoverage c, double from, double seconds, ScenicFeature? feature, bool usable = true, bool onAir = true, bool held = false, ScenicShotRole role = ScenicShotRole.Establish)
    { for (int i = 0; i <= (int)(seconds * 10); i++) c.Observe(from + i * .1, onAir, usable, held, feature, role); }
    private static void MatchEvidence()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Site());
        Assert(c.Matches(Feature()));
        Assert(!c.Matches(Feature(kind: ScenicFeatureKind.Rock)));
        Assert(!c.Matches(Feature(kind: ScenicFeatureKind.Landscape)));
        Assert(!c.Matches(Feature(x: 80))); Assert(!c.Matches(Feature(y: 100)));
        Assert(!c.Matches(Feature(scope: "Room"))); Assert(!c.Matches(Feature(scope: "Opening")));
        Assert(!c.Matches(null));
    }
    private static void MeaningfulScale()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Site());
        Assert(c.Matches(Feature(radius: 2))); Assert(!c.Meaningful(Feature(radius: 2)));
        Assert(c.Meaningful(Feature(radius: 8)));
        c.Set(Site("tiny", extent: 3)); Assert(c.Meaningful(Feature(radius: 2)));
    }
    private static void ActualCoverage()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Site());
        Feed(c, 0, 5, Feature(), usable: false); Eq(0d, c.ShownSeconds);
        Feed(c, 5, 5, Feature(kind: ScenicFeatureKind.Rock)); Eq(0d, c.ShownSeconds);
        Feed(c, 10, 5, Feature(x: 100)); Eq(0d, c.ShownSeconds);
        Feed(c, 15, 5, Feature(radius: 2)); Eq(0d, c.ShownSeconds);
        Feed(c, 20, 15.1, Feature()); Assert(c.Shown); Assert(c.Satisfied);
    }
    private static void IntentLifetime()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Site()); Feed(c, 0, 5, Feature());
        double seconds = c.ShownSeconds; Assert(!c.Set(Site())); Eq(seconds, c.ShownSeconds);
        Assert(c.Set(Site("next"))); Eq(0d, c.ShownSeconds); Assert(!c.Shown);
        c.Set(null); Assert(c.Site == null); Assert(!c.Prioritize); Assert(!c.Unavailable);
        c.Set(Site()); c.Reset(); Assert(c.Site == null);
    }
    private static void BoundedFallback()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Site()); Feed(c, 0, 30.1, Feature(kind: ScenicFeatureKind.Landscape));
        Assert(c.Unavailable); Assert(!c.Prioritize); Assert(!c.Satisfied); Eq(0d, c.ShownSeconds);
        // A later actually readable subject may recover without blessing any
        // previous generic scenery as coverage of the requested landmark.
        c.Observe(30.2, true, true, false, Feature()); Assert(c.Shown); Assert(!c.Unavailable);
    }
    private static void PresentationTiming()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Site()); Feed(c, 0, 5, Feature(), held: true);
        Eq(0d, c.ShownSeconds); Eq(0d, c.SearchElapsed);
        Feed(c, 5, 5, Feature(), onAir: false); Eq(0d, c.ShownSeconds);
        c.Observe(1000, true, true, false, Feature()); Assert(c.ShownSeconds <= .25);
        c.Observe(1, true, true, false, Feature()); Assert(c.Site == null);
    }
    private static void ContextBeforeDetail()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Site());
        Feed(c, 0, 20, Feature(), role: ScenicShotRole.Detail); Eq(0d, c.ShownSeconds); Assert(!c.Shown); Assert(!c.Satisfied);
        Feed(c, 20, 6.1, Feature()); Assert(c.EstablishedSeconds >= 6);
        Feed(c, 26.1, 9.1, Feature(radius: 2), role: ScenicShotRole.Detail); Assert(c.Satisfied);
    }
    private static WorldInterestSite Coast() => new WorldInterestSite("coast:1", "", "Meadows coast", WorldInterestKind.Natural,
        new ObserverPoint(0, 10, 0), new ObserverPoint(24, 8, 24), new ObserverPoint(0, 15, 0), 0, 50,
        biome: "Meadows", landscape: ScenicLandscape.Coast);
    private static void ObservedCoast()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Coast());
        var feature = Feature(kind: ScenicFeatureKind.Coastline, radius: 18, scope: null);
        Assert(c.Meaningful(feature)); Feed(c, 0, 15.1, feature); Assert(c.Satisfied);
        Eq("Observed coastline at destination", c.CoverageScope);
    }
    private static void CoastFallback()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Coast());
        Assert(!c.Matches(Feature()));
        Feed(c, 0, 30.1, Feature(kind: ScenicFeatureKind.Landscape)); Assert(c.Unavailable); Eq(0d, c.ShownSeconds);
        Assert(!c.Matches(Feature(kind: ScenicFeatureKind.Coastline, x: 100)));
    }
    private static void LocalSettlementScope()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Site(extent: 150)); Feed(c, 0, 6.1, Feature());
        Eq("Local Building at destination", c.CoverageScope);
        Assert(!c.Satisfied);
    }
    private static void CompletedSequence()
    {
        var c = new DestinationSubjectCoverage(); c.Set(Site()); var f = Feature();
        Assert(c.Priority(f) > c.Priority(Feature(radius: 2)));
        Assert(c.Priority(f) > c.Priority(Feature(kind: ScenicFeatureKind.Coastline)));
        Feed(c, 0, 15.1, f); Assert(c.Satisfied); Eq(0d, c.Priority(f)); Assert(!c.Prioritize);
    }
}
