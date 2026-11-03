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
    private Bounds nearestOmitted;
    private float nearestOmittedDistance, surveyMilliseconds;
    private ScenicSurveyRefresh refreshAction;
    internal long QueryId;
    internal string QueryStage = "unspecified";
    internal ScenicVisibility(string owner = "unspecified") { this.owner = owner; }
    private readonly List<Entry> foliage = new List<Entry>(Capacity);
    private long catalogueRevision = -1;
    private readonly List<float> distances = new List<float>(Capacity);
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
        internal readonly ScenicFoliageScene.Observation Observation;
        internal Renderer Renderer => Observation.Renderer;
        internal readonly Bounds[]? Local;
        internal Entry(ScenicFoliageScene.Observation observation, Bounds[]? local)
        { Observation = observation; Local = local; }
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
        ScenicTrace.Event(owner, "Survey reset", QueryId);
        foliage.Clear(); distances.Clear(); refreshedAt = float.NegativeInfinity;
        coveredRadius = 0; furthest = 0; furthestDistance = 0;
        catalogueRevision = -1; checkedFrame = -1; activeCount = 0;
    }

    internal void Refresh(Vector3 surveyOrigin)
    {
        float now = Time.unscaledTime;
        float displacement = Vector3.Distance(surveyOrigin, origin);
        ScenicFoliageScene scene = ScenicFoliageScene.Shared;
        scene.Ensure(surveyOrigin);
        bool catalogueChanged = catalogueRevision != scene.Revision;
        refreshAction = ScenicObservationPolicy.LocalRefresh(now, refreshedAt, displacement, coveredRadius, catalogueChanged);
        if (ScenicTrace.Enabled)
        {
            CameraTraceRecord row = ScenicTrace.Row("refresh").Field("owner", owner).Field("query", QueryId)
                .Field("policy", "local-v2").Field("catalogueChanged", catalogueChanged).Field("catalogueRevision", scene.Revision)
                .Field("stage", QueryStage).Field("survey", surveyNumber).Field("refreshedAt", refreshedAt)
                .Field("distance", displacement).Field("radius", coveredRadius).Field("action", refreshAction.ToString());
            ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Vector(row, "camera", surveyOrigin), "origin", origin));
        }
        if (!ScenicObservationPolicy.NeedsRefresh(refreshAction)) return;
        surveyNumber++; omitted = candidates = detailed = coarseCount = 0;
        nearestOmitted = default; nearestOmittedDistance = float.PositiveInfinity;
        long surveyStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        origin = surveyOrigin; refreshedAt = now; catalogueRevision = scene.Revision;
        coveredRadius = Mathf.Min(SurveyRadius, scene.CoverageAt(surveyOrigin));
        foliage.Clear(); distances.Clear(); furthest = 0; furthestDistance = 0;
        detailRemaining = DetailBudget; checkedFrame = -1;
        obstructionMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
        // The scene owns discovery. This is a cheap local projection of its
        // retained renderers, including every coarse bound touching the detailed
        // foreground neighborhood. Distant renderers cannot consume its budget.
        foreach (ScenicFoliageScene.Observation observation in scene.Renderers)
        {
            Renderer renderer = observation.Renderer;
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!observation.TryBounds(out Bounds bounds))
            { coveredRadius = observation.LimitUnknownCoverage(origin, coveredRadius); continue; }
            Bounds expanded = bounds; expanded.Expand(.4f);
            if (expanded.SqrDistance(origin) < SurveyRadius * SurveyRadius) Add(observation, bounds);
        }
        surveyMilliseconds = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - surveyStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        if (ScenicTrace.Enabled)
        {
            CameraTraceRecord row = ScenicTrace.Row("survey").Field("owner", owner).Field("survey", surveyNumber)
                .Field("query", QueryId).Field("stage", QueryStage).Field("radius", coveredRadius)
                .Field("retained", foliage.Count).Field("omitted", omitted).Field("candidates", candidates)
                .Field("detailCellsUsed", DetailBudget - detailRemaining).Field("milliseconds", surveyMilliseconds)
                .Field("nearestOmittedDistance", nearestOmittedDistance);
            row = ScenicTrace.Vector(ScenicTrace.Vector(ScenicTrace.Vector(row, "origin", origin), "omittedCentre", nearestOmitted.center), "omittedExtent", nearestOmitted.extents);
            ScenicTrace.Record(row);
        }
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
                name.IndexOf("sapling", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return transform.GetComponentInParent<TreeBase>() || transform.GetComponentInParent<TreeLog>();
    }

    private void Add(ScenicFoliageScene.Observation observation, Bounds coarse)
    {
        Renderer renderer = observation.Renderer;
        // Only the camera's local foreground needs fine geometry. Far bounds
        // remain a bounded discovery envelope and are refined after recentering.
        if (coarse.SqrDistance(origin) <= DetailRadius * DetailRadius && detailRemaining > 0)
        {
            Bounds[]? cells = ScenicFoliageScene.Shared.Geometry(renderer);
            if (cells != null && cells.Length <= detailRemaining)
            {
                detailRemaining -= cells.Length; detailed++;
                Add(new Entry(observation, cells), coarse);
                return;
            }
        }
        coarseCount++; Add(new Entry(observation, null), coarse);
    }

    private void Add(Entry entry, Bounds bounds)
    {
        if (!TargetRoster.Finite(bounds.center) || !TargetRoster.Finite(bounds.extents) || bounds.size.sqrMagnitude < 0.01f) return;
        bounds.Expand(0.4f); // Clearance from shader-animated leaf edges.
        float distance = Mathf.Sqrt(bounds.SqrDistance(origin));
        if (distance >= SurveyRadius) return;
        candidates++;
        if (foliage.Count < Capacity)
        {
            foliage.Add(entry); distances.Add(distance);
            if (distance >= furthestDistance) { furthest = foliage.Count - 1; furthestDistance = distance; }
            return;
        }
        // Keep the nearest bounds. Retain a conservative coverage radius so a
        // truncated cache never silently declares an unexamined position clear.
        if (distance >= furthestDistance) { Omit(bounds, distance); return; }
        if (!foliage[furthest].Observation.TryBounds(out Bounds evicted)) { coveredRadius = 0; evicted = default; }
        evicted.Expand(.4f);
        Omit(evicted, furthestDistance);
        foliage[furthest] = entry; distances[furthest] = distance;
        furthestDistance = 0;
        for (int i = 0; i < distances.Count; i++)
            if (distances[i] >= furthestDistance) { furthest = i; furthestDistance = distances[i]; }
    }

    private void Omit(Bounds bounds, float distance)
    {
        omitted++; coveredRadius = Mathf.Min(coveredRadius, distance);
        if (distance < nearestOmittedDistance) { nearestOmittedDistance = distance; nearestOmitted = bounds; }
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
                .Field("retained", foliage.Count).Field("omitted", omitted).Field("candidates", candidates)
                .Field("detailed", detailed).Field("coarse", coarseCount).Field("detailRemaining", detailRemaining)
                .Field("activeBounds", activeCount).Field("surveyMilliseconds", surveyMilliseconds).Field("focusVisible", focusVisible).Field("foreground", foreground)
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
        if (checkedFrame == Time.frameCount) return;
        checkedFrame = Time.frameCount; activeCount = 0;
        for (int i = 0; i < foliage.Count; i++)
        {
            Entry entry = foliage[i]; Renderer renderer = entry.Renderer;
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!entry.Observation.TryBounds(out Bounds observed))
            { coveredRadius = entry.Observation.LimitUnknownCoverage(origin, coveredRadius); continue; }
            if (entry.Local == null) AddActive(observed);
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
    private Result Invalid(string reason)
    { ScenicTrace.Event(owner + "-invalid", reason, QueryId); return new Result(false, false, false, 0, 0, reason); }
}
