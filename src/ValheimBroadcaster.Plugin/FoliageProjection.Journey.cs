using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class FoliageProjection
{
    internal sealed class JourneyObservation
    {
        internal readonly float[] Depth = new float[JourneyViewPolicy.SampleCount];
        internal readonly float[] UncertainDepth = new float[JourneyViewPolicy.SampleCount];
        internal readonly bool[] Known = new bool[JourneyViewPolicy.SampleCount];
        internal bool Complete, BodyIntersection, NearPlaneIntersection;
        internal int ExactRenderers, UnsupportedRenderers, ContainingBounds;
        internal int CompletedFrame = -1;
        internal string Reason = "Triangle observations pending";
        internal bool Intersects => BodyIntersection || NearPlaneIntersection;
    }
    private sealed class JourneyWork
    {
        internal readonly Vector3 Camera, NearCenter, NearExtents;
        internal readonly Quaternion Rotation;
        internal readonly float Fov, Aspect, Near;
        internal readonly long Revision, Epoch, QueryId;
        internal readonly string Stage;
        internal readonly Vector3[] Directions;
        internal readonly List<ScenicFoliageScene.Observation> Sources = new List<ScenicFoliageScene.Observation>();
        internal readonly List<(Renderer Renderer, Matrix4x4 Matrix, Mesh? Mesh, Bounds Bounds, bool Enabled, bool ForcedOff, bool Active)> Observed =
            new List<(Renderer, Matrix4x4, Mesh?, Bounds, bool, bool, bool)>();
        internal readonly float[] Certain = new float[JourneyViewPolicy.SampleCount], Uncertain = new float[JourneyViewPolicy.SampleCount];
        internal readonly JourneyObservation Result = new JourneyObservation();
        internal int SourceIndex, Shape = -2;
        internal Renderer? Renderer;
        internal Matrix4x4 Matrix;
        internal Bounds Broad, NearBounds;
        internal FoliageTriangleGeometry? Geometry;
        internal FoliageTriangleGeometry.Query? Pending;
        internal bool SourceStamped;
        internal JourneyWork(Vector3 camera, Quaternion rotation, float fov, float aspect, float near,
            Vector3[] directions, long revision, long epoch, long queryId, string stage)
        {
            Camera = camera; Rotation = rotation; Fov = fov; Aspect = aspect; Near = near;
            Revision = revision; Epoch = epoch; QueryId = queryId; Stage = stage; Directions = (Vector3[])directions.Clone();
            float tangent = Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
            NearCenter = camera + rotation * (Vector3.forward * near);
            NearExtents = new Vector3(near * tangent * aspect, near * tangent, .04f);
            Vector3 x = rotation * Vector3.right * NearExtents.x, y = rotation * Vector3.up * NearExtents.y,
                z = rotation * Vector3.forward * NearExtents.z;
            NearBounds = new Bounds(NearCenter, new Vector3(Math.Abs(x.x) + Math.Abs(y.x) + Math.Abs(z.x),
                Math.Abs(x.y) + Math.Abs(y.y) + Math.Abs(z.y), Math.Abs(x.z) + Math.Abs(y.z) + Math.Abs(z.z)) * 2);
            for (int i = 0; i < Certain.Length; i++) Certain[i] = Uncertain[i] = (float)JourneyViewPolicy.FoliageRange;
        }
        internal bool Matches(Vector3 camera, Quaternion rotation, float fov, float aspect, float near, long epoch) =>
            Camera == camera && Rotation == rotation && Fov == fov && Aspect == aspect && Near == near && Epoch == epoch;
    }
    private JourneyWork? journeyWork, otherJourneyWork;
    private readonly Dictionary<long, float> journeyTraceTimes = new Dictionary<long, float>();
    private static int journeyTraceFrame = -1, journeyTraceCount;
    private void ResetJourneyObservation() { journeyWork = otherJourneyWork = null; journeyTraceTimes.Clear(); }

    // Bounding boxes select triangle work; they never prove a leaf occupies a
    // camera point or sightline. Unsupported renderer precision is explicitly
    // uncertain visual evidence. Actual scene/catalogue incompleteness stays
    // pending in the caller and cannot be disguised as unsupported geometry.
    internal JourneyObservation ObserveJourney(Vector3 camera, Quaternion rotation, float fov, float aspect,
        float near, Vector3[] directions, long queryId, string stage)
    {
        CameraWorkKind queryKind = CameraWorkSchedule.FoliageQueryKind(CameraWorkBudget.Shared.WithinLive);
        if (!CameraWorkBudget.Shared.TryBegin(queryKind, out CameraWorkBudget.Lease lease))
            return new JourneyObservation { Reason = "Triangle observation budget is pending" };
        using (lease)
        {
            long sceneEpoch = CameraSpatialScene.Shared.Epoch;
            bool Matches(JourneyWork? candidate) => candidate != null && candidate.Matches(camera, rotation, fov, aspect, near, sceneEpoch) &&
                ScenicFoliageScene.Shared.CoverageFor(candidate.Revision, camera, (float)JourneyViewPolicy.FoliageRange) >= JourneyViewPolicy.FoliageRange &&
                (!candidate.Result.Complete || SourcesUnchanged(candidate));
            bool matched = Matches(journeyWork);
            if (!matched && Matches(otherJourneyWork))
            { JourneyWork? swap = journeyWork; journeyWork = otherJourneyWork; otherJourneyWork = swap; matched = true; }
            if (!matched)
            {
                // The live rig observes a proposed pose and its retained picture
                // with one visibility owner. Keep both exact queries alive so a
                // retained-picture check cannot discard unfinished movement work.
                otherJourneyWork = journeyWork;
                journeyWork = new JourneyWork(camera, rotation, fov, aspect, near, directions, revision, sceneEpoch, queryId, stage);
                foreach (Entry entry in retained) journeyWork.Sources.Add(entry.Observation);
                TraceContaining(journeyWork);
            }
            JourneyWork work = journeyWork!;
            if (work.Result.Complete) return work.Result;
            for (int step = 0; step < 32 && !work.Result.Complete && CameraWorkBudget.Shared.CanContinue(queryKind); step++)
            {
                if (work.SourceIndex >= work.Sources.Count) { Complete(work); break; }
                ScenicFoliageScene.Observation source = work.Sources[work.SourceIndex];
                Renderer renderer = source.Renderer;
                if (renderer && !work.SourceStamped)
                {
                    work.Observed.Add((renderer, renderer.localToWorldMatrix, MeshOf(renderer), renderer.bounds,
                        renderer.enabled, renderer.forceRenderingOff, renderer.gameObject.activeInHierarchy));
                    work.SourceStamped = true;
                }
                if (!renderer || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy)
                { Next(work); continue; }
                if (!source.TryBounds(out Bounds broad))
                { work.Result.Reason = "Retained foliage bounds became unavailable"; journeyWork = null; return work.Result; }
                broad.Expand(.4f);
                if (broad.SqrDistance(camera) > JourneyViewPolicy.FoliageRange * JourneyViewPolicy.FoliageRange && !broad.Intersects(work.NearBounds))
                { Next(work); continue; }
                if (work.Renderer == null)
                {
                    work.Renderer = renderer; work.Matrix = renderer.localToWorldMatrix; work.Broad = broad;
                    work.Geometry = ScenicFoliageScene.Shared.ExactGeometry(renderer, out FoliageGeometryAvailability availability, out string why);
                    if (availability == FoliageGeometryAvailability.Pending)
                    { work.Renderer = null; work.Result.Reason = why; return work.Result; }
                    TraceGeometry(work, renderer, availability, why);
                    if (work.Geometry == null)
                    {
                        work.Result.UnsupportedRenderers++;
                        for (int i = 0; i < work.Directions.Length; i++)
                            if (RayBound(work.Broad, camera, work.Directions[i], out float hit))
                                work.Uncertain[i] = Mathf.Min(work.Uncertain[i], hit);
                        Next(work); continue;
                    }
                    work.Result.ExactRenderers++;
                }
                if (renderer.localToWorldMatrix != work.Matrix)
                { work.Result.Reason = "Foliage transform changed during triangle observations"; journeyWork = null; return work.Result; }
                try
                {
                    if (work.Pending == null)
                    {
                        if (work.Shape == -2)
                        {
                            if (work.Broad.SqrDistance(camera) > .25f * .25f) { work.Shape++; continue; }
                            work.Pending = work.Geometry!.BeginSphere(work.Matrix, camera, .25f);
                        }
                        else if (work.Shape == -1)
                        {
                            if (!work.Broad.Intersects(work.NearBounds)) { work.Shape++; continue; }
                            work.Pending = work.Geometry!.BeginBox(work.Matrix, work.NearCenter, rotation, work.NearExtents);
                        }
                        else
                        {
                            if (work.Shape >= work.Directions.Length) { Next(work); continue; }
                            if (!RayBound(work.Broad, camera, work.Directions[work.Shape], out float hit) || hit > work.Certain[work.Shape])
                            { work.Shape++; continue; }
                            work.Pending = work.Geometry!.BeginRay(work.Matrix, camera, work.Directions[work.Shape], work.Certain[work.Shape]);
                        }
                    }
                    FoliageTriangleQueryStatus status = work.Pending.Step(32);
                    if (status == FoliageTriangleQueryStatus.Pending) continue;
                    if (status == FoliageTriangleQueryStatus.Hit)
                    {
                        if (work.Shape < 0)
                        {
                            work.Result.BodyIntersection = work.Shape == -2; work.Result.NearPlaneIntersection = work.Shape == -1;
                            work.Result.Reason = work.Shape == -2 ? "Camera intersects observed mesh triangles" : "Near plane intersects observed mesh triangles";
                            TraceGeometry(work, renderer, FoliageGeometryAvailability.Exact, work.Result.Reason, work.Pending.TriangleIndex);
                            Complete(work); break;
                        }
                        work.Certain[work.Shape] = Mathf.Min(work.Certain[work.Shape], (float)work.Pending.Distance);
                    }
                    work.Pending = null; work.Shape++;
                }
                catch (ArgumentException)
                {
                    // Unsupported affine geometry cannot become a positive
                    // intersection or a clear triangle result.
                    work.Result.UnsupportedRenderers++;
                    for (int i = 0; i < work.Directions.Length; i++)
                        if (RayBound(work.Broad, camera, work.Directions[i], out float hit))
                            work.Uncertain[i] = Mathf.Min(work.Uncertain[i], hit);
                    TraceGeometry(work, renderer, FoliageGeometryAvailability.Unsupported, "Triangle query transform or shape unsupported");
                    Next(work);
                }
            }
            return work.Result;
        }
    }

    private static bool RayBound(Bounds bounds, Vector3 origin, Vector3 direction, out float hit)
    {
        if (bounds.Contains(origin)) { hit = 0; return true; }
        return bounds.IntersectRay(new Ray(origin, direction), out hit) && hit >= 0 && hit < JourneyViewPolicy.FoliageRange;
    }
    private static void Next(JourneyWork work)
    { work.SourceIndex++; work.Renderer = null; work.Geometry = null; work.Pending = null; work.Shape = -2; work.SourceStamped = false; }
    private void Complete(JourneyWork work)
    {
        if (!SourcesUnchanged(work))
        {
            work.Result.BodyIntersection = work.Result.NearPlaneIntersection = false;
            work.Result.Reason = "Foliage sources changed before triangle observations completed";
            journeyWork = null; return;
        }
        for (int i = 0; i < work.Result.Depth.Length; i++)
        {
            work.Result.Known[i] = work.Uncertain[i] >= JourneyViewPolicy.FoliageRange;
            work.Result.Depth[i] = work.Certain[i];
            work.Result.UncertainDepth[i] = work.Uncertain[i];
        }
        work.Result.Complete = true;
        work.Result.CompletedFrame = Time.frameCount;
        if (!work.Result.Intersects) work.Result.Reason = work.Result.UnsupportedRenderers > 0
            ? "Observed foliage includes unsupported triangle precision" : "Exact retained triangle observations completed";
    }
    private static Mesh? MeshOf(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skin) return skin.sharedMesh;
        MeshFilter? filter = renderer.GetComponent<MeshFilter>(); return filter ? filter.sharedMesh : null;
    }
    private static bool SourcesUnchanged(JourneyWork work)
    {
        foreach (var item in work.Observed)
            if (!item.Renderer || item.Renderer.localToWorldMatrix != item.Matrix || MeshOf(item.Renderer) != item.Mesh ||
                item.Renderer.bounds != item.Bounds || item.Renderer.enabled != item.Enabled ||
                item.Renderer.forceRenderingOff != item.ForcedOff || item.Renderer.gameObject.activeInHierarchy != item.Active)
                return false;
        return true;
    }
    private void TraceContaining(JourneyWork work)
    {
        RefreshActive(); int recorded = 0;
        for (int i = 0; i < ActiveCount; i++)
        {
            if (!active[i].Contains(work.Camera)) continue;
            work.Result.ContainingBounds++;
            if (!ScenicTrace.Enabled || recorded++ >= 8) continue;
            ActiveSource source = activeSources[i]; Renderer renderer = source.Renderer;
            long key = ((long)(renderer ? renderer.GetInstanceID() : 0) << 32) ^ ((long)source.Precision << 24) ^ (uint)(source.Part + 1);
            if (!MayTrace(key)) continue;
            MeshFilter? filter = renderer ? renderer.GetComponent<MeshFilter>() : null; Mesh? mesh = filter ? filter.sharedMesh : null;
            CameraTraceRecord trace = ScenicTrace.Row("foliage-containing-bound").Field("projection", id).Field("owner", owner)
                .Field("query", work.QueryId).Field("stage", work.Stage).Field("rendererId", renderer ? renderer.GetInstanceID() : 0)
                .Field("rendererType", renderer ? renderer.GetType().Name : "missing").Field("meshId", mesh ? mesh.GetInstanceID() : 0)
                .Field("readable", mesh && mesh.isReadable).Field("vertices", mesh ? mesh.vertexCount : 0)
                .Field("precision", source.Precision.ToString()).Field("part", source.Part).Field("margin", .2)
                .Field("revision", revision).Field("sceneEpoch", work.Epoch);
            ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Vector(ScenicTrace.Vector(trace, "camera", work.Camera),
                "center", active[i].center), "extent", active[i].extents));
        }
    }
    private void TraceGeometry(JourneyWork work, Renderer renderer, FoliageGeometryAvailability availability, string reason, int triangle = -1)
    {
        if (!ScenicTrace.Enabled) return;
        if (!MayTrace(((long)renderer.GetInstanceID() << 32) ^ ((long)availability << 24) ^ (uint)(triangle + 65536))) return;
        MeshFilter? filter = renderer.GetComponent<MeshFilter>(); Mesh? mesh = filter ? filter.sharedMesh : null;
        ScenicTrace.Record(ScenicTrace.Row("foliage-narrowphase").Field("projection", id).Field("owner", owner)
            .Field("query", work.QueryId).Field("stage", work.Stage).Field("rendererId", renderer.GetInstanceID())
            .Field("rendererType", renderer.GetType().Name).Field("meshId", mesh ? mesh.GetInstanceID() : 0)
            .Field("readable", mesh && mesh.isReadable).Field("vertices", mesh ? mesh.vertexCount : 0)
            .Field("precision", availability.ToString()).Field("shape", work.Shape).Field("triangle", triangle).Field("reason", reason));
    }
    private bool MayTrace(long key)
    {
        if (journeyTraceFrame != Time.frameCount) { journeyTraceFrame = Time.frameCount; journeyTraceCount = 0; }
        if (journeyTraceCount >= 12) return false;
        if (journeyTraceTimes.TryGetValue(key, out float at) && Time.unscaledTime >= at && Time.unscaledTime - at < 2) return false;
        if (journeyTraceTimes.Count >= 256) journeyTraceTimes.Clear();
        journeyTraceTimes[key] = Time.unscaledTime; journeyTraceCount++; return true;
    }
}
