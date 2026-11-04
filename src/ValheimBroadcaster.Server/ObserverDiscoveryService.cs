using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Server;

// Reads only already-generated zone identities and existing character ZDOs.
// Procedural height/biome queries do not instantiate zones or assign simulation ownership.
internal sealed class ObserverDiscoveryService
{
    private static readonly FieldInfo? GeneratedField = AccessTools.Field(typeof(ZoneSystem), "m_generatedZones");
    private readonly Dictionary<ZNetPeer, float> metadataAt = new Dictionary<ZNetPeer, float>();
    private readonly Dictionary<ZNetPeer, float> nextReply = new Dictionary<ZNetPeer, float>();
    private readonly Dictionary<string, ObserverRegion> catalog = new Dictionary<string, ObserverRegion>(StringComparer.Ordinal);
    private readonly Dictionary<string, WorldInterestSite> natural = new Dictionary<string, WorldInterestSite>(StringComparer.Ordinal);
    private readonly Queue<Vector2s> pending = new Queue<Vector2s>();
    private readonly HashSet<string> queued = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> surveyed = new HashSet<string>(StringComparer.Ordinal);
    private readonly Queue<string> surveyedOrder = new Queue<string>();
    private LandscapeSurvey? survey;
    private Vector2s surveyZone;
    internal IReadOnlyList<WorldInterestSite> NaturalDestinations => natural.Values.ToArray();
    private readonly Queue<string> catalogOrder = new Queue<string>();
    private readonly Dictionary<ZNetPeer, int> pageStarts = new Dictionary<ZNetPeer, int>();
    private HashSet<Vector2s>? generated;
    private ZoneSystem? zones;
    private long sequence;
    private float nextScan;
    private int scanX, scanZ, directionX = 1, directionZ, legLength = 1, legProgress, completedLegs;
    private const int CatalogLimit = 4096, CoordinateBudget = 1024, TerrainBudget = 32, MaxZone = 312;
    internal int RegionCount => catalog.Count;

    internal ObserverDiscoveryService()
    {
        if (GeneratedField?.FieldType != typeof(HashSet<Vector2s>))
            throw new MissingFieldException("Unsupported generated-zone inventory; observer discovery unavailable.");
    }

    internal void Reset()
    {
        metadataAt.Clear(); nextReply.Clear(); pageStarts.Clear(); catalog.Clear(); catalogOrder.Clear();
        ResetTerrain(); ResetCursor(); generated = null; zones = null; sequence = 0; nextScan = 0;
    }
    internal void ReceivedMetadata(ZNetPeer peer, float now) => metadataAt[peer] = now;
    internal void Tick(IReadOnlyList<ZNetPeer> peers, float now, bool enabled)
    {
        var live = new HashSet<ZNetPeer>(peers);
        foreach (ZNetPeer gone in metadataAt.Keys.Where(peer => !live.Contains(peer)).ToArray()) metadataAt.Remove(gone);
        foreach (ZNetPeer gone in nextReply.Keys.Where(peer => !live.Contains(peer)).ToArray()) nextReply.Remove(gone);
        foreach (ZNetPeer gone in pageStarts.Keys.Where(peer => !live.Contains(peer)).ToArray()) pageStarts.Remove(gone);
        if (!enabled || now < nextScan || !ZoneSystem.instance || WorldGenerator.instance == null) return;
        nextScan = now + 1;
        if (zones != ZoneSystem.instance)
        {
            ResetTerrain(); ResetCursor(); catalog.Clear(); catalogOrder.Clear(); pageStarts.Clear();
            zones = ZoneSystem.instance; generated = GeneratedField!.GetValue(zones) as HashSet<Vector2s>;
        }
        if (generated == null) return;
        // Queue nearby and world-wide evidence independently of query work.
        // One partially sampled zone survives a tick boundary, even with a
        // one-query budget. Nearby retries cannot consume the global cursor.
        foreach (ZNetPeer peer in peers.Take(16))
        {
            Vector3 reference = peer.GetRefPos();
            if (!peer.IsReady() || !Finite(reference.x) || !Finite(reference.z)) continue;
            Vector2s around = ZoneSystem.GetZone(reference);
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                QueueZone(new Vector2s(around.x + x, around.y + z), 32);
        }
        for (int i = 0; i < CoordinateBudget && pending.Count < 128; i++) QueueZone(NextZone(), 128);
        int budget = TerrainBudget;
        while (budget > 0 && (survey != null || pending.Count > 0))
        {
            if (survey == null) { surveyZone = pending.Dequeue(); survey = new LandscapeSurvey(); }
            Vector3 center = ZoneSystem.GetZonePos(surveyZone);
            ObserverPoint offset = LandscapeSurvey.Offset(survey.NextSample);
            Vector3 point = center + new Vector3(offset.X, 0, offset.Z);
            budget--; survey.Observe(WorldGenerator.instance.GetHeight(point));
            if (!survey.Complete) continue;
            string id = ZoneId(surveyZone); queued.Remove(id);
            if (surveyed.Add(id)) { surveyedOrder.Enqueue(id); if (surveyed.Count > 8192) surveyed.Remove(surveyedOrder.Dequeue()); }
            if (survey.TryDestination(zones!.m_waterLevel, out ObserverPoint arrival, out ScenicLandscape landscape))
            {
                center.x += arrival.X; center.z += arrival.Z; center.y = arrival.Y;
                string biome = Clean(WorldGenerator.instance.GetBiome(center).ToString());
                if (catalog.Count >= CatalogLimit) { string removed = catalogOrder.Dequeue(); catalog.Remove(removed); natural.Remove(removed); }
                catalog.Add(id, new ObserverRegion(id, biome, Point(center))); catalogOrder.Enqueue(id);
                natural.Add(id, new WorldInterestSite(id, "", biome + " / " + landscape.ToString(), WorldInterestKind.Natural,
                    Point(center), new ObserverPoint(24, 8, 24), Point(center), 0, 50, biome: biome, landscape: landscape));
            }
            survey = null;
        }
    }
    private static string ZoneId(Vector2s zone) => zone.x.ToString(CultureInfo.InvariantCulture) + ":" + zone.y.ToString(CultureInfo.InvariantCulture);
    private void QueueZone(Vector2s zone, int limit)
    {
        if (pending.Count >= limit || !generated!.Contains(zone)) return;
        string id = ZoneId(zone);
        if (catalog.ContainsKey(id) || surveyed.Contains(id) || queued.Contains(id) || !NeighborhoodGenerated(zone)) return;
        Vector3 p = ZoneSystem.GetZonePos(zone); if (!Finite(p.x) || !Finite(p.z)) return;
        queued.Add(id); pending.Enqueue(zone);
    }
    private void ResetTerrain()
    { natural.Clear(); pending.Clear(); queued.Clear(); surveyed.Clear(); surveyedOrder.Clear(); survey = null; }

    private Vector2s NextZone()
    {
        // Probe a persistent coordinate cursor rather than enumerating the live
        // HashSet. Continuous generation therefore cannot restart discovery at
        // its first elements. The finite square covers every allowed X/Z point.
        if (Math.Abs(scanX) > MaxZone || Math.Abs(scanZ) > MaxZone) ResetCursor();
        var result = new Vector2s(scanX, scanZ);
        scanX += directionX; scanZ += directionZ;
        if (++legProgress == legLength)
        {
            legProgress = 0;
            int previousX = directionX; directionX = -directionZ; directionZ = previousX;
            if (++completedLegs == 2)
            {
                completedLegs = 0; legLength++;
            }
        }
        return result;
    }
    private void ResetCursor()
    {
        scanX = scanZ = directionZ = legProgress = completedLegs = 0;
        directionX = legLength = 1;
    }
    private bool NeighborhoodGenerated(Vector2s zone)
    {
        for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            if (!generated!.Contains(new Vector2s(zone.x + x, zone.y + z))) return false;
        return true;
    }

    internal bool MayReply(ZNetPeer peer, float now)
    {
        if (nextReply.TryGetValue(peer, out float next) && now < next) return false;
        nextReply[peer] = now + 1;
        return true;
    }
    internal ObserverDiscoverySnapshot Build(ZNetPeer observer, IEnumerable<ZNetPeer> peers, Func<ZNetPeer, bool> isObserver,
        long worldId, float now, bool enabled, bool sharePrivate)
    {
        var players = new List<ObserverPlayer>();
        var regions = new List<ObserverRegion>();
        if (enabled)
        {
            foreach (ZNetPeer peer in peers.Where(peer => peer.IsReady() && !isObserver(peer)).OrderBy(peer => peer.m_uid).Take(ObserverDiscoveryProtocol.MaxPlayers))
            {
                bool hasCharacter = !peer.m_characterID.IsNone();
                ZDO? character = hasCharacter ? ZDOMan.instance?.GetZDO(peer.m_characterID) : null;
                bool spawned = character != null && character.IsValid();
                ObserverPoint? position = null;
                // Metadata freshness establishes that this connected peer is still
                // reporting. The destination itself is its current character ZDO,
                // never the connection's potentially camera-relative reference.
                if (spawned && (sharePrivate || peer.m_publicRefPos) && metadataAt.TryGetValue(peer, out float seen) &&
                    now >= seen && now - seen < ObserverDiscoveryProtocol.FreshSeconds)
                {
                    Vector3 point = character!.GetPosition();
                    if (Finite(point.x) && Finite(point.y) && Finite(point.z)) position = Point(point);
                }
                players.Add(new ObserverPlayer(peer.m_uid.ToString(CultureInfo.InvariantCulture),
                    hasCharacter ? peer.m_characterID.ToString() : "", Clean(peer.m_playerName), position, spawned));
            }
            ObserverRegion[] available = catalog.Values.ToArray();
            if (available.Length > 0)
            {
                int begin = pageStarts.TryGetValue(observer, out int stored) ? stored % available.Length : 0;
                int count = Math.Min(available.Length, ObserverDiscoveryProtocol.MaxRegions);
                for (int i = 0; i < count; i++) regions.Add(available[(begin + i) % available.Length]);
                pageStarts[observer] = (begin + count) % available.Length;
            }
        }
        return new ObserverDiscoverySnapshot(++sequence, worldId, enabled, sharePrivate, players, regions);
    }
    private static ObserverPoint Point(Vector3 point) => new ObserverPoint(point.x, point.y, point.z);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 20000;
    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var result = new System.Text.StringBuilder(64);
        foreach (char character in value!)
        {
            if (char.IsControl(character)) continue;
            if (result.Length >= 64) break;
            result.Append(character);
        }
        // Drop an unmatched high surrogate at the truncation boundary. Invalid
        // network names must not prevent discovery for all connected observers.
        for (int i = 0; i < result.Length; i++)
        {
            if (char.IsHighSurrogate(result[i]))
            {
                if (i + 1 < result.Length && char.IsLowSurrogate(result[i + 1])) { i++; continue; }
                result[i] = '?';
            }
            else if (char.IsLowSurrogate(result[i])) result[i] = '?';
        }
        return result.ToString();
    }
}
