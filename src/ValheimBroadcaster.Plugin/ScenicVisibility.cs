using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Conservative scenic-shot screening. Local triangle groups or submesh bounds
// refine nearby foliage; unreadable/unsupported renderers retain coarse bounds.
// This is not an alpha-accurate measurement of rendered leaves.
internal sealed class ScenicVisibility
{
    private const int Capacity = 512;
    private const float SurveyRadius = 110f, ForegroundRange = 6f, DetailRadius = 16f, ReuseDistance = 8f;
    private const int DetailBudget = 1024;
    private readonly List<Entry> foliage = new List<Entry>(Capacity);
    private readonly ScenicFoliageGeometry geometry = new ScenicFoliageGeometry();
    private readonly List<float> distances = new List<float>(Capacity);
    private readonly HashSet<int> groups = new HashSet<int>();
    private Vector3 origin;
    private float refreshedAt = float.NegativeInfinity, coveredRadius;
    private int furthest;
    private float furthestDistance;
    private int obstructionMask;
    private readonly Collider[] overlaps = new Collider[32];
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private readonly bool[] foregroundBounds = new bool[Capacity + DetailBudget];
    private readonly Bounds[] activeBounds = new Bounds[Capacity + DetailBudget];
    private int detailRemaining, checkedFrame = -1, activeCount;

    private readonly struct Entry
    {
        internal readonly Renderer Renderer;
        internal readonly Bounds[]? Local;
        internal Entry(Renderer renderer, Bounds[]? local)
        { Renderer = renderer; Local = local; }
    }

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
        geometry.Reset(); checkedFrame = -1; activeCount = 0;
    }

    internal void Refresh(Vector3 surveyOrigin)
    {
        float now = Time.unscaledTime;
        // Candidate poses must not trigger repeated whole-scene surveys in one
        // render frame when opposite viewpoints cross the refresh distance.
        if (now >= refreshedAt && now - refreshedAt < 0.5f) return;
        float displacement = Vector3.Distance(surveyOrigin, origin);
        // Reuse stays inside the detailed foreground neighborhood. A truncated
        // nearest-renderer set cannot certify an omitted foreground, so refresh
        // as soon as the minimum survey throttle permits in that case too.
        if (now >= refreshedAt && now - refreshedAt < 5f && displacement <= ReuseDistance &&
            displacement + ForegroundRange < coveredRadius) return;
        origin = surveyOrigin; refreshedAt = now; coveredRadius = SurveyRadius;
        foliage.Clear(); distances.Clear(); groups.Clear(); furthest = 0; furthestDistance = 0;
        geometry.BeginSurvey(); detailRemaining = DetailBudget; checkedFrame = -1;
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
                        { Add(detail); detailed = true; }
                }
                if (!detailed)
                {
                    // An empty/inactive first LOD must not hide a visible lower
                    // LOD from the survey. Let other renderers in this group in.
                    groups.Remove(group.GetInstanceID());
                    Add(renderer);
                }
            }
            else Add(renderer);
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

    private void Add(Renderer renderer)
    {
        Bounds coarse = renderer.bounds;
        // Only the camera's local foreground needs fine geometry. Far bounds
        // remain a bounded discovery envelope and are refined after recentering.
        if (coarse.SqrDistance(origin) <= DetailRadius * DetailRadius && detailRemaining > 0)
        {
            Bounds[]? cells = geometry.Get(renderer);
            if (cells != null && cells.Length <= detailRemaining)
            {
                detailRemaining -= cells.Length;
                Add(new Entry(renderer, cells), coarse);
                return;
            }
        }
        Add(new Entry(renderer, null), coarse);
    }

    private void Add(Entry entry, Bounds bounds)
    {
        if (!TargetRoster.Finite(bounds.center) || !TargetRoster.Finite(bounds.extents) || bounds.size.sqrMagnitude < 0.01f) return;
        bounds.Expand(0.4f); // Clearance from shader-animated leaf edges.
        float distance = Mathf.Sqrt(bounds.SqrDistance(origin));
        if (distance >= SurveyRadius) return;
        if (foliage.Count < Capacity)
        {
            foliage.Add(entry); distances.Add(distance);
            if (distance >= furthestDistance) { furthest = foliage.Count - 1; furthestDistance = distance; }
            return;
        }
        // Keep the nearest bounds. Retain a conservative coverage radius so a
        // truncated cache never silently declares an unexamined position clear.
        if (distance >= furthestDistance) { coveredRadius = Mathf.Min(coveredRadius, distance); return; }
        coveredRadius = Mathf.Min(coveredRadius, furthestDistance);
        foliage[furthest] = entry; distances[furthest] = distance;
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
        Refresh(camera); RefreshActive();
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
        // A far coarse canopy is discovery evidence, not proof that a queried
        // camera lies inside leaves. Hold the same candidate until the bounded
        // survey can recenter and inspect its own local foreground.
        float requiredRadius = Vector3.Distance(camera, origin) + ForegroundRange;
        bool complete = requiredRadius < coveredRadius && requiredRadius < DetailRadius;
        float foreground = 0;
        bool insideLeaves = false;
        for (int i = 0; i < activeCount; i++)
        {
            Bounds bound = activeBounds[i];
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
                for (int i = 0; i < activeCount; i++)
                {
                    if (!foregroundBounds[i] || !activeBounds[i].IntersectRay(ray, out float hit) || hit > ForegroundRange) continue;
                    blocked += x == 0 && y == 0 ? 4 : 1;
                    break;
                }
            }
            foreground = blocked / 12f;
        }
        float score = (focusVisible ? 1f : 0.3f) * (1f - 0.85f * foreground) * (complete ? 1f : 0.65f);
        string reason = !complete ? "Scenic foliage survey incomplete" : insideLeaves ? "Scenic camera intersects retained vegetation bounds" :
            foreground >= 0.5f ? "Foreground vegetation dominates the scenic frame" :
            !focusVisible ? "Scenic feature is outside the frame or hidden" : "Readable scenic composition";
        return new Result(true, focusVisible, complete, Mathf.Clamp01(score), foreground, reason);
    }

    private void RefreshActive()
    {
        if (checkedFrame == Time.frameCount) return;
        checkedFrame = Time.frameCount; activeCount = 0;
        for (int i = 0; i < foliage.Count; i++)
        {
            Entry entry = foliage[i]; Renderer renderer = entry.Renderer;
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (entry.Local == null) AddActive(renderer.bounds);
            else
            {
                Matrix4x4 matrix = renderer.localToWorldMatrix;
                foreach (Bounds local in entry.Local) AddActive(FoliageGeometryMath.Transform(local, matrix));
            }
        }
    }

    private void AddActive(Bounds bound)
    {
        if (!FoliageGeometryMath.Valid(bound)) return;
        bound.Expand(.4f); activeBounds[activeCount++] = bound;
    }

    // Only a measured camera-inside-canopy rejection may propose this alternate
    // elevation. A missing/throttled survey cannot invent an escape. The caller
    // must validate the entire raised route, including surface, foreground,
    // focus, corridor and observer reach, before displaying any of it.
    internal bool TryCanopyLift(Vector3 camera, out float lift)
    {
        lift = 0;
        if (!TargetRoster.Finite(camera)) return false;
        Refresh(camera); RefreshActive();
        float requiredRadius = Vector3.Distance(camera, origin) + ForegroundRange;
        bool complete = requiredRadius < coveredRadius && requiredRadius < DetailRadius;
        return FoliageGeometryMath.TryCanopyLift(camera, activeBounds, activeCount,
            complete, ForegroundRange, out lift);
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
        {
            Collider hit = hits[i].collider;
            if (!Solid(hit)) continue;
            // Buried rocks and foundations often enclose terrain in their
            // bounds. That terrain is still an obstruction, never the visible
            // surface of the intended object.
            if (hit.GetComponentInParent<Heightmap>() || hit.gameObject.layer == LayerMask.NameToLayer("terrain") ||
                !subjectBounds.HasValue || !subjectBounds.Value.Contains(hits[i].point)) return false;
        }
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
