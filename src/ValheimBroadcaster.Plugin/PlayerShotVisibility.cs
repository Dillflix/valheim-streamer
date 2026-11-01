using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Read-only quality measurements for player shots. A canopy bound is a coarse
// hint, never evidence that a scene is unloaded or that travel is required.
internal sealed class PlayerShotVisibility
{
    private const int Capacity = 512;
    internal const int MaximumSubjects = 6;
    private readonly Subject[] singleSubject = new Subject[1];
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

    internal readonly struct Subject
    {
        internal readonly Vector3 Focus;
        internal readonly float Weight, Radius;
        internal readonly bool Primary;
        internal Subject(Vector3 focus, float weight, float radius, bool primary)
        { Focus = focus; Weight = weight; Radius = radius; Primary = primary; }
    }

    internal readonly struct Result
    {
        internal readonly float Score, SolidOcclusion, FoliageOcclusion, ForegroundOcclusion;
        internal readonly float PrimaryVisibility, GroupVisibility;
        internal readonly bool CameraClear, SurveyComplete, PrimaryInFrame;
        // Input subject indexes that are clearly represented in this actual pose.
        internal readonly int VisibleMask;
        internal readonly string Reason;
        internal Result(float score, bool clear, float solid, float foliage, float foreground,
            bool complete, string reason, bool primaryInFrame = false, float primaryVisibility = 0,
            float groupVisibility = 0, int visibleMask = 0)
        {
            Score = score; CameraClear = clear; SolidOcclusion = solid;
            FoliageOcclusion = foliage; ForegroundOcclusion = foreground;
            SurveyComplete = complete; Reason = reason; PrimaryInFrame = primaryInFrame;
            PrimaryVisibility = primaryVisibility; GroupVisibility = groupVisibility; VisibleMask = visibleMask;
        }
    }

    private readonly struct SubjectMeasure
    {
        internal readonly float Screen, Solid, Leaves, Visibility, Quality, Readability;
        internal SubjectMeasure(float screen, float solid, float leaves, float visibility, float quality, float readability)
        { Screen = screen; Solid = solid; Leaves = leaves; Visibility = visibility; Quality = quality; Readability = readability; }
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

    // Compatibility overload for the fixed center-aimed camera. Procedural views
    // must pass their actual orientation, including during blends and prediction.
    internal Result Evaluate(Vector3 camera, Vector3 focus, float fov, float aspect, float nearClip = 0.3f)
    {
        Vector3 view = focus - camera;
        if (!TargetRoster.Finite(view) || view.sqrMagnitude < 0.25f) return Invalid("Invalid camera direction");
        return Evaluate(camera, Quaternion.LookRotation(view, Vector3.up), focus, fov, aspect, nearClip);
    }

    internal Result Evaluate(Vector3 camera, Quaternion rotation, Vector3 focus, float fov, float aspect, float nearClip = 0.3f)
    {
        singleSubject[0] = new Subject(focus, 1f, 0.6f, true);
        return Evaluate(camera, rotation, singleSubject, fov, aspect, nearClip);
    }

    // All projection, foreground and near-plane measurements use the rendered
    // pose. The first marked primary must remain represented; nearby friends
    // cannot make a shot that loses its primary score highly.
    internal Result Evaluate(Vector3 camera, Quaternion rotation, IReadOnlyList<Subject> subjects,
        float fov, float aspect, float nearClip = 0.3f)
    {
        if (!ValidPose(camera, rotation, fov, aspect, nearClip) || subjects == null ||
            subjects.Count == 0 || subjects.Count > MaximumSubjects) return Invalid("Invalid camera or subject geometry");
        int primary = 0;
        bool marked = false;
        for (int i = 0; i < subjects.Count; i++)
        {
            Subject subject = subjects[i];
            if (!TargetRoster.Finite(subject.Focus) || !Finite(subject.Weight) || subject.Weight < 0 ||
                !Finite(subject.Radius) || subject.Radius <= 0) return Invalid("Invalid subject bounds");
            if (!marked && subject.Primary) { primary = i; marked = true; }
        }
        rotation = rotation.normalized;
        Vector3 focus = subjects[primary].Focus;
        Refresh(focus); RefreshActive();
        Vector3 forward = rotation * Vector3.forward, right = rotation * Vector3.right, up = rotation * Vector3.up;
        float tangent = Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
        // Physical clearance remains separate from readable composition and
        // leaf coverage. An offscreen player does not mean the world unloaded.
        int count = Physics.OverlapSphereNonAlloc(camera, 0.25f, overlaps, obstructionMask, QueryTriggerInteraction.Ignore);
        if (SolidOverlap(count)) return Invalid("Camera intersects solid geometry");
        count = Physics.OverlapBoxNonAlloc(camera + forward * nearClip,
            new Vector3(nearClip * tangent * aspect, nearClip * tangent, 0.04f), overlaps,
            rotation, obstructionMask, QueryTriggerInteraction.Ignore);
        if (SolidOverlap(count)) return Invalid("Camera near plane intersects solid geometry");

        Quaternion inverse = Quaternion.Inverse(rotation);
        SubjectMeasure main = default;
        float groupQuality = 0, groupVisibility = 0, weight = 0, solid = 0, leaves = 0;
        bool complete = Vector3.Distance(camera, origin) + ForegroundRange < coveredRadius;
        int visibleMask = 0;
        for (int i = 0; i < subjects.Count; i++)
        {
            Subject subject = subjects[i];
            SubjectMeasure value = MeasureSubject(camera, inverse, subject, fov, aspect, nearClip);
            if (i == primary) main = value;
            float importance = i == primary ? Mathf.Max(1f, subject.Weight) : Mathf.Clamp(subject.Weight, 0, 2f);
            groupQuality += value.Quality * importance; groupVisibility += value.Visibility * importance;
            solid += value.Solid * importance; leaves += value.Leaves * importance; weight += importance;
            if (value.Screen >= 0.75f && value.Visibility >= 0.6f && value.Readability >= 0.8f)
                visibleMask |= 1 << i;
            complete &= Vector3.Distance(subject.Focus, origin) + subject.Radius * 2f < coveredRadius;
        }
        groupQuality /= weight; groupVisibility /= weight; solid /= weight; leaves /= weight;
        float foreground = 0;
        float distance = Mathf.Max(nearClip, Vector3.Distance(camera, focus));
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            Vector3 direction = (forward + right * (x * 0.6f * tangent * aspect) + up * (y * 0.6f * tangent)).normalized;
            if (FoliageAlong(camera, direction, Mathf.Min(ForegroundRange, distance)))
                foreground += x == 0 && y == 0 ? 4f / 12f : 1f / 12f;
        }
        bool primaryInFrame = main.Screen >= 0.6f;
        float quality = (0.65f * main.Quality + 0.35f * groupQuality) * (1f - 0.35f * foreground);
        if (!complete) quality *= 0.75f;
        // Positive quality for any physically safe, in-frame view preserves a
        // fallback under broad canopy bounds. Lost primaries cannot win on group size.
        quality = primaryInFrame ? Mathf.Clamp(quality, 0.03f, 1f) : 0;
        string reason = !primaryInFrame ? "Primary player outside camera framing" :
            main.Solid > 0.2f ? "Primary player obscured by solid geometry" :
            main.Leaves > 0.2f || foreground > 0.3f ? "Foliage obscures player shot" :
            !complete ? "Foliage survey incomplete" : "Clear player composition";
        return new Result(quality, true, solid, leaves, Mathf.Clamp01(foreground), complete,
            reason, primaryInFrame, main.Visibility, groupVisibility, visibleMask);
    }

    internal float SubjectVisibility(Vector3 camera, Quaternion rotation, Subject subject,
        float fov, float aspect, float nearClip = 0.3f)
    {
        if (!ValidPose(camera, rotation, fov, aspect, nearClip) || !TargetRoster.Finite(subject.Focus) ||
            !Finite(subject.Radius) || subject.Radius <= 0) return 0;
        Refresh(subject.Focus); RefreshActive();
        return MeasureSubject(camera, Quaternion.Inverse(rotation.normalized), subject, fov, aspect, nearClip).Visibility;
    }

    private SubjectMeasure MeasureSubject(Vector3 camera, Quaternion inverse, Subject subject,
        float fov, float aspect, float nearClip)
    {
        // Focus follows the standing-player +1.5m convention. Scale for larger
        // actors; width and projection are bounded rather than collider-dependent.
        float scale = Mathf.Clamp(subject.Radius / 0.6f, 0.5f, 4f);
        float solid = 0, leaves = 0, screen = 0, relativeHeight = 0;
        MeasurePoint(camera, inverse, subject.Focus + Vector3.up * (0.25f * scale), 0.3f, scale,
            fov, aspect, nearClip, ref solid, ref leaves, ref screen, ref relativeHeight);
        MeasurePoint(camera, inverse, subject.Focus + Vector3.down * (0.55f * scale), 0.5f, scale,
            fov, aspect, nearClip, ref solid, ref leaves, ref screen, ref relativeHeight);
        MeasurePoint(camera, inverse, subject.Focus + Vector3.down * (1.2f * scale), 0.2f, scale,
            fov, aspect, nearClip, ref solid, ref leaves, ref screen, ref relativeHeight);
        float readability = Mathf.Clamp01(relativeHeight / 0.02f);
        float visible = screen * (1f - solid) * (1f - 0.65f * leaves) * readability;
        float quality = screen * (1f - 0.7f * solid - 0.45f * leaves * (1f - solid)) * Mathf.Lerp(0.7f, 1f, readability);
        return new SubjectMeasure(screen, solid, leaves, visible, quality, readability);
    }

    private void MeasurePoint(Vector3 camera, Quaternion inverse, Vector3 target, float weight, float scale,
        float fov, float aspect, float nearClip, ref float solid, ref float leaves, ref float screen, ref float relativeHeight)
    {
        Vector3 local = inverse * (target - camera);
        ShotProjection.Result projection = ShotProjection.Measure(local.x, local.y, local.z, 0.25f * scale, fov, aspect, nearClip);
        screen += (float)projection.VisibleFraction * weight;
        relativeHeight = Mathf.Max(relativeHeight, (float)projection.RelativeHeight * 3f);
        if (projection.VisibleFraction > 0) Sample(camera, target, weight, ref solid, ref leaves);
    }

    private void RefreshActive()
    {
        if (checkedFrame == Time.frameCount) return;
        checkedFrame = Time.frameCount;
        for (int i = 0; i < foliage.Count; i++)
        {
            Renderer renderer = foliage[i].Renderer;
            activeFoliage[i] = renderer && renderer.enabled && renderer.gameObject.activeInHierarchy;
        }
    }

    private static bool ValidPose(Vector3 camera, Quaternion rotation, float fov, float aspect, float nearClip)
    {
        float magnitude = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        return TargetRoster.Finite(camera) && Finite(magnitude) && magnitude > 0.0001f && Finite(fov) &&
            Finite(aspect) && Finite(nearClip) && fov >= 1f && fov <= 170f && aspect > 0 && nearClip > 0;
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
