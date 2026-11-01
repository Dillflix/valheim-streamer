using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// Conservative scenic-shot screening. Renderer bounds cover foliage that has no
// physics collider; they are not an alpha-accurate measurement of rendered leaves.
internal sealed class ScenicVisibility
{
    private const int Capacity = 512;
    private const float SurveyRadius = 110f, ForegroundRange = 6f;
    private readonly List<Bounds> foliage = new List<Bounds>(Capacity);
    private readonly List<float> distances = new List<float>(Capacity);
    private readonly HashSet<int> groups = new HashSet<int>();
    private Vector3 origin;
    private float refreshedAt = float.NegativeInfinity, coveredRadius;
    private int furthest;
    private float furthestDistance;
    private int obstructionMask;

    internal void Reset()
    {
        foliage.Clear(); distances.Clear(); groups.Clear(); refreshedAt = float.NegativeInfinity;
        coveredRadius = 0; furthest = 0; furthestDistance = 0;
    }

    internal void Refresh(Vector3 surveyOrigin)
    {
        float now = Time.unscaledTime;
        if (now >= refreshedAt && now - refreshedAt < 5f && (surveyOrigin - origin).sqrMagnitude <= 1600f) return;
        origin = surveyOrigin; refreshedAt = now; coveredRadius = SurveyRadius;
        foliage.Clear(); distances.Clear(); groups.Clear(); furthest = 0; furthestDistance = 0;
        obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        // Scene-wide enumeration is deliberately outside the per-frame evaluator.
        // Only active renderers are returned. No prefab assets are inspected.
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!renderer || !renderer.enabled || renderer.bounds.SqrDistance(origin) > 112f * 112f ||
                !Vegetation(renderer.transform)) continue;
            LODGroup? group = renderer.GetComponentInParent<LODGroup>();
            if (group)
            {
                if (!groups.Add(group.GetInstanceID())) continue;
                LOD[] levels = group.GetLODs();
                // Higher LODs describe the same canopy; retain the detailed level
                // once rather than filling the cache with duplicate tree bounds.
                bool detailed = false;
                if (levels.Length > 0)
                {
                    foreach (Renderer detail in levels[0].renderers)
                        if (detail && detail.enabled && detail.gameObject.activeInHierarchy)
                        { Add(detail.bounds); detailed = true; }
                }
                if (!detailed)
                {
                    // An empty/inactive first LOD must not hide a visible lower
                    // LOD from the survey. Let other renderers in this group in.
                    groups.Remove(group.GetInstanceID());
                    Add(renderer.bounds);
                }
            }
            else Add(renderer.bounds);
        }
    }

    private static bool Vegetation(Transform transform)
    {
        // Trees, bushes and leaf meshes have different collider arrangements.
        // An LODGroup alone is not vegetation: buildings and rocks use it too.
        for (Transform? current = transform; current; current = current.parent)
        {
            string name = current.name;
            if (name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("shrub", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("leaves", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("foliage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("sapling", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return transform.GetComponentInParent<TreeBase>() || transform.GetComponentInParent<TreeLog>();
    }

    private void Add(Bounds bounds)
    {
        if (!TargetRoster.Finite(bounds.center) || !TargetRoster.Finite(bounds.extents) || bounds.size.sqrMagnitude < 0.01f) return;
        bounds.Expand(0.4f); // Clearance from shader-animated leaf edges.
        float distance = Mathf.Sqrt(bounds.SqrDistance(origin));
        if (distance >= SurveyRadius) return;
        if (foliage.Count < Capacity)
        {
            foliage.Add(bounds); distances.Add(distance);
            if (distance >= furthestDistance) { furthest = foliage.Count - 1; furthestDistance = distance; }
            return;
        }
        // Keep the nearest bounds. Retain a conservative coverage radius so a
        // truncated cache never silently declares an unexamined position clear.
        if (distance >= furthestDistance) { coveredRadius = Mathf.Min(coveredRadius, distance); return; }
        coveredRadius = Mathf.Min(coveredRadius, furthestDistance);
        foliage[furthest] = bounds; distances[furthest] = distance;
        furthestDistance = 0;
        for (int i = 0; i < distances.Count; i++)
            if (distances[i] >= furthestDistance) { furthest = i; furthestDistance = distances[i]; }
    }

    internal bool Clear(Vector3 camera, Vector3 focus, float fov, out string reason)
    {
        reason = "clear";
        if (!TargetRoster.Finite(camera) || !TargetRoster.Finite(focus) || float.IsNaN(fov) || float.IsInfinity(fov) || fov <= 0 || fov >= 180)
        { reason = "Invalid scenic camera geometry"; return false; }
        if (Vector3.Distance(camera, origin) + ForegroundRange >= coveredRadius)
        { reason = "Scenic position outside surveyed foliage coverage"; return false; }
        if (Physics.CheckSphere(camera, 0.6f, obstructionMask, QueryTriggerInteraction.Ignore))
        { reason = "Scenic camera intersects solid geometry"; return false; }
        Vector3 view = focus - camera;
        float distance = view.magnitude;
        if (distance < 2f) { reason = "Scenic shot is too compressed"; return false; }
        Vector3 forward = view / distance;
        // Match the live rig's full focus-to-camera clearance test so a chosen
        // path is not immediately rejected by a different radius or endpoint.
        if (Physics.SphereCast(focus, 0.3f, -forward, out RaycastHit _, distance, obstructionMask, QueryTriggerInteraction.Ignore))
        { reason = "Scenic subject is hidden by solid geometry"; return false; }
        foreach (Bounds bound in foliage)
            if (bound.Contains(camera)) { reason = "Scenic camera is inside vegetation bounds"; return false; }
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(forward, right).normalized;
        float vertical = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        int blocked = 0;
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            Vector3 direction = (forward + right * (x * 0.65f * vertical * aspect) + up * (y * 0.65f * vertical)).normalized;
            Ray ray = new Ray(camera, direction);
            foreach (Bounds bound in foliage)
            {
                if (bound.SqrDistance(camera) > ForegroundRange * ForegroundRange || !bound.IntersectRay(ray, out float hit) || hit > ForegroundRange) continue;
                blocked += x == 0 && y == 0 ? 4 : 1;
                break;
            }
            if (blocked >= 6) { reason = "Foreground vegetation covers most of the scenic frame"; return false; }
        }
        return true;
    }
}
