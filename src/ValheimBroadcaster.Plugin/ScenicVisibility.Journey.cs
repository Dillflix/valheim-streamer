using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class ScenicVisibility
{
    private readonly JourneyViewSample[] journeySamples = new JourneyViewSample[JourneyViewPolicy.SampleCount];
    private readonly Vector3[] journeyDirections = new Vector3[JourneyViewPolicy.SampleCount];

    // Only a stationary, already admitted picture may reuse foliage evidence
    // while its next triangle query runs. Solid and scene checks still run live.
    private FoliageProjection.JourneyObservation? stationaryLeaves;
    private Vector3 stationaryCamera;
    private Quaternion stationaryRotation;
    private float stationaryFov, stationaryAspect, stationaryNear, stationaryAt;
    private long stationaryPlan, stationaryEpoch;

    internal readonly struct JourneyViewObservation
    {
        internal readonly JourneyViewPolicy.Result View;
        internal readonly bool CameraClear, InsideVegetation, SurveyComplete, SceneReady;
        internal readonly Vector3 ReadinessPoint;
        internal JourneyViewObservation(JourneyViewPolicy.Result view, bool clear, bool inside,
            bool complete, bool sceneReady, Vector3 readinessPoint)
        { View = view; CameraClear = clear; InsideVegetation = inside; SurveyComplete = complete;
          SceneReady = sceneReady; ReadinessPoint = readinessPoint; }
    }

    // Shared by native route preflight, opening search, live movement and proof
    // renewal. The caller owns the camera-work scope. This bounded 15-cell
    // query observes picture depth; it does not certify the body corridor.
    internal JourneyViewObservation EvaluateJourney(Vector3 camera, Quaternion rotation, float fov,
        float aspect, float nearClip, bool stationaryPicture = false, bool continuing = false)
    {
        float magnitude = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        if (!TargetRoster.Finite(camera) || !Finite(magnitude) || magnitude < .0001f ||
            !Finite(fov) || fov < 1 || fov > 170 || !Finite(aspect) || aspect <= 0 || !Finite(nearClip) || nearClip <= 0)
            return new JourneyViewObservation(JourneyViewPolicy.Evaluate(null, true, false, false, nearClip),
                true, false, false, false, camera);
        Refresh(camera); RefreshActive(); rotation = rotation.normalized;
        Vector3 forward = rotation * Vector3.forward, right = rotation * Vector3.right, up = rotation * Vector3.up;
        float vertical = Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
        Vector3 readinessPoint = camera + forward * (float)JourneyViewPolicy.ObservationRange;
        bool complete = ScenicObservationPolicy.Coverage(Vector3.Distance(camera, origin), coveredRadius) == ScenicCoverageGap.None;
        bool inside = false;
        int count = Physics.OverlapSphereNonAlloc(camera, .25f, overlaps, obstructionMask, QueryTriggerInteraction.Ignore);
        bool cameraClear = !SolidOverlap(count);
        if (cameraClear)
        {
            count = Physics.OverlapBoxNonAlloc(camera + forward * nearClip,
                new Vector3(nearClip * vertical * aspect, nearClip * vertical, .04f), overlaps,
                rotation, obstructionMask, QueryTriggerInteraction.Ignore);
            cameraClear = !SolidOverlap(count);
        }
        bool sceneReady = PlayerShotReadiness.PositionReady(camera, true);
        bool solidCameraClear = cameraClear;
        bool retainedLeaves = false;
        long epoch = CameraSpatialScene.Shared.Epoch;
        if (stationaryPicture && (!cameraClear || !complete || !sceneReady || stationaryPlan != QueryId ||
            stationaryEpoch != epoch || !stationaryCamera.Equals(camera) || !stationaryRotation.Equals(rotation) ||
            stationaryFov != fov || stationaryAspect != aspect || stationaryNear != nearClip))
            stationaryLeaves = null;
        FoliageProjection.JourneyObservation? leaves = null;
        if (cameraClear && complete)
        {
            for (int row = 0; row < JourneyViewPolicy.Rows; row++)
            for (int column = 0; column < JourneyViewPolicy.Columns; column++)
            {
                // Samples span the useful picture, including its outer context,
                // and use the same actual composed rotation as final rendering.
                float x = (column - 2) * .40f, y = (row - 1) * .65f;
                journeyDirections[row * JourneyViewPolicy.Columns + column] =
                    (forward + right * (x * vertical * aspect) + up * (y * vertical)).normalized;
            }
            leaves = foliage.ObserveJourney(camera, rotation, fov, aspect, nearClip, journeyDirections, QueryId, QueryStage);
            if (stationaryPicture && sceneReady && leaves.Complete && !leaves.Intersects)
            {
                stationaryLeaves = leaves; stationaryAt = Time.unscaledTime;
                stationaryCamera = camera; stationaryRotation = rotation;
                stationaryFov = fov; stationaryAspect = aspect; stationaryNear = nearClip;
                stationaryPlan = QueryId; stationaryEpoch = epoch;
            }
            else if (stationaryPicture && leaves.Intersects) stationaryLeaves = null;
            else if (stationaryPicture && continuing && !leaves.Complete && stationaryLeaves != null && Time.unscaledTime - stationaryAt <= .20f)
            {
                leaves = stationaryLeaves;
                retainedLeaves = true;
            }
            inside = leaves.Complete && leaves.BodyIntersection;
            cameraClear &= !leaves.Complete || !leaves.NearPlaneIntersection;
            if (leaves.Complete && !inside && cameraClear)
            for (int sample = 0; sample < journeyDirections.Length; sample++)
            {
                Vector3 direction = journeyDirections[sample];
                bool loaded = PlayerShotReadiness.PositionReady(camera + direction * (float)JourneyViewPolicy.ObservationRange, true);
                sceneReady &= loaded;
                double solidDepth = JourneyViewPolicy.ObservationRange;
                count = Physics.RaycastNonAlloc(camera, direction, hits, (float)JourneyViewPolicy.ObservationRange,
                    obstructionMask, QueryTriggerInteraction.Ignore);
                bool known = loaded && count < hits.Length;
                for (int i = 0; i < count; i++)
                    if (Solid(hits[i].collider)) solidDepth = System.Math.Min(solidDepth, hits[i].distance);
                journeySamples[sample] = new JourneyViewSample(solidDepth, leaves.Depth[sample], known, leaves.Known[sample], leaves.UncertainDepth[sample]);
            }
        }
        JourneyViewPolicy.Result view = JourneyViewPolicy.Evaluate(journeySamples, cameraClear,
            complete && leaves != null && leaves.Complete, inside, nearClip);
        if (view.Status == JourneyViewStatus.Unknown && leaves != null && !leaves.Complete)
            view = new JourneyViewPolicy.Result(JourneyViewStatus.Unknown, 0, 0, 0, 0, 0, leaves.Reason);
        if (leaves != null && leaves.Complete && leaves.Intersects)
            view = new JourneyViewPolicy.Result(JourneyViewStatus.CameraBlocked, 0, 0, 0, 0, 0, leaves.Reason);
        if (ScenicTrace.Enabled)
        {
            CameraTraceRecord trace = ScenicTrace.Row("journey-view").Field("owner", owner).Field("query", QueryId).Field("stage", QueryStage)
                .Field("status", view.Status.ToString()).Field("cameraClear", cameraClear).Field("insideVegetation", inside)
                .Field("solidCameraClear", solidCameraClear).Field("nearPlaneFoliage", leaves?.NearPlaneIntersection ?? false)
                .Field("surveyComplete", complete).Field("sceneReady", sceneReady).Field("nearOcclusion", view.NearOcclusion)
                .Field("foliageCoverage", view.FoliageCoverage).Field("meanDepth", view.MeanDepth)
                .Field("foliageUncertainty", view.FoliageUncertainty).Field("triangleComplete", leaves?.Complete == true && !retainedLeaves).Field("retainedTriangleResult", retainedLeaves)
                .Field("containingBroadBounds", leaves?.ContainingBounds ?? 0).Field("exactRenderers", leaves?.ExactRenderers ?? 0)
                .Field("unsupportedRenderers", leaves?.UnsupportedRenderers ?? 0)
                .Field("usefulColumns", view.UsefulColumns).Field("score", view.Score).Field("reason", view.Reason);
            if (cameraClear && !inside && complete && leaves != null && leaves.Complete)
                for (int i = 0; i < journeySamples.Length; i++) trace.Field("solid" + i, journeySamples[i].SolidDepth)
                    .Field("foliage" + i, journeySamples[i].FoliageDepth).Field("known" + i, journeySamples[i].Known)
                    .Field("foliageKnown" + i, journeySamples[i].FoliageKnown).Field("uncertainFoliage" + i, journeySamples[i].UncertainFoliageDepth);
            ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Vector(trace, "camera", camera), "forward", forward));
        }
        return new JourneyViewObservation(view, cameraClear, inside, complete, sceneReady, readinessPoint);
    }
}
