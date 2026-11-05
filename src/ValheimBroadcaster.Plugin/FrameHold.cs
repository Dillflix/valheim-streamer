using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed class FrameHold
{
    private readonly FrameContinuity continuity = new FrameContinuity();
    private readonly ProgramFramePolicy program = new ProgramFramePolicy();
    private readonly ProgramSlate slate = new ProgramSlate();
    internal bool ScenicTransition => slate.ScenicTransition;
    internal Texture SlateTexture => slate.Texture;
    internal void SetScenicTransition(bool value) => slate.SetScenicTransition(value);
    private Func<bool> active = () => false, ready = () => false, hardFailure = () => true;
    private Action<string> warning = _ => { };
    private Camera? camera;
    private FrameHoldRenderer? renderer;
    private RenderTexture? frame;
    private bool captureFailed, hardBoundary;
    private int renderedFrame = -1, reportedFrame = -1;
    private FramePresentation renderedPresentation = FramePresentation.Slate, lastPresentation = FramePresentation.Slate;
    private long holds, slates;
    private float testAt = -1;
    internal Action<RenderTexture, long, int>? ObserveLiveFrame;
    internal Action? PictureUnavailable;
    internal Func<DiagnosticPictureContext>? DescribePicture;
    internal Func<string>? DescribeOperatorSubject;
    private DiagnosticPictureContext sourceContext;
    private string sourceSubject = "";
    private int sourceFrame = -1;
    private float sourceTime;
    private CameraRenderPose acceptedPose, appliedPose, preRenderPose;
    private int appliedFrame = -1, preRenderFrame = -1, appliedCamera;
    private long appliedGeneration, appliedShot, appliedRevision, appliedScene;
    private int appliedCandidate = -1;
    private readonly CameraRenderAuditEvents auditEvents = new CameraRenderAuditEvents();
    private readonly JourneyPresentationPolicy journeyPresentation = new JourneyPresentationPolicy();
    private JourneyPresentationSample journeyCapture;
    private double appliedScenicProgress = double.NaN;
    internal CameraRenderAudit LastRenderAudit { get; private set; }
    internal long Generation => program.Generation;
    // Read-only presentation state for commands/status outside IMGUI. Metadata
    // belongs to the captured source, including when that source is held.
    internal FramePresentation CurrentPresentation => program.Select(Time.unscaledTime, Time.frameCount, active(), hardFailure(),
        renderer && renderer.isActiveAndEnabled && frame && frame.IsCreated() && !captureFailed).Presentation;
    internal DiagnosticPictureContext OperatorPicture => sourceContext;
    internal string OperatorSubject => sourceSubject;
    internal bool HasRecentLiveFrame => renderer && renderedFrame >= Time.frameCount - 1 && renderedPresentation == FramePresentation.Live;
    internal bool TryTakeScenicCapture(JourneyPresentationIdentity identity, out JourneyPresentationSample sample)
    {
        sample = default;
        if (!active() || hardFailure() || !camera || !renderer || !renderer.isActiveAndEnabled ||
            !frame || !frame.IsCreated() || captureFailed || sourceContext.Mode != "Scenic" || !sourceContext.Usable)
            return false;
        return journeyPresentation.TryTake(journeyCapture, renderedPresentation, renderedFrame, sourceFrame,
            Time.frameCount, Time.unscaledTimeAsDouble, program.Generation, identity,
            camera.GetInstanceID(), out sample);
    }
    internal string LastFailure { get; set; } = "none";
    internal string Diagnostics => $"Presentation: {lastPresentation}; {holds} held frames, {slates} slate frames; last gap: {LastFailure}; camera compositor {(renderer && renderedFrame >= Time.frameCount - 1 ? "active" : "awaiting render")}; clean program epoch {program.Generation}; capture {(captureFailed ? "unavailable" : "available")}; graphics {SystemInfo.graphicsDeviceType}. " + PoseAuditDiagnostics;
    internal string PoseAuditDiagnostics => $"Pose audit: {LastRenderAudit.Status}; {auditEvents.Observed} live captures, {auditEvents.Changed} changed, {auditEvents.Incomplete} unmatched; " +
        "applied/render " + Describe(LastRenderAudit.AppliedToPreRender) + "; render/capture " + Describe(LastRenderAudit.PreRenderToCapture) + ".";
    private static string Describe(CameraPoseDifference delta) => delta.Known
        ? $"{delta.PositionMetres:F3}m / {delta.RotationDegrees:F3}deg / lens {delta.FovDegrees:F3}deg / projection {delta.ProjectionMaximum:F5}" : "unavailable";

    // Called after a successful final camera solve and before Unity renders.
    // Numeric shot identity is captured now, never inferred by a GPU callback.
    internal void RecordApplied(Camera output, CameraRenderPose accepted, long shot, long revision, long scene, int candidate,
        double scenicProgress = double.NaN)
    {
        SynchronizeBoundary();
        if (!output || output != camera || !active() || hardFailure()) return;
        acceptedPose = accepted; appliedPose = CameraPoseSampler.Sample(output);
        appliedFrame = Time.frameCount; appliedCamera = output.GetInstanceID(); appliedGeneration = program.Generation;
        appliedShot = shot; appliedRevision = revision; appliedScene = scene; appliedCandidate = candidate;
        appliedScenicProgress = scenicProgress;
    }

    internal void BeforeRender(FrameHoldRenderer source)
    {
        if (!ReferenceEquals(source, renderer) || !camera || !active() || hardFailure()) return;
        preRenderPose = CameraPoseSampler.Sample(camera); preRenderFrame = Time.frameCount;
    }

    private CameraRenderAudit CaptureAudit(DiagnosticPictureContext context)
    {
        if (!camera) return default;
        var audit = new CameraRenderAudit(program.Generation, appliedShot, appliedRevision, appliedScene, appliedCandidate,
            camera.GetInstanceID(), appliedFrame, preRenderFrame, Time.frameCount,
            acceptedPose, appliedPose, preRenderPose, CameraPoseSampler.Sample(camera),
            appliedCamera == camera.GetInstanceID() && appliedGeneration == program.Generation &&
            appliedShot == context.Shot && appliedRevision == context.Revision &&
            appliedScene == context.PictureScene && appliedCandidate == context.Candidate,
            appliedGeneration, appliedCamera);
        LastRenderAudit = audit;
        journeyCapture = context.Mode == "Scenic" && context.Usable
            ? new JourneyPresentationSample(audit, Time.unscaledTimeAsDouble, appliedScenicProgress) : default;
        float now = Time.unscaledTime;
        auditEvents.Observe(audit, now);
        FlushPoseAudit(now);
        return audit;
    }

    private void FlushPoseAudit(double now, bool final = false)
    {
        // Retaining one anomaly across the cooldown matters: a one-frame late
        // writer may be absent from the 12fps video and already healthy next frame.
        try
        {
            if (ScenicTrace.Enabled && auditEvents.TryTake(now, out CameraRenderAudit audit, out double observedAt, final))
                ScenicTrace.Record(audit.Append(ScenicTrace.Row("pose-audit")).Field("poseObservedAt", observedAt)
                    .Field("auditedFrames", auditEvents.Observed).Field("changedFrames", auditEvents.Changed).Field("unmatchedFrames", auditEvents.Incomplete)
                    .Field("maximumPositionMetres", auditEvents.MaximumPositionMetres).Field("maximumRotationDegrees", auditEvents.MaximumRotationDegrees)
                    .Field("maximumFovDegrees", auditEvents.MaximumFovDegrees).Field("maximumProjectionDelta", auditEvents.MaximumProjectionDelta));
        }
        catch { } // Diagnostic I/O must not affect the broadcast compositor.
    }

    internal void Configure(Func<bool> isActive, Func<bool> isReady, Func<bool> isHardFailure, Action<string> reportWarning)
    { active = isActive; ready = isReady; hardFailure = isHardFailure; warning = reportWarning; }

    internal void Attach(Camera source)
    {
        if (camera == source && renderer && renderer.isActiveAndEnabled) return;
        Detach();
        camera = source;
        // Append after the game's existing post effects. No extra camera render.
        renderer = source.gameObject.AddComponent<FrameHoldRenderer>();
        renderer.Owner = this;
    }

    internal string Test()
    {
        if (!active() || hardFailure() || !renderer || !frame || !frame.IsCreated() || captureFailed ||
            renderedFrame < Time.frameCount - 1 || renderedPresentation != FramePresentation.Live)
            return "Wait for a live broadcast picture before running the held-frame test.";
        testAt = Time.unscaledTime + 2f;
        return "Close the console; a 0.5-second held-frame test starts in 2 seconds.";
    }

    internal void Render(RenderTexture source, RenderTexture destination)
    {
        SynchronizeBoundary();
        FlushPoseAudit(Time.unscaledTime);
        if (!active()) { Blit(source, destination); return; }
        float now = Time.unscaledTime;
        bool hard = hardFailure();
        if (hard) testAt = -1;
        bool testing = testAt >= 0 && now >= testAt && now < testAt + 0.5f;
        if (testAt >= 0 && now >= testAt + 0.5f) testAt = -1;
        bool live = ready() && !testing;
        try
        {
            if (frame && (!frame.IsCreated() || frame.width != source.width || frame.height != source.height ||
                frame.graphicsFormat != source.graphicsFormat)) Release();
            FramePresentation mode = continuity.Select(now, live, hard);
            if (mode == FramePresentation.Live && !captureFailed)
            {
                slate.SetScenicTransition(false);
                if (!frame)
                {
                    var descriptor = source.descriptor;
                    descriptor.depthBufferBits = 0;
                    descriptor.msaaSamples = 1;
                    descriptor.bindMS = false;
                    descriptor.useMipMap = descriptor.autoGenerateMips = false;
                    descriptor.enableRandomWrite = false;
                    frame = new RenderTexture(descriptor) { name = "Broadcaster held camera frame" };
                    if (!frame.Create()) throw new InvalidOperationException("Camera frame allocation failed.");
                }
                Blit(source, frame!);
                try { sourceContext = DescribePicture?.Invoke() ?? default; }
                catch { sourceContext = new DiagnosticPictureContext("Unknown", 0, 0, false, "Diagnostic context unavailable"); }
                try { sourceContext = sourceContext.WithRenderAudit(CaptureAudit(sourceContext)); }
                catch { LastRenderAudit = default; sourceContext = sourceContext.WithRenderAudit(default); }
                try { sourceSubject = DescribeOperatorSubject?.Invoke() ?? ""; }
                catch { sourceSubject = ""; }
                sourceFrame = Time.frameCount; sourceTime = now;
                continuity.Captured(now);
            }
            if (mode == FramePresentation.Hold && (!frame || captureFailed)) mode = FramePresentation.Slate;
            // Live and Hold have the identical final texture-to-camera path. Never
            // reinterpret an end-of-frame screenshot through IMGUI coordinates.
            if (mode == FramePresentation.Slate) Blit(slate.Texture, destination);
            else Blit(frame && !captureFailed ? frame : source, destination);
            renderedPresentation = mode;
        }
        catch (Exception error)
        {
            Release();
            if (!captureFailed) warning("Camera frame hold unavailable (" + error.GetType().Name + "); recovery slate remains enabled.");
            captureFailed = true;
            renderedPresentation = live && !hard ? FramePresentation.Live : FramePresentation.Slate;
            Blit(renderedPresentation == FramePresentation.Live ? source : slate.Texture, destination);
        }
        renderedFrame = Time.frameCount;
        program.Rendered(now, renderedFrame, renderedPresentation);
        // Picture analysis receives only the actual clean live render, not the
        // console, operator overlays, held frame, or recovery card. Consumers
        // copy synchronously into their own textures before asynchronous work.
        if (renderedPresentation == FramePresentation.Live && frame && !captureFailed)
        {
            RenderTexture target = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try { ObserveLiveFrame?.Invoke(frame, program.Generation, renderedFrame); }
            catch (Exception error)
            {
                warning("Rendered-picture analysis unavailable (" + error.GetType().Name + "); clean broadcast output continues.");
                ObserveLiveFrame = null;
            }
            finally { RenderTexture.active = target; GL.sRGBWrite = srgb; }
        }
        else NotifyUnavailable();
    }

    internal ProgramFrameStamp CopyProgramFrame(RenderTexture destination, bool flipCamera = false, bool flipSlate = false)
    {
        SynchronizeBoundary();
        ProgramFrameStamp stamp = program.Select(Time.unscaledTime, Time.frameCount, active(), hardFailure(),
            renderer && renderer.isActiveAndEnabled && frame && frame.IsCreated() && !captureFailed);
        RenderTexture previousTarget = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            // Calibration measures each source family through this same blit
            // path. This normalizes the encoder copy; local presentation stays
            // in Unity's regular layout. Picture analysis separately calibrates
            // and normalizes its private sample without modifying this source.
            bool isSlate = stamp.Presentation == FramePresentation.Slate;
            Blit(isSlate ? slate.Texture : frame!, destination, isSlate ? flipSlate : flipCamera);
        }
        finally { RenderTexture.active = previousTarget; GL.sRGBWrite = srgb; }
        return stamp;
    }

    internal bool IsProgramFrameCurrent(ProgramFrameStamp stamp)
    {
        SynchronizeBoundary();
        return program.AcceptReadback(stamp, Time.unscaledTime, active(), hardFailure());
    }

    internal DiagnosticPictureContext DiagnosticContext(ProgramFrameStamp stamp, out int frameId, out float at)
    {
        if (stamp.Presentation != FramePresentation.Slate)
        { frameId = sourceFrame; at = sourceTime; return sourceContext; }
        frameId = -1; at = (float)stamp.CopiedAt;
        // A slate has no world picture to attribute to the last camera subject.
        return new DiagnosticPictureContext("Recovery", 0, 0, false, LastFailure);
    }

    internal bool IsGenerationCurrent(long generation)
    {
        SynchronizeBoundary();
        return active() && !hardFailure() && generation == program.Generation;
    }

    private void SynchronizeBoundary()
    {
        bool hard = !active() || hardFailure();
        if (hard && !hardBoundary)
        {
            continuity.Reset(); program.Invalidate(); testAt = -1;
            ClearPoseEvidence();
            NotifyUnavailable();
        }
        hardBoundary = hard;
    }

    private void NotifyUnavailable()
    {
        try { PictureUnavailable?.Invoke(); }
        catch { PictureUnavailable = null; }
    }

    // GUI adds text or an opaque fallback only. It never draws the held image.
    internal FramePresentation Present()
    {
        SynchronizeBoundary();
        bool hard = hardFailure();
        if (hard) { continuity.Reset(); testAt = -1; }
        FramePresentation result = !hard && renderer && renderedFrame == Time.frameCount
            ? renderedPresentation : FramePresentation.Slate;
        if (Event.current.type == EventType.Repaint)
        {
            if (reportedFrame != Time.frameCount)
            {
                if (result == FramePresentation.Hold) holds++;
                if (result == FramePresentation.Slate) slates++;
            }
            reportedFrame = Time.frameCount;
            lastPresentation = result;
        }
        return result;
    }

    internal static void Blit(Texture source, RenderTexture destination, bool flipVertical = false)
    {
        bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear && (!destination || destination.sRGB);
            if (flipVertical) Graphics.Blit(source, destination, new Vector2(1, -1), new Vector2(0, 1));
            else Graphics.Blit(source, destination);
        }
        finally { GL.sRGBWrite = srgb; }
        // OnRenderImage must leave its output bound, unlike end-of-frame capture.
    }

    internal void LostRenderer(FrameHoldRenderer lost)
    {
        if (!ReferenceEquals(renderer, lost)) return;
        FlushPoseAudit(Time.unscaledTime, true);
        lost.Owner = null;
        // Retire a temporarily disabled camera's component as well; otherwise
        // reactivation would accumulate abandoned pass-through image effects.
        UnityEngine.Object.Destroy(lost);
        renderer = null; camera = null;
        Release(); renderedFrame = -1; testAt = -1;
        sourceContext = default; sourceSubject = ""; sourceFrame = -1;
        ClearPoseEvidence();
    }

    internal void Reset()
    {
        FlushPoseAudit(Time.unscaledTime, true);
        Detach(); holds = slates = 0; captureFailed = false; reportedFrame = -1;
        hardBoundary = false;
        auditEvents.Reset();
        lastPresentation = FramePresentation.Slate; LastFailure = "none";
        slate.SetScenicTransition(false);
        slate.Dispose();
    }

    private void Detach()
    {
        if (renderer)
        {
            renderer.Owner = null;
            renderer.enabled = false;
            UnityEngine.Object.Destroy(renderer);
        }
        renderer = null; camera = null;
        Release(); renderedFrame = -1; testAt = -1;
        ClearPoseEvidence();
    }

    private void Release()
    {
        continuity.Reset(); program.Invalidate(); NotifyUnavailable();
        if (frame) { frame.Release(); UnityEngine.Object.Destroy(frame); }
        frame = null;
    }

    private void ClearPoseEvidence()
    {
        acceptedPose = appliedPose = preRenderPose = default;
        appliedFrame = preRenderFrame = -1; appliedCamera = 0; appliedGeneration = -1;
        appliedShot = appliedRevision = appliedScene = 0; appliedCandidate = -1;
        LastRenderAudit = default;
        journeyCapture = default; appliedScenicProgress = double.NaN; journeyPresentation.Reset();
    }
}
