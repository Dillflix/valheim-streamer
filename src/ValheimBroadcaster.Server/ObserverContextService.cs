using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Server;

// Polls only existing replicated character ZDOs. Never instantiates a region,
// takes ownership, runs AI, or interprets health loss as proof of combat.
internal sealed class ObserverContextService
{
    private sealed class Observation
    {
        internal ZDOID Character;
        internal uint Revision;
        internal Vector3 Position;
        internal ObserverPoint? Velocity;
        internal float StateAt, HeartbeatAt = -1, HealthLossAt = -1;
        internal float? Health;
    }
    private readonly Dictionary<ZNetPeer, Observation> samples = new Dictionary<ZNetPeer, Observation>();
    private readonly Dictionary<ZNetPeer, float> nextReply = new Dictionary<ZNetPeer, float>();
    private long sequence;
    internal void Reset() { Suspend(); sequence = 0; }
    internal void Suspend() { samples.Clear(); nextReply.Clear(); }
    internal void ReceivedMetadata(ZNetPeer peer, float now)
    {
        if (!samples.TryGetValue(peer, out Observation? sample))
        {
            if (samples.Count >= ObserverContextProtocol.MaxPlayers) return;
            samples.Add(peer, sample = new Observation());
        }
        sample.HeartbeatAt = now;
    }
    internal bool MayReply(ZNetPeer peer, float now)
    {
        if (nextReply.TryGetValue(peer, out float next)) { if (now < next) return false; }
        else if (nextReply.Count >= ObserverContextProtocol.MaxPlayers) return false;
        nextReply[peer] = now + 1; return true;
    }
    internal void Tick(IReadOnlyList<ZNetPeer> peers, Func<ZNetPeer, bool> observer, float now, bool enabled)
    {
        var live = new HashSet<ZNetPeer>(peers);
        foreach (ZNetPeer gone in samples.Keys.Where(peer => !live.Contains(peer)).ToArray()) samples.Remove(gone);
        foreach (ZNetPeer gone in nextReply.Keys.Where(peer => !live.Contains(peer)).ToArray()) nextReply.Remove(gone);
        if (!enabled) { samples.Clear(); return; }
        foreach (ZNetPeer peer in peers.Where(peer => peer.IsReady() && !observer(peer)).Take(ObserverContextProtocol.MaxPlayers))
        {
            if (peer.m_characterID.IsNone() || !samples.TryGetValue(peer, out Observation? sample)) continue;
            ZDO? zdo = ZDOMan.instance?.GetZDO(peer.m_characterID);
            if (zdo == null || !zdo.IsValid()) continue;
            Vector3 position = zdo.GetPosition();
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z)) continue;
            bool changed = sample.Character != peer.m_characterID;
            if (!changed && sample.Revision == zdo.DataRevision) continue;
            float elapsed = now - sample.StateAt;
            Vector3 movement = position - sample.Position;
            if (!changed && elapsed >= 0.1f && elapsed <= 5f && movement.magnitude <= 30f * elapsed)
            {
                Vector3 velocity = movement / elapsed;
                sample.Velocity = new ObserverPoint(velocity.x, velocity.y, velocity.z);
            }
            else sample.Velocity = null;
            // The actual Character.GetHealth / SetHealth methods use this ZDO key.
            // Missing fields remain unknown instead of fabricating a maximum health.
            float health = zdo.GetFloat(ZDOVars.s_health, -1f);
            float? knownHealth = !float.IsNaN(health) && !float.IsInfinity(health) && health >= 0 ? health : (float?)null;
            if (changed) sample.HealthLossAt = -1;
            else if (knownHealth.HasValue && sample.Health.HasValue && knownHealth.Value + 0.5f < sample.Health.Value)
                sample.HealthLossAt = now;
            sample.Character = peer.m_characterID; sample.Revision = zdo.DataRevision;
            sample.Position = position; sample.StateAt = now; sample.Health = knownHealth;
        }
    }
    internal ObserverContextSnapshot Build(IReadOnlyList<ZNetPeer> peers, Func<ZNetPeer, bool> observer,
        long worldId, float now, bool enabled, bool sharePrivate)
    {
        var players = new List<ObserverPlayerContext>();
        if (enabled)
            foreach (ZNetPeer peer in peers.Where(peer => peer.IsReady() && !observer(peer)).OrderBy(peer => peer.m_uid).Take(ObserverContextProtocol.MaxPlayers))
            {
                if ((!sharePrivate && !peer.m_publicRefPos) || !samples.TryGetValue(peer, out Observation? sample) ||
                    sample.HeartbeatAt < 0 || peer.m_characterID.IsNone() || sample.Character != peer.m_characterID) continue;
                ZDO? current = ZDOMan.instance?.GetZDO(peer.m_characterID);
                if (current == null || !current.IsValid()) continue;
                players.Add(new ObserverPlayerContext(peer.m_uid.ToString(CultureInfo.InvariantCulture), sample.Character.ToString(),
                    new ObserverPoint(sample.Position.x, sample.Position.y, sample.Position.z), sample.Velocity,
                    Age(now, sample.StateAt), Age(now, sample.HeartbeatAt), sample.HealthLossAt < 0 ? null : (float?)Age(now, sample.HealthLossAt)));
            }
        return new ObserverContextSnapshot(++sequence, worldId, enabled, players);
    }
    private static float Age(float now, float then) => Mathf.Clamp(now - then, 0, ObserverContextProtocol.MaximumAgeSeconds);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 20000;
}
