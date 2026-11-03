using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ValheimBroadcaster.Core;

// Operational destination feedback, separate from game/world state. Timestamps
// use a caller-supplied clock (UTC seconds for persisted server history).
public sealed class DestinationFeedbackLedger
{
    private const int Limit = 1024, Magic = 0x56424648;
    private sealed class Entry { internal int Good, Bad, Streak; internal double Updated, Until; }
    private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    public int Count => entries.Count;
    public void Clear() => entries.Clear();
    public void Record(string id, DestinationOutcome outcome, double now)
    {
        WorldInterestProtocol.Identity(id); Time(now);
        if (!Enum.IsDefined(typeof(DestinationOutcome), outcome)) throw new ArgumentException("Invalid outcome.");
        if (!entries.TryGetValue(id, out Entry? e))
        {
            if (entries.Count >= Limit) entries.Remove(entries.OrderBy(p => p.Value.Updated).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key);
            entries.Add(id, e = new Entry());
        }
        // A clock correction must not turn a timestamp from the future into a permanent ban.
        if (now < e.Updated || now - e.Updated > 86400) e.Good = e.Bad = e.Streak = 0;
        e.Updated = now;
        if (outcome == DestinationOutcome.UsableFootage) { e.Good = Math.Min(1000, e.Good + 1); e.Streak = 0; e.Until = now; }
        else
        {
            e.Bad = Math.Min(1000, e.Bad + 1); e.Streak = Math.Min(4, e.Streak + 1);
            e.Until = now + Math.Min(1800, (outcome == DestinationOutcome.TravelFailed ? 120 : 300) * Math.Pow(2, e.Streak - 1));
        }
    }
    public float RetryAfter(string id, double now)
    { Time(now); return entries.TryGetValue(id, out Entry? e) && now >= e.Updated ? (float)Math.Max(0, Math.Min(1800, e.Until - now)) : 0; }
    public float Adjustment(string id, double now)
    { Time(now); return entries.TryGetValue(id, out Entry? e) && now >= e.Updated && now - e.Updated < 86400 ? Math.Min(12, e.Good * 2) - Math.Min(30, e.Bad * 5) : 0; }
    public byte[] Save(long world)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s, Encoding.UTF8, true);
        w.Write(Magic); w.Write(1); w.Write(world); w.Write(entries.Count);
        foreach (var p in entries.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            byte[] id = Encoding.ASCII.GetBytes(p.Key); w.Write((byte)id.Length); w.Write(id);
            w.Write(p.Value.Good); w.Write(p.Value.Bad); w.Write(p.Value.Streak); w.Write(p.Value.Updated); w.Write(p.Value.Until);
        }
        w.Flush(); return s.ToArray();
    }
    public void Load(byte[] bytes, long world)
    {
        if (bytes == null || bytes.Length < 20 || bytes.Length > 100000) throw new ArgumentException("Invalid destination history size.");
        var next = new Dictionary<string, Entry>(StringComparer.Ordinal);
        using var s = new MemoryStream(bytes, false); using var r = new BinaryReader(s, Encoding.UTF8, true);
        if (r.ReadInt32() != Magic || r.ReadInt32() != 1 || r.ReadInt64() != world) throw new ArgumentException("Destination history world/version mismatch.");
        int count = r.ReadInt32(); if (count < 0 || count > Limit) throw new ArgumentException("History count exceeds limit.");
        for (int i = 0; i < count; i++)
        {
            int size = r.ReadByte(); if (size == 0 || size > 64 || size > s.Length - s.Position) throw new ArgumentException("Invalid history ID.");
            byte[] raw = r.ReadBytes(size); foreach (byte b in raw) if (b > 127) throw new ArgumentException("Invalid history ID.");
            string id = WorldInterestProtocol.Identity(Encoding.ASCII.GetString(raw));
            var e = new Entry { Good = r.ReadInt32(), Bad = r.ReadInt32(), Streak = r.ReadInt32(), Updated = r.ReadDouble(), Until = r.ReadDouble() };
            Time(e.Updated); Time(e.Until);
            if (e.Good < 0 || e.Good > 1000 || e.Bad < 0 || e.Bad > 1000 || e.Streak < 0 || e.Streak > 4 ||
                e.Until < e.Updated || e.Until - e.Updated > 1800 || next.ContainsKey(id)) throw new ArgumentException("Invalid history entry.");
            next.Add(id, e);
        }
        if (s.Position != s.Length) throw new ArgumentException("Trailing history data.");
        entries.Clear(); foreach (var p in next) entries.Add(p.Key, p.Value);
    }
    private static void Time(double t) { if (double.IsNaN(t) || double.IsInfinity(t) || t < 0 || t > 1e12) throw new ArgumentException("Invalid history clock."); }
}

public sealed class DestinationVisit
{
    public string? Id { get; private set; }
    public float FootageSeconds { get; private set; }
    private bool reported;
    public void Begin(string id) { Id = WorldInterestProtocol.Identity(id); FootageSeconds = 0; reported = false; }
    public void Reset() { Id = null; FootageSeconds = 0; reported = false; }
    public DestinationOutcome? Observe(float dt, bool scenicPicture, bool searchExpired)
    {
        WorldInterestProtocol.Number(dt, 3600);
        if (Id == null || reported) return null;
        if (scenicPicture) FootageSeconds += Math.Min(dt, 1);
        if (FootageSeconds >= 15) { reported = true; return DestinationOutcome.UsableFootage; }
        if (!scenicPicture && searchExpired) { reported = true; return DestinationOutcome.PictureUnavailable; }
        return null;
    }
}
