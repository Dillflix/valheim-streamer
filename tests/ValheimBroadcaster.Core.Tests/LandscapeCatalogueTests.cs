using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ValheimBroadcaster.Core;

internal static class LandscapeCatalogueTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Landscape survey finds dry shoreline when the zone centre is underwater", Shoreline);
        yield return ("Landscape survey resumes a one-query budget without restarting", Resumable);
        yield return ("Landscape survey does not infer inland from missing samples or accept underwater approaches", UnknownAndWater);
        yield return ("Landscape survey separates elevated inland from coast and remains inside its generated zone", ElevationAndBounds);
        yield return ("Optional interest v2 preserves biome landscape and source metadata", ProtocolRoundTrip);
        yield return ("Legacy interests remain wire compatible and cannot silently carry new natural kinds", LegacyProtocol);
        yield return ("Interest v2 rejects malformed metadata and source variants", MalformedProtocol);
        yield return ("Catalogue quotas preserve rare biome and coastline amid dense common landmarks", CatalogueRepresentation);
        yield return ("Interest cache ages classified evidence without discarding metadata", CacheMetadata);
    }
    private static void Eq<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
    private static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
    private static void Bad(Action action) { try { action(); } catch (ArgumentException) { return; } catch (EndOfStreamException) { return; } throw new Exception("Expected invalid input rejection"); }
    private static LandscapeSurvey Survey(params float[] values)
    { var survey = new LandscapeSurvey(); foreach (float height in values) survey.Observe(height); return survey; }
    private static void Shoreline()
    {
        var s = Survey(20, 35, 20, 20, 20, 36, 36, 20, 20);
        Assert(s.TryDestination(30, out ObserverPoint p, out ScenicLandscape type)); Eq(ScenicLandscape.Coast, type);
        Eq(24f, p.X); Eq(0f, p.Z); Eq(43f, p.Y); // Ground 35, safely above water; never original underwater centre.
    }
    private static void Resumable()
    {
        var s = new LandscapeSurvey();
        for (int tick = 0; tick < LandscapeSurvey.SampleCount; tick++)
        {
            Eq(tick, s.NextSample); Assert(!s.Complete); Assert(!s.TryDestination(30, out _, out _));
            s.Observe(tick == 0 ? 20 : 35);
        }
        Assert(s.Complete); Assert(s.TryDestination(30, out _, out ScenicLandscape type)); Eq(ScenicLandscape.Coast, type);
        try { s.Observe(35); } catch (InvalidOperationException) { return; } throw new Exception("Survey accepted extra work");
    }
    private static void UnknownAndWater()
    {
        Assert(!Survey(20, 20, 20, 20, 20, 20, 20, 20, 20).TryDestination(30, out _, out _));
        Assert(!Survey(32, 32, 32, 32, 32, 32, 32, 32, 32).TryDestination(30, out _, out _));
        var missing = Survey(35, float.NaN, 35, 35, 35, 35, 35, 35, 35);
        Assert(missing.TryDestination(30, out _, out ScenicLandscape type)); Eq(ScenicLandscape.Unknown, type);
        Assert(!Survey(float.NaN, float.PositiveInfinity, 990, 999, 30, 30, 30, 30, 30).TryDestination(30, out _, out _));
    }
    private static void ElevationAndBounds()
    {
        Assert(Survey(150, 150, 150, 150, 150, 150, 150, 150, 150).TryDestination(30, out _, out ScenicLandscape high)); Eq(ScenicLandscape.Highland, high);
        Assert(Survey(50, 50, 50, 50, 50, 50, 50, 50, 50).TryDestination(30, out _, out ScenicLandscape inland)); Eq(ScenicLandscape.Inland, inland);
        for (int i = 0; i < LandscapeSurvey.SampleCount; i++) { var p = LandscapeSurvey.Offset(i); Assert(Math.Abs(p.X) < 32 && Math.Abs(p.Z) < 32); }
        Bad(() => LandscapeSurvey.Offset(-1)); Bad(() => LandscapeSurvey.Offset(9));
    }
    private static WorldInterestSite Site(string id, string biome, ScenicLandscape landscape, WorldInterestKind kind = WorldInterestKind.Natural) =>
        new WorldInterestSite(id, "", biome, kind, new ObserverPoint(500, 40, 500), new ObserverPoint(24, 8, 24), new ObserverPoint(500, 48, 500), 0, 50,
            biome: biome, landscape: landscape);
    private static WorldInterestPage Page(params WorldInterestSite[] sites) => new WorldInterestPage(7, 1, 1, 0, 1, true, true, sites);
    private static void ProtocolRoundTrip()
    {
        var page = Page(Site("1:1", "Meadows", ScenicLandscape.Coast), Site("base:1", "Mountain", ScenicLandscape.Unknown, WorldInterestKind.Base));
        var decoded = WorldInterestProtocol.DecodeV2(WorldInterestProtocol.EncodeV2(page));
        Eq("Meadows", decoded.Sites[0].Biome); Eq(ScenicLandscape.Coast, decoded.Sites[0].Landscape); Eq(WorldInterestKind.Natural, decoded.Sites[0].Kind);
        Eq(WorldInterestKind.Base, decoded.Sites[1].Kind); Eq("Mountain", decoded.Sites[1].Biome);
        var feedback = new DestinationFeedback(7, 1, "base:1", DestinationOutcome.SubjectUnavailable, 0);
        Eq(DestinationOutcome.SubjectUnavailable, WorldInterestProtocol.DecodeFeedback(WorldInterestProtocol.Encode(feedback)).Outcome);
    }
    private static void LegacyProtocol()
    {
        var page = Page(Site("base:1", "Mountain", ScenicLandscape.Highland, WorldInterestKind.Base));
        byte[] legacy = WorldInterestProtocol.Encode(page);
        var read = WorldInterestProtocol.Decode(legacy); Eq("", read.Sites[0].Biome); Eq(ScenicLandscape.Unknown, read.Sites[0].Landscape);
        Eq(1, BitConverter.ToInt32(legacy, 4)); Eq(2, BitConverter.ToInt32(WorldInterestProtocol.EncodeV2(page), 4));
        Bad(() => WorldInterestProtocol.DecodeV2(legacy)); Bad(() => WorldInterestProtocol.Decode(WorldInterestProtocol.EncodeV2(page)));
        Bad(() => WorldInterestProtocol.Encode(Page(Site("1:1", "Meadows", ScenicLandscape.Coast))));
    }
    private static void MalformedProtocol()
    {
        byte[] bytes = WorldInterestProtocol.EncodeV2(Page(Site("1:1", "Meadows", ScenicLandscape.Coast)));
        for (int i = 0; i < bytes.Length; i++) { byte[] shortPacket = bytes.Take(i).ToArray(); Bad(() => WorldInterestProtocol.DecodeV2(shortPacket)); }
        byte[] unknown = (byte[])bytes.Clone(); unknown[unknown.Length - 1] = 255; Bad(() => WorldInterestProtocol.DecodeV2(unknown));
        Bad(() => WorldInterestProtocol.DecodeV2(bytes.Concat(new byte[] { 1 }).ToArray()));
        Bad(() => Site("1:1", new string('b', 33), ScenicLandscape.Coast));
        Bad(() => Site("1:1", "Meadows", (ScenicLandscape)99));
    }
    private static void CatalogueRepresentation()
    {
        var many = Enumerable.Range(0, 500).Select(i => Site("landmark:" + i, "BlackForest", ScenicLandscape.Unknown, WorldInterestKind.Landmark))
            .Concat(new[] { Site("coast", "Meadows", ScenicLandscape.Coast), Site("high", "Mountain", ScenicLandscape.Highland), Site("base", "Plains", ScenicLandscape.Unknown, WorldInterestKind.Base) }).ToArray();
        var selected = WorldInterestDistribution.Take(many, 16);
        Eq(16, selected.Count); Assert(selected.Any(s => s.Id == "coast") && selected.Any(s => s.Id == "high") && selected.Any(s => s.Id == "base"));
        Eq(string.Join(",", selected.Select(s => s.Id)), string.Join(",", WorldInterestDistribution.Take(many.Reverse(), 16).Select(s => s.Id)));
        Eq(0, WorldInterestDistribution.Take(many, 0).Count); Bad(() => WorldInterestDistribution.Take(many, 1025));
    }
    private static void CacheMetadata()
    {
        var c = new WorldInterestCache(); c.Accept(Page(Site("1:1", "Meadows", ScenicLandscape.Coast)), 10);
        var aged = c.Available(20).Single(); Eq(10f, aged.Age); Eq("Meadows", aged.Biome); Eq(ScenicLandscape.Coast, aged.Landscape);
    }
}
