using System;

namespace ValheimBroadcaster.Core;

/// <summary>Value-only projection snapshot: no per-render arrays or Unity dependency.</summary>
public readonly struct CameraProjectionSnapshot
{
    private readonly double a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p;
    public CameraProjectionSnapshot(double m00, double m01, double m02, double m03,
        double m10, double m11, double m12, double m13, double m20, double m21, double m22, double m23,
        double m30, double m31, double m32, double m33)
    { a = m00; b = m01; c = m02; d = m03; e = m10; f = m11; g = m12; h = m13;
      i = m20; j = m21; k = m22; l = m23; m = m30; n = m31; o = m32; p = m33; }
    public double this[int index] => index switch {
        0 => a, 1 => b, 2 => c, 3 => d, 4 => e, 5 => f, 6 => g, 7 => h,
        8 => i, 9 => j, 10 => k, 11 => l, 12 => m, 13 => n, 14 => o, 15 => p,
        _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public bool Valid
    {
        get
        {
            bool nonzero = false;
            for (int x = 0; x < 16; x++) { if (!CameraRenderPose.Finite(this[x])) return false; nonzero |= this[x] != 0; }
            return nonzero;
        }
    }
    public double Difference(CameraProjectionSnapshot other)
    { double result = 0; for (int x = 0; x < 16; x++) result = Math.Max(result, Math.Abs(this[x] - other[x])); return result; }
}

/// <summary>Exact camera pose/lens at a pipeline boundary, not a target or desired offset.</summary>
public readonly struct CameraRenderPose
{
    public readonly double X, Y, Z, Qx, Qy, Qz, Qw, Fov;
    public readonly CameraProjectionSnapshot Projection, NonJitteredProjection;
    public CameraRenderPose(double x, double y, double z, double qx, double qy, double qz, double qw,
        double fov, CameraProjectionSnapshot projection, CameraProjectionSnapshot nonJitteredProjection = default)
    { X = x; Y = y; Z = z; Qx = qx; Qy = qy; Qz = qz; Qw = qw; Fov = fov; Projection = projection;
      NonJitteredProjection = nonJitteredProjection.Valid ? nonJitteredProjection : projection; }
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private double QuaternionLengthSquared => Qx * Qx + Qy * Qy + Qz * Qz + Qw * Qw;
    public bool Valid => Finite(X) && Finite(Y) && Finite(Z) && Finite(Qx) && Finite(Qy) &&
        Finite(Qz) && Finite(Qw) && QuaternionLengthSquared >= .000001 && QuaternionLengthSquared <= 100 &&
        Finite(Fov) && Fov > 0 && Fov < 180 && Projection.Valid;
    public CameraPoseDifference Difference(CameraRenderPose other)
    {
        if (!Valid || !other.Valid) return default;
        double dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        double dot = Math.Abs(Qx * other.Qx + Qy * other.Qy + Qz * other.Qz + Qw * other.Qw) /
            Math.Sqrt(QuaternionLengthSquared * other.QuaternionLengthSquared);
        double raw = Projection.Difference(other.Projection), stable = NonJitteredProjection.Difference(other.NonJitteredProjection);
        bool jitter = raw > .0001 && stable <= .0001 && raw <= .01;
        // Ordinary temporal jitter changes only the two off-axis projection
        // terms. Never excuse a flipped/scaled/custom matrix as harmless jitter.
        for (int index = 0; jitter && index < 16; index++)
            if (index != 2 && index != 6 && Math.Abs(Projection[index] - other.Projection[index]) > .0001) jitter = false;
        return new CameraPoseDifference(Math.Sqrt(dx * dx + dy * dy + dz * dz),
            2 * Math.Acos(Math.Min(1, dot)) * 180 / Math.PI, Math.Abs(Fov - other.Fov), raw, stable, jitter);
    }
    internal CameraTraceRecord Append(CameraTraceRecord row, string prefix) => row
        .Field(prefix + "X", X).Field(prefix + "Y", Y).Field(prefix + "Z", Z)
        .Field(prefix + "Qx", Qx).Field(prefix + "Qy", Qy).Field(prefix + "Qz", Qz).Field(prefix + "Qw", Qw)
        .Field(prefix + "Fov", Fov);
}

public readonly struct CameraPoseDifference
{
    public readonly bool Known, BoundedProjectionJitter;
    public readonly double PositionMetres, RotationDegrees, FovDegrees, ProjectionMaximum, NonJitteredProjectionMaximum;
    public CameraPoseDifference(double position, double rotation, double fov, double projection, double nonJittered, bool jitter)
    { Known = true; PositionMetres = position; RotationDegrees = rotation; FovDegrees = fov;
      ProjectionMaximum = projection; NonJitteredProjectionMaximum = nonJittered; BoundedProjectionJitter = jitter; }
    // Small tolerances absorb float roundoff, not a visible relocation or roll.
    public bool Changed => Known && (PositionMetres > .01 || RotationDegrees > .1 || FovDegrees > .01 ||
        ProjectionMaximum > .0001 && !BoundedProjectionJitter);
    internal CameraTraceRecord Append(CameraTraceRecord row, string prefix) => row.Field(prefix + "Known", Known)
        .Field(prefix + "Metres", Known ? PositionMetres : double.NaN)
        .Field(prefix + "Degrees", Known ? RotationDegrees : double.NaN)
        .Field(prefix + "FovDegrees", Known ? FovDegrees : double.NaN)
        .Field(prefix + "Projection", Known ? ProjectionMaximum : double.NaN)
        .Field(prefix + "NonJitteredProjection", Known ? NonJitteredProjectionMaximum : double.NaN)
        .Field(prefix + "BoundedJitter", Known && BoundedProjectionJitter);
}

public enum CameraRenderAuditStatus { Unavailable, Matched, MissingApplication, MissingPreRender, IdentityMismatch, InvalidPose, Changed }

/// <summary>Immutable evidence travels with the captured source. A held picture
/// retains its original audit; subsequent camera updates cannot reattribute it.</summary>
public readonly struct CameraRenderAudit
{
    public readonly CameraRenderAuditStatus Status;
    public readonly long Generation, AppliedGeneration, Shot, Revision, Scene;
    public readonly int Candidate, CameraId, AppliedCameraId, AppliedFrame, PreRenderFrame, CaptureFrame;
    public readonly CameraRenderPose Accepted, Applied, PreRender, Captured;
    public readonly CameraPoseDifference AcceptedToApplied, AppliedToPreRender, PreRenderToCapture;
    public bool ProjectionMatches => Status == CameraRenderAuditStatus.Matched;
    public CameraRenderAudit(long generation, long shot, long revision, long scene, int candidate,
        int cameraId, int appliedFrame, int preRenderFrame, int captureFrame,
        CameraRenderPose accepted, CameraRenderPose applied, CameraRenderPose preRender, CameraRenderPose captured,
        bool sameIdentity = true, long appliedGeneration = -1, int appliedCameraId = 0)
    {
        Generation = generation; Shot = shot; Revision = revision; Scene = scene; Candidate = candidate;
        CameraId = cameraId; AppliedFrame = appliedFrame; PreRenderFrame = preRenderFrame; CaptureFrame = captureFrame;
        AppliedGeneration = appliedGeneration; AppliedCameraId = appliedCameraId;
        Accepted = accepted; Applied = applied; PreRender = preRender; Captured = captured;
        AcceptedToApplied = accepted.Difference(applied);
        AppliedToPreRender = appliedFrame == captureFrame && preRenderFrame == captureFrame ? applied.Difference(preRender) : default;
        PreRenderToCapture = preRenderFrame == captureFrame ? preRender.Difference(captured) : default;
        Status = appliedFrame != captureFrame ? CameraRenderAuditStatus.MissingApplication :
            !sameIdentity ? CameraRenderAuditStatus.IdentityMismatch :
            preRenderFrame != captureFrame ? CameraRenderAuditStatus.MissingPreRender :
            !accepted.Valid || !applied.Valid || !preRender.Valid || !captured.Valid ? CameraRenderAuditStatus.InvalidPose :
            AcceptedToApplied.Changed || AppliedToPreRender.Changed || PreRenderToCapture.Changed ? CameraRenderAuditStatus.Changed :
            CameraRenderAuditStatus.Matched;
    }
    public CameraTraceRecord Append(CameraTraceRecord row)
    {
        row.Field("poseAudit", Status.ToString());
        if (Status == CameraRenderAuditStatus.Unavailable) return row;
        row.Field("poseGeneration", Generation).Field("poseShot", Shot).Field("poseRevision", Revision)
            .Field("poseScene", Scene).Field("poseCandidate", Candidate).Field("poseCamera", CameraId)
            .Field("poseAppliedGeneration", AppliedGeneration).Field("poseAppliedCamera", AppliedCameraId)
            .Field("poseAppliedFrame", AppliedFrame).Field("posePreRenderFrame", PreRenderFrame).Field("poseCaptureFrame", CaptureFrame);
        AcceptedToApplied.Append(row, "acceptedToApplied"); AppliedToPreRender.Append(row, "appliedToRender");
        PreRenderToCapture.Append(row, "renderToCapture");
        Accepted.Append(row, "accepted"); Applied.Append(row, "applied"); PreRender.Append(row, "render"); Captured.Append(row, "captured");
        // Projection evidence is separate from readback row orientation. These
        // values describe the CPU camera matrix, never a verdict about GPU rows.
        row.Field("renderProjectionM00", PreRender.Projection[0]).Field("renderProjectionM02", PreRender.Projection[2])
            .Field("renderProjectionM11", PreRender.Projection[5]).Field("renderProjectionM12", PreRender.Projection[6])
            .Field("renderProjectionM22", PreRender.Projection[10]).Field("renderProjectionM23", PreRender.Projection[11])
            .Field("renderProjectionM32", PreRender.Projection[14]).Field("renderProjectionM33", PreRender.Projection[15]);
        return row;
    }
}

/// <summary>One bounded pending anomaly survives the reporting cooldown and
/// subsequent healthy frames. Reports retain the original observation time.</summary>
public sealed class CameraRenderAuditEvents
{
    private CameraRenderAudit pending;
    private bool hasPending;
    private double pendingAt, nextReport, lastObservation = -1;
    public long Observed { get; private set; }
    public long Changed { get; private set; }
    public long Incomplete { get; private set; }
    public double MaximumPositionMetres { get; private set; }
    public double MaximumRotationDegrees { get; private set; }
    public double MaximumFovDegrees { get; private set; }
    public double MaximumProjectionDelta { get; private set; }
    public void Observe(CameraRenderAudit audit, double at)
    {
        Validate(at);
        if (at < lastObservation) { hasPending = false; nextReport = 0; }
        lastObservation = at; Observed++;
        Accumulate(audit.AcceptedToApplied); Accumulate(audit.AppliedToPreRender); Accumulate(audit.PreRenderToCapture);
        if (audit.Status == CameraRenderAuditStatus.Changed) Changed++;
        else if (audit.Status != CameraRenderAuditStatus.Matched) Incomplete++;
        if (audit.Status == CameraRenderAuditStatus.Matched || audit.Status == CameraRenderAuditStatus.Unavailable) return;
        if (!hasPending || Severity(audit) > Severity(pending))
        { pending = audit; pendingAt = at; hasPending = true; }
    }
    public bool TryTake(double now, out CameraRenderAudit audit, out double observedAt, bool final = false)
    {
        Validate(now);
        audit = default; observedAt = 0;
        if (!hasPending || !final && now < nextReport) return false;
        audit = pending; observedAt = pendingAt; hasPending = false; nextReport = now + 1;
        return true;
    }
    private static double Severity(CameraRenderAudit value) => value.Status == CameraRenderAuditStatus.Changed
        ? 1000 + Math.Min(1000, Math.Max(value.AcceptedToApplied.RotationDegrees,
            Math.Max(value.AppliedToPreRender.RotationDegrees, value.PreRenderToCapture.RotationDegrees)))
        : (int)value.Status;
    private void Accumulate(CameraPoseDifference delta)
    {
        if (!delta.Known) return;
        MaximumPositionMetres = Math.Max(MaximumPositionMetres, delta.PositionMetres);
        MaximumRotationDegrees = Math.Max(MaximumRotationDegrees, delta.RotationDegrees);
        MaximumFovDegrees = Math.Max(MaximumFovDegrees, delta.FovDegrees);
        MaximumProjectionDelta = Math.Max(MaximumProjectionDelta, delta.ProjectionMaximum);
    }
    private static void Validate(double at)
    { if (!CameraRenderPose.Finite(at) || at < 0) throw new ArgumentOutOfRangeException(nameof(at)); }
    public void Reset()
    {
        pending = default; hasPending = false; pendingAt = nextReport = 0; lastObservation = -1; Observed = Changed = Incomplete = 0;
        MaximumPositionMetres = MaximumRotationDegrees = MaximumFovDegrees = MaximumProjectionDelta = 0;
    }
}
