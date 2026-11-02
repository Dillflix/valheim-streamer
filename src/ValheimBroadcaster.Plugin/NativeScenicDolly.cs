using System;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>
/// Immutable world-space path shared by scenic candidate cameras. Authored
/// landmarks still define aim; Unity Splines owns the actual curve and arc-length
/// evaluation. This object does not advance time or authorize camera movement.
/// </summary>
internal sealed class ScenicSplinePath : IDisposable
{
    private const int KnotCount = 17;
    private ScenicShotPlan? plan;
    private float shapeStrength;
    internal Spline? Spline { get; private set; }
    internal bool Ready => plan != null && Spline != null && Length > .01f;
    internal float Length { get; private set; }

    internal bool Prepare(ScenicShotPlan source, float strength)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (!Finite(strength) || strength < 0 || strength > 1.5f)
            throw new ArgumentOutOfRangeException(nameof(strength));
        // Motion settings affect the rig's progress clock after preparation,
        // never the already developed path shape or its cached distance table.
        if (ReferenceEquals(plan, source)) return Ready;
        Reset(); plan = source; shapeStrength = strength;
        var positions = new Vector3[KnotCount];
        for (int i = 0; i < KnotCount; i++)
            positions[i] = FromCore(source.Sample(i / (double)(KnotCount - 1), strength).CameraPosition);
        var knots = new BezierKnot[KnotCount];
        for (int i = 0; i < KnotCount; i++)
        {
            Vector3 before = positions[Mathf.Max(0, i - 1)], after = positions[Mathf.Min(KnotCount - 1, i + 1)];
            Vector3 tangent = (after - before) / (i == 0 || i == KnotCount - 1 ? 3f : 6f);
            // Bound handles to the local sample spacing. Native curve evaluation
            // remains subject to actual segment/foliage/readability validation.
            float spacing = i == 0 ? Vector3.Distance(positions[0], after) :
                i == KnotCount - 1 ? Vector3.Distance(before, positions[i]) :
                Mathf.Min(Vector3.Distance(before, positions[i]), Vector3.Distance(positions[i], after));
            tangent = Vector3.ClampMagnitude(tangent, spacing * .45f);
            knots[i] = new BezierKnot((float3)positions[i], (float3)(-tangent), (float3)tangent, quaternion.identity);
        }
        var spline = new Spline(knots, false);
        // Broken mode preserves the deliberate, bounded handles supplied above.
        spline.SetTangentMode(TangentMode.Broken);
        float length = spline.GetLength();
        if (!Finite(length) || length <= .01f || length > 192f) return false;
        Spline = spline; Length = length;
        return true;
    }

    internal ScenicShotSample Sample(float progress)
    {
        if (!Ready) throw new InvalidOperationException("No prepared scenic spline.");
        if (!Finite(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
        float p = Mathf.Clamp01(progress);
        if (plan!.IsDirected && !plan.UseNativeSpline) return plan.Sample(p);
        Vector3 camera = SplineUtility.EvaluatePosition(Spline!, Ease(p));
        return plan!.Sample(p, shapeStrength).WithCameraPosition(ToCore(camera));
    }

    internal float Distance(float progress)
    {
        if (!Finite(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
        return Length * Ease(Mathf.Clamp01(progress));
    }

    /// <summary>Propose a finite shot through measured readable native poses.
    /// Native arc-length progress is not the authored quadratic parameter, so
    /// passing it directly to ScenicShotPlan.Slice would shift both endpoints.
    /// A three-point fitted proposal preserves measured endpoints/aims and a
    /// half-distance body point. Its new native curve must be fully validated;
    /// clear samples alone never certify the paths between them.</summary>
    internal ScenicShotPlan ProposeMeasuredSlice(float startProgress, float endProgress)
    {
        if (!Ready || !plan!.IsDirected) throw new InvalidOperationException("No directed scenic route is prepared.");
        if (!Finite(startProgress) || !Finite(endProgress) || startProgress < 0 || endProgress > 1 || startProgress >= endProgress)
            throw new ArgumentOutOfRangeException(nameof(startProgress));
        if (!plan.UseNativeSpline) return plan.Slice(startProgress, endProgress);
        ScenicShotSample first = Sample(startProgress), last = Sample(endProgress);
        // SplineUtility's normalized spline coordinate is distance normalized
        // over the entire spline, matching Sample and the selected Dolly Body.
        Vector3 middle = SplineUtility.EvaluatePosition(Spline!, (Ease(startProgress) + Ease(endProgress)) * .5f);
        Vector3 control = 2f * middle - .5f * (FromCore(first.CameraPosition) + FromCore(last.CameraPosition));
        return ScenicShotPlan.Directed(plan.Id, plan.Feature!, plan.Role,
            first.CameraPosition, last.CameraPosition, first.AimPosition, last.AimPosition,
            plan.FieldOfView, first.ScreenX, first.ScreenY, ToCore(control), plan.UseNativeSpline);
    }

    // Short smooth ramps and a steady cruise apply to distance, not knot parameter. Unequal
    // knot spacing therefore cannot produce accidental acceleration or stalls.
    private static float Ease(float value) => (float)ScenicMotionProfile.Progress(value);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static Vector3 FromCore(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
    private static AutonomyPosition ToCore(Vector3 value) => new AutonomyPosition(value.x, value.y, value.z);
    internal void Reset() { plan = null; Spline = null; Length = 0; shapeStrength = 0; }
    public void Dispose() => Reset();
}

/// <summary>
/// One native Dolly body and a translated non-rendering container per candidate.
/// The container has no colliders, Rigidbody, network identity or world effects.
/// </summary>
internal sealed class NativeScenicDolly : IDisposable
{
    private readonly GameObject pathObject;
    private readonly SplineContainer container;
    private readonly CinemachineSplineDolly dolly;
    private Spline? prepared;
    private float acceptedDistance;
    private bool hasAccepted;
    internal bool Enabled { get => dolly && dolly.enabled; set { if (dolly) dolly.enabled = value; } }
    internal float Distance => dolly ? dolly.CameraPosition : 0;

    internal NativeScenicDolly(CinemachineCamera camera, Transform parent)
    {
        if (!camera) throw new ArgumentNullException(nameof(camera));
        pathObject = new GameObject("ValheimBroadcaster.ScenicSpline") { hideFlags = HideFlags.HideAndDontSave };
        pathObject.transform.SetParent(parent, false);
        pathObject.transform.rotation = Quaternion.identity;
        pathObject.transform.localScale = Vector3.one;
        container = pathObject.AddComponent<SplineContainer>();
        dolly = camera.gameObject.AddComponent<CinemachineSplineDolly>();
        dolly.enabled = false;
        dolly.CameraRotation = CinemachineSplineDolly.RotationMode.Default;
        dolly.SplineOffset = Vector3.zero;
        dolly.Damping = default;
        dolly.AutomaticDolly = default;
        dolly.Spline = container;
        // There is no existing path position to preserve at construction.
        // Set units directly to avoid allocating a NativeSpline for an empty container.
        dolly.SplineSettings.Units = PathIndexUnit.Distance;
    }

    internal void Configure(ScenicSplinePath path, float progress, Vector3 worldOffset, bool hold = false)
    {
        if (path == null || !path.Ready) { Enabled = false; return; }
        if (!Finite(worldOffset.x) || !Finite(worldOffset.y) || !Finite(worldOffset.z)) { Enabled = false; return; }
        if (!ReferenceEquals(prepared, path.Spline))
        {
            dolly.SplineSettings.InvalidateCache();
            container.Spline = path.Spline!;
            prepared = path.Spline; hasAccepted = false;
        }
        // Translation is deliberately in world axes. SplineOffset follows the
        // curve's tangent axes and would rotate alternate-view offsets in an arc.
        pathObject.transform.position = worldOffset;
        pathObject.transform.rotation = Quaternion.identity;
        dolly.CameraPosition = hold && hasAccepted ? acceptedDistance : path.Distance(progress);
        Enabled = true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal void AcceptPosition()
    {
        if (!Enabled) return;
        acceptedDistance = dolly.CameraPosition; hasAccepted = true;
    }

    internal void HoldPosition()
    {
        if (dolly && hasAccepted) dolly.CameraPosition = acceptedDistance;
    }

    internal void Reset()
    {
        Enabled = false; prepared = null; hasAccepted = false; acceptedDistance = 0;
        if (dolly) dolly.SplineSettings.InvalidateCache();
        if (container) container.Splines = Array.Empty<Spline>();
    }

    public void Dispose()
    {
        Reset();
        if (dolly) UnityEngine.Object.Destroy(dolly);
        if (pathObject) UnityEngine.Object.Destroy(pathObject);
    }
}
