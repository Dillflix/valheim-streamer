using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// Survey origin bounds searches to the currently loaded region; it is never the
// camera's focus. Scenic shots use changing ground paths with recent-location exclusion.
internal sealed class IdleShots
{
    private readonly Dictionary<string, Character> creatures = new Dictionary<string, Character>(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> lastPositions = new Dictionary<string, Vector3>(StringComparer.Ordinal);
    private readonly Dictionary<string, float> lastMotion = new Dictionary<string, float>(StringComparer.Ordinal);
    private readonly List<string> readyIds = new List<string>(32);
    private readonly Queue<Vector3> recentPlaces = new Queue<Vector3>(12);
    private float radius = 50;
    private Vector3 surveyOrigin, pathStart, pathEnd;
    private int sample;
    internal bool HasRegion { get; private set; }
    internal bool HasScenicPath { get; private set; }
    internal bool SceneReady => HasRegion && Ready(surveyOrigin);
    internal void SetRegion(Vector3 origin) { Clear(); surveyOrigin = origin; HasRegion = TargetRoster.Finite(origin); }
    internal void Clear()
    {
        creatures.Clear(); readyIds.Clear(); lastPositions.Clear(); lastMotion.Clear(); recentPlaces.Clear();
        HasRegion = HasScenicPath = false; sample = 0;
    }

    internal bool BeginScenicPath()
    {
        HasScenicPath = false;
        if (!SceneReady) return false;
        // Bounded search, changing angular/radial samples on every attempt. No orbit.
        for (int attempt = 0; attempt < 24; attempt++)
        {
            int index = sample++;
            if (sample > 100000) sample = 0;
            float angle = index * 137.50776f * Mathf.Deg2Rad;
            float distance = 18f + index % 5 * 8f;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            Vector3 candidate = surveyOrigin + direction * distance;
            if (!Ground(candidate, out Vector3 start)) continue;
            bool recent = false;
            foreach (Vector3 previous in recentPlaces) if ((previous - start).sqrMagnitude < 144f) { recent = true; break; }
            if (recent) continue;
            Vector3 tangent = new Vector3(direction.z, 0, -direction.x);
            if (!Ground(start + tangent * 12f, out Vector3 end)) continue;
            if (Mathf.Abs(end.y - start.y) > 8f || !Ground((start + end) * 0.5f, out Vector3 middle) ||
                Mathf.Abs(middle.y - (start.y + end.y) * 0.5f) > 3f) continue;
            pathStart = start; pathEnd = end; HasScenicPath = true;
            recentPlaces.Enqueue(start);
            if (recentPlaces.Count > 12) recentPlaces.Dequeue();
            return true;
        }
        return false; // Do not disguise exhaustion as an endlessly repeated location.
    }
    internal Vector3 ScenicFocus(float fraction) => Vector3.Lerp(pathStart, pathEnd, Mathf.Clamp01(fraction)) + Vector3.up * 1.5f;
    private static bool Ground(Vector3 point, out Vector3 ground)
    {
        ground = default;
        if (!Ready(point) || !Physics.Raycast(point + Vector3.up * 80f, Vector3.down, out RaycastHit hit,
                240f, LayerMask.GetMask("terrain", "static_solid", "piece"), QueryTriggerInteraction.Ignore)) return false;
        ground = hit.point;
        return Ready(ground);
    }

    internal void Refresh(float searchRadius)
    {
        radius = searchRadius;
        creatures.Clear();
        if (!SceneReady) { lastPositions.Clear(); lastMotion.Clear(); return; }
        foreach (Character actor in Character.GetAllCharacters())
        {
            if (!Eligible(actor) || actor.GetZDOID().IsNone()) continue;
            string id = actor.GetZDOID().ToString();
            creatures[id] = actor;
            if (!lastPositions.TryGetValue(id, out Vector3 previous) || (previous - actor.transform.position).sqrMagnitude >= 0.25f)
            { lastPositions[id] = actor.transform.position; lastMotion[id] = Time.unscaledTime; }
            if (creatures.Count >= 32) break;
        }
        var gone = new List<string>();
        foreach (string id in lastPositions.Keys) if (!creatures.ContainsKey(id)) gone.Add(id);
        foreach (string id in gone) { lastPositions.Remove(id); lastMotion.Remove(id); }
    }

    internal IReadOnlyList<string> ReadyIds()
    {
        readyIds.Clear();
        foreach (var entry in creatures) if (Eligible(entry.Value, false) && Moving(entry.Key)) readyIds.Add(entry.Key);
        return readyIds;
    }
    private bool Moving(string id) => lastMotion.TryGetValue(id, out float moved) && Time.unscaledTime - moved < 10f;
    internal Character? Find(string? id) => id != null && creatures.TryGetValue(id, out Character? actor) && Eligible(actor) && Moving(id) ? actor : null;
    private bool Eligible(Character actor, bool checkScene = true) => actor && actor.isActiveAndEnabled && !actor.IsPlayer() && !actor.IsDead() &&
        TargetRoster.Finite(actor.transform.position) && (actor.transform.position - surveyOrigin).sqrMagnitude <= radius * radius && (!checkScene || Ready(actor.transform.position));
    internal static bool Ready(Vector3 position) => TargetRoster.Finite(position) && ZNetScene.instance && ZNetScene.instance.IsAreaReady(position);
}
