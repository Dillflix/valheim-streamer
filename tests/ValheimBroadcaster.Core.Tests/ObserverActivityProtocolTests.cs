using System;
using System.Collections.Generic;
using System.IO;
using ValheimBroadcaster.Core;

internal static class ObserverActivityProtocolTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Activity round trip preserves complete cohort and unknown unspawned members", RoundTrip);
        yield return ("Activity rejects truncated, trailing and oversized packets", InvalidEnvelope);
        yield return ("Activity rejects malformed flags, versions and nonfinite wire ages", MalformedFields);
        yield return ("Activity rejects oversized and invalid UTF8 identity text", InvalidText);
        yield return ("Activity bounds durations and refuses idle claims for unknown characters", Bounds);
        yield return ("Activity bounds cohorts and rejects duplicate player or character identities", CohortBounds);
        yield return ("Activity cache cannot use incomplete or disabled cohorts", CompleteCohort);
        yield return ("Activity sample and heartbeat freshness expire independently of receipt", SourceFreshness);
        yield return ("Activity receipt time never extends an observed idle interval", NoIdleExtrapolation);
        yield return ("Activity replay cannot refresh a cached observation", Replay);
        yield return ("Activity evidence is bound to character and connection world", Identity);
        yield return ("Activity cache rejects invalid clocks and requires explicit reset", Clock);
    }

    private static ObserverPlayerActivity Player(string id = "123", string character = "123:456", float idle = 60,
        float sampleAge = 0, float heartbeatAge = 0) => new ObserverPlayerActivity(id, character, true, idle, sampleAge, heartbeatAge);
    private static ObserverActivitySnapshot Snapshot(long sequence = 1, ObserverPlayerActivity? player = null, long world = 42) =>
        new ObserverActivitySnapshot(sequence, world, true, true, new[] { player ?? Player() });
    private static void Check(bool condition) { if (!condition) throw new Exception("Activity assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (EndOfStreamException) { return; }
        throw new Exception("Malformed observer activity accepted.");
    }

    private static void RoundTrip()
    {
        var source = new List<ObserverPlayerActivity>
        {
            Player("Jötunn 🦌", "123:456", 81, 1, 2),
            new ObserverPlayerActivity("124", "", false, 0, 3600, 0)
        };
        var original = new ObserverActivitySnapshot(9, 42, true, true, source);
        source.Clear(); // The published cohort must not change behind the cache.
        ObserverActivitySnapshot copy = ObserverActivityProtocol.Decode(ObserverActivityProtocol.Encode(original));
        Check(copy.Sequence == 9 && copy.WorldId == 42 && copy.Enabled && copy.Complete && copy.Players.Count == 2);
        Check(copy.Players[0].Id == "Jötunn 🦌" && copy.Players[0].ObservedIdleSeconds == 81);
        Check(copy.Players[0].SampleAgeSeconds == 1 && copy.Players[0].HeartbeatAgeSeconds == 2);
        Check(!copy.Players[1].Known && copy.Players[1].CharacterId == "" && !copy.Players[1].Fresh(0));
        var empty = new ObserverActivitySnapshot(10, 42, true, true, Array.Empty<ObserverPlayerActivity>());
        Check(ObserverActivityProtocol.Decode(ObserverActivityProtocol.Encode(empty)).Complete);
    }

    private static void InvalidEnvelope()
    {
        byte[] bytes = ObserverActivityProtocol.Encode(Snapshot());
        for (int length = 0; length < bytes.Length; length++)
        {
            var partial = new byte[length]; Array.Copy(bytes, partial, length);
            Reject(() => ObserverActivityProtocol.Decode(partial));
        }
        Reject(() => ObserverActivityProtocol.Decode(new byte[ObserverActivityProtocol.MaxPacketBytes + 1]));
        var trailing = new byte[bytes.Length + 1]; bytes.CopyTo(trailing, 0);
        Reject(() => ObserverActivityProtocol.Decode(trailing));
        Reject(() => ObserverActivityProtocol.Decode(null!));
    }

    private static void MalformedFields()
    {
        byte[] bytes = ObserverActivityProtocol.Encode(Snapshot());
        foreach (int offset in new[] { 0, 4, 24, 25, FlagOffset(bytes, 0) })
        {
            byte[] bad = (byte[])bytes.Clone(); bad[offset] = 99;
            Reject(() => ObserverActivityProtocol.Decode(bad));
        }
        byte[] sequence = (byte[])bytes.Clone(); Array.Clear(sequence, 8, 8);
        Reject(() => ObserverActivityProtocol.Decode(sequence));
        byte[] count = (byte[])bytes.Clone(); count[26] = count[27] = 255;
        Reject(() => ObserverActivityProtocol.Decode(count));
        int fields = FlagOffset(bytes, 0) + 1;
        for (int field = 0; field < 3; field++)
        {
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
            {
                byte[] bad = (byte[])bytes.Clone();
                BitConverter.GetBytes(invalid).CopyTo(bad, fields + field * 4);
                Reject(() => ObserverActivityProtocol.Decode(bad));
            }
        }
        byte[] unknown = (byte[])bytes.Clone(); unknown[FlagOffset(bytes, 0)] = 0;
        Reject(() => ObserverActivityProtocol.Decode(unknown)); // Known idle=60 cannot be relabeled unknown.
    }

    private static void InvalidText()
    {
        byte[] bytes = ObserverActivityProtocol.Encode(Snapshot());
        byte[] large = (byte[])bytes.Clone(); large[28] = large[29] = 255;
        Reject(() => ObserverActivityProtocol.Decode(large));
        byte[] utf8 = (byte[])bytes.Clone(); utf8[30] = 255;
        Reject(() => ObserverActivityProtocol.Decode(utf8));
        byte[] control = (byte[])bytes.Clone(); control[30] = 10;
        Reject(() => ObserverActivityProtocol.Decode(control));
        Reject(() => Player(new string('x', 65)));
        Reject(() => Player(character: new string('x', 65)));
        Reject(() => Player("")); Reject(() => Player(character: ""));
    }

    private static void Bounds()
    {
        Reject(() => Player(idle: -1)); Reject(() => Player(idle: float.NaN)); Reject(() => Player(idle: 86401));
        Reject(() => Player(sampleAge: 3601)); Reject(() => Player(heartbeatAge: 3601));
        Reject(() => Player(heartbeatAge: float.NegativeInfinity));
        Reject(() => new ObserverPlayerActivity("123", "", false, 1, 0, 0));
        Check(Player(idle: 86400, sampleAge: 3600, heartbeatAge: 3600).ObservedIdleSeconds == 86400);
        Check(!Player().Fresh(-1) && !Player().Fresh(double.NaN) && !Player().Fresh(double.PositiveInfinity));
    }

    private static void CohortBounds()
    {
        var players = new List<ObserverPlayerActivity>();
        for (int i = 0; i < ObserverActivityProtocol.MaxPlayers; i++) players.Add(Player(i.ToString(), i + ":1"));
        Check(ObserverActivityProtocol.Decode(ObserverActivityProtocol.Encode(new ObserverActivitySnapshot(1, 42, true, true, players))).Players.Count == 64);
        players.Add(Player("extra", "extra:1"));
        Reject(() => new ObserverActivitySnapshot(1, 42, true, false, players));
        Reject(() => new ObserverActivitySnapshot(1, 42, true, true, new[] { Player(), Player() }));
        Reject(() => new ObserverActivitySnapshot(1, 42, true, true, new[] { Player(), Player("124") }));
        Reject(() => new ObserverActivitySnapshot(1, 42, true, true, new ObserverPlayerActivity[] { null! }));
        byte[] bytes = ObserverActivityProtocol.Encode(new ObserverActivitySnapshot(1, 42, true, true,
            new[] { Player("123", "123:456"), Player("124", "124:456") }));
        int nextId = FlagOffset(bytes, 0) + 13;
        byte[] duplicateId = (byte[])bytes.Clone(); duplicateId[nextId + 4] = (byte)'3';
        Reject(() => ObserverActivityProtocol.Decode(duplicateId));
        int nextCharacter = nextId + 2 + 3 + 2;
        byte[] duplicateCharacter = (byte[])bytes.Clone(); duplicateCharacter[nextCharacter + 2] = (byte)'3';
        Reject(() => ObserverActivityProtocol.Decode(duplicateCharacter));
    }

    private static void CompleteCohort()
    {
        var cache = new ObserverActivityCache(); Check(cache.Snapshot == null && !cache.Fresh(0));
        cache.Accept(new ObserverActivitySnapshot(1, 42, true, false, new[] { Player() }), 0);
        Check(!cache.Fresh(0) && cache.Find("123", "123:456", 0) == null);
        cache.Accept(Snapshot(2), 1); Check(cache.Fresh(1) && cache.Find("123", "123:456", 1) != null);
        cache.Accept(new ObserverActivitySnapshot(3, 42, false, false, Array.Empty<ObserverPlayerActivity>()), 2);
        Check(!cache.Fresh(2) && cache.Find("123", "123:456", 2) == null);
        Reject(() => new ObserverActivitySnapshot(1, 42, false, true, Array.Empty<ObserverPlayerActivity>()));
        Reject(() => new ObserverActivitySnapshot(1, 42, false, false, new[] { Player() }));
    }

    private static void SourceFreshness()
    {
        var cache = new ObserverActivityCache(); cache.Accept(Snapshot(1, Player(sampleAge: 5)), 100);
        Check(cache.Find("123", "123:456", 100.9) != null && cache.Find("123", "123:456", 101) == null);
        cache.Accept(Snapshot(2, Player(sampleAge: 7)), 102);
        Check(cache.Find("123", "123:456", 102) == null);
        cache.Accept(Snapshot(3, Player(heartbeatAge: 5)), 103);
        Check(cache.Find("123", "123:456", 103.9) != null && cache.Find("123", "123:456", 104) == null);
        cache.Accept(Snapshot(4, Player(heartbeatAge: 7)), 104);
        Check(cache.Find("123", "123:456", 104) == null);
        cache.Accept(Snapshot(5), 105);
        Check(cache.Fresh(110.9) && !cache.Fresh(111) && cache.Find("123", "123:456", 111) == null);
    }

    private static void NoIdleExtrapolation()
    {
        var cache = new ObserverActivityCache(); cache.Accept(Snapshot(1, Player(idle: 59.5f)), 100);
        ObserverPlayerActivity? later = cache.Find("123", "123:456", 105);
        Check(later != null && later.ObservedIdleSeconds == 59.5f);
        cache.Accept(Snapshot(2, Player(idle: 60)), 105);
        Check(cache.Find("123", "123:456", 105)!.ObservedIdleSeconds == 60);
    }

    private static void Replay()
    {
        var cache = new ObserverActivityCache(); cache.Accept(Snapshot(), 100);
        Check(!cache.Accept(Snapshot(), 105) && cache.Age(105) == 5);
        Check(!cache.Accept(Snapshot(), 106) && !cache.Fresh(106));
        Check(cache.Accept(Snapshot(2), 107));
        Check(!cache.Accept(Snapshot(1), 108) && cache.Age(108) == 1);
    }

    private static void Identity()
    {
        var cache = new ObserverActivityCache(); cache.Accept(Snapshot(), 10);
        Check(cache.Find("124", "123:456", 10) == null && cache.Find("123", "123:999", 10) == null);
        Reject(() => cache.Accept(Snapshot(2, world: 43), 11));
        Check(cache.Snapshot!.WorldId == 42 && cache.Snapshot.Sequence == 1);
        cache.Accept(Snapshot(2, Player(character: "123:999", idle: 0)), 11);
        Check(cache.Find("123", "123:456", 11) == null && cache.Find("123", "123:999", 11)!.ObservedIdleSeconds == 0);
        cache.Accept(Snapshot(3, new ObserverPlayerActivity("123", "", false, 0, 0, 0)), 12);
        Check(cache.Find("123", "123:999", 12) == null);
        cache.Reset(); Check(cache.Snapshot == null && !cache.Fresh(12));
        Check(cache.Accept(Snapshot(1, world: 43), 13) && cache.Find("123", "123:456", 13) != null);
    }

    private static void Clock()
    {
        var cache = new ObserverActivityCache(); cache.Accept(Snapshot(), 10);
        foreach (double bad in new[] { -1, 9, double.NaN, double.PositiveInfinity })
        {
            Reject(() => cache.Accept(Snapshot(2), bad));
            Check(double.IsPositiveInfinity(cache.Age(bad)) && !cache.Fresh(bad));
        }
        Check(cache.Snapshot!.Sequence == 1 && cache.Find("123", "123:456", 10) != null);
    }

    private static int FlagOffset(byte[] bytes, int index)
    {
        using (var stream = new MemoryStream(bytes, false))
        using (var reader = new BinaryReader(stream))
        {
            stream.Position = 28;
            for (int i = 0; ; i++)
            {
                int idLength = reader.ReadUInt16(); stream.Position += idLength;
                int characterLength = reader.ReadUInt16(); stream.Position += characterLength;
                if (i == index) return (int)stream.Position;
                stream.Position += 13;
            }
        }
    }
}
