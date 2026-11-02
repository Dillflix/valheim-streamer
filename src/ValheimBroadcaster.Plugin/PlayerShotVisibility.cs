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
    private readonly ShotProjection.Point[] bodyCorners = new ShotProjection.Point[8];
    private const float SurveyRadius = 100f, ForegroundRange = 10f;
    private const float RefreshSeconds = 3f, RefreshMovement = 10f, MinimumSurveySeconds = 1.5f;
    private readonly List<FoliageEntry> foliage = new List<FoliageEntry>(Capacity);
    private readonly HashSet<int> groups = new HashSet<int>();
    private readonly bool[] activeFoliage = new bool[Capacity];
    private readonly Bounds[] activeBounds = new Bounds[Capacity];
    private readonly int[] viewFoliage = new int[Capacity];
    private int viewFoliageCount;
    private bool refreshRequested;
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
        internal readonly float Weight, Radius, Height, BottomOffset;
        internal readonly bool Primary;
        internal Subject(Vector3 focus, float weight, float radius, bool primary, float height = 0, float bottomOffset = -1)
        {
            Focus = focus; Weight = weight; Radius = radius; Primary = primary;
            float scale = Mathf.Clamp(radius / 0.6f, 0.5f, 4f);
            Height = height > 0 ? height : 1.85f * scale;
            BottomOffset = bottomOffset >= 0 ? bottomOffset : 1.5f * scale;
        }
    }

    internal readonly struct Result
    {
        internal readonly float Score, SolidOcclusion, FoliageOcclusion, ForegroundOcclusion;
        internal readonly float PrimaryVisibility, GroupVisibility;
        internal readonly float PrimarySolidOcclusion, PrimaryFoliageOcclusion;
        internal readonly float PrimaryReadability, PrimarySafeFrame, PrimaryHeadroom, PrimaryFrameHeight;
        internal readonly float PrimaryScreenX, PrimaryScreenY, PrimaryFocusX, PrimaryFocusY;
        internal readonly bool CameraClear, SurveyComplete, PrimaryInFrame;
        // Input subject indexes that are clearly represented in this actual pose.
        internal readonly int VisibleMask;
        internal readonly string Reason;
        internal Result(float score, bool clear, float solid, float foliage, float foreground,
            bool complete, string reason, bool primaryInFrame = false, float primaryVisibility = 0,
            float groupVisibility = 0, int visibleMask = 0, ShotProjection.Envelope primaryEnvelope = default,
            ShotProjection.Result primaryFocus = default, float primarySolid = 0, float primaryFoliage = 0)
        {
            Score = score; CameraClear = clear; SolidOcclusion = solid;
            FoliageOcclusion = foliage; ForegroundOcclusion = foreground;
            SurveyComplete = complete; Reason = reason; PrimaryInFrame = primaryInFrame;
            PrimaryVisibility = primaryVisibility; GroupVisibility = groupVisibility; VisibleMask = visibleMask;
            PrimarySolidOcclusion = primarySolid; PrimaryFoliageOcclusion = primaryFoliage;
            PrimaryReadability = (float)primaryEnvelope.Readability; PrimarySafeFrame = (float)primaryEnvelope.SafeFraction;
            PrimaryHeadroom = (float)primaryEnvelope.Headroom; PrimaryFrameHeight = (float)primaryEnvelope.RelativeHeight;
            PrimaryScreenX = (float)primaryEnvelope.X; PrimaryScreenY = (float)primaryEnvelope.Y;
            PrimaryFocusX = (float)primaryFocus.X; PrimaryFocusY = (float)primaryFocus.Y;
        }

        internal PictureHealth Picture(bool scenic = false) => scenic
            ? PictureQuality.Scenic(CameraClear, PrimaryInFrame, Score, ForegroundOcclusion, SurveyComplete)
            : PictureQuality.Actor(CameraClear, PrimaryInFrame, PrimaryVisibility, PrimaryReadability,
                PrimarySolidOcclusion, PrimaryFoliageOcclusion, ForegroundOcclusion, SurveyComplete);
    }

    private readonly struct SubjectMeasure
    {
        internal readonly float Screen, Solid, Leaves, Visibility, Quality, Readability;
        internal readonly ShotProjection.Envelope Envelope;
        internal SubjectMeasure(ShotProjection.Envelope envelope, float solid, float leaves, float visibility, float quality)
        {
            Envelope = envelope; Screen = (float)envelope.VisibleFraction; Solid = solid; Leaves = leaves;
            Visibility = visibility; Quality = quality; Readability = (float)envelope.Readability;
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
        refreshRequested = false;
        Array.Clear(overlaps, 0, overlaps.Length);
        Array.Clear(hits, 0, hits.Length);
    }

    internal void Refresh(Vector3 focus)
    {
        if (!TargetRoster.Finite(focus)) return;
        float now = Time.unscaledTime;
        bool displaced = (focus - origin).sqrMagnitude > RefreshMovement * RefreshMovement;
        if (now >= refreshedAt && now - refreshedAt < RefreshSeconds && !displaced && !refreshRequested) return;
        // Even multiple candidate evaluations or exceptional target motion cannot
        // initiate repeated whole-scene surveys within a single render frame.
        if (now >= refreshedAt && now - refreshedAt < MinimumSurveySeconds) return;
        origin = focus; refreshedAt = now; coveredRadius = SurveyRadius;
        refreshRequested = false;
        foliage.Clear(); groups.Clear(); furthest = 0; furthestDistance = 0; checkedFrame = -1;
        obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        // Unity's scene discovery allocates this temporary array only at survey
        // time. Retained bounds and every per-candidate physics buffer are capped.
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!renderer || !renderer.enabled || renderer.bounds.SqrDistance(origin) > SurveyRadius * SurveyRadius ||
                renderer.GetComponentInParent<Character>() || !Vegetation(renderer)) continue;
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

    // Recovery can ask for newly loaded/moved foliage to be surveyed sooner;
    // Refresh still limits whole-scene discovery to once per 1.5 seconds.
    internal void RequestSurveyRefresh() => refreshRequested = true;

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
                !ValidSubject(subject)) return Invalid("Invalid subject bounds");
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
        float farthest = ForegroundRange;
        for (int i = 0; i < subjects.Count; i++)
        {
            Subject subject = subjects[i];
            farthest = Mathf.Max(farthest, Vector3.Distance(camera, subject.Focus) + subject.Height + subject.Radius * 2);
        }
        PrepareFoliage(camera, rotation, tangent, aspect, farthest);
        SubjectMeasure main = default;
        float groupQuality = 0, groupVisibility = 0, weight = 0, solid = 0, leaves = 0;
        // Only the queried segments need guaranteed coverage. Requiring a full
        // foreground-radius sphere around the camera wrongly labels many dense
        // but completely surveyed camera-to-subject paths as unknown.
        bool complete = Vector3.Distance(camera, origin) < coveredRadius;
        int visibleMask = 0;
        for (int i = 0; i < subjects.Count; i++)
        {
            Subject subject = subjects[i];
            SubjectMeasure value = MeasureSubject(camera, inverse, subject, fov, aspect, nearClip, i == primary);
            if (i == primary) main = value;
            float importance = i == primary ? Mathf.Max(1f, subject.Weight) : Mathf.Clamp(subject.Weight, 0, 2f);
            groupQuality += value.Quality * importance; groupVisibility += value.Visibility * importance;
            solid += value.Solid * importance; leaves += value.Leaves * importance; weight += importance;
            if (value.Screen >= 0.85f && value.Visibility >= PictureQuality.MinimumActorVisibility &&
                value.Readability >= PictureQuality.MinimumActorReadability && value.Envelope.Headroom >= 0 &&
                value.Solid <= PictureQuality.MaximumPrimarySolidOcclusion && value.Leaves <= PictureQuality.MaximumPrimaryFoliageOcclusion)
                visibleMask |= 1 << i;
            complete &= Vector3.Distance(subject.Focus, origin) + Mathf.Max(subject.Radius * 2f, subject.Height) < coveredRadius;
        }
        groupQuality /= weight; groupVisibility /= weight; solid /= weight; leaves /= weight;
        float foreground = 0;
        float distance = Mathf.Max(nearClip, Vector3.Distance(camera, focus));
        // A denser screen-space grid detects foreground sheets around a clear
        // center ray. The full-width body samples below remain the main signal.
        for (int y = -2; y <= 2; y++)
        for (int x = -2; x <= 2; x++)
        {
            // Thirteen samples: 3x3 central coverage plus four outer midsides.
            // Peripheral corners do not need the same density as the subject.
            if (Math.Abs(x) + Math.Abs(y) > 2) continue;
            Vector3 direction = (forward + right * (x * 0.35f * tangent * aspect) + up * (y * 0.35f * tangent)).normalized;
            float range = Mathf.Min(ForegroundRange, distance);
            complete &= Vector3.Distance(camera + direction * range, origin) < coveredRadius;
            if (FoliageAlong(camera, direction, range))
                foreground += Math.Abs(x) <= 1 && Math.Abs(y) <= 1 ? 2f / 22f : 1f / 22f;
        }
        bool primaryInFrame = main.Screen >= 0.6f;
        // Group coverage may distinguish equally readable primary shots, but
        // cannot rescue a tiny, clipped or heavily obscured primary actor.
        float quality = main.Quality * (0.7f + 0.3f * groupQuality) * (1f - 0.35f * foreground);
        if (!complete) quality *= 0.75f;
        // Positive quality for any physically safe, in-frame view preserves a
        // fallback under broad canopy bounds. Lost primaries cannot win on group size.
        quality = primaryInFrame ? Mathf.Clamp(quality, 0.03f, 1f) : 0;
        if (!complete || foreground > PictureQuality.MaximumForegroundOcclusion) visibleMask = 0;
        string reason = !primaryInFrame ? "Primary player outside camera framing" :
            main.Readability < 0.8f ? "Primary player too small for readable coverage" :
            main.Envelope.VisibleFraction < .98 ? "Primary body clipped by camera frame" :
            main.Envelope.HeadroomScore < .8 ? "Primary player needs headroom" :
            main.Envelope.SafeFraction < .9 ? "Primary player near frame edge" :
            main.Solid > 0.2f ? "Primary player obscured by solid geometry" :
            main.Leaves > 0.2f || foreground > 0.3f ? "Foliage obscures player shot" :
            !complete ? "Foliage survey incomplete" : "Clear player composition";
        Vector3 localFocus = inverse * (focus - camera);
        ShotProjection.Result projectedFocus = ShotProjection.Measure(localFocus.x, localFocus.y, localFocus.z, 0, fov, aspect, nearClip);
        return new Result(quality, true, solid, leaves, Mathf.Clamp01(foreground), complete,
            reason, primaryInFrame, main.Visibility, groupVisibility, visibleMask, main.Envelope, projectedFocus,
            main.Solid, main.Leaves);
    }

    internal float SubjectVisibility(Vector3 camera, Quaternion rotation, Subject subject,
        float fov, float aspect, float nearClip = 0.3f)
    {
        if (!ValidPose(camera, rotation, fov, aspect, nearClip) || !TargetRoster.Finite(subject.Focus) ||
            !ValidSubject(subject)) return 0;
        Refresh(subject.Focus); RefreshActive();
        rotation = rotation.normalized;
        PrepareFoliage(camera, rotation, Mathf.Tan(fov * Mathf.Deg2Rad * .5f), aspect,
            Vector3.Distance(camera, subject.Focus) + subject.Height + subject.Radius * 2);
        return MeasureSubject(camera, Quaternion.Inverse(rotation), subject, fov, aspect, nearClip).Visibility;
    }

    // Geometry-only helper for composing intentional shot sizes and diagnostics.
    // It has no physics queries, foliage scans, scene checks or allocations.
    internal ShotProjection.Envelope ProjectSubject(Vector3 camera, Quaternion rotation, Subject subject,
        float fov, float aspect, float nearClip = 0.3f)
    {
        if (!ValidPose(camera, rotation, fov, aspect, nearClip) || !ValidSubject(subject)) return default;
        return ProjectBody(camera, Quaternion.Inverse(rotation.normalized), subject, fov, aspect, nearClip);
    }

    private ShotProjection.Envelope ProjectBody(Vector3 camera, Quaternion inverse, Subject subject,
        float fov, float aspect, float nearClip)
    {
        // Focus is above the actor root, not the body's center. Actual Character
        // height supplied by the scene adapter keeps feet/head in the envelope,
        // including shorter stances and differently sized opponents.
        Vector3 bottom = subject.Focus - Vector3.up * subject.BottomOffset;
        // Radius encloses the actor horizontally. Do not shrink one world axis:
        // the actor may turn independently of both the camera and its velocity.
        float halfWidth = Mathf.Clamp(subject.Radius, 0.15f, 8f);
        int index = 0;
        for (int y = 0; y < 2; y++)
        for (int z = -1; z <= 1; z += 2)
        for (int x = -1; x <= 1; x += 2)
        {
            Vector3 point = bottom + new Vector3(x * halfWidth, y * subject.Height, z * halfWidth);
            Vector3 local = inverse * (point - camera);
            bodyCorners[index++] = new ShotProjection.Point(local.x, local.y, local.z);
        }
        return ShotProjection.MeasureEnvelope(bodyCorners, fov, aspect, nearClip);
    }

    private SubjectMeasure MeasureSubject(Vector3 camera, Quaternion inverse, Subject subject,
        float fov, float aspect, float nearClip, bool detailed = true)
    {
        ShotProjection.Envelope envelope = ProjectBody(camera, inverse, subject, fov, aspect, nearClip);
        if (!envelope.InFront || envelope.VisibleFraction <= 0) return new SubjectMeasure(envelope, 0, 0, 0, 0);
        Vector3 bottom = subject.Focus - Vector3.up * subject.BottomOffset;
        float solid = 0, leaves = 0;
        // Cover body width as well as its center: a narrow gap between leaves
        // must not stand in for a visible torso. Camera-relative shoulder/hip
        // samples remain within the conservative actor envelope.
        Vector3 across = Vector3.Cross(Vector3.up, subject.Focus - camera).normalized * (subject.Radius * .65f);
        MeasurePoint(camera, inverse, bottom + Vector3.up * (subject.Height * .92f), detailed ? .2f : .3f,
            fov, aspect, nearClip, ref solid, ref leaves);
        MeasurePoint(camera, inverse, bottom + Vector3.up * (subject.Height * .55f), detailed ? .24f : .5f,
            fov, aspect, nearClip, ref solid, ref leaves);
        MeasurePoint(camera, inverse, bottom + Vector3.up * (subject.Height * .12f), detailed ? .12f : .2f,
            fov, aspect, nearClip, ref solid, ref leaves);
        for (int side = -1; detailed && side <= 1; side += 2)
        {
            MeasurePoint(camera, inverse, bottom + Vector3.up * (subject.Height * .72f) + across * side, .12f,
                fov, aspect, nearClip, ref solid, ref leaves);
            MeasurePoint(camera, inverse, bottom + Vector3.up * (subject.Height * .35f) + across * side, .1f,
                fov, aspect, nearClip, ref solid, ref leaves);
        }
        solid = Mathf.Clamp01(solid); leaves = Mathf.Clamp01(leaves);
        float visible = (float)(envelope.VisibleFraction * envelope.Readability) * (1f - solid) * (1f - .65f * leaves);
        float quality = (float)envelope.Quality * (1f - .7f * solid - .45f * leaves * (1f - solid));
        return new SubjectMeasure(envelope, solid, leaves, visible, quality);
    }

    private void MeasurePoint(Vector3 camera, Quaternion inverse, Vector3 target, float weight,
        float fov, float aspect, float nearClip, ref float solid, ref float leaves)
    {
        Vector3 local = inverse * (target - camera);
        ShotProjection.Result projection = ShotProjection.Measure(local.x, local.y, local.z, 0, fov, aspect, nearClip);
        if (projection.CenterInFrame) Sample(camera, target, weight, ref solid, ref leaves);
    }

    private static bool ValidSubject(Subject subject) => TargetRoster.Finite(subject.Focus) &&
        Finite(subject.Radius) && subject.Radius > 0 && Finite(subject.Height) && subject.Height > 0 &&
        subject.Height <= 32 && Finite(subject.BottomOffset) && subject.BottomOffset >= 0 && subject.BottomOffset <= 32;

    private void RefreshActive()
    {
        if (checkedFrame == Time.frameCount) return;
        checkedFrame = Time.frameCount;
        for (int i = 0; i < foliage.Count; i++)
        {
            Renderer renderer = foliage[i].Renderer;
            activeFoliage[i] = renderer && renderer.enabled && renderer.gameObject.activeInHierarchy;
            if (!activeFoliage[i]) continue;
            // Trees can fall or animate between surveys. Never use the old
            // world-space bound after the corresponding renderer moved.
            Bounds bounds = renderer!.bounds;
            activeFoliage[i] = TargetRoster.Finite(bounds.center) && TargetRoster.Finite(bounds.extents);
            bounds.Expand(.4f);
            activeBounds[i] = bounds;
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
        for (int sample = 0; sample < viewFoliageCount; sample++)
        {
            int i = viewFoliage[sample];
            Bounds bound = activeBounds[i];
            Vector3 min = bound.min, max = bound.max;
            // Cheap segment bounds prune most trees before native AABB ray tests.
            if (max.x < rayMin.x || min.x > rayMax.x || max.y < rayMin.y || min.y > rayMax.y ||
                max.z < rayMin.z || min.z > rayMax.z) continue;
            if (bound.Contains(camera) || bound.IntersectRay(ray, out float hit) && hit < distance) return true;
        }
        return false;
    }

    private void PrepareFoliage(Vector3 camera, Quaternion rotation, float tangent, float aspect, float distance)
    {
        viewFoliageCount = 0;
        Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up, forward = rotation * Vector3.forward;
        Vector3 leftPlane = -right - forward * (tangent * aspect), rightPlane = right - forward * (tangent * aspect);
        Vector3 bottomPlane = -up - forward * tangent, topPlane = up - forward * tangent;
        for (int i = 0; i < foliage.Count; i++)
        {
            if (!activeFoliage[i]) continue;
            Bounds bounds = activeBounds[i];
            Vector3 offset = bounds.center - camera, extent = bounds.extents;
            float depth = Vector3.Dot(forward, offset), depthExtent = Extent(forward, extent);
            if (depth + depthExtent < 0 || depth - depthExtent > distance ||
                Outside(leftPlane, offset, extent) || Outside(rightPlane, offset, extent) ||
                Outside(bottomPlane, offset, extent) || Outside(topPlane, offset, extent)) continue;
            viewFoliage[viewFoliageCount++] = i;
        }
    }

    private static bool Outside(Vector3 normal, Vector3 offset, Vector3 extent) => Vector3.Dot(normal, offset) > Extent(normal, extent);
    private static float Extent(Vector3 normal, Vector3 extent) =>
        Mathf.Abs(normal.x) * extent.x + Mathf.Abs(normal.y) * extent.y + Mathf.Abs(normal.z) * extent.z;

    private bool SolidOverlap(int count)
    {
        if (count == overlaps.Length) return true;
        for (int i = 0; i < count; i++) if (Solid(overlaps[i])) return true;
        return false;
    }

    private static bool Solid(Collider collider) => collider && collider.enabled &&
        !collider.isTrigger && !collider.GetComponentInParent<Character>();

    private static bool Vegetation(Renderer renderer)
    {
        Transform transform = renderer.transform;
        for (Transform? current = transform; current; current = current.parent)
        {
            string name = current.name;
            if (name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("shrub", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("leaves", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("foliage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("sapling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("fern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("vine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("ivy", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        if (transform.GetComponentInParent<TreeBase>() || transform.GetComponentInParent<TreeLog>()) return true;
        // A shader hint covers vegetation whose object names were replaced by
        // a mod. It remains heuristic, not an alpha/silhouette measurement.
        Material material = renderer.sharedMaterial;
        string shader = material && material.shader ? material.shader.name : "";
        return shader.IndexOf("vegetation", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shader.IndexOf("foliage", StringComparison.OrdinalIgnoreCase) >= 0 ||
            shader.IndexOf("leaves", StringComparison.OrdinalIgnoreCase) >= 0;
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
