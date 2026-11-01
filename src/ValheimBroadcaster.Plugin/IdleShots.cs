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
    private int sample, attemptsLeft;
    private readonly ScenicVisibility visibility = new ScenicVisibility();
    private Vector3 scenicOffset;
    private float blockedSince = -1, nextViewCheck;
    private bool lastViewClear;
    internal bool Searching => attemptsLeft > 0;
    internal Vector3 ScenicOffset => scenicOffset;
    internal int RejectedViews { get; private set; }
    internal string ViewReason { get; private set; } = "No scenic shot";
    internal string Diagnostics => "Scenic view: " + ViewReason + "; searching " + Searching + "; rejected views " + RejectedViews;
    internal bool HasRegion { get; private set; }
    internal bool HasScenicPath { get; private set; }
    internal bool SceneReady => HasRegion && Ready(surveyOrigin);
    internal void SetRegion(Vector3 origin) { Clear(); surveyOrigin = origin; HasRegion = TargetRoster.Finite(origin); }
    internal void Clear()
    {
        creatures.Clear(); readyIds.Clear(); lastPositions.Clear(); lastMotion.Clear(); recentPlaces.Clear();
        HasRegion = HasScenicPath = false; sample = attemptsLeft = 0; blockedSince = -1; nextViewCheck = 0; lastViewClear = false; RejectedViews = 0; ViewReason = "No scenic shot"; visibility.Reset();
    }

    internal void BeginScenicPath()
    {
        HasScenicPath = false; blockedSince = -1;
        attemptsLeft = SceneReady ? 144 : 0;
        ViewReason = "Searching for a clear scenic composition";
    }

    // Spread the search across updates instead of testing a forest in one frame.
    internal bool Search(BroadcastSettings settings)
    {
        if (!Searching || !SceneReady) { attemptsLeft = 0; return false; }
        visibility.Refresh(Player.m_localPlayer ? Player.m_localPlayer.transform.position : surveyOrigin);
        for (int attempt = 0; attempt < 3 && attemptsLeft > 0; attempt++, attemptsLeft--)
        {
            int index = sample++;
            if (sample > 600000) sample = 0;
            int place = index / 6, view = index % 6;
            float angle = place * 137.50776f * Mathf.Deg2Rad;
            float distance = 18f + place % 5 * 8f;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
            if (!Ground(surveyOrigin + direction * distance, out Vector3 start)) continue;
            bool recent = false;
            foreach (Vector3 previous in recentPlaces) if ((previous - start).sqrMagnitude < 144f) { recent = true; break; }
            if (recent) continue;
            Vector3 tangent = new Vector3(direction.z, 0, -direction.x);
            if (!Ground(start + tangent * 12f, out Vector3 end)) continue;
            if (Mathf.Abs(end.y - start.y) > 8f || !Ground((start + end) * 0.5f, out Vector3 middle) ||
                Mathf.Abs(middle.y - (start.y + end.y) * 0.5f) > 3f) continue;
            float heading = (settings.Azimuth.Value + view * 60f) * Mathf.Deg2Rad;
            float height = Mathf.Min(60f, settings.Height.Value + (view % 3) * 8f);
            Vector3 offset = new Vector3(Mathf.Sin(heading) * settings.Distance.Value, height, Mathf.Cos(heading) * settings.Distance.Value);
            // Five points cover the intended path, not just a clear starting frame.
            bool clear = true;
            for (int point = 0; point < 5; point++)
            {
                Vector3 focus = Vector3.Lerp(start, end, point / 4f) + Vector3.up * 1.5f;
                Vector3 camera = focus + offset;
                if (!Ready(focus) || !Ready(camera) || !Ready(camera + Vector3.down * 2f)) { clear = false; break; }
                if (Player.m_localPlayer && Vector3.Distance(Player.m_localPlayer.transform.position, camera + Vector3.down * 2f) >= 91f)
                { clear = false; break; }
                if (!visibility.Clear(camera, focus, settings.FieldOfView.Value, out string reason))
                { clear = false; ViewReason = reason; RejectedViews++; break; }
            }
            if (!clear) continue;
            pathStart = start; pathEnd = end; scenicOffset = offset;
            HasScenicPath = true; attemptsLeft = 0; nextViewCheck = 0; lastViewClear = true; blockedSince = -1;
            ViewReason = "Clear scenic composition";
            recentPlaces.Enqueue(start);
            if (recentPlaces.Count > 12) recentPlaces.Dequeue();
            return true;
        }
        return false;
    }
    internal Vector3 ScenicFocus(float fraction) =>
        Vector3.Lerp(pathStart, pathEnd, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(fraction))) + Vector3.up * 1.5f;
    internal bool ViewClear(Vector3 camera, Vector3 focus, float fov)
    {
        if (Time.unscaledTime < nextViewCheck) return lastViewClear;
        nextViewCheck = Time.unscaledTime + 0.25f;
        visibility.Refresh(camera);
        lastViewClear = visibility.Clear(camera, focus, fov, out string reason);
        ViewReason = lastViewClear ? "Clear scenic composition" : reason;
        return lastViewClear;
    }
    internal bool ReportPresentation(bool ready, string reason)
    {
        if (!HasScenicPath) return false;
        if (ready) { blockedSince = -1; return false; }
        ViewReason = reason;
        if (blockedSince < 0) blockedSince = Time.unscaledTime;
        if (Time.unscaledTime - blockedSince < 1.5f) return false;
        RejectedViews++; HasScenicPath = false; attemptsLeft = 0; blockedSince = -1;
        return true;
    }
    private static bool Ground(Vector3 point, out Vector3 ground)
    {
        ground = default;
        if (!Ready(point) || !Physics.Raycast(point + Vector3.up * 80f, Vector3.down, out RaycastHit hit,
                240f, LayerMask.GetMask("terrain"), QueryTriggerInteraction.Ignore)) return false;
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
