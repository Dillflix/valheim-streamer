using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

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
    private readonly Collider[] overlaps = new Collider[32];
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private readonly bool[] foregroundBounds = new bool[Capacity];

    internal readonly struct Result
    {
        internal readonly bool CameraClear, FocusVisible, SurveyComplete;
        internal readonly float Score, ForegroundOcclusion;
        internal readonly string Reason;
        internal Result(bool clear, bool visible, bool complete, float score, float foreground, string reason)
        { CameraClear = clear; FocusVisible = visible; SurveyComplete = complete;
          Score = score; ForegroundOcclusion = foreground; Reason = reason; }
    }

    internal void Reset()
    {
        foliage.Clear(); distances.Clear(); groups.Clear(); refreshedAt = float.NegativeInfinity;
        coveredRadius = 0; furthest = 0; furthestDistance = 0;
    }

    internal void Refresh(Vector3 surveyOrigin)
    {
        float now = Time.unscaledTime;
        // Candidate poses must not trigger repeated whole-scene surveys in one
        // render frame when opposite viewpoints cross the refresh distance.
        if (now >= refreshedAt && now - refreshedAt < 0.5f) return;
        float displacement = Vector3.Distance(surveyOrigin, origin);
        // In a dense forest the retained nearest-renderer set may cover less
        // than 40m. The ordinary movement threshold cannot certify an omitted
        // foreground: refresh as soon as the minimum survey throttle permits.
        if (now >= refreshedAt && now - refreshedAt < 5f && displacement <= 40f &&
            displacement + ForegroundRange < coveredRadius) return;
        origin = surveyOrigin; refreshedAt = now; coveredRadius = SurveyRadius;
        foliage.Clear(); distances.Clear(); groups.Clear(); furthest = 0; furthestDistance = 0;
        obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        // Scene-wide enumeration is deliberately outside the per-frame evaluator.
        // Only active renderers are returned. No prefab assets are inspected.
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!renderer || !renderer.enabled || renderer.bounds.SqrDistance(origin) > 112f * 112f ||
                renderer.GetComponentInParent<Character>() || !Vegetation(renderer.transform)) continue;
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

    // Landscape quality uses the actual presented orientation/lens. Foliage and
    // focus visibility are editorial evidence, never physical area readiness.
    internal Result Evaluate(Vector3 camera, Quaternion rotation, Vector3 focus, float fov, float aspect, float nearClip,
        Bounds? subjectBounds = null)
    {
        float magnitude = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        if (!TargetRoster.Finite(camera) || !TargetRoster.Finite(focus) || !Finite(magnitude) || magnitude < 0.0001f ||
            !Finite(fov) || fov < 1 || fov > 170 || !Finite(aspect) || aspect <= 0 || !Finite(nearClip) || nearClip <= 0)
            return Invalid("Invalid scenic camera geometry");
        Refresh(camera);
        rotation = rotation.normalized;
        Vector3 forward = rotation * Vector3.forward, right = rotation * Vector3.right, up = rotation * Vector3.up;
        float vertical = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        int count = Physics.OverlapSphereNonAlloc(camera, 0.25f, overlaps, obstructionMask, QueryTriggerInteraction.Ignore);
        if (SolidOverlap(count)) return Invalid("Scenic camera intersects solid geometry");
        count = Physics.OverlapBoxNonAlloc(camera + forward * nearClip,
            new Vector3(nearClip * vertical * aspect, nearClip * vertical, 0.04f), overlaps,
            rotation, obstructionMask, QueryTriggerInteraction.Ignore);
        if (SolidOverlap(count)) return Invalid("Scenic near plane intersects solid geometry");

        Vector3 local = Quaternion.Inverse(rotation) * (focus - camera);
        ShotProjection.Result projected = ShotProjection.Measure(local.x, local.y, local.z, 0, fov, aspect, nearClip);
        bool focusVisible = projected.CenterInFrame && FocusClear(camera, focus, subjectBounds);
        bool complete = Vector3.Distance(camera, origin) + ForegroundRange < coveredRadius;
        float foreground = 0;
        bool insideLeaves = false;
        for (int i = 0; i < foliage.Count; i++)
        {
            Bounds bound = foliage[i];
            foregroundBounds[i] = bound.SqrDistance(camera) <= ForegroundRange * ForegroundRange;
            if (bound.Contains(camera)) insideLeaves = true;
        }
        if (insideLeaves) foreground = 1;
        else
        {
            int blocked = 0;
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                Vector3 direction = (forward + right * (x * 0.65f * vertical * aspect) + up * (y * 0.65f * vertical)).normalized;
                Ray ray = new Ray(camera, direction);
                for (int i = 0; i < foliage.Count; i++)
                {
                    if (!foregroundBounds[i] || !foliage[i].IntersectRay(ray, out float hit) || hit > ForegroundRange) continue;
                    blocked += x == 0 && y == 0 ? 4 : 1;
                    break;
                }
            }
            foreground = blocked / 12f;
        }
        float score = (focusVisible ? 1f : 0.3f) * (1f - 0.85f * foreground) * (complete ? 1f : 0.65f);
        string reason = !complete ? "Scenic foliage survey incomplete" : insideLeaves ? "Scenic camera is inside vegetation bounds" :
            foreground >= 0.5f ? "Foreground vegetation dominates the scenic frame" :
            !focusVisible ? "Scenic feature is outside the frame or hidden" : "Readable scenic composition";
        return new Result(true, focusVisible, complete, Mathf.Clamp01(score), foreground, reason);
    }

    // Explicit legacy/fixed camera comparison still uses a centered view.
    internal bool Clear(Vector3 camera, Vector3 focus, float fov, out string reason)
    {
        if ((focus - camera).sqrMagnitude < 0.01f) { reason = "Invalid scenic camera direction"; return false; }
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        Result result = Evaluate(camera, Quaternion.LookRotation(focus - camera, Vector3.up), focus, fov, aspect, 0.3f);
        reason = result.Reason;
        return result.CameraClear && result.FocusVisible && result.Score >= 0.5f;
    }

    internal bool SegmentClear(Vector3 from, Vector3 to)
    {
        if (!TargetRoster.Finite(from) || !TargetRoster.Finite(to)) return false;
        if (obstructionMask == 0) obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        Vector3 delta = to - from;
        if (delta.sqrMagnitude <= 0.0001f) return true;
        int count = Physics.SphereCastNonAlloc(from, 0.25f, delta.normalized, hits, delta.magnitude,
            obstructionMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        for (int i = 0; i < count; i++) if (Solid(hits[i].collider)) return false;
        return true;
    }

    internal bool NearPlaneCorridorClear(Vector3 from, Quaternion fromRotation, Vector3 to, Quaternion toRotation,
        float fov, float aspect, float nearClip)
    {
        if (obstructionMask == 0) obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        float height = nearClip * Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
        float width = height * aspect;
        Vector3 first = from + fromRotation * (Vector3.forward * nearClip);
        Vector3 last = to + toRotation * (Vector3.forward * nearClip);
        Vector3 delta = last - first;
        Quaternion middle = Quaternion.Slerp(fromRotation, toRotation, .5f);
        // The midpoint-oriented box encloses the rotating near rectangle. The
        // extra near-distance margin also bounds its curved centre trajectory.
        float padding = (new Vector3(width, height, .04f).magnitude + nearClip) *
            Mathf.Sin(Quaternion.Angle(fromRotation, toRotation) * .5f * Mathf.Deg2Rad);
        Vector3 extents = new Vector3(width + padding, height + padding, .04f + padding);
        int count = Physics.OverlapBoxNonAlloc(first, extents, overlaps, middle, obstructionMask, QueryTriggerInteraction.Ignore);
        if (SolidOverlap(count)) return false;
        if (delta.sqrMagnitude <= .000001f) return true;
        count = Physics.BoxCastNonAlloc(first, extents, delta.normalized, hits, middle, delta.magnitude,
            obstructionMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        for (int i = 0; i < count; i++) if (Solid(hits[i].collider)) return false;
        return true;
    }

    internal static Quaternion Compose(Vector3 camera, Vector3 focus, float screenX, float screenY, float fov, float aspect)
    {
        Quaternion centered = Quaternion.LookRotation(focus - camera, Vector3.up);
        float tangent = Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
        return centered * Quaternion.Euler(Mathf.Atan(2 * screenY * tangent) * Mathf.Rad2Deg,
            -Mathf.Atan(2 * screenX * tangent * aspect) * Mathf.Rad2Deg, 0);
    }

    private bool FocusClear(Vector3 camera, Vector3 focus, Bounds? subjectBounds)
    {
        Vector3 delta = focus - camera;
        float distance = delta.magnitude;
        if (distance < 0.5f) return false;
        int count = Physics.RaycastNonAlloc(camera, delta / distance, hits, Mathf.Max(0, distance - 0.25f),
            obstructionMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        // A rock or building is framed around its measured extent; the sightline
        // should terminate on its surface instead of requiring a ray through it
        // to its centre. Unrelated foreground hits outside that extent still
        // reject the shot. Body, near-plane and foliage checks are unchanged.
        for (int i = 0; i < count; i++)
            if (Solid(hits[i].collider) && (!subjectBounds.HasValue || !subjectBounds.Value.Contains(hits[i].point))) return false;
        return true;
    }

    private bool SolidOverlap(int count)
    {
        if (count == overlaps.Length) return true;
        for (int i = 0; i < count; i++) if (Solid(overlaps[i])) return true;
        return false;
    }
    private static bool Solid(Collider collider) => collider && collider.enabled && !collider.isTrigger &&
        !collider.GetComponentInParent<Character>();
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static Result Invalid(string reason) => new Result(false, false, false, 0, 0, reason);
}
