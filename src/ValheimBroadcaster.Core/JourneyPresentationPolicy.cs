using System;

namespace ValheimBroadcaster.Core;

/// <summary>Identity retained at the camera solve, before rendering. The next
/// Update must use this snapshot, not the camera's already-expired frame context.</summary>
public readonly struct JourneyPresentationIdentity
{
    public readonly long Shot, Revision, Scene;
    public readonly int Frame;
    public JourneyPresentationIdentity(long shot, long revision, long scene, int frame)
    { Shot = shot; Revision = revision; Scene = scene; Frame = frame; }
    public bool Valid => Shot > 0 && Revision >= 0 && Scene > 0 && Frame >= 0;
}

/// <summary>The progress, clock and actual camera pose belonging to one clean
/// captured source. Later camera solves cannot change this evidence.</summary>
public readonly struct JourneyPresentationSample
{
    public readonly CameraRenderAudit Audit;
    public readonly double Time, Progress;
    public JourneyPresentationSample(CameraRenderAudit audit, double time, double progress)
    { Audit = audit; Time = time; Progress = progress; }
    public AutonomyPosition Position => new AutonomyPosition((float)Audit.Captured.X,
        (float)Audit.Captured.Y, (float)Audit.Captured.Z);
}

/// <summary>Consumes preceding-render evidence during the next Update. This is
/// deliberately independent of the current-frame encoder/IMGUI picture selector:
/// that frame has not rendered yet. A source can earn journey credit only once.</summary>
public sealed class JourneyPresentationPolicy
{
    private long consumedGeneration = -1;
    private int consumedFrame = -1;

    public bool TryTake(JourneyPresentationSample sample, FramePresentation lastPresentation,
        int renderedFrame, int sourceFrame, int updateFrame, double now, long generation,
        JourneyPresentationIdentity expected, int cameraId, out JourneyPresentationSample accepted)
    {
        accepted = default;
        return expected.Valid && TryTake(sample, lastPresentation, renderedFrame, sourceFrame, updateFrame,
            now, generation, expected.Shot, expected.Revision, expected.Scene, cameraId, expected.Frame, out accepted);
    }

    public bool TryTake(JourneyPresentationSample sample, FramePresentation lastPresentation,
        int renderedFrame, int sourceFrame, int updateFrame, double now, long generation,
        long shot, long revision, long scene, int cameraId, int expectedFrame,
        out JourneyPresentationSample accepted)
    {
        accepted = default;
        CameraRenderAudit audit = sample.Audit;
        if (lastPresentation != FramePresentation.Live || updateFrame < 0 || renderedFrame < updateFrame - 1 ||
            renderedFrame > updateFrame || sourceFrame != renderedFrame || expectedFrame != sourceFrame ||
            audit.CaptureFrame != sourceFrame || generation <= 0 || generation < consumedGeneration ||
            audit.Generation != generation || audit.AppliedGeneration != generation ||
            audit.Shot != shot || shot <= 0 || audit.Revision != revision || audit.Scene != scene ||
            cameraId == 0 || audit.CameraId != cameraId || audit.AppliedCameraId != cameraId ||
            audit.Status != CameraRenderAuditStatus.Matched || !audit.Captured.Valid ||
            Math.Abs(audit.Captured.X) > 20000 || Math.Abs(audit.Captured.Y) > 20000 || Math.Abs(audit.Captured.Z) > 20000 ||
            !Finite(sample.Progress) || sample.Progress < 0 || sample.Progress > 1 ||
            !Finite(sample.Time) || !Finite(now) || sample.Time < 0 || now < sample.Time ||
            now - sample.Time > ProgramFramePolicy.MaximumRenderAge ||
            consumedGeneration == generation && sourceFrame <= consumedFrame)
            return false;
        consumedGeneration = generation; consumedFrame = sourceFrame;
        accepted = sample;
        return true;
    }

    public void Reset() { consumedGeneration = -1; consumedFrame = -1; }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
