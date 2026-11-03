using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace ValheimBroadcaster.Core;

public enum WorldInterestKind : byte { Base = 1, BaseArea = 2, Landmark = 3 }
public enum DestinationOutcome : byte { UsableFootage = 1, PictureUnavailable = 2, TravelFailed = 3 }

// These extents describe a subject, not collision clearance or scene readiness.
public sealed class WorldInterestSite
{
    public string Id { get; }
    public string ParentId { get; }
    public string Name { get; }
    public WorldInterestKind Kind { get; }
    public ObserverPoint Center { get; }
    public ObserverPoint Extents { get; }
    public ObserverPoint Arrival { get; }
    public int Pieces { get; }
    public float Priority { get; }
    public float Age { get; }
    public float RetryAfter { get; }
    public WorldInterestSite(string id, string parent, string name, WorldInterestKind kind, ObserverPoint center,
        ObserverPoint extents, ObserverPoint arrival, int pieces, float priority, float age = 0, float retryAfter = 0)
    {
        Id = WorldInterestProtocol.Identity(id); ParentId = WorldInterestProtocol.Identity(parent, true);
        Name = ObserverDiscoveryProtocol.Text(name, 64, false);
        if (!Enum.IsDefined(typeof(WorldInterestKind), kind) || id == parent || pieces < 0 || pieces > 10000000 ||
            extents.X < 0 || extents.Y < 0 || extents.Z < 0 || extents.X > 512 || extents.Y > 256 || extents.Z > 512)
            throw new ArgumentException("Invalid world-interest subject.");
        WorldInterestProtocol.Number(priority, 100); WorldInterestProtocol.Number(age, 86400); WorldInterestProtocol.Number(retryAfter, 3600);
        Kind = kind; Center = center; Extents = extents; Arrival = arrival; Pieces = pieces; Priority = priority; Age = age; RetryAfter = retryAfter;
    }
    public WorldInterestSite WithReport(float age, float priority, float retry) =>
        new WorldInterestSite(Id, ParentId, Name, Kind, Center, Extents, Arrival, Pieces, priority, age, retry);
}

public sealed class WorldInterestPage
{
    public long World { get; }
    public long Sequence { get; }
    public long Cycle { get; }
    public int Page { get; }
    public int Pages { get; }
    public bool Enabled { get; }
    public bool Complete { get; }
    public IReadOnlyList<WorldInterestSite> Sites { get; }
    public WorldInterestPage(long world, long sequence, long cycle, int page, int pages, bool enabled, bool complete, IReadOnlyList<WorldInterestSite> sites)
    {
        if (sequence < 1 || cycle < 1 || pages < 1 || pages > WorldInterestProtocol.MaxPages || page < 0 || page >= pages ||
            sites == null || sites.Count > WorldInterestProtocol.PageSize || !enabled && (sites.Count != 0 || page != 0 || pages != 1))
            throw new ArgumentException("Invalid world-interest page.");
        var copy = new WorldInterestSite[sites.Count]; var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < copy.Length; i++) { copy[i] = sites[i] ?? throw new ArgumentException("Null site."); if (!ids.Add(copy[i].Id)) throw new ArgumentException("Duplicate site."); }
        World = world; Sequence = sequence; Cycle = cycle; Page = page; Pages = pages; Enabled = enabled; Complete = complete;
        Sites = new ReadOnlyCollection<WorldInterestSite>(copy);
    }
}

public sealed class DestinationFeedback
{
    public long World { get; }
    public long Sequence { get; }
    public string Id { get; }
    public DestinationOutcome Outcome { get; }
    public float FootageSeconds { get; }
    public DestinationFeedback(long world, long sequence, string id, DestinationOutcome outcome, float footageSeconds)
    {
        if (sequence < 1 || !Enum.IsDefined(typeof(DestinationOutcome), outcome)) throw new ArgumentException("Invalid destination feedback.");
        WorldInterestProtocol.Number(footageSeconds, 3600);
        if (outcome == DestinationOutcome.UsableFootage && footageSeconds < 15 || outcome != DestinationOutcome.UsableFootage && footageSeconds != 0)
            throw new ArgumentException("Feedback must distinguish sustained footage from failed attempts.");
        World = world; Sequence = sequence; Id = WorldInterestProtocol.Identity(id); Outcome = outcome; FootageSeconds = footageSeconds;
    }
}

public static class WorldInterestProtocol
{
    public const string Request = "ValheimBroadcaster.Observer.InterestRequest1", Reply = "ValheimBroadcaster.Observer.InterestReply1",
        Feedback = "ValheimBroadcaster.Observer.InterestFeedback1";
    public const int Version = 1, PageSize = 64, MaxSites = 1024, MaxPages = MaxSites / PageSize, MaxPacketBytes = 32768;
    public const double FreshSeconds = 45, SiteSeconds = 600, ProbeSeconds = 8;
    private const int Magic = 0x56424931, FeedbackMagic = 0x56424631;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    public static string Identity(string value, bool empty = false)
    {
        if (value == null || value.Length > 64 || !empty && value.Length == 0) throw new ArgumentException("Invalid destination ID.");
        foreach (char c in value) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == ':' || c == '-' || c == '_')) throw new ArgumentException("Invalid destination ID.");
        return value;
    }
    internal static void Number(float v, float maximum) { if (float.IsNaN(v) || float.IsInfinity(v) || v < 0 || v > maximum) throw new ArgumentOutOfRangeException(nameof(v)); }
    public static byte[] Encode(WorldInterestPage page)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Utf8, true);
        w.Write(Magic); w.Write(Version); w.Write(page.World); w.Write(page.Sequence); w.Write(page.Cycle);
        w.Write(page.Page); w.Write(page.Pages); w.Write(page.Enabled); w.Write(page.Complete); w.Write((ushort)page.Sites.Count);
        foreach (var s in page.Sites)
        {
            Text(w, s.Id); Text(w, s.ParentId); Text(w, s.Name); w.Write((byte)s.Kind);
            Point(w, s.Center); Point(w, s.Extents); Point(w, s.Arrival); w.Write(s.Pieces); w.Write(s.Priority); w.Write(s.Age); w.Write(s.RetryAfter);
        }
        w.Flush(); if (stream.Length > MaxPacketBytes) throw new ArgumentException("Interest packet exceeds limit."); return stream.ToArray();
    }
    public static WorldInterestPage Decode(byte[] bytes)
    {
        using var stream = Read(bytes, 44, MaxPacketBytes); using var r = new BinaryReader(stream, Utf8, true);
        if (r.ReadInt32() != Magic || r.ReadInt32() != Version) throw new ArgumentException("Unsupported interest protocol.");
        long world = r.ReadInt64(), sequence = r.ReadInt64(), cycle = r.ReadInt64(); int page = r.ReadInt32(), pages = r.ReadInt32();
        bool enabled = Flag(r), complete = Flag(r); int count = r.ReadUInt16();
        if (count > PageSize) throw new ArgumentException("Interest count exceeds limit.");
        var sites = new WorldInterestSite[count];
        for (int i = 0; i < count; i++) sites[i] = new WorldInterestSite(Text(r), Text(r), Text(r), (WorldInterestKind)r.ReadByte(),
            Point(r), Point(r), Point(r), r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        End(stream); return new WorldInterestPage(world, sequence, cycle, page, pages, enabled, complete, sites);
    }
    public static byte[] Encode(DestinationFeedback feedback)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Utf8, true);
        w.Write(FeedbackMagic); w.Write(Version); w.Write(feedback.World); w.Write(feedback.Sequence); Text(w, feedback.Id);
        w.Write((byte)feedback.Outcome); w.Write(feedback.FootageSeconds); w.Flush(); return stream.ToArray();
    }
    public static DestinationFeedback DecodeFeedback(byte[] bytes)
    {
        using var stream = Read(bytes, 32, 128); using var r = new BinaryReader(stream, Utf8, true);
        if (r.ReadInt32() != FeedbackMagic || r.ReadInt32() != Version) throw new ArgumentException("Unsupported feedback protocol.");
        var f = new DestinationFeedback(r.ReadInt64(), r.ReadInt64(), Text(r), (DestinationOutcome)r.ReadByte(), r.ReadSingle()); End(stream); return f;
    }
    private static MemoryStream Read(byte[] bytes, int min, int max)
    { if (bytes == null || bytes.Length < min || bytes.Length > max) throw new ArgumentException("Invalid packet size."); return new MemoryStream(bytes, false); }
    private static void End(MemoryStream s) { if (s.Position != s.Length) throw new ArgumentException("Trailing packet data."); }
    private static void Text(BinaryWriter w, string s) { byte[] b = Utf8.GetBytes(s); w.Write((ushort)b.Length); w.Write(b); }
    private static string Text(BinaryReader r)
    { int n = r.ReadUInt16(); if (n > 256 || n > r.BaseStream.Length - r.BaseStream.Position) throw new ArgumentException("Invalid string size."); return Utf8.GetString(r.ReadBytes(n)); }
    private static void Point(BinaryWriter w, ObserverPoint p) { w.Write(p.X); w.Write(p.Y); w.Write(p.Z); }
    private static ObserverPoint Point(BinaryReader r) => new ObserverPoint(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    private static bool Flag(BinaryReader r) { byte b = r.ReadByte(); if (b > 1) throw new ArgumentException("Invalid flag."); return b == 1; }
}

// A cycle is an immutable server catalogue. Publish only after all its pages;
// removed sites disappear atomically. Receipt freshness never rejuvenates source age.
public sealed class WorldInterestCache
{
    private long world, sequence, cycle;
    private int pages;
    private bool enabled, complete;
    private double received = double.NegativeInfinity;
    private readonly Dictionary<int, List<Observation>> staged = new Dictionary<int, List<Observation>>();
    private readonly List<Observation> current = new List<Observation>();
    private sealed class Observation { internal WorldInterestSite Site = null!; internal double At; }
    public int Count => current.Count;
    public bool Complete { get; private set; }
    public long Revision { get; private set; }
    public bool Fresh(double now) => enabled && now >= received && now - received < WorldInterestProtocol.FreshSeconds;
    public void Reset() { world = sequence = cycle = 0; pages = 0; enabled = Complete = false; received = double.NegativeInfinity; staged.Clear(); current.Clear(); Revision++; }
    public bool Accept(WorldInterestPage p, double now)
    {
        if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < received || sequence != 0 && world != p.World) throw new ArgumentException("Invalid interest receipt.");
        if (p.Sequence <= sequence || p.Cycle < cycle) return false;
        world = p.World; sequence = p.Sequence; received = now; enabled = p.Enabled;
        if (!enabled) { current.Clear(); staged.Clear(); cycle = p.Cycle; Complete = false; Revision++; return true; }
        if (cycle != p.Cycle) { staged.Clear(); cycle = p.Cycle; pages = p.Pages; complete = p.Complete; }
        if (pages != p.Pages || complete != p.Complete || staged.ContainsKey(p.Page)) throw new ArgumentException("Inconsistent interest cycle.");
        var entries = new List<Observation>(); foreach (var s in p.Sites) entries.Add(new Observation { Site = s, At = now });
        staged.Add(p.Page, entries);
        if (staged.Count == pages)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal); var next = new List<Observation>();
            for (int i = 0; i < pages; i++) foreach (var e in staged[i]) { if (!ids.Add(e.Site.Id)) throw new ArgumentException("Duplicate destination across pages."); next.Add(e); }
            current.Clear(); current.AddRange(next); Complete = complete; Revision++;
        }
        return true;
    }
    public IReadOnlyList<WorldInterestSite> Available(double now)
    {
        var result = new List<WorldInterestSite>(); if (!Fresh(now)) return result;
        foreach (var o in current)
        {
            float elapsed = (float)Math.Max(0, now - o.At);
            if (o.Site.Age + elapsed < WorldInterestProtocol.SiteSeconds)
                result.Add(o.Site.WithReport(o.Site.Age + elapsed, o.Site.Priority, Math.Max(0, o.Site.RetryAfter - elapsed)));
        }
        return result;
    }
}
