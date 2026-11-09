using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Server;

// Metadata-only inventory of existing persistent objects and placed locations.
// No Instantiate, ownership transfer, ZDO writes, zone generation or AI activation.
internal sealed class ObserverWorldInterestService
{
    private static readonly FieldInfo? Sectors = AccessTools.Field(typeof(ZDOMan), "m_objectsBySector");
    private static readonly FieldInfo? Generated = AccessTools.Field(typeof(ZoneSystem), "m_generatedZones");
    private readonly WorldBaseIndex bases = new WorldBaseIndex();
    private readonly Dictionary<int, int> prefabKinds = new Dictionary<int, int>();
    private readonly Dictionary<string, Record> catalogue = new Dictionary<string, Record>(StringComparer.Ordinal);
    private readonly Dictionary<string, WorldInterestSite> landmarks = new Dictionary<string, WorldInterestSite>(StringComparer.Ordinal);
    private readonly Dictionary<ZNetPeer, Reader> readers = new Dictionary<ZNetPeer, Reader>();
    private readonly Dictionary<ZNetPeer, Reader> readers2 = new Dictionary<ZNetPeer, Reader>();
    private readonly RecentGravestones<ZDOID> graves = new RecentGravestones<ZDOID>();
    private readonly Func<int> graveMinutes;
    private readonly Func<IReadOnlyList<WorldInterestSite>> naturalProvider;
    private readonly Dictionary<string, int> preparationGroups = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly DestinationFeedbackLedger history = new DestinationFeedbackLedger();
    private readonly Action<string> log;
    private ZDOMan? manager;
    private ZoneSystem? zones;
    private List<ZDO>[]? sectors;
    private HashSet<Vector2s>? generated;
    private IEnumerator<ZoneSystem.LocationInstance>? locations;
    private int sector, item, expectedCount, objects, scannedLocations;
    private bool collecting, complete, mutated, haveSweep, historyDirty;
    private float nextTick, nextSweep, sweepAt, nextPublish, nextSave;
    private long worldId, sequence, cycle;
    private WorldInterestSite[]? proposals;
    private readonly Dictionary<string, Record> preparing = new Dictionary<string, Record>(StringComparer.Ordinal);
    private int proposalIndex, baseCount, landmarkCount, rejectedApproaches;
    private float proposedAt;
    private bool proposedComplete;
    private string? historyPath;
    private sealed class Record { internal WorldInterestSite Site = null!; internal float At; internal RecentGravestones<ZDOID>.Marker? Grave; }
    private sealed class Reader
    {
        internal float NextRequest, NextFeedback;
        internal long Cycle, FeedbackSequence;
        internal int Page;
        internal bool Complete;
        internal Record[] Sites = Array.Empty<Record>();
        internal readonly Dictionary<string, float> Issued = new Dictionary<string, float>(StringComparer.Ordinal);
    }
    internal ObserverWorldInterestService(Action<string> log, Func<IReadOnlyList<WorldInterestSite>> naturalProvider, Func<int> graveMinutes)
    { this.log = log; this.naturalProvider = naturalProvider; this.graveMinutes = graveMinutes; }
    internal string Status => $"World interests: {catalogue.Count} destinations; {bases.Count} construction cells; {graves.Count} recent gravestone areas observed; {objects} objects examined; " +
        $"{(collecting ? "indexing" : complete ? "survey complete" : "partial survey")}; {rejectedApproaches} approaches outside eligible generated dry land; feedback history {history.Count}";
    internal void Reset()
    {
        Save(); locations?.Dispose(); locations = null; bases.Clear(); graves.Clear(); prefabKinds.Clear(); catalogue.Clear(); landmarks.Clear(); readers.Clear(); readers2.Clear(); preparationGroups.Clear(); history.Clear();
        manager = null; zones = null; sectors = null; generated = null; historyPath = null; worldId = sequence = cycle = 0;
        collecting = complete = mutated = haveSweep = historyDirty = false; nextTick = nextSweep = nextPublish = nextSave = 0;
        sector = item = expectedCount = objects = scannedLocations = 0;
        proposals = null; preparing.Clear();
    }
    internal void Tick(float now, bool enabled)
    {
        if (!enabled || now < nextTick || !ZNet.instance || ZDOMan.instance == null || !ZoneSystem.instance || !ZNetScene.instance || WorldGenerator.instance == null) return;
        nextTick = now + .05f;
        if (manager != ZDOMan.instance || zones != ZoneSystem.instance || worldId != ZNet.instance.GetWorldUID())
        {
            Reset(); manager = ZDOMan.instance; zones = ZoneSystem.instance; worldId = ZNet.instance.GetWorldUID();
            if (Sectors?.FieldType != typeof(List<ZDO>[]) || Generated?.FieldType != typeof(HashSet<Vector2s>)) throw new MissingFieldException("Unsupported world-interest inventory layout.");
            sectors = Sectors.GetValue(manager) as List<ZDO>[]; generated = Generated.GetValue(zones) as HashSet<Vector2s>;
            historyPath = Path.Combine(Paths.ConfigPath, "ValheimBroadcaster", "destinations-" + worldId.ToString(CultureInfo.InvariantCulture) + ".bin");
            try { if (File.Exists(historyPath)) { if (new FileInfo(historyPath).Length > 100000) throw new IOException("History exceeds bound."); history.Load(File.ReadAllBytes(historyPath), worldId); } }
            catch (Exception error) { log("Destination history ignored after " + error.GetType().Name + "; rebuilding from current world evidence."); }
        }
        if (sectors == null || generated == null) return;
        foreach (var readersByVersion in new[] { readers, readers2 })
            foreach (ZNetPeer peer in readersByVersion.Keys.Where(p => !ZNet.instance.GetPeers().Contains(p)).ToArray()) readersByVersion.Remove(peer);
        if (historyDirty && now >= nextSave) { nextSave = now + 60; Save(); }
        if (proposals != null && !PublishStep()) return;
        if (!collecting)
        {
            if (now < nextSweep) return;
            // Array identity can change after a world reload; never keep a stale inventory.
            sectors = Sectors!.GetValue(manager) as List<ZDO>[];
            if (sectors == null) return;
            bases.Clear(); graves.Clear(); landmarks.Clear(); sector = item = expectedCount = objects = scannedLocations = 0;
            mutated = false; collecting = true; sweepAt = now; nextPublish = now + 5;
            locations?.Dispose(); locations = zones!.GetLocationList().GetEnumerator();
        }
        long began = Stopwatch.GetTimestamp(); int work = 0, emptyBudget = 2048;
        int window = graveMinutes();
        long worldTicks = ZNet.instance.GetTime().Ticks;
        while (sector < sectors.Length && work < 512 && emptyBudget-- > 0)
        {
            List<ZDO>? bucket = sectors[sector];
            if (bucket == null || item >= bucket.Count) { sector++; item = expectedCount = 0; continue; }
            if (item == 0) expectedCount = bucket.Count; else if (expectedCount != bucket.Count) mutated = true;
            ZDO zdo = bucket[item++]; work++; objects++;
            if (zdo != null && zdo.IsValid())
            {
                long death = window > 0 ? zdo.GetLong(ZDOVars.s_timeOfDeath, 0L) : 0;
                bool recent = RecentGravestones<ZDOID>.Remaining(worldTicks, death, window) > 0;
                bool placed = zdo.GetLong(ZDOVars.s_creator, 0L) != 0;
                if (recent || placed)
                {
                    Vector3 p = zdo.GetPosition();
                    if (Finite(p) && p.y < 990 && p.y > -1000)
                    {
                        int kind = Kind(zdo.GetPrefab());
                        if (recent && kind == 3 && zdo.GetLong(ZDOVars.s_owner, 0L) != 0)
                        {
                            var zone = ZoneSystem.GetZone(p);
                            graves.Observe(zone.x, zone.y, Point(p), death, zdo.m_uid);
                        }
                        else if (placed && (kind == 1 || kind == 2)) bases.Add(Point(p), kind == 2);
                    }
                }
            }
            if ((Stopwatch.GetTimestamp() - began) / (double)Stopwatch.Frequency >= .002) break;
        }
        // The location inventory normally changes only during generation. If a
        // structural mutation occurs, preserve completed evidence and retry on
        // the next sweep rather than allowing one enumerator to stall discovery.
        if (locations != null)
        {
            try
            {
                for (int i = 0; i < 32; i++)
                {
                    if (!locations.MoveNext()) { locations.Dispose(); locations = null; break; }
                    if (++scannedLocations > 32768) { mutated = true; locations.Dispose(); locations = null; break; }
                    var l = locations.Current;
                    if (!l.m_placed || l.m_location == null || !Finite(l.m_position) || l.m_position.y >= 990) continue;
                    Vector2s zone = ZoneSystem.GetZone(l.m_position);
                    string id = "landmark:" + zone.x.ToString(CultureInfo.InvariantCulture) + ":" + zone.y.ToString(CultureInfo.InvariantCulture);
                    float radius = Mathf.Clamp(l.m_location.m_exteriorRadius, 3, 100);
                    string label = l.m_location.m_prefabName ?? "Placed landmark";
                    label = new string(label.Where(c => !char.IsControl(c) && !char.IsSurrogate(c)).Take(64).ToArray()); if (label.Length == 0) label = "Placed landmark";
                    if (landmarks.Count < 2048 || landmarks.ContainsKey(id))
                        landmarks[id] = new WorldInterestSite(id, "", label, WorldInterestKind.Landmark, Point(l.m_position),
                            new ObserverPoint(radius, 8, radius), Point(l.m_position), 0, 50);
                    else mutated = true;
                }
            }
            catch (InvalidOperationException) { mutated = true; locations?.Dispose(); locations = null; }
        }
        bool done = sector >= sectors.Length && locations == null;
        if (done || !haveSweep && now >= nextPublish)
        {
            Publish(now, done && !mutated && !bases.Truncated); nextPublish = now + 10;
            if (done) { haveSweep = true; collecting = false; nextSweep = now + 20; }
        }
    }
    private int Kind(int hash)
    {
        if (prefabKinds.TryGetValue(hash, out int k)) return k;
        GameObject? prefab = ZNetScene.instance.GetPrefab(hash);
        if (!prefab) return 0; // A not-yet-loaded prefab remains eligible next sweep.
        k = prefab!.GetComponent<TombStone>() ? 3 : prefab.GetComponent<Piece>() ? (prefab.GetComponent<Bed>() || prefab.GetComponent<CraftingStation>() ? 2 : 1) : 0;
        if (prefabKinds.Count < 8192) prefabKinds[hash] = k;
        return k;
    }
    private void Publish(float now, bool full)
    {
        var graveSites = graves.Markers.Select(m => new WorldInterestSite(m.Id, "", "Recent gravestone area",
            WorldInterestKind.Natural, m.Position, new ObserverPoint(8, 3, 8), m.Position, 0, 24));
        proposals = bases.Build().Concat(landmarks.Values).Concat(graveSites).OrderByDescending(s => s.Priority).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        preparing.Clear(); proposalIndex = baseCount = landmarkCount = rejectedApproaches = 0;
        proposedAt = sweepAt; proposedComplete = full; preparationGroups.Clear();
    }
    private bool PublishStep()
    {
        // At most two approach proposals (eight terrain queries) per tick.
        // Publication remains atomic while this bounded preparation is underway.
        int checks = 0, visited = 0;
        while (proposalIndex < proposals!.Length && checks < 2 && visited++ < 64)
        {
            WorldInterestSite s = proposals[proposalIndex++]; bool landmark = s.Kind == WorldInterestKind.Landmark;
            string biome = WorldGenerator.instance.GetBiome(Vector(s.Arrival)).ToString();
            string group = (s.IsGravestoneArea ? "grave:" : landmark ? "landmark:" : "base:") + biome;
            preparationGroups.TryGetValue(group, out int retained);
            if (retained >= 128) { proposedComplete = false; continue; }
            checks++;
            if (!Arrival(s, out ObserverPoint arrival)) { rejectedApproaches++; continue; }
            if (landmark) landmarkCount++; else if (!s.IsGravestoneArea) baseCount++;
            preparationGroups[group] = retained + 1;
            preparing.Add(s.Id, new Record {
                At = proposedAt, Grave = graves.Find(s.Id),
                Site = new WorldInterestSite(s.Id, s.ParentId, s.Name, s.Kind, s.Center, s.Extents, arrival, s.Pieces, s.Priority, biome: biome)
            });
        }
        if (proposalIndex < proposals.Length) return false;
        catalogue.Clear();
        int naturalSlots = 512 - preparing.Values.Count(r => r.Grave != null);
        var natural = naturalProvider();
        if (natural.Count > naturalSlots) proposedComplete = false;
        foreach (var site in WorldInterestDistribution.Take(natural, naturalSlots))
            preparing.Add(site.Id, new Record { At = Time.realtimeSinceStartup, Site = site });
        if (baseCount > 256 || landmarkCount > 256) proposedComplete = false;
        var chosen = preparing.Values.Where(r => r.Site.Kind == WorldInterestKind.Natural).Select(r => r.Site)
            .Concat(WorldInterestDistribution.Take(preparing.Values.Where(r => r.Site.Kind == WorldInterestKind.Base || r.Site.Kind == WorldInterestKind.BaseArea).Select(r => r.Site), 256))
            .Concat(WorldInterestDistribution.Take(preparing.Values.Where(r => r.Site.Kind == WorldInterestKind.Landmark).Select(r => r.Site), 256));
        foreach (WorldInterestSite site in chosen) catalogue.Add(site.Id, preparing[site.Id]);
        complete = proposedComplete; preparing.Clear(); proposals = null; log(Status); return true;
    }
    private bool Arrival(WorldInterestSite site, out ObserverPoint arrival)
    {
        arrival = default;
        // Choose an outside approach to a local subject area, not the centroid
        // of a sprawling base. This remains a terrain proposal, never a clearance proof.
        Vector3 center = Vector(site.Arrival);
        float offset = site.Kind == WorldInterestKind.Landmark ? Mathf.Min(48, site.Extents.X + 12) : 24;
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = center + (i == 0 ? Vector3.right : i == 1 ? Vector3.left : i == 2 ? Vector3.forward : Vector3.back) * offset;
            Vector2s zone = ZoneSystem.GetZone(p);
            bool known = true;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) if (!generated!.Contains(new Vector2s(zone.x + x, zone.y + z))) known = false;
            if (!known) continue;
            float height = WorldGenerator.instance.GetHeight(p);
            if (float.IsNaN(height) || float.IsInfinity(height) || height <= zones!.m_waterLevel + 2 || height >= 990) continue;
            p.y = Mathf.Max(height + 8, site.Kind == WorldInterestKind.Base || site.Kind == WorldInterestKind.BaseArea ? center.y : height + 8);
            if (!Finite(p) || p.y >= 998) continue;
            arrival = Point(p); return true;
        }
        return false;
    }
    internal WorldInterestPage? Request(ZNetPeer peer, float now, bool enabled, bool version2 = false)
    {
        var readers = version2 ? readers2 : this.readers;
        if (!readers.TryGetValue(peer, out Reader? reader)) { if (readers.Count >= 64) return null; readers.Add(peer, reader = new Reader()); }
        if (now < reader.NextRequest) return null; reader.NextRequest = now + 1;
        if (!enabled)
        { reader.Sites = Array.Empty<Record>(); reader.Issued.Clear(); reader.Page = 0; return new WorldInterestPage(ZNet.instance.GetWorldUID(), ++sequence, ++cycle, 0, 1, false, false, Array.Empty<WorldInterestSite>()); }
        if (reader.Page == 0)
        {
            reader.Cycle = ++cycle; reader.Sites = catalogue.Values.Where(r => version2 || r.Site.Kind != WorldInterestKind.Natural).OrderBy(r => r.Site.Id, StringComparer.Ordinal).ToArray(); reader.Complete = complete;
        }
        int pages = Math.Max(1, (reader.Sites.Length + WorldInterestProtocol.PageSize - 1) / WorldInterestProtocol.PageSize);
        var rows = new List<WorldInterestSite>(); double utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int window = graveMinutes();
        long worldTicks = ZNet.instance.GetTime().Ticks;
        foreach (var r in reader.Sites.Skip(reader.Page * WorldInterestProtocol.PageSize).Take(WorldInterestProtocol.PageSize))
        {
            float age = Mathf.Clamp(now - r.At, 0, 86400);
            if (r.Grave != null)
            {
                // Readers page through a snapshot. Never reissue an expired or
                // destroyed (looted) grave just because it was in that snapshot.
                var grave = r.Grave;
                double remaining = RecentGravestones<ZDOID>.Remaining(worldTicks, grave.DeathTicks, window);
                ZDO? live = manager?.GetZDO(grave.Handle);
                if (remaining <= 0 || live == null || !live.IsValid() ||
                    live.GetLong(ZDOVars.s_timeOfDeath, 0L) != grave.DeathTicks) continue;
                // v2 peers already expire reports at SiteSeconds. Shorten the
                // final report's lifetime to the remaining death window.
                age = Mathf.Max(0, (float)(WorldInterestProtocol.SiteSeconds - remaining));
            }
            rows.Add(r.Site.WithReport(age, Mathf.Clamp(r.Site.Priority + history.Adjustment(r.Site.Id, utc), 0, 100), history.RetryAfter(r.Site.Id, utc)));
            reader.Issued[r.Site.Id] = now;
        }
        foreach (string id in reader.Issued.Where(p => now - p.Value > 600).Select(p => p.Key).ToArray()) reader.Issued.Remove(id);
        // Bound historical issued identities even while the live catalogue changes.
        while (reader.Issued.Count > WorldInterestProtocol.MaxSites) reader.Issued.Remove(reader.Issued.OrderBy(p => p.Value).First().Key);
        var page = new WorldInterestPage(ZNet.instance.GetWorldUID(), ++sequence, reader.Cycle, reader.Page, pages, true, reader.Complete, rows);
        reader.Page = (reader.Page + 1) % pages; return page;
    }
    internal bool Feedback(ZNetPeer peer, DestinationFeedback feedback, float now, bool version2 = false)
    {
        var readers = version2 ? readers2 : this.readers;
        if (feedback.World != worldId || !readers.TryGetValue(peer, out Reader? reader) || now < reader.NextFeedback ||
            feedback.Sequence <= reader.FeedbackSequence || !reader.Issued.TryGetValue(feedback.Id, out float issued) || now - issued > 600 ||
            !catalogue.TryGetValue(feedback.Id, out Record? record)) return false;
        if (feedback.Outcome != DestinationOutcome.TravelFailed && Vector3.Distance(peer.GetRefPos(), Vector(record.Site.Arrival)) > 180) return false;
        reader.NextFeedback = now + 1; reader.FeedbackSequence = feedback.Sequence;
        history.Record(feedback.Id, feedback.Outcome, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); historyDirty = true;
        log("Destination feedback: " + feedback.Id + "; " + feedback.Outcome + "; usable seconds " + feedback.FootageSeconds.ToString("F1", CultureInfo.InvariantCulture));
        return true;
    }
    internal void Save()
    {
        if (!historyDirty || historyPath == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            string temporary = historyPath + ".tmp"; File.WriteAllBytes(temporary, history.Save(worldId));
            if (File.Exists(historyPath)) File.Replace(temporary, historyPath, null); else File.Move(temporary, historyPath);
            historyDirty = false;
        }
        catch (Exception error) { log("Destination history save failed after " + error.GetType().Name + "; in-memory feedback remains active."); }
    }
    private static bool Finite(Vector3 p) => !(float.IsNaN(p.x) || float.IsInfinity(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.z)) &&
        Math.Abs(p.x) < 19990 && Math.Abs(p.y) < 19990 && Math.Abs(p.z) < 19990;
    private static ObserverPoint Point(Vector3 p) => new ObserverPoint(p.x, p.y, p.z);
    private static Vector3 Vector(ObserverPoint p) => new Vector3(p.X, p.Y, p.Z);
}
