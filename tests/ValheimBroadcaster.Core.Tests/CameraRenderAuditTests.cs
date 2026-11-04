using System;
using System.Collections.Generic;
using System.Text.Json;
using ValheimBroadcaster.Core;

internal static class CameraRenderAuditTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Render audit distinguishes unchanged camera from translation after application", Translation);
        yield return ("Render audit detects upside-down orientation at the same position", Rotation);
        yield return ("Render audit treats equivalent quaternion signs as the same orientation", QuaternionSign);
        yield return ("Render audit distinguishes lens changes and projection inversion", LensAndProjection);
        yield return ("Render audit records bounded temporal jitter without claiming a camera override", Jitter);
        yield return ("Render audit separates stale application, missing render and changed identity", MissingIdentity);
        yield return ("Render audit attributes late camera changes to the render-to-capture boundary", PostRender);
        yield return ("Render audit rejects invalid poses and produces valid JSON for unknown deltas", Invalid);
        yield return ("Held picture retains the source camera audit across later shots", RetainedSource);
        yield return ("Camera pose provenance fits the bounded diagnostic frame record", MetadataBound);
        yield return ("One-frame camera anomaly survives reporting cooldown and subsequent healthy frames", BriefAnomaly);
        yield return ("Camera anomaly reporter bounds rate and flushes final evidence", ReportingLifetime);
    }

    private static void Check(bool condition, string why = "Camera render audit assertion failed")
    { if (!condition) throw new Exception(why); }
    private static CameraProjectionSnapshot Projection(double vertical = 1, double offset = 0) =>
        new CameraProjectionSnapshot(1, 0, offset, 0, 0, vertical, 0, 0, 0, 0, -1, -.2, 0, 0, -1, 0);
    private static CameraRenderPose Pose(double x = 0, double qz = 0, double qw = 1, double fov = 65,
        double vertical = 1, double offset = 0, bool stable = false) =>
        new CameraRenderPose(x, 3, 5, 0, 0, qz, qw, fov, Projection(vertical, offset), stable ? Projection() : default);
    private static CameraRenderAudit Audit(CameraRenderPose applied, CameraRenderPose render, CameraRenderPose captured,
        int appliedFrame = 8, int preRenderFrame = 8, bool identity = true, long shot = 15) =>
        new CameraRenderAudit(3, shot, 7, 4, 2, 122, appliedFrame, preRenderFrame, 8,
            applied, applied, render, captured, identity);

    private static void Translation()
    {
        var pose = Pose(); Check(Audit(pose, pose, pose).Status == CameraRenderAuditStatus.Matched);
        var moved = Pose(x: 2); var audit = Audit(pose, moved, moved);
        Check(audit.Status == CameraRenderAuditStatus.Changed && audit.AppliedToPreRender.PositionMetres == 2);
        Check(audit.PreRenderToCapture.PositionMetres == 0 && !audit.ProjectionMatches);
        var accepted = new CameraRenderAudit(3, 15, 7, 4, 2, 122, 8, 8, 8, pose, moved, moved, moved);
        Check(accepted.AcceptedToApplied.PositionMetres == 2 && accepted.AppliedToPreRender.PositionMetres == 0);
    }
    private static void Rotation()
    {
        var flipped = Pose(qz: 1, qw: 0); var audit = Audit(Pose(), flipped, flipped);
        Check(audit.Status == CameraRenderAuditStatus.Changed && audit.AppliedToPreRender.PositionMetres == 0);
        Check(Math.Abs(audit.AppliedToPreRender.RotationDegrees - 180) < .0001);
    }
    private static void QuaternionSign()
    { var negative = Pose(qw: -1); Check(Audit(Pose(), negative, negative).Status == CameraRenderAuditStatus.Matched); }
    private static void LensAndProjection()
    {
        var zoom = Pose(fov: 45); var lens = Audit(Pose(), zoom, zoom);
        Check(lens.Status == CameraRenderAuditStatus.Changed && lens.AppliedToPreRender.FovDegrees == 20);
        var inverted = Pose(vertical: -1); var projection = Audit(Pose(), inverted, inverted);
        Check(projection.Status == CameraRenderAuditStatus.Changed && projection.AppliedToPreRender.RotationDegrees == 0);
        Check(projection.AppliedToPreRender.ProjectionMaximum == 2 && !projection.AppliedToPreRender.BoundedProjectionJitter);
    }
    private static void Jitter()
    {
        var jittered = Pose(offset: .002, stable: true); var jitter = Audit(Pose(), jittered, jittered);
        Check(jitter.Status == CameraRenderAuditStatus.Matched && jitter.AppliedToPreRender.ProjectionMaximum == .002);
        Check(jitter.AppliedToPreRender.NonJitteredProjectionMaximum == 0 && jitter.AppliedToPreRender.BoundedProjectionJitter);
        var offset = Pose(offset: .2, stable: true);
        Check(Audit(Pose(), offset, offset).Status == CameraRenderAuditStatus.Changed, "Large off-axis change was excused as jitter");
        var scaled = Pose(vertical: 1.01, stable: true);
        Check(Audit(Pose(), scaled, scaled).Status == CameraRenderAuditStatus.Changed, "Matrix scale change was excused as jitter");
        var custom = Pose(offset: .002);
        Check(Audit(Pose(), custom, custom).Status == CameraRenderAuditStatus.Changed, "Non-jittered shift was ignored");
    }
    private static void MissingIdentity()
    {
        var pose = Pose();
        Check(Audit(pose, pose, pose, appliedFrame: 7).Status == CameraRenderAuditStatus.MissingApplication);
        Check(!Audit(pose, pose, pose, appliedFrame: 7).AppliedToPreRender.Known);
        Check(Audit(pose, pose, pose, preRenderFrame: 7).Status == CameraRenderAuditStatus.MissingPreRender);
        Check(Audit(pose, pose, pose, identity: false).Status == CameraRenderAuditStatus.IdentityMismatch);
    }
    private static void PostRender()
    {
        var audit = Audit(Pose(), Pose(), Pose(qz: 1, qw: 0));
        Check(audit.Status == CameraRenderAuditStatus.Changed && audit.AppliedToPreRender.RotationDegrees == 0);
        Check(audit.PreRenderToCapture.RotationDegrees == 180);
    }
    private static void Invalid()
    {
        var audit = Audit(Pose(), Pose(x: double.NaN), Pose());
        Check(audit.Status == CameraRenderAuditStatus.InvalidPose && !audit.AppliedToPreRender.Known);
        using var row = JsonDocument.Parse(audit.Append(new CameraTraceRecord("pose-audit", 1)).ToString());
        Check(row.RootElement.GetProperty("appliedToRenderMetres").ValueKind == JsonValueKind.Null);
        Check(default(CameraRenderAudit).Status == CameraRenderAuditStatus.Unavailable);
    }
    private static void RetainedSource()
    {
        var source = new DiagnosticPictureContext("Scenic", 15, 7, true, "Readable").WithRenderAudit(Audit(Pose(), Pose(), Pose()));
        var held = source;
        source = new DiagnosticPictureContext("Scenic", 16, 8, true, "Next view")
            .WithRenderAudit(Audit(Pose(), Pose(x: 2), Pose(x: 2), shot: 16));
        using var row = JsonDocument.Parse(held.Append(new CameraTraceRecord("video-sample", 2).Field("presentation", "Hold")).ToString());
        Check(row.RootElement.GetProperty("shot").GetInt64() == 15 && row.RootElement.GetProperty("poseShot").GetInt64() == 15);
        Check(row.RootElement.GetProperty("poseCaptureFrame").GetInt32() == 8 && row.RootElement.GetProperty("poseAudit").GetString() == "Matched");
        Check(source.RenderAudit.Status == CameraRenderAuditStatus.Changed);
    }
    private static void MetadataBound()
    {
        var context = new DiagnosticPictureContext("Player", 15, 7, true, new string('x', 512),
            perspective: new string('x', 512), intent: new string('x', 512), encounter: new string('x', 512), coverage: new string('x', 512))
            .WithRenderAudit(Audit(Pose(), Pose(), Pose()));
        string record = context.Append(new CameraTraceRecord("video-sample", 12345.6789)
            .Field("videoSession", new string('x', 64)).Field("traceSession", new string('x', 64))).ToString();
        using var row = JsonDocument.Parse(record);
        Check(record.Length < 8192, "Audit exhausts the diagnostic writer's per-record limit");
    }
    private static void BriefAnomaly()
    {
        var events = new CameraRenderAuditEvents();
        events.Observe(Audit(Pose(), Pose(x: 1), Pose(x: 1)), 0);
        Check(events.TryTake(0, out _, out _));
        events.Observe(Audit(Pose(), Pose(qz: 1, qw: 0), Pose(qz: 1, qw: 0), shot: 17), .4);
        Check(!events.TryTake(.4, out _, out _));
        events.Observe(Audit(Pose(), Pose(), Pose(), shot: 18), .5);
        Check(events.TryTake(1, out var retained, out var observedAt));
        Check(retained.Shot == 17 && retained.AppliedToPreRender.RotationDegrees == 180 && observedAt == .4,
            "Healthy later frame erased brief pose anomaly or replaced its source time");
        Check(events.Observed == 3 && events.Changed == 2 && events.Incomplete == 0);
        Check(events.MaximumPositionMetres == 1 && events.MaximumRotationDegrees == 180);
    }
    private static void ReportingLifetime()
    {
        var events = new CameraRenderAuditEvents();
        events.Observe(Audit(Pose(), Pose(), Pose(), appliedFrame: 7), 2);
        Check(events.TryTake(2, out _, out _));
        events.Observe(Audit(Pose(), Pose(), Pose(), appliedFrame: 7), 2.2);
        Check(!events.TryTake(2.9, out _, out _) && events.TryTake(2.9, out _, out _, final: true));
        Check(!events.TryTake(3, out _, out _));
        events.Reset(); Check(events.Observed == 0 && events.Changed == 0 && events.Incomplete == 0);
        events.Observe(Audit(Pose(), Pose(), Pose()), 0);
        Check(!events.TryTake(0, out _, out _), "Healthy startup consumed the anomaly event interval");
    }
}
