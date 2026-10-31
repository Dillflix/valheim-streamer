using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// Session-only anchor and loaded-creature bindings. No spawned actors, persistent
// world coordinates, global object scans, or network reference changes.
internal sealed class IdleShots
{
    private readonly Dictionary<string, Character> creatures = new Dictionary<string, Character>(StringComparer.Ordinal);
    private readonly List<string> readyIds = new List<string>(32);
    private float radius = 50;
    internal Vector3 Anchor { get; private set; }
    internal bool HasAnchor { get; private set; }
    internal bool SceneReady => HasAnchor && Ready(Anchor);
    internal void SetAnchor(Vector3 anchor) { Clear(); Anchor = anchor; HasAnchor = TargetRoster.Finite(anchor); }
    internal void Clear() { creatures.Clear(); readyIds.Clear(); HasAnchor = false; }

    internal void Refresh(float searchRadius)
    {
        radius = searchRadius;
        creatures.Clear();
        if (!SceneReady) return;
        foreach (Character actor in Character.GetAllCharacters())
        {
            if (!Eligible(actor) || actor.GetZDOID().IsNone()) continue;
            creatures[actor.GetZDOID().ToString()] = actor;
            if (creatures.Count >= 32) break;
        }
    }

    internal IReadOnlyList<string> ReadyIds()
    {
        readyIds.Clear();
        foreach (var entry in creatures) if (Eligible(entry.Value)) readyIds.Add(entry.Key);
        return readyIds;
    }
    internal Character? Find(string? id) => id != null && creatures.TryGetValue(id, out Character? actor) && Eligible(actor) ? actor : null;
    private bool Eligible(Character actor) => actor && actor.isActiveAndEnabled && !actor.IsPlayer() && !actor.IsDead() &&
        TargetRoster.Finite(actor.transform.position) && (actor.transform.position - Anchor).sqrMagnitude <= radius * radius && Ready(actor.transform.position);
    internal static bool Ready(Vector3 position) => TargetRoster.Finite(position) && ZNetScene.instance && ZNetScene.instance.IsAreaReady(position);
}
