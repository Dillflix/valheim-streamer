using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Server;

// Observed world activity, not proof that a user has stopped providing input.
// Inventory/menu use and no-station crafting can be silent in vanilla. We read
// existing ZDOs and item prefab metadata only; no region/object is instantiated.
internal sealed class ObserverActivityService
{
    private sealed class Sample
    {
        internal readonly ObservedIdleClock Clock = new ObservedIdleClock();
        internal ZDOID Character;
        internal Vector3 PositionAnchor, ReferenceAnchor;
        internal float SampleAt = -1, HeartbeatAt = -1, Health;
        internal float MovementAt = -1, ActionAt = -1, HealthLossAt = -1, WorkAt = -1;
        internal int EmoteId;
        internal bool HasAnchor;
    }
    private readonly Dictionary<ZNetPeer, Sample> samples = new Dictionary<ZNetPeer, Sample>();
    private readonly Dictionary<ZNetPeer, float> replies = new Dictionary<ZNetPeer, float>();
    private readonly Dictionary<ZNetPeer, float> eventReplies = new Dictionary<ZNetPeer, float>();
    private long sequence, eventSequence;
    private static readonly HashSet<int> ManualMethods = new HashSet<int>(new[]
    {
        // Audited player interaction callers, excluding autonomous effects,
        // ownership requests and AI/world simulation emitted by the same client.
        "RPC_RequestOpen", "RPC_RequestStack", "RPC_RequestTakeAll", "UseDoor",
        "RPC_Pick", "Pick", "RPC_AddOre", "RPC_EmptyProcessed", "RPC_AddFuel",
        "RPC_Tap", "RPC_RequestIncinerate", "ToggleEnabled", "TogglePermitted"
    }.Select(name => name.GetStableHashCode()));

    internal void Reset() { Suspend(); sequence = eventSequence = 0; }
    internal void Suspend() { samples.Clear(); replies.Clear(); eventReplies.Clear(); }
    private Sample? Get(ZNetPeer peer)
    {
        if (samples.TryGetValue(peer, out Sample? sample)) return sample;
        if (samples.Count >= ObserverActivityProtocol.MaxPlayers) return null;
        samples.Add(peer, sample = new Sample()); return sample;
    }
    internal void ReceivedMetadata(ZNetPeer peer, float now)
    {
        Sample? sample = Get(peer);
        if (sample == null) return;
        // The native handler has already parsed m_refPos from this authenticated
        // peer's two-second ServerSyncedPlayerData heartbeat.
        sample.HeartbeatAt = now;
        if (!Position(peer.m_refPos)) { sample.Clock.Observe(now, false, false); sample.HasAnchor = false; return; }
        if (sample.HasAnchor && Vector3.Distance(sample.ReferenceAnchor, peer.m_refPos) >= .6f)
        {
            sample.Clock.Activity(now); sample.ReferenceAnchor = peer.m_refPos;
            if (sample.Character == peer.m_characterID) sample.MovementAt = now;
        }
    }
    internal bool MayReply(ZNetPeer peer, float now)
    {
        if (replies.TryGetValue(peer, out float next)) { if (now < next) return false; }
        else if (replies.Count >= ObserverActivityProtocol.MaxPlayers) return false;
        replies[peer] = now + 1; return true;
    }
    internal bool MayReplyEvents(ZNetPeer peer, float now)
    {
        if (eventReplies.TryGetValue(peer, out float next)) { if (now < next) return false; }
        else if (eventReplies.Count >= ObserverEventProtocol.MaxPlayers) return false;
        eventReplies[peer] = now + 1; return true;
    }
    internal void RoutedAction(ZNetPeer peer, ZPackage package, float now)
    {
        int position = package.GetPos();
        try
        {
            if (package.Size() > 65536 || peer.m_characterID.IsNone()) return;
            package.ReadLong(); // message ID
            long sender = package.ReadLong(); package.ReadLong(); // recipient
            ZDOID target = package.ReadZDOID();
            int method = package.ReadInt(), size = package.ReadInt();
            if (sender != peer.m_uid || target.IsNone() || size < 0 || size != package.Size() - package.GetPos()) return;
            // No argument decode or allocation is necessary: observing a bounded
            // request is enough to conservatively cancel idle, never grant it.
            bool trigger = target == peer.m_characterID && method == "SetTrigger".GetStableHashCode();
            if (trigger || ManualMethods.Contains(method))
            {
                Sample? sample = Get(peer);
                if (sample != null)
                {
                    sample.Clock.Activity(now);
                    // Trigger identity is deliberately not decoded into an attack:
                    // jump/emote and manual interactions are action evidence too.
                    if (sample.Character == peer.m_characterID) sample.ActionAt = now;
                }
            }
        }
        catch { /* Observation must never change native ordinary-peer handling. */ }
        finally { package.SetPos(position); }
    }
    internal void Tick(IReadOnlyList<ZNetPeer> peers, Func<ZNetPeer, bool> observer, float now, bool enabled)
    {
        var live = new HashSet<ZNetPeer>(peers);
        foreach (ZNetPeer gone in samples.Keys.Where(peer => !live.Contains(peer)).ToArray()) samples.Remove(gone);
        foreach (ZNetPeer gone in replies.Keys.Where(peer => !live.Contains(peer)).ToArray()) replies.Remove(gone);
        foreach (ZNetPeer gone in eventReplies.Keys.Where(peer => !live.Contains(peer)).ToArray()) eventReplies.Remove(gone);
        if (!enabled) { samples.Clear(); return; }
        foreach (ZNetPeer peer in peers.Where(peer => peer.IsReady() && !observer(peer)).Take(ObserverActivityProtocol.MaxPlayers))
        {
            Sample? sample = Get(peer);
            if (sample == null) continue;
            ZDO? zdo = peer.m_characterID.IsNone() ? null : ZDOMan.instance?.GetZDO(peer.m_characterID);
            bool known = zdo != null && zdo.IsValid() && sample.HeartbeatAt >= 0 &&
                now >= sample.HeartbeatAt && now - sample.HeartbeatAt < ObserverActivityProtocol.FreshSeconds && Position(peer.m_refPos);
            if (!known) { Unknown(sample, now); continue; }
            Vector3 position = zdo!.GetPosition(), velocity = zdo.GetVec3(ZDOVars.s_bodyVelocity, Vector3.zero);
            float health = zdo.GetFloat(ZDOVars.s_health, -1);
            if (!Position(position) || !Vector(velocity, 100) || !Finite(health) || health <= 0 || !TryBusy(zdo, out bool busy))
            { Unknown(sample, now); continue; }
            bool changed = sample.Character != peer.m_characterID || !sample.HasAnchor;
            int emote = zdo.GetInt(ZDOVars.s_emoteID, 0);
            bool moved = !changed && (Vector3.Distance(position, sample.PositionAnchor) >= .6f ||
                Vector3.Distance(peer.m_refPos, sample.ReferenceAnchor) >= .6f);
            bool activity = changed || moved || velocity.sqrMagnitude >= .04f || busy ||
                !changed && (health < sample.Health - .1f || emote != sample.EmoteId);
            if (changed)
            {
                sample.Clock.Reset();
                sample.MovementAt = sample.ActionAt = sample.HealthLossAt = sample.WorkAt = -1;
            }
            if (moved || velocity.sqrMagnitude >= .04f) sample.MovementAt = now;
            if (!changed && health < sample.Health - .1f) sample.HealthLossAt = now;
            if (!changed && emote != sample.EmoteId || busy) sample.ActionAt = now;
            if (zdo.GetInt(AnimationKey("crafting"), 0) != 0) sample.WorkAt = now;
            // Compare to a retained quiet anchor, not the previous sample: small
            // steps accumulate while stationary replication jitter stays bounded.
            if (changed || moved || activity)
            { sample.PositionAnchor = position; sample.ReferenceAnchor = peer.m_refPos; }
            sample.Character = peer.m_characterID; sample.HasAnchor = true; sample.Health = health; sample.EmoteId = emote;
            sample.SampleAt = now; sample.Clock.Observe(now, true, activity);
        }
    }
    private static void Unknown(Sample sample, float now)
    {
        sample.Clock.Observe(now, false, false); sample.HasAnchor = false; sample.SampleAt = now;
        sample.MovementAt = sample.ActionAt = sample.HealthLossAt = sample.WorkAt = -1;
    }

    private static int AnimationKey(string name) => unchecked(438569 + ZSyncAnimation.GetHash(name));
    private static bool TryBusy(ZDO zdo, out bool busy)
    {
        busy = zdo.GetBool(ZDOVars.s_isBlockingHash, false) || zdo.GetBool(ZDOVars.s_dodgeinv, false) ||
            zdo.GetInt(AnimationKey("crafting"), 0) != 0;
        // Starting/stopping an emote changes emoteID and resets the clock in
        // Tick; an unchanged sitting pose must eventually qualify as idle.
        // drawpercent is deliberately excluded: vanilla does not clear it on
        // release. Read the equipped item's real draw-state boolean instead.
        return DrawState(zdo, zdo.GetInt(ZDOVars.s_rightItem, 0), ref busy) &&
            DrawState(zdo, zdo.GetInt(ZDOVars.s_leftItem, 0), ref busy);
    }
    private static bool DrawState(ZDO zdo, int itemHash, ref bool busy)
    {
        if (itemHash == 0) return true;
        if (!ObjectDB.instance) return false;
        GameObject? prefab = ObjectDB.instance.GetItemPrefab(itemHash);
        ItemDrop? item = prefab ? prefab.GetComponent<ItemDrop>() : null;
        if (!item || item.m_itemData?.m_shared == null) return false;
        Attack? attack = item.m_itemData.m_shared.m_attack;
        string? draw = attack?.m_drawAnimationState;
        if (!string.IsNullOrEmpty(draw))
        {
            if (draw!.Length > 128) return false;
            busy |= zdo.GetInt(AnimationKey(draw), 0) != 0;
        }
        return true;
    }
    internal ObserverActivitySnapshot Build(IReadOnlyList<ZNetPeer> peers, Func<ZNetPeer, bool> observer,
        long worldId, float now, bool enabled)
    {
        var players = new List<ObserverPlayerActivity>();
        if (!enabled) return new ObserverActivitySnapshot(++sequence, worldId, false, false, players);
        ZNetPeer[] ordinary = peers.Where(peer => peer.IsReady() && !observer(peer)).OrderBy(peer => peer.m_uid).ToArray();
        var seenCharacters = new HashSet<ZDOID>();
        bool complete = ordinary.Length <= ObserverActivityProtocol.MaxPlayers;
        foreach (ZNetPeer peer in ordinary.Take(ObserverActivityProtocol.MaxPlayers))
        {
            samples.TryGetValue(peer, out Sample? sample);
            bool identity = !peer.m_characterID.IsNone() && seenCharacters.Add(peer.m_characterID);
            if (!peer.m_characterID.IsNone() && !identity) complete = false;
            bool known = identity && sample != null && sample.Character == peer.m_characterID && sample.Clock.Known &&
                sample.SampleAt >= 0 && now - sample.SampleAt < ObserverActivityProtocol.FreshSeconds &&
                sample.HeartbeatAt >= 0 && now - sample.HeartbeatAt < ObserverActivityProtocol.FreshSeconds;
            players.Add(new ObserverPlayerActivity(peer.m_uid.ToString(CultureInfo.InvariantCulture),
                identity ? peer.m_characterID.ToString() : "", known, known ? (float)sample!.Clock.ObservedIdleSeconds : 0,
                Age(now, sample?.SampleAt ?? -1), Age(now, sample?.HeartbeatAt ?? -1)));
        }
        return new ObserverActivitySnapshot(++sequence, worldId, true, complete, players);
    }
    internal ObserverEventSnapshot BuildEvents(IReadOnlyList<ZNetPeer> peers, Func<ZNetPeer, bool> observer,
        long worldId, float now, bool enabled, bool sharePrivate)
    {
        var players = new List<ObserverPlayerEvents>();
        if (!enabled) return new ObserverEventSnapshot(++eventSequence, worldId, false, players);
        var seenCharacters = new HashSet<ZDOID>();
        foreach (ZNetPeer peer in peers.Where(peer => peer.IsReady() && !observer(peer)).OrderBy(peer => peer.m_uid).Take(ObserverEventProtocol.MaxPlayers))
        {
            if (!sharePrivate && !peer.m_publicRefPos) continue;
            samples.TryGetValue(peer, out Sample? sample);
            bool identity = !peer.m_characterID.IsNone() && seenCharacters.Add(peer.m_characterID);
            bool known = identity && sample != null && sample.Character == peer.m_characterID && sample.Clock.Known &&
                sample.SampleAt >= 0 && now >= sample.SampleAt && now - sample.SampleAt < ObserverEventProtocol.FreshSeconds &&
                sample.HeartbeatAt >= 0 && now >= sample.HeartbeatAt && now - sample.HeartbeatAt < ObserverEventProtocol.FreshSeconds;
            players.Add(new ObserverPlayerEvents(peer.m_uid.ToString(CultureInfo.InvariantCulture),
                identity ? peer.m_characterID.ToString() : "", known, Age(now, sample?.SampleAt ?? -1), Age(now, sample?.HeartbeatAt ?? -1),
                known ? EventAge(now, sample!.MovementAt) : null, known ? EventAge(now, sample!.ActionAt) : null,
                known ? EventAge(now, sample!.HealthLossAt) : null, known ? EventAge(now, sample!.WorkAt) : null));
        }
        return new ObserverEventSnapshot(++eventSequence, worldId, true, players);
    }
    private static float? EventAge(float now, float then) => then >= 0 && now >= then && now - then <= ObserverEventProtocol.EventHistorySeconds
        ? now - then : (float?)null;
    private static float Age(float now, float then) => then < 0 ? 3600 : Mathf.Clamp(now - then, 0, 3600);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Vector(Vector3 value, float bound) => Finite(value.x) && Finite(value.y) && Finite(value.z) &&
        Mathf.Abs(value.x) <= bound && Mathf.Abs(value.y) <= bound && Mathf.Abs(value.z) <= bound;
    private static bool Position(Vector3 value) => Vector(value, 20000);
}
