using System;
using System.Collections.Generic;
using System.IO;
using ValheimBroadcaster.Core;

internal static class ObserverContextTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Optional context preserves sample freshness and health-loss evidence", RoundTrip);
        yield return ("Context rejects truncated and oversized wire payloads", InvalidEnvelope);
        yield return ("Context rejects unbounded ages, velocities and duplicate peers", Bounds);
        yield return ("Repeated context packets cannot refresh stale player observations", SampleFreshness);
        yield return ("Context never binds another character or world to current evidence", Identity);
        yield return ("Unavailable or disabled optional context remains empty", OptionalFallback);
        yield return ("Health loss expires independently of continued state reports", HealthLossExpiry);
    }
    private static ObserverPlayerContext Player(float stateAge = 0, float metadataAge = 0, float? healthLossAge = 0, string character = "123:456") =>
        new ObserverPlayerContext("123", character, new ObserverPoint(12, 30, 4), new ObserverPoint(2, 0, -1), stateAge, metadataAge, healthLossAge);
    private static ObserverContextSnapshot Snapshot(long sequence = 1, ObserverPlayerContext? player = null, long world = 42) =>
        new ObserverContextSnapshot(sequence, world, true, new[] { player ?? Player() });
    private static void Check(bool ok) { if (!ok) throw new Exception("Context assertion failed."); }
    private static void Reject(Action run)
    {
        try { run(); }
        catch (ArgumentException) { return; }
        catch (EndOfStreamException) { return; }
        throw new Exception("Malformed context accepted.");
    }
    private static void RoundTrip()
    {
        ObserverContextSnapshot copy = ObserverContextProtocol.Decode(ObserverContextProtocol.Encode(Snapshot(7, Player(3, 1, 2))));
        Check(copy.Sequence == 7 && copy.WorldId == 42 && copy.Enabled && copy.Players.Count == 1);
        ObserverPlayerContext item = copy.Players[0];
        Check(item.CharacterId == "123:456" && item.Position.Y == 30 && item.Velocity!.Value.X == 2);
        Check(item.StateAgeSeconds == 3 && item.MetadataAgeSeconds == 1 && item.HealthLossAgeSeconds == 2);
        Check(item.Fresh(6.9) && !item.Fresh(7));
    }
    private static void InvalidEnvelope()
    {
        byte[] bytes = ObserverContextProtocol.Encode(Snapshot());
        for (int length = 0; length < bytes.Length; length++)
        { var partial = new byte[length]; Array.Copy(bytes, partial, length); Reject(() => ObserverContextProtocol.Decode(partial)); }
        Reject(() => ObserverContextProtocol.Decode(new byte[ObserverContextProtocol.MaxPacketBytes + 1]));
        byte[] version = (byte[])bytes.Clone(); version[4] = 99; Reject(() => ObserverContextProtocol.Decode(version));
        byte[] flag = (byte[])bytes.Clone(); flag[24] = 2; Reject(() => ObserverContextProtocol.Decode(flag));
        byte[] count = (byte[])bytes.Clone(); count[25] = 255; count[26] = 255; Reject(() => ObserverContextProtocol.Decode(count));
        var extra = new byte[bytes.Length + 1]; bytes.CopyTo(extra, 0); Reject(() => ObserverContextProtocol.Decode(extra));
    }
    private static void Bounds()
    {
        Reject(() => Player(float.NaN)); Reject(() => Player(-1)); Reject(() => Player(0, float.PositiveInfinity));
        Reject(() => Player(0, 0, 3601)); Reject(() => Player(character: ""));
        Reject(() => new ObserverPlayerContext("1", "1:1", new ObserverPoint(0, 0, 0), new ObserverPoint(31, 0, 0), 0, 0, null));
        Reject(() => new ObserverContextSnapshot(1, 42, true, new[] { Player(), Player() }));
        Reject(() => new ObserverContextSnapshot(1, 42, false, new[] { Player() }));
    }
    private static void SampleFreshness()
    {
        var cache = new ObserverContextCache();
        Check(cache.Accept(Snapshot(1, Player(8, 1)), 100));
        Check(cache.Find("123", "123:456", 101) != null);
        Check(cache.Find("123", "123:456", 102) == null);
        Check(cache.Accept(Snapshot(2, Player(12, 1)), 104));
        Check(cache.Find("123", "123:456", 104) == null);
        cache.Accept(Snapshot(3, Player(0, 10)), 105);
        Check(cache.Find("123", "123:456", 105) == null);
        Check(!cache.Accept(Snapshot(2), 106));
        Check(cache.Find("123", "123:456", 106) == null);
    }
    private static void Identity()
    {
        var cache = new ObserverContextCache(); cache.Accept(Snapshot(), 1);
        Check(cache.Find("123", "123:999", 1) == null && cache.Find("999", "123:456", 1) == null);
        Reject(() => cache.Accept(Snapshot(2, world: 43), 2));
        Reject(() => cache.Accept(Snapshot(2), 0));
        cache.Reset(); Check(cache.Find("123", "123:456", 2) == null);
        cache.Accept(Snapshot(1, world: 43), 3); Check(cache.Find("123", "123:456", 3) != null);
    }
    private static void OptionalFallback()
    {
        var cache = new ObserverContextCache();
        Check(cache.Snapshot == null && cache.Find("123", "123:456", 10000) == null);
        cache.Accept(new ObserverContextSnapshot(1, 42, false, Array.Empty<ObserverPlayerContext>()), 0);
        Check(cache.Find("123", "123:456", 1) == null);
        cache.Accept(Snapshot(2), 2); Check(cache.Find("123", "123:456", 2) != null);
        cache.Accept(new ObserverContextSnapshot(3, 42, false, Array.Empty<ObserverPlayerContext>()), 3);
        Check(cache.Find("123", "123:456", 3) == null);
    }
    private static void HealthLossExpiry()
    {
        Check(Player(0, 0, 4).RecentHealthLoss(1.9));
        Check(!Player(0, 0, 4).RecentHealthLoss(2));
        Check(!Player(0, 0, null).RecentHealthLoss(0));
        Check(!Player(10, 0, 0).RecentHealthLoss(0));
        // New ordinary state packets may be fresh while old damage evidence expires.
        var cache = new ObserverContextCache(); cache.Accept(Snapshot(1, Player(0, 0, 0)), 0);
        cache.Accept(Snapshot(2, Player(0, 0, 7)), 7);
        Check(cache.Find("123", "123:456", 7) != null && !cache.Find("123", "123:456", 7)!.RecentHealthLoss(0));
    }
}
