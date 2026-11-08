using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Read-only quality measurements for player shots. A canopy bound is a coarse
// hint, never evidence that a scene is unloaded or that travel is required.
internal sealed class PlayerShotVisibility
{
    private const int Capacity = FoliageProjection.BoundsCapacity;
    internal const int MaximumSubjects = 16;
    private readonly LoadedActorOcclusionSurvey actors = LoadedActorOcclusionSurvey.Shared;
    private readonly Subject[] singleSubject = new Subject[1];
    private readonly ShotProjection.Point[] bodyCorners = new ShotProjection.Point[8];
    private const float ForegroundRange = 10f;
    private readonly FoliageProjection foliage = FoliageProjection.Actors;
    private Bounds[] activeBounds => foliage.ActiveBounds;
    private readonly int[] viewFoliage = new int[Capacity];
    private int viewFoliageCount;
    private readonly Collider[] overlaps = new Collider[32];
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private Vector3 origin;
    private float coveredRadius;
    private int obstructionMask;

    internal readonly struct Subject
    {
        internal readonly Vector3 Focus;
        internal readonly float Weight, Radius, Height, BottomOffset;
        internal readonly bool Primary;
        internal readonly ShotScale Scale;
        internal readonly int ActorId;
        internal Subject(Vector3 focus, float weight, float radius, bool primary, float height = 0, float bottomOffset = -1,
            ShotScale scale = ShotScale.Full, int actorId = 0)
        {
            ActorId = actorId; Focus = focus; Weight = weight; Radius = radius; Primary = primary;
            float size = Mathf.Clamp(radius / 0.6f, 0.5f, 4f);
            Height = height > 0 ? height : 1.85f * size;
            BottomOffset = bottomOffset >= 0 ? bottomOffset : 1.5f * size;
            // A primary's intentional close framing never awards coverage to a
            // companion whose legs/head happen to be outside the same picture.
            Scale = ShotScalePolicy.ForSubject(scale, primary);
        }
    }

    internal readonly struct Result
    {
        internal readonly float Score, SolidOcclusion, FoliageOcclusion, ForegroundOcclusion;
        internal readonly float PrimaryVisibility, GroupVisibility;
        internal readonly float PrimarySolidOcclusion, PrimaryFoliageOcclusion;
        internal readonly float ActorOcclusion, PrimaryActorOcclusion;
        internal readonly float PrimaryReadability, PrimarySafeFrame, PrimaryHeadroom, PrimaryFrameHeight;
        internal readonly float PrimaryScreenX, PrimaryScreenY, PrimaryFocusX, PrimaryFocusY;
        internal readonly bool CameraClear, SurveyComplete, PrimaryInFrame;
        internal readonly ShotScale PrimaryScale;
        // Input subject indexes that are clearly represented in this actual pose.
        internal readonly int VisibleMask;
        internal readonly string Reason;
        internal Result(float score, bool clear, float solid, float foliage, float foreground,
            bool complete, string reason, bool primaryInFrame = false, float primaryVisibility = 0,
            float groupVisibility = 0, int visibleMask = 0, ShotProjection.Envelope primaryEnvelope = default,
            ShotProjection.Result primaryFocus = default, float primarySolid = 0, float primaryFoliage = 0,
            ShotScale primaryScale = ShotScale.Full, float actorOcclusion = 0, float primaryActorOcclusion = 0)
        {
            Score = score; CameraClear = clear; SolidOcclusion = solid;
            FoliageOcclusion = foliage; ForegroundOcclusion = foreground;
            SurveyComplete = complete; Reason = reason; PrimaryInFrame = primaryInFrame;
            PrimaryVisibility = primaryVisibility; GroupVisibility = groupVisibility; VisibleMask = visibleMask;
            PrimarySolidOcclusion = primarySolid; PrimaryFoliageOcclusion = primaryFoliage;
            PrimaryScale = primaryScale; ActorOcclusion = actorOcclusion; PrimaryActorOcclusion = primaryActorOcclusion;
            PrimaryReadability = (float)primaryEnvelope.Readability; PrimarySafeFrame = (float)primaryEnvelope.SafeFraction;
            PrimaryHeadroom = (float)primaryEnvelope.Headroom; PrimaryFrameHeight = (float)primaryEnvelope.RelativeHeight;
            PrimaryScreenX = (float)primaryEnvelope.X; PrimaryScreenY = (float)primaryEnvelope.Y;
            PrimaryFocusX = (float)primaryFocus.X; PrimaryFocusY = (float)primaryFocus.Y;
        }

        internal PictureHealth Picture(bool scenic = false) => scenic
            ? PictureQuality.Scenic(CameraClear, PrimaryInFrame, Score, ForegroundOcclusion, SurveyComplete)
            : PrimaryActorOcclusion > PictureQuality.MaximumPrimarySolidOcclusion
                ? new PictureHealth(false, "Primary actor obscured by another actor body")
                : PictureQuality.Actor(CameraClear, PrimaryInFrame, PrimaryVisibility, PrimaryReadability,
                PrimarySolidOcclusion, PrimaryFoliageOcclusion, ForegroundOcclusion, SurveyComplete);
    }

    private readonly struct SubjectMeasure
    {
        internal readonly float Screen, Solid, Leaves, Actors, Visibility, Quality, Readability;
        internal readonly ShotProjection.Envelope Envelope;
        internal readonly bool CriticalVisible;
        internal SubjectMeasure(ShotProjection.Envelope envelope, float solid, float leaves, float visibility, float quality,
            bool criticalVisible = true, float actors = 0)
        {
            Envelope = envelope; Screen = (float)envelope.VisibleFraction; Solid = solid; Leaves = leaves;
            Visibility = visibility; Quality = quality; Readability = (float)envelope.Readability;
            CriticalVisible = criticalVisible; Actors = actors;
        }
    }

    internal void Reset()
    {
        // Reset only this query's buffers. World observations are shared across
        // standby/live rigs and remain owned by their world-bound services.
        coveredRadius = 0; viewFoliageCount = 0;
        Array.Clear(overlaps, 0, overlaps.Length); Array.Clear(hits, 0, hits.Length);
    }

    internal void Refresh(Vector3 focus)
    {
        if (!TargetRoster.Finite(focus)) return;
        Vector3 carrier = Player.m_localPlayer ? Player.m_localPlayer.transform.position : focus;
        foliage.Refresh(carrier, carrier + Vector3.up * 2);
        origin = foliage.Origin; coveredRadius = foliage.CoveredRadius;
        if (obstructionMask == 0) obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
    }

    internal void RequestSurveyRefresh() => foliage.RequestRefresh();

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
        Refresh(focus); RefreshActive(); actors.Refresh(focus);
        Vector3 forward = rotation * Vector3.forward, right = rotation * Vector3.right, up = rotation * Vector3.up;
        float tangent = Mathf.Tan(fov * Mathf.Deg2Rad * 0.5f);
        // The camera is a virtual point, not a solid body. Only actual
        // penetration of solid geometry invalidates a pose (an epsilon radius
        // catches the camera point sitting in a surface); standing NEAR a
        // stump or post is fine - the deoccluder displaces around props and
        // the exact near-plane box below guards the rendered image.
        int count = Physics.OverlapSphereNonAlloc(camera, 0.05f, overlaps, obstructionMask, QueryTriggerInteraction.Ignore);
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
        float groupQuality = 0, groupVisibility = 0, weight = 0, solid = 0, leaves = 0, actorOcclusion = 0;
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
            solid += value.Solid * importance; leaves += value.Leaves * importance;
            actorOcclusion += value.Actors * importance; weight += importance;
            if (value.CriticalVisible && ShotScalePolicy.EnvelopeReadable(value.Envelope, subject.Scale) &&
                value.Visibility >= PictureQuality.MinimumActorVisibility &&
                value.Readability >= PictureQuality.MinimumActorReadability && value.Envelope.Headroom >= 0 &&
                value.Solid <= PictureQuality.MaximumPrimarySolidOcclusion && value.Leaves <= PictureQuality.MaximumPrimaryFoliageOcclusion &&
                value.Actors <= PictureQuality.MaximumPrimarySolidOcclusion)
                visibleMask |= 1 << i;
            complete &= Vector3.Distance(subject.Focus, origin) + Mathf.Max(subject.Radius * 2f, subject.Height) < coveredRadius;
        }
        groupQuality /= weight; groupVisibility /= weight; solid /= weight; leaves /= weight; actorOcclusion /= weight;
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
        ShotScale primaryScale = subjects[primary].Scale;
        bool primaryInFrame = main.CriticalVisible && ShotScalePolicy.EnvelopeReadable(main.Envelope, primaryScale);
        // Group coverage may distinguish equally readable primary shots, but
        // cannot rescue a tiny, clipped or heavily obscured primary actor.
        float quality = main.Quality * (0.7f + 0.3f * groupQuality) * (1f - 0.35f * foreground);
        if (!complete) quality *= 0.75f;
        // Positive quality for any physically safe, in-frame view preserves a
        // fallback under broad canopy bounds. Lost primaries cannot win on group size.
        // An imperfect but physically valid view keeps a graded fallback score.
        // The stricter required-envelope verdict separately controls readability.
        quality = main.Screen >= .6f ? Mathf.Clamp(quality, 0.03f, 1f) : 0;
        if (!complete || foreground > PictureQuality.MaximumForegroundOcclusion) visibleMask = 0;
        string reason = !main.CriticalVisible ? "Medium framing loses a required head or torso sightline" :
            !primaryInFrame ? "Required primary envelope outside readable camera framing" :
            main.Readability < 0.8f ? "Primary player too small for readable coverage" :
            main.Envelope.VisibleFraction < .98 ? "Primary body clipped by camera frame" :
            main.Envelope.HeadroomScore < .8 ? "Primary player needs headroom" :
            main.Envelope.SafeFraction < .9 ? "Primary player near frame edge" :
            main.Solid > 0.2f ? "Primary player obscured by solid geometry" :
            main.Actors > 0.2f ? "Primary player obscured by another actor body" :
            main.Leaves > 0.2f || foreground > 0.3f ? "Foliage obscures player shot" :
            !complete ? "Foliage survey incomplete" : "Clear " + primaryScale.ToString().ToLowerInvariant() + " actor composition";
        Vector3 localFocus = inverse * (focus - camera);
        ShotProjection.Result projectedFocus = ShotProjection.Measure(localFocus.x, localFocus.y, localFocus.z, 0, fov, aspect, nearClip);
        return new Result(quality, true, solid, leaves, Mathf.Clamp01(foreground), complete,
            reason, primaryInFrame, main.Visibility, groupVisibility, visibleMask, main.Envelope, projectedFocus,
            main.Solid, main.Leaves, primaryScale, actorOcclusion, main.Actors);
    }

    internal float SubjectVisibility(Vector3 camera, Quaternion rotation, Subject subject,
        float fov, float aspect, float nearClip = 0.3f)
    {
        if (!ValidPose(camera, rotation, fov, aspect, nearClip) || !TargetRoster.Finite(subject.Focus) ||
            !ValidSubject(subject)) return 0;
        Refresh(subject.Focus); RefreshActive(); actors.Refresh(subject.Focus);
        rotation = rotation.normalized;
        PrepareFoliage(camera, rotation, Mathf.Tan(fov * Mathf.Deg2Rad * .5f), aspect,
            Vector3.Distance(camera, subject.Focus) + subject.Height + subject.Radius * 2);
        return MeasureSubject(camera, Quaternion.Inverse(rotation), subject, fov, aspect, nearClip).Visibility;
    }

    // Call after the actor view evaluation so this uses the same bounded survey.
    // A readable actor alone does not certify a claimed player/object interaction.
    // This is a point-of-interest test, not object recognition or task inference.
    internal bool InterestVisible(Vector3 camera, Quaternion rotation, Vector3 interest,
        float fov, float aspect, float nearClip = .3f, int actorId = 0)
    {
        if (!ValidPose(camera, rotation, fov, aspect, nearClip) || !TargetRoster.Finite(interest) ||
            coveredRadius <= 0 || Vector3.Distance(camera, origin) >= coveredRadius ||
            Vector3.Distance(interest, origin) >= coveredRadius) return false;
        Quaternion normalized = rotation.normalized;
        Vector3 local = Quaternion.Inverse(normalized) * (interest - camera);
        var point = ShotProjection.Measure(local.x, local.y, local.z, 0, fov, aspect, nearClip);
        if (!point.CenterInFrame || point.X < .06 || point.X > .94 || point.Y < .06 || point.Y > .94) return false;
        RefreshActive();
        PrepareFoliage(camera, normalized, Mathf.Tan(fov * Mathf.Deg2Rad * .5f), aspect,
            Vector3.Distance(camera, interest) + .5f);
        float solid = 0, leaves = 0;
        Sample(camera, interest, 1, ref solid, ref leaves);
        return solid < .5f && leaves < .5f && !actors.Occludes(camera, interest, actorId);
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
        // Derive the required envelope from real dimensions. Only an explicitly
        // requested Medium primary may omit the legs. Its head and complete
        // upper-body envelope still need safe framing and line of sight.
        ShotScaleProfile profile = ShotScalePolicy.Profile(subject.Scale);
        Vector3 bottom = subject.Focus - Vector3.up * subject.BottomOffset +
            Vector3.up * (subject.Height * profile.LowerBodyFraction);
        float requiredHeight = subject.Height * (1 - profile.LowerBodyFraction);
        // Radius encloses the actor horizontally. Do not shrink one world axis:
        // the actor may turn independently of both the camera and its velocity.
        float halfWidth = Mathf.Clamp(subject.Radius, 0.15f, 8f);
        int index = 0;
        for (int y = 0; y < 2; y++)
        for (int z = -1; z <= 1; z += 2)
        for (int x = -1; x <= 1; x += 2)
        {
            Vector3 point = bottom + new Vector3(x * halfWidth, y * requiredHeight, z * halfWidth);
            Vector3 local = inverse * (point - camera);
            bodyCorners[index++] = new ShotProjection.Point(local.x, local.y, local.z);
        }
        return ShotProjection.MeasureEnvelope(bodyCorners, fov, aspect, nearClip, profile.MinimumReadableHeight);
    }

    private SubjectMeasure MeasureSubject(Vector3 camera, Quaternion inverse, Subject subject,
        float fov, float aspect, float nearClip, bool detailed = true)
    {
        ShotProjection.Envelope envelope = ProjectBody(camera, inverse, subject, fov, aspect, nearClip);
        if (!envelope.InFront || envelope.VisibleFraction <= 0) return new SubjectMeasure(envelope, 0, 0, 0, 0);
        Vector3 bottom = subject.Focus - Vector3.up * subject.BottomOffset;
        float solid = 0, leaves = 0, actorOcclusion = 0;
        // Cover body width as well as its center: a narrow gap between leaves
        // must not stand in for a visible torso. Camera-relative shoulder/hip
        // samples remain within the conservative actor envelope.
        Vector3 across = Vector3.Cross(Vector3.up, subject.Focus - camera).normalized * (subject.Radius * .65f);
        bool medium = subject.Scale == ShotScale.Medium;
        bool headVisible = MeasurePoint(camera, inverse, bottom + Vector3.up * (subject.Height * .92f), detailed ? .2f : .3f,
            fov, aspect, nearClip, ref solid, ref leaves, ref actorOcclusion, subject.ActorId);
        bool torsoVisible = MeasurePoint(camera, inverse, bottom + Vector3.up * (subject.Height * (medium ? .74f : .55f)), detailed ? .24f : .5f,
            fov, aspect, nearClip, ref solid, ref leaves, ref actorOcclusion, subject.ActorId);
        MeasurePoint(camera, inverse, bottom + Vector3.up * (subject.Height * (medium ? .48f : .12f)), detailed ? .12f : .2f,
            fov, aspect, nearClip, ref solid, ref leaves, ref actorOcclusion, subject.ActorId);
        for (int side = -1; detailed && side <= 1; side += 2)
        {
            MeasurePoint(camera, inverse, bottom + Vector3.up * (subject.Height * (medium ? .82f : .72f)) + across * side, .12f,
                fov, aspect, nearClip, ref solid, ref leaves, ref actorOcclusion, subject.ActorId);
            MeasurePoint(camera, inverse, bottom + Vector3.up * (subject.Height * (medium ? .54f : .35f)) + across * side, .1f,
                fov, aspect, nearClip, ref solid, ref leaves, ref actorOcclusion, subject.ActorId);
        }
        solid = Mathf.Clamp01(solid); leaves = Mathf.Clamp01(leaves); actorOcclusion = Mathf.Clamp01(actorOcclusion);
        float visible = (float)(envelope.VisibleFraction * envelope.Readability) * (1f - solid) * (1f - .65f * leaves) * (1f - actorOcclusion);
        float quality = (float)envelope.Quality * (1f - .7f * solid - .45f * leaves * (1f - solid)) *
            (.82f + .18f * ShotScalePolicy.SizeQuality(envelope, subject.Scale)) * (1f - .8f * actorOcclusion);
        return new SubjectMeasure(envelope, solid, leaves, visible, quality, !medium || headVisible && torsoVisible, actorOcclusion);
    }

    private bool MeasurePoint(Vector3 camera, Quaternion inverse, Vector3 target, float weight,
        float fov, float aspect, float nearClip, ref float solid, ref float leaves, ref float actorOcclusion, int actorId)
    {
        Vector3 local = inverse * (target - camera);
        ShotProjection.Result projection = ShotProjection.Measure(local.x, local.y, local.z, 0, fov, aspect, nearClip);
        if (!projection.CenterInFrame) return false;
        float pointSolid = 0, pointLeaves = 0;
        Sample(camera, target, 1, ref pointSolid, ref pointLeaves);
        bool actorBlocked = actors.Occludes(camera, target, actorId);
        solid += pointSolid * weight; leaves += pointLeaves * weight;
        if (actorBlocked) actorOcclusion += weight;
        return pointSolid < .5f && pointLeaves < .5f && !actorBlocked;
    }

    private static bool ValidSubject(Subject subject) => TargetRoster.Finite(subject.Focus) &&
        Finite(subject.Radius) && subject.Radius > 0 && Finite(subject.Height) && subject.Height > 0 &&
        subject.Height <= 32 && Finite(subject.BottomOffset) && subject.BottomOffset >= 0 && subject.BottomOffset <= 32 &&
        (int)subject.Scale >= 0 && (int)subject.Scale <= (int)ShotScale.Medium;

    private void RefreshActive()
    {
        foliage.RefreshActive(); origin = foliage.Origin; coveredRadius = foliage.CoveredRadius;
    }

    private static bool ValidPose(Vector3 camera, Quaternion rotation, float fov, float aspect, float nearClip)
    {
        float magnitude = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        return TargetRoster.Finite(camera) && Finite(magnitude) && magnitude > 0.0001f && Finite(fov) &&
            Finite(aspect) && Finite(nearClip) && fov >= 1f && fov <= 170f && aspect > 0 && nearClip > 0;
    }

    // Reused by the adapter when validating travel blends and repair arcs.
    // A thin ray keeps long moves from passing through terrain and structure
    // walls; it deliberately models no camera body, so clutter beside the
    // route (stumps, posts, fences) cannot block movement. The deoccluder
    // displaces around props and visibility scoring judges the picture.
    internal bool SegmentClear(Vector3 from, Vector3 to)
    {
        if (!TargetRoster.Finite(from) || !TargetRoster.Finite(to)) return false;
        if (obstructionMask == 0) obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        Vector3 delta = to - from;
        if (!TargetRoster.Finite(delta) || !Finite(delta.sqrMagnitude)) return false;
        if (delta.sqrMagnitude <= 0.0001f) return true;
        int count = Physics.RaycastNonAlloc(from, delta.normalized, hits,
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
        for (int i = 0; i < foliage.ActiveCount; i++)
        {
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

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static Result Invalid(string reason) => new Result(0, false, 1, 0, 0, false, reason);
}
