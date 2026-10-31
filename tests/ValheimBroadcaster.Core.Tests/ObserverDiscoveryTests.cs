using System;
using System.Collections.Generic;
using System.IO;
using ValheimBroadcaster.Core;

internal static class ObserverDiscoveryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Discovery round trip preserves identities and Unicode", RoundTrip);
        yield return ("Discovery malformed sizes and versions are rejected", InvalidEnvelope);
        yield return ("Discovery rejects oversized text before allocation", InvalidTextLength);
        yield return ("Discovery rejects nonfinite coordinates and false spawned destinations", InvalidDestinations);
        yield return ("Discovery bounds inventory counts and rejects duplicate identities", InventoryBounds);
        yield return ("Discovery freshness cannot be extended by replayed sequence", Freshness);
        yield return ("Discovery world changes require an explicit connection reset", WorldReset);
        yield return ("Discovery region pages accumulate within a strict bound", RegionCache);
        yield return ("Disabling discovery clears previously disclosed destinations", Disabled);
        yield return ("Discovery rejects invalid and backwards receipt clocks", Clock);
    }
    private static ObserverDiscoverySnapshot Sample(long sequence = 1, long world = 42) =>
        new ObserverDiscoverySnapshot(sequence, world, true, true,
            new[] { new ObserverPlayer("123", "123:456", "Jötunn 🦌", new ObserverPoint(10, 20, -30), true), new ObserverPlayer("124", "", "Joining", null, false) },
            new[] { new ObserverRegion("0:0", "Meadows", new ObserverPoint(0, 30, 0)) });
    private static void Check(bool condition) { if (!condition) throw new Exception("Discovery assertion failed."); }
    private static void Reject(Action run)
    {
        try { run(); }
        catch (ArgumentException) { return; }
        catch (EndOfStreamException) { return; }
        throw new Exception("Malformed discovery input was accepted.");
    }
    private static void RoundTrip()
    {
        ObserverDiscoverySnapshot copy = ObserverDiscoveryProtocol.Decode(ObserverDiscoveryProtocol.Encode(Sample()));
        Check(copy.Sequence == 1 && copy.WorldId == 42 && copy.Players.Count == 2 && copy.Regions.Count == 1);
        Check(copy.Players[0].Name == "Jötunn 🦌" && copy.Players[0].CharacterId == "123:456" && copy.Players[0].Position!.Value.Z == -30);
        Check(!copy.Players[1].Position.HasValue && !copy.Players[1].IsSpawned);
    }
    private static void InvalidEnvelope()
    {
        byte[] bytes = ObserverDiscoveryProtocol.Encode(Sample());
        Reject(() => ObserverDiscoveryProtocol.Decode(new byte[ObserverDiscoveryProtocol.MaxPacketBytes + 1]));
        for (int length = 0; length < bytes.Length; length++)
        { var partial = new byte[length]; Array.Copy(bytes, partial, length); Reject(() => ObserverDiscoveryProtocol.Decode(partial)); }
        byte[] version = (byte[])bytes.Clone(); version[4] = 99; Reject(() => ObserverDiscoveryProtocol.Decode(version));
        byte[] flag = (byte[])bytes.Clone(); flag[24] = 2; Reject(() => ObserverDiscoveryProtocol.Decode(flag));
        var extra = new byte[bytes.Length + 1]; bytes.CopyTo(extra, 0); Reject(() => ObserverDiscoveryProtocol.Decode(extra));
    }
    private static void InvalidTextLength()
    {
        byte[] bytes = ObserverDiscoveryProtocol.Encode(Sample());
        bytes[28] = 255; bytes[29] = 255;
        Reject(() => ObserverDiscoveryProtocol.Decode(bytes));
        Reject(() => new ObserverPlayer("1", "1:1", new string('x', 65), null, true));
        Reject(() => new ObserverPlayer("1", "1:1", "bad\nname", null, true));
    }
    private static void InvalidDestinations()
    {
        Reject(() => new ObserverPoint(float.NaN, 0, 0)); Reject(() => new ObserverPoint(0, float.PositiveInfinity, 0));
        Reject(() => new ObserverPoint(20001, 0, 0));
        Reject(() => new ObserverPlayer("1", "", "x", new ObserverPoint(0, 0, 0), true));
        Reject(() => new ObserverPlayer("1", "1:1", "x", new ObserverPoint(0, 0, 0), false));
    }
    private static void InventoryBounds()
    {
        byte[] bytes = ObserverDiscoveryProtocol.Encode(Sample()); bytes[26] = 255; bytes[27] = 255;
        Reject(() => ObserverDiscoveryProtocol.Decode(bytes));
        ObserverPlayer item = Sample().Players[0];
        Reject(() => new ObserverDiscoverySnapshot(1, 42, true, true, new[] { item, item }, Array.Empty<ObserverRegion>()));
        var regions = new List<ObserverRegion>();
        for (int i = 0; i <= ObserverDiscoveryProtocol.MaxRegions; i++) regions.Add(new ObserverRegion(i.ToString(), "Meadows", new ObserverPoint(0, 30, 0)));
        Reject(() => ObserverDiscoveryProtocol.Encode(new ObserverDiscoverySnapshot(1, 42, true, true, Array.Empty<ObserverPlayer>(), regions)));
    }
    private static void Freshness()
    {
        var cache = new ObserverDiscoveryCache(); Check(!cache.Fresh(0));
        Check(cache.Accept(Sample(), 1)); Check(cache.Fresh(10.9)); Check(!cache.Fresh(11));
        Check(!cache.Accept(Sample(), 11)); Check(!cache.Fresh(11));
        Check(cache.Accept(Sample(2), 12)); Check(cache.Fresh(12)); Check(!cache.Fresh(11));
    }
    private static void WorldReset()
    {
        var cache = new ObserverDiscoveryCache(); cache.Accept(Sample(), 1);
        Reject(() => cache.Accept(Sample(2, 43), 2)); Check(cache.Snapshot!.WorldId == 42);
        cache.Reset(); Check(cache.Snapshot == null && !cache.Fresh(2));
        Check(cache.Accept(Sample(1, 43), 2));
    }
    private static void RegionCache()
    {
        var cache = new ObserverDiscoveryCache();
        for (int page = 0; page < 10; page++)
        {
            var regions = new List<ObserverRegion>();
            for (int i = 0; i < 128; i++) regions.Add(new ObserverRegion((page * 128 + i).ToString(), "Meadows", new ObserverPoint(0, 30, 0)));
            cache.Accept(new ObserverDiscoverySnapshot(page + 1, 42, true, true, Array.Empty<ObserverPlayer>(), regions), page);
            Check(cache.Snapshot!.Regions.Count <= ObserverDiscoveryProtocol.MaxCachedRegions);
        }
        Check(cache.Snapshot!.Regions.Count == ObserverDiscoveryProtocol.MaxCachedRegions);
    }
    private static void Disabled()
    {
        var cache = new ObserverDiscoveryCache(); cache.Accept(Sample(), 1);
        cache.Accept(new ObserverDiscoverySnapshot(2, 42, false, false, Array.Empty<ObserverPlayer>(), Array.Empty<ObserverRegion>()), 2);
        Check(cache.Snapshot!.Regions.Count == 0 && cache.Snapshot.Players.Count == 0 && !cache.Snapshot.DiscoveryEnabled);
        Reject(() => new ObserverDiscoverySnapshot(3, 42, false, false, Sample().Players, Array.Empty<ObserverRegion>()));
    }
    private static void Clock()
    {
        var cache = new ObserverDiscoveryCache();
        Reject(() => cache.Accept(Sample(), double.NaN)); Reject(() => cache.Accept(Sample(), double.PositiveInfinity));
        cache.Accept(Sample(), 10); Reject(() => cache.Accept(Sample(2), 9)); Check(cache.Snapshot!.Sequence == 1);
    }
}
