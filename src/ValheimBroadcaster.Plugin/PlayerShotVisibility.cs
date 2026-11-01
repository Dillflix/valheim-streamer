using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// Read-only quality measurements for player shots. A canopy bound is a coarse
// hint, never evidence that a scene is unloaded or that travel is required.
internal sealed class PlayerShotVisibility
{
    private const int Capacity = 512;
    private const float SurveyRadius = 100f, ForegroundRange = 6f;
    private const float RefreshSeconds = 3f, RefreshMovement = 20f;
    private readonly List<FoliageEntry> foliage = new List<FoliageEntry>(Capacity);
    private readonly HashSet<int> groups = new HashSet<int>();
    private readonly bool[] activeFoliage = new bool[Capacity];
    private int checkedFrame = -1;
    private readonly Collider[] overlaps = new Collider[32];
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private Vector3 origin;
    private float refreshedAt = float.NegativeInfinity, coveredRadius;
    private int obstructionMask, furthest;
    private float furthestDistance;

    internal readonly struct Result
    {
        internal readonly float Score, SolidOcclusion, FoliageOcclusion, ForegroundOcclusion;
        internal readonly bool CameraClear, SurveyComplete;
        internal readonly string Reason;
        internal Result(float score, bool clear, float solid, float foliage, float foreground, bool complete, string reason)
        {
            Score = score; CameraClear = clear; SolidOcclusion = solid;
            FoliageOcclusion = foliage; ForegroundOcclusion = foreground;
            SurveyComplete = complete; Reason = reason;
        }
    }

    private readonly struct FoliageEntry
    {
        internal readonly Renderer Renderer;
        internal readonly Bounds Bounds;
        internal readonly float Distance;
        internal FoliageEntry(Renderer renderer, Bounds bounds, float distance)
        { Renderer = renderer; Bounds = bounds; Distance = distance; }
    }

    internal void Reset()
    {
        foliage.Clear(); groups.Clear(); coveredRadius = 0; refreshedAt = float.NegativeInfinity;
        furthest = 0; furthestDistance = 0; checkedFrame = -1;
        Array.Clear(overlaps, 0, overlaps.Length);
        Array.Clear(hits, 0, hits.Length);
    }

    internal void Refresh(Vector3 focus)
    {
        if (!TargetRoster.Finite(focus)) return;
        float now = Time.unscaledTime;
        bool displaced = (focus - origin).sqrMagnitude > RefreshMovement * RefreshMovement;
        if (now >= refreshedAt && now - refreshedAt < RefreshSeconds && !displaced) return;
        // Even multiple candidate evaluations or exceptional target motion cannot
        // initiate repeated whole-scene surveys within a single render frame.
        if (now >= refreshedAt && now - refreshedAt < 0.5f) return;
        origin = focus; refreshedAt = now; coveredRadius = SurveyRadius;
        foliage.Clear(); groups.Clear(); furthest = 0; furthestDistance = 0; checkedFrame = -1;
        obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        // Unity's scene discovery allocates this temporary array only at survey
        // time. Retained bounds and every per-candidate physics buffer are capped.
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!renderer || !renderer.enabled || renderer.bounds.SqrDistance(origin) > SurveyRadius * SurveyRadius ||
                renderer.GetComponentInParent<Character>() || !Vegetation(renderer.transform)) continue;
            LODGroup? group = renderer.GetComponentInParent<LODGroup>();
            if (!group) { Add(renderer); continue; }
            if (!groups.Add(group.GetInstanceID())) continue;
            LOD[] levels = group.GetLODs();
            bool detailed = false;
            if (levels.Length > 0)
                foreach (Renderer detail in levels[0].renderers)
                    if (detail && detail.enabled && detail.gameObject.activeInHierarchy)
                    { Add(detail); detailed = true; }
            if (!detailed)
            {
                groups.Remove(group.GetInstanceID());
                Add(renderer);
            }
        }
    }

    // focus is the standing player's position + 1.5m, matching the camera adapter.
    // Geometry readiness and carrier reachability are deliberately owned by it.
    internal Result Evaluate(Vector3 camera, Vector3 focus, float fov, float aspect, float nearClip = 0.3f)
    {
        if (!TargetRoster.Finite(camera) || !TargetRoster.Finite(focus) || !Finite(fov) ||
            !Finite(aspect) || !Finite(nearClip) || fov < 1f || fov > 170f || aspect <= 0 || nearClip <= 0)
            return Invalid("Invalid camera geometry");
        Vector3 view = focus - camera;
        float distance = view.magnitude;
        if (!Finite(distance) || distance < 0.5f) return Invalid("Camera too close to subject focus or distance invalid");
        Refresh(focus);
        if (checkedFrame != Time.frameCount)
        {
            checkedFrame = Time.frameCount;
            for (int i = 0; i < foliage.Count; i++)
            {
                Renderer renderer = foliage[i].Renderer;
                activeFoliage[i] = renderer && renderer.enabled && renderer.gameObject.activeInHierarchy;
            }
        }
        Vector3 forward = view / distance;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        right.Normalize();
        Vector3 up = Vector3.Cross(forward, right).normalized;
        Quaternion rotation = Quaternion.LookRotation(forward, up);
        float tangent = Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);

        // The near-plane rectangle can clip a branch even with a clear central
        // ray. Check it and the camera origin separately, ignoring characters.
        int count = Physics.OverlapSphereNonAlloc(camera, 0.25f, overlaps, obstructionMask, QueryTriggerInteraction.Ignore);
        if (SolidOverlap(count)) return Invalid("Camera intersects solid geometry");
        count = Physics.OverlapBoxNonAlloc(camera + forward * nearClip,
            new Vector3(nearClip * tangent * aspect, nearClip * tangent, 0.04f), overlaps,
            rotation, obstructionMask, QueryTriggerInteraction.Ignore);
        if (SolidOverlap(count)) return Invalid("Camera near plane intersects solid geometry");

        float solid = 0, leaves = 0;
        Sample(camera, focus + Vector3.up * 0.25f, 0.3f, ref solid, ref leaves);
        Sample(camera, focus + Vector3.down * 0.55f, 0.5f, ref solid, ref leaves);
        Sample(camera, focus + Vector3.down * 1.2f, 0.2f, ref solid, ref leaves);
        float foreground = 0;
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            Vector3 direction = (forward + right * (x * 0.6f * tangent * aspect) + up * (y * 0.6f * tangent)).normalized;
            if (FoliageAlong(camera, direction, Mathf.Min(ForegroundRange, distance)))
                foreground += x == 0 && y == 0 ? 4f / 12f : 1f / 12f;
        }
        bool complete = Vector3.Distance(camera, origin) + ForegroundRange < coveredRadius &&
            Vector3.Distance(focus, origin) + 1.5f < coveredRadius;
        // Leaves without colliders must affect ranking, but broad canopy bounds
        // must not make every otherwise usable forest shot unavailable.
        float quality = 1f - 0.7f * solid - 0.45f * leaves * (1f - solid) - 0.35f * foreground;
        if (!complete) quality *= 0.75f;
        string reason = solid > 0.2f ? "Subject obscured by solid geometry" :
            leaves > 0.2f || foreground > 0.3f ? "Foliage obscures player shot" :
            !complete ? "Foliage survey incomplete" : "Clear player shot";
        return new Result(Mathf.Clamp(quality, 0.03f, 1f), true, solid, leaves,
            Mathf.Clamp01(foreground), complete, reason);
    }

    // Reused by the adapter when validating the actual interpolated movement.
    // This checks physical safety only; foliage never blocks carrier movement.
    internal bool SegmentClear(Vector3 from, Vector3 to)
    {
        if (!TargetRoster.Finite(from) || !TargetRoster.Finite(to)) return false;
        if (obstructionMask == 0) obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        Vector3 delta = to - from;
        if (!TargetRoster.Finite(delta) || !Finite(delta.sqrMagnitude)) return false;
        if (delta.sqrMagnitude <= 0.0001f) return true;
        int count = Physics.SphereCastNonAlloc(from, 0.25f, delta.normalized, hits,
            delta.magnitude, obstructionMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        for (int i = 0; i < count; i++) if (Solid(hits[i].collider)) return false;
        return true;
    }

    private void Sample(Vector3 camera, Vector3 target, float weight, ref float solid, ref float leaves)
    {
        Vector3 delta = target - camera;
        float distance = delta.magnitude;
        if (distance <= 0.05f) return;
        Vector3 direction = delta / distance;
        int count = Physics.RaycastNonAlloc(camera, direction, hits, Mathf.Max(0, distance - 0.05f),
            obstructionMask, QueryTriggerInteraction.Ignore);
        bool blocked = count == hits.Length; // A saturated query is not proof of clearance.
        for (int i = 0; i < count && !blocked; i++) blocked = Solid(hits[i].collider);
        if (blocked) solid += weight;
        if (FoliageAlong(camera, direction, distance)) leaves += weight;
    }

    private bool FoliageAlong(Vector3 camera, Vector3 direction, float distance)
    {
        var ray = new Ray(camera, direction);
        Vector3 end = camera + direction * distance;
        Vector3 rayMin = Vector3.Min(camera, end), rayMax = Vector3.Max(camera, end);
        for (int i = 0; i < foliage.Count; i++)
        {
            if (!activeFoliage[i]) continue;
            Bounds bound = foliage[i].Bounds;
            Vector3 min = bound.min, max = bound.max;
            // Cheap segment bounds prune most trees before native AABB ray tests.
            if (max.x < rayMin.x || min.x > rayMax.x || max.y < rayMin.y || min.y > rayMax.y ||
                max.z < rayMin.z || min.z > rayMax.z) continue;
            if (bound.Contains(camera) || bound.IntersectRay(ray, out float hit) && hit < distance) return true;
        }
        return false;
    }

    private bool SolidOverlap(int count)
    {
        if (count == overlaps.Length) return true;
        for (int i = 0; i < count; i++) if (Solid(overlaps[i])) return true;
        return false;
    }

    private static bool Solid(Collider collider) => collider && collider.enabled &&
        !collider.isTrigger && !collider.GetComponentInParent<Character>();

    private static bool Vegetation(Transform transform)
    {
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

    private void Add(Renderer renderer)
    {
        Bounds bounds = renderer.bounds;
        if (!TargetRoster.Finite(bounds.center) || !TargetRoster.Finite(bounds.extents) || bounds.size.sqrMagnitude < 0.01f) return;
        bounds.Expand(0.4f);
        float distance = Mathf.Sqrt(bounds.SqrDistance(origin));
        if (distance >= SurveyRadius) return;
        var entry = new FoliageEntry(renderer, bounds, distance);
        if (foliage.Count < Capacity)
        {
            foliage.Add(entry);
            if (distance >= furthestDistance) { furthest = foliage.Count - 1; furthestDistance = distance; }
            return;
        }
        // Retain nearest bounds and remember where omitted coverage begins.
        if (distance >= furthestDistance) { coveredRadius = Mathf.Min(coveredRadius, distance); return; }
        coveredRadius = Mathf.Min(coveredRadius, furthestDistance);
        foliage[furthest] = entry; furthestDistance = 0;
        for (int i = 0; i < foliage.Count; i++)
            if (foliage[i].Distance >= furthestDistance) { furthest = i; furthestDistance = foliage[i].Distance; }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static Result Invalid(string reason) => new Result(0, false, 1, 0, 0, false, reason);
}
