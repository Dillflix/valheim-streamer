using System;
using System.Collections.Generic;
using System.IO;
using ValheimBroadcaster.Core;

internal static class ObserverEventProtocolTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Event feed preserves independent evidence ages and unknown characters", RoundTrip);
        yield return ("Event feed rejects truncated trailing and oversized packets", Envelope);
        yield return ("Event feed validates version flags and source and event ages", Malformed);
        yield return ("Event feed rejects invalid identity text and duplicate subjects", Identities);
        yield return ("Event feed bounds population and copies the source cohort", Cohort);
        yield return ("Unknown and disabled event feeds cannot claim activity", Unknown);
        yield return ("Event receipt cannot refresh stale source or heartbeat evidence", Freshness);
        yield return ("Event replay cannot renew freshness", Replay);
        yield return ("Event cache binds world and character and resets across sessions", Bindings);
        yield return ("Event age adjustment preserves unknowns and ages observations forward", Ages);
        yield return ("Event cache rejects invalid and reversed clocks", Clock);
    }
    private static ObserverPlayerEvents Player(string id = "7", string character = "7:1", float sample = 0,
        float heartbeat = 0, float? movement = 1, float? action = 2, float? loss = 3, float? work = 4) =>
        new ObserverPlayerEvents(id, character, true, sample, heartbeat, movement, action, loss, work);
    private static ObserverEventSnapshot Snapshot(long sequence = 1, ObserverPlayerEvents? player = null, long world = 42) =>
        new ObserverEventSnapshot(sequence, world, true, new[] { player ?? Player() });
    private static void Check(bool condition) { if (!condition) throw new Exception("Event evidence assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; } catch (EndOfStreamException) { return; }
        throw new Exception("Invalid event evidence accepted.");
    }
    private static void RoundTrip()
    {
        var source = new ObserverEventSnapshot(5, 42, true, new[]
        { Player("Jötunn 🦌"), new ObserverPlayerEvents("8", "", false, 3600, 0) });
        var copy = ObserverEventProtocol.Decode(ObserverEventProtocol.Encode(source));
        Check(copy.Sequence == 5 && copy.WorldId == 42 && copy.Enabled && copy.Players.Count == 2);
        var p = copy.Players[0];
        Check(p.Id == "Jötunn 🦌" && p.MovementAgeSeconds == 1 && p.ActionAgeSeconds == 2 && p.HealthLossAgeSeconds == 3 && p.WorkAgeSeconds == 4);
        Check(!copy.Players[1].Known && copy.Players[1].CharacterId == "" && !copy.Players[1].Fresh(0));
    }
    private static void Envelope()
    {
        byte[] bytes = ObserverEventProtocol.Encode(Snapshot());
        for (int length = 0; length < bytes.Length; length++)
        { var partial = new byte[length]; Array.Copy(bytes, partial, length); Reject(() => ObserverEventProtocol.Decode(partial)); }
        var trailing = new byte[bytes.Length + 1]; bytes.CopyTo(trailing, 0); Reject(() => ObserverEventProtocol.Decode(trailing));
        Reject(() => ObserverEventProtocol.Decode(new byte[ObserverEventProtocol.MaxPacketBytes + 1]));
        Reject(() => ObserverEventProtocol.Decode(null!));
    }
    private static int FlagOffset(byte[] bytes)
    {
        int at = 27; at += 2 + BitConverter.ToUInt16(bytes, at); at += 2 + BitConverter.ToUInt16(bytes, at); return at;
    }
    private static void Malformed()
    {
        byte[] bytes = ObserverEventProtocol.Encode(Snapshot()); int known = FlagOffset(bytes);
        foreach (int offset in new[] { 0, 4, 24, known, known + 9, known + 14, known + 19, known + 24 })
        { var bad = (byte[])bytes.Clone(); bad[offset] = 99; Reject(() => ObserverEventProtocol.Decode(bad)); }
        foreach (int offset in new[] { known + 1, known + 5, known + 10, known + 15, known + 20, known + 25 })
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, -1f, 3601f })
            { var bad = (byte[])bytes.Clone(); BitConverter.GetBytes(value).CopyTo(bad, offset); Reject(() => ObserverEventProtocol.Decode(bad)); }
        var zeroSequence = (byte[])bytes.Clone(); Array.Clear(zeroSequence, 8, 8); Reject(() => ObserverEventProtocol.Decode(zeroSequence));
        var population = (byte[])bytes.Clone(); population[25] = population[26] = 255; Reject(() => ObserverEventProtocol.Decode(population));
    }
    private static void Identities()
    {
        Reject(() => Player("")); Reject(() => Player(character: "")); Reject(() => Player(new string('x', 65)));
        Reject(() => Player("bad\nidentity"));
        Reject(() => new ObserverEventSnapshot(1, 42, true, new[] { Player(), Player() }));
        Reject(() => new ObserverEventSnapshot(1, 42, true, new[] { Player(), Player("8") }));
        byte[] bytes = ObserverEventProtocol.Encode(Snapshot());
        var text = (byte[])bytes.Clone(); text[29] = 255; Reject(() => ObserverEventProtocol.Decode(text));
        var count = (byte[])bytes.Clone(); count[27] = count[28] = 255; Reject(() => ObserverEventProtocol.Decode(count));
    }
    private static void Cohort()
    {
        var players = new List<ObserverPlayerEvents>();
        for (int i = 0; i < 64; i++) players.Add(Player(i.ToString(), i + ":1"));
        var snapshot = new ObserverEventSnapshot(1, 42, true, players); players.Clear();
        Check(ObserverEventProtocol.Decode(ObserverEventProtocol.Encode(snapshot)).Players.Count == 64);
        for (int i = 0; i < 65; i++) players.Add(Player(i.ToString(), i + ":1"));
        Reject(() => new ObserverEventSnapshot(1, 42, true, players));
        Reject(() => new ObserverEventSnapshot(1, 42, true, new ObserverPlayerEvents[] { null! }));
    }
    private static void Unknown()
    {
        Reject(() => new ObserverPlayerEvents("7", "", false, 0, 0, actionAgeSeconds: 0));
        Reject(() => new ObserverEventSnapshot(1, 42, false, new[] { Player() }));
        var cache = new ObserverEventCache();
        cache.Accept(new ObserverEventSnapshot(1, 42, false, Array.Empty<ObserverPlayerEvents>()), 0);
        Check(!cache.Fresh(0) && cache.Find("7", "7:1", 0) == null);
        cache.Accept(new ObserverEventSnapshot(2, 42, true, new[] { new ObserverPlayerEvents("7", "7:1", false, 0, 0) }), 0);
        Check(cache.Find("7", "7:1", 0) == null);
    }
    private static void Freshness()
    {
        Check(Player().Fresh(5.9) && !Player().Fresh(6));
        Check(!Player(sample: 5).Fresh(1) && !Player(heartbeat: 5).Fresh(1));
        Check(!Player().Fresh(-1) && !Player().Fresh(double.NaN) && !Player().Fresh(double.PositiveInfinity));
        var cache = new ObserverEventCache(); cache.Accept(Snapshot(player: Player(sample: 6)), 10);
        Check(cache.Find("7", "7:1", 10) == null);
        cache.Accept(Snapshot(2, Player(heartbeat: 6)), 10); Check(cache.Find("7", "7:1", 10) == null);
    }
    private static void Replay()
    {
        var cache = new ObserverEventCache(); cache.Accept(Snapshot(5), 10);
        Check(!cache.Accept(Snapshot(5), 14) && !cache.Accept(Snapshot(4), 15) && cache.Age(15) == 5);
        Check(cache.Find("7", "7:1", 16) == null);
    }
    private static void Bindings()
    {
        var cache = new ObserverEventCache(); cache.Accept(Snapshot(), 0);
        Check(cache.Find("7", "other", 0) == null && cache.Find("other", "7:1", 0) == null);
        Reject(() => cache.Accept(Snapshot(2, world: 43), 1));
        cache.Reset(); Check(cache.Snapshot == null && !cache.Fresh(0)); cache.Accept(Snapshot(world: 43), 0);
        Check(cache.Find("7", "7:1", 0) != null);
    }
    private static void Ages()
    {
        ObserverPlayerEvents aged = Player(movement: null, action: .5f, loss: 3599, work: null).Elapsed(2);
        Check(aged.ActionAgeSeconds == 2.5 && aged.HealthLossAgeSeconds == 3600 && aged.MovementAgeSeconds == null && aged.WorkAgeSeconds == null);
        Check(aged.SampleAgeSeconds == 2 && aged.HeartbeatAgeSeconds == 2 && aged.Fresh(3.9) && !aged.Fresh(4));
        Reject(() => aged.Elapsed(-1)); Reject(() => aged.Elapsed(float.NaN));
    }
    private static void Clock()
    {
        var cache = new ObserverEventCache();
        foreach (double now in new[] { -1d, double.NaN, double.PositiveInfinity }) Reject(() => cache.Accept(Snapshot(), now));
        cache.Accept(Snapshot(), 10); Reject(() => cache.Accept(Snapshot(2), 9));
        Check(double.IsPositiveInfinity(cache.Age(9)) && double.IsPositiveInfinity(cache.Age(double.NaN)));
    }
}
