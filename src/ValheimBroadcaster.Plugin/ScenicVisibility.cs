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
    private const float SurveyRadius = ScenicObservationPolicy.DetailRadius, ForegroundRange = ScenicObservationPolicy.ForegroundRange, DetailRadius = ScenicObservationPolicy.DetailRadius;
    private const int DetailBudget = 1024;
    private readonly string owner;
    private long surveyNumber;
    private int omitted, candidates, detailed, coarseCount;
    private float surveyMilliseconds;
    private ScenicSurveyRefresh refreshAction;
    internal long QueryId;
    internal string QueryStage = "unspecified";
    internal ScenicVisibility(string owner = "unspecified") { this.owner = owner; }
    private readonly FoliageProjection foliage = new FoliageProjection(SurveyRadius);
    private Vector3 origin;
    private float refreshedAt = float.NegativeInfinity, coveredRadius;
    private int obstructionMask;
    private readonly Collider[] overlaps = new Collider[32];
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private readonly bool[] foregroundBounds = new bool[FoliageProjection.BoundsCapacity];
    private Bounds[] activeBounds => foliage.ActiveBounds;
    private int detailRemaining, activeCount;
    private string focusRayResult = "Not queried", focusBlocker = "";
    private float focusHitDistance = -1;
    private Vector3 focusHitPoint;

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
        ScenicTrace.Event(owner, "Survey reset", QueryId);
        foliage.Reset(); refreshedAt = float.NegativeInfinity; coveredRadius = 0; activeCount = 0;
    }

    internal void Refresh(Vector3 surveyOrigin)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        float before = foliage.RefreshedAt;
        foliage.Refresh(surveyOrigin);
        refreshAction = ScenicObservationPolicy.LocalRefresh(Time.unscaledTime, refreshedAt,
            Vector3.Distance(surveyOrigin, origin), coveredRadius, foliage.Revision != ScenicFoliageScene.Shared.Revision);
        origin = foliage.Origin; refreshedAt = foliage.RefreshedAt; coveredRadius = foliage.CoveredRadius;
        omitted = foliage.Omitted; candidates = foliage.RetainedCount + omitted;
        detailed = foliage.Detailed; coarseCount = foliage.Coarse; detailRemaining = foliage.DetailRemaining;
        if (before != refreshedAt) surveyNumber++;
        surveyMilliseconds = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        if (obstructionMask == 0) obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
    }

    internal static bool Vegetation(Transform transform)
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
                name.IndexOf("sapling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("fern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("vine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("ivy", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return transform.GetComponentInParent<TreeBase>() || transform.GetComponentInParent<TreeLog>();
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
        focusRayResult = projected.CenterInFrame ? "Not queried" : "Outside frame";
        focusBlocker = ""; focusHitDistance = -1; focusHitPoint = default;
        bool focusVisible = projected.CenterInFrame && FocusClear(camera, focus, subjectBounds);
        // A projection is usable only inside its measured local coverage. The
        // projection can recenter immediately from scene-owned observations;
        // missing catalogue/retention evidence remains unknown.
        float displacement = Vector3.Distance(camera, origin);
        ScenicCoverageGap gap = ScenicObservationPolicy.Coverage(displacement, coveredRadius);
        bool complete = gap == ScenicCoverageGap.None;
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
        string reason = !complete ? "Scenic foliage survey incomplete: " + gap + " (" + refreshAction + ")" : insideLeaves ? "Scenic camera intersects retained vegetation bounds" :
            foreground >= 0.5f ? "Foreground vegetation dominates the scenic frame" :
            !focusVisible ? "Scenic feature is outside the frame or hidden" : "Readable scenic composition";
        if (ScenicTrace.Enabled)
        {
            CameraTraceRecord row = ScenicTrace.Row("coverage").Field("owner", owner).Field("query", QueryId).Field("stage", QueryStage)
                .Field("survey", surveyNumber).Field("distance", displacement).Field("radius", coveredRadius)
                .Field("age", Time.unscaledTime - refreshedAt).Field("gap", (long)gap)
                .Field("retained", foliage.RetainedCount).Field("omitted", omitted).Field("candidates", candidates)
                .Field("detailed", detailed).Field("coarse", coarseCount).Field("detailRemaining", detailRemaining)
                .Field("activeBounds", activeCount).Field("surveyMilliseconds", surveyMilliseconds).Field("focusVisible", focusVisible).Field("foreground", foreground)
                .Field("focusInFrame", projected.CenterInFrame).Field("focusRayResult", focusRayResult)
                .Field("focusBlocker", focusBlocker).Field("focusHitDistance", focusHitDistance)
                .Field("focusHitX", focusHitPoint.x).Field("focusHitY", focusHitPoint.y).Field("focusHitZ", focusHitPoint.z)
                .Field("score", score).Field("fov", fov).Field("aspect", aspect).Field("near", nearClip)
                .Field("rotationX", rotation.x).Field("rotationY", rotation.y).Field("rotationZ", rotation.z).Field("rotationW", rotation.w)
                .Field("reason", reason);
            ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Vector(ScenicTrace.Vector(row, "camera", camera), "origin", origin), "focus", focus));
        }
        if (!complete) ScenicTrace.Incomplete(owner, gap, displacement, coveredRadius, omitted);
        return new Result(true, focusVisible, complete, Mathf.Clamp01(score), foreground, reason);
    }

    private void RefreshActive()
    {
        foliage.RefreshActive(); activeCount = foliage.ActiveCount;
        origin = foliage.Origin; coveredRadius = foliage.CoveredRadius;
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
        float displacement = Vector3.Distance(camera, origin);
        ScenicCoverageGap gap = ScenicObservationPolicy.Coverage(displacement, coveredRadius);
        bool complete = gap == ScenicCoverageGap.None;
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

    // Pixel-independent subject samples share the exact live foliage projection.
    // Physical rays are owned by the caller so only the identified NPC's own
    // colliders may be ignored, never a broad camp bounding box.
    internal bool SubjectPointVisible(Vector3 camera, Quaternion rotation, Vector3 point, float fov, float aspect, float nearClip)
    {
        Vector3 local = Quaternion.Inverse(rotation) * (point - camera);
        if (!ShotProjection.Measure(local.x, local.y, local.z, 0, fov, aspect, nearClip).CenterInFrame) return false;
        Refresh(camera); RefreshActive();
        if (ScenicObservationPolicy.Coverage(Vector3.Distance(camera, origin), coveredRadius) != ScenicCoverageGap.None) return false;
        Vector3 delta = point - camera;
        if (delta.sqrMagnitude < .25f) return false;
        Ray ray = new Ray(camera, delta.normalized);
        for (int i = 0; i < activeCount; i++)
            if (activeBounds[i].IntersectRay(ray, out float distance) && distance < delta.magnitude - .15f) return false;
        return true;
    }

    private bool FocusClear(Vector3 camera, Vector3 focus, Bounds? subjectBounds)
    {
        Vector3 delta = focus - camera;
        float distance = delta.magnitude;
        if (distance < 0.5f) { focusRayResult = "Invalid focus distance"; return false; }
        int count = Physics.RaycastNonAlloc(camera, delta / distance, hits, Mathf.Max(0, distance - 0.25f),
            obstructionMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) { focusRayResult = "Hit buffer saturated; unknown sightline"; return false; }
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
            bool terrain = hit.GetComponentInParent<Heightmap>() || hit.gameObject.layer == LayerMask.NameToLayer("terrain");
            if (terrain || !subjectBounds.HasValue || !subjectBounds.Value.Contains(hits[i].point))
            {
                // RaycastNonAlloc does not promise sorted hits. Preserve the
                // nearest actual blocker without adding a second physics query.
                if (focusHitDistance < 0 || hits[i].distance < focusHitDistance)
                {
                    focusRayResult = terrain ? "Terrain blocks focus" : "Solid geometry blocks focus";
                    string name = hit.name;
                    focusBlocker = name.Length > 96 ? name.Substring(0, 96) : name;
                    focusHitDistance = hits[i].distance; focusHitPoint = hits[i].point;
                }
            }
        }
        if (focusHitDistance >= 0) return false;
        focusRayResult = "Clear focus sightline"; return true;
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
    private Result Invalid(string reason)
    { ScenicTrace.Event(owner + "-invalid", reason, QueryId); return new Result(false, false, false, 0, 0, reason); }
}
