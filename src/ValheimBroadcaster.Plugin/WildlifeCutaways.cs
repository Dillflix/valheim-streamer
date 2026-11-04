using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Global authorization comes from the companion's complete cohort. Loaded
// actors can veto it sooner, but cannot stand in for missing distant evidence.
internal sealed class WildlifeCutaways
{
    private sealed class LocalSample
    {
        internal Player Actor = null!;
        internal Vector3 Anchor;
        internal float Health, LastActive, SampleAt;
        internal int Emote;
    }
    private readonly WildlifeCutawayPolicy policy = new WildlifeCutawayPolicy();
    private readonly List<WildlifePlayerEvidence> evidence = new List<WildlifePlayerEvidence>(64);
    private readonly Dictionary<string, LocalSample> local = new Dictionary<string, LocalSample>(StringComparer.Ordinal);
    private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> removed = new List<string>(64);
    private ZNet? world;
    private bool manualOverride;
    private static readonly int Crafting = unchecked(438569 + ZSyncAnimation.GetHash("crafting"));
    internal bool Allowed => policy.AllowCutaway;
    internal string Diagnostics { get; private set; } = "Wildlife: waiting for activity evidence";

    internal bool Update(TargetRoster roster, RemoteObserverClient remote, bool suppressed, bool active)
    {
        if (world != ZNet.instance) { Reset(); world = ZNet.instance; }
        suppressed |= manualOverride; manualOverride = false;
        float now = Time.realtimeSinceStartup;
        evidence.Clear(); seen.Clear();
        ObserverActivitySnapshot? snapshot = remote.Activity;
        bool complete = remote.DiscoveryFresh && remote.ActivityFresh && snapshot?.Enabled == true && snapshot.Complete &&
            snapshot.WorldId == ZNet.instance.GetWorldUID() && snapshot.Players.Count == roster.ConnectedOthers &&
            roster.Targets.Count == roster.ConnectedOthers;
        double age = remote.ActivityAge;
        foreach (TargetRoster.Target target in roster.Targets.Values)
        {
            string id = target.Identity.Id;
            seen.Add(id);
            ObserverPlayerActivity? source = null;
            if (snapshot != null)
                foreach (ObserverPlayerActivity item in snapshot.Players)
                    if (item.Id == id) { source = item; break; }
            bool known = source != null && source.CharacterId == target.CharacterId && source.Fresh(age);
            double idle = known ? source!.ObservedIdleSeconds : 0;
            double freshness = known ? ObserverActivityProtocol.FreshSeconds - age -
                Math.Max(source!.SampleAgeSeconds, source.HeartbeatAgeSeconds) : 0;
            bool localActive = false;
            Player? actor = target.LoadedPlayer;
            if (actor)
            {
                if (!TargetRoster.Finite(actor.transform.position) || actor.IsDead() || actor.IsTeleporting() || actor.GetZDOID().IsNone())
                    known = false;
                else
                {
                    ZDO? zdo = actor.GetComponent<ZNetView>()?.GetZDO();
                    float health = actor.GetHealth();
                    int emote = zdo?.GetInt(ZDOVars.s_emoteID, 0) ?? 0;
                    bool freshLocal = local.TryGetValue(id, out LocalSample? sample) && sample.Actor == actor &&
                        now >= sample.SampleAt && now - sample.SampleAt < 1f;
                    if (!freshLocal)
                    {
                        // First local sighting may use the independently sampled
                        // server history. Otherwise normal 30s player rotation
                        // could prevent a distant cohort ever reaching 60s.
                        bool stalled = sample != null && sample.Actor == actor;
                        sample = new LocalSample { Actor = actor, Anchor = actor.transform.position,
                            Health = health, LastActive = stalled ? now : now - (float)idle, Emote = emote };
                        local[id] = sample;
                    }
                    bool moved = Vector3.Distance(sample!.Anchor, actor.transform.position) >= .15f;
                    localActive = moved || actor.InAttack() || actor.IsBlocking() || actor.InDodge() ||
                        PlayerSceneContext.IsDrawingRanged(actor) ||
                        health + .5f < sample.Health || emote != sample.Emote || (zdo?.GetInt(Crafting, 0) ?? 0) != 0;
                    if (localActive) { sample.LastActive = now; sample.Anchor = actor.transform.position; }
                    sample.Health = health; sample.Emote = emote; sample.SampleAt = now;
                    idle = Math.Min(idle, Math.Max(0, now - sample.LastActive));
                }
            }
            else local.Remove(id);
            if (!known) complete = false;
            evidence.Add(new WildlifePlayerEvidence(id, !known ? WildlifePlayerActivity.Unknown :
                localActive ? WildlifePlayerActivity.Active : WildlifePlayerActivity.Idle, idle, now + Math.Max(0, freshness)));
        }
        removed.Clear();
        foreach (string id in local.Keys) if (!seen.Contains(id)) removed.Add(id);
        foreach (string id in removed) local.Remove(id);
        bool result = policy.Update(now, evidence, complete, suppressed, active);
        Diagnostics = "Wildlife: " + policy.Reason + "; all-player observed idle " + policy.IdleSeconds.ToString("F1") +
            "/60s; rearm " + policy.RearmRemainingSeconds.ToString("F0") + "s; " + remote.ActivityStatus;
        return result;
    }

    internal void Reset()
    {
        policy.Reset(); local.Clear(); evidence.Clear(); seen.Clear(); removed.Clear(); world = null;
        manualOverride = false;
        Diagnostics = "Wildlife: waiting for activity evidence";
    }

    internal void CancelForManual() => manualOverride = true;
}
