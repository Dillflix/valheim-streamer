using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin.Streaming;

/// <summary>Interval photographs of the existing clean program picture. A
/// separate bounded readback/encoder keeps this independent of live RTMP/AAC.
/// Only Live source frames are photographs; holds/slates are omitted. A hard
/// compositor epoch change ends the clip instead of splicing unrelated sites.</summary>
internal sealed class TimelapseCapture : MonoBehaviour
{
    private FrameHold? program;
    private Func<bool>? sessionValid;
    private Func<string>? executable;
    private Action<string>? log;
    private string directory = "", failure = "", stopReason = "Stopped";
    private bool configured, active, pending, closing, reported, completionReported;
    private TimelapseClock? clock;
    private TimelapseVideoWriter? writer, pendingOwner;
    private ProgramOrientationProbe? orientation;
    private RenderTexture? texture;
    private byte[]? pendingPixels;
    private ProgramFrameStamp pendingStamp;
    private string pendingMetadata = "";
    private long generation;
    private long omitted;
    internal bool Active => active;
    internal string Status
    {
        get
        {
            string state = failure.Length != 0 ? failure : active
                ? orientation?.Ready != true ? "calibrating orientation" : "recording"
                : closing || writer?.Closed == false ? "finalizing" : stopReason;
            string output = writer == null ? "" : writer.Closed && writer.Written == 0 ? "; no photographs saved" : "; " + writer.OutputPath;
            return "Timelapse: " + state + (clock == null ? "" : "; " + clock.IntervalSeconds.ToString("0.###", CultureInfo.InvariantCulture) +
                "s source interval → " + clock.OutputFps + "fps silent 1280x720; " + (writer?.Written ?? 0) + " frames / " +
                ((writer?.Written ?? 0) / (double)clock.OutputFps).ToString("0.0", CultureInfo.InvariantCulture) + "s video; " +
                omitted + " omitted, " + clock.MissedIntervals + " missed intervals, " + (writer?.Dropped ?? 0) + " dropped; " +
                (active ? clock.Remaining(Time.unscaledTimeAsDouble).ToString("0", CultureInfo.InvariantCulture) + "s capture remaining" : "capture ended")) + output;
        }
    }

    internal void Configure(FrameHold source, Func<bool> isSessionValid, Func<string> ffmpegPath, string root, Action<string> logger)
    {
        if (configured) throw new InvalidOperationException("Timelapse capture is already configured.");
        program = source; sessionValid = isSessionValid; executable = ffmpegPath; log = logger;
        directory = Path.Combine(root, "recordings", "ValheimBroadcaster", "timelapse");
        configured = true; StartCoroutine(CaptureFrames());
    }

    internal string StartCapture(double intervalSeconds = 2, double captureSeconds = 600, int outputFps = 30)
    {
        if (!configured) return "Timelapse is not configured.";
        if (active || closing || pending || orientation?.Pending == true || writer?.Closed == false)
            return "Timelapse is already recording or finalizing; stop it and wait for finalization before starting another clip.";
        try
        {
            if (sessionValid?.Invoke() != true || program == null || !program.HasRecentLiveFrame)
                return "Wait for a live broadcast picture before starting timelapse capture.";
            var requested = new TimelapseClock(intervalSeconds, captureSeconds, outputFps);
            if (!SystemInfo.supportsAsyncGPUReadback) return "Timelapse requires asynchronous GPU readback; live broadcasting is unchanged.";
            clock = requested; writer = null; failure = ""; stopReason = "Recording"; reported = completionReported = false; omitted = 0;
            texture = new RenderTexture(TimelapseVideoWriter.Width, TimelapseVideoWriter.Height, 0, RenderTextureFormat.ARGB32)
                { name = "Broadcaster timelapse photograph", filterMode = FilterMode.Bilinear };
            if (!texture.Create()) throw new InvalidOperationException("Timelapse output texture unavailable.");
            orientation?.Dispose(); orientation = new ProgramOrientationProbe();
            generation = program.Generation; active = true;
            return "Timelapse armed: " + captureSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " real seconds, one live image every " +
                intervalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s, " + outputFps + "fps → up to " +
                requested.PlannedOutputSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "s silent video. Live RTMP audio is unchanged. " + directory;
        }
        catch (ArgumentException e) { return "Invalid timelapse settings: " + e.Message; }
        catch (Exception e) { Fail("Initialization failed (" + e.GetType().Name + ")."); return Status; }
    }

    private void Update()
    {
        if (!configured) return;
        if (writer?.Error != null && failure.Length == 0) Fail(writer.Error);
        if (!active)
        {
            FinishStop();
            if (writer?.Closed == true && !completionReported)
            { completionReported = true; log?.Invoke(Status); }
            return;
        }
        try
        {
            if (sessionValid?.Invoke() != true) { StopCapture("Broadcast session ended"); return; }
            if (program == null || program.Generation != generation) { StopCapture("Scene changed; clip finalized before the next location"); return; }
            orientation!.Tick();
            if (orientation.Failure.Length != 0) { Fail("Orientation calibration failed; no unverified timelapse recorded."); return; }
            if (orientation.Ready && writer == null)
            {
                writer = new TimelapseVideoWriter(directory, executable!(), BroadcasterPlugin.PluginVersion, clock!);
                clock!.Start(Time.unscaledTimeAsDouble);
                log?.Invoke("Timelapse recording started: " + writer.OutputPath + " (silent interval capture; live RTMP/AAC unchanged).");
            }
            if (clock!.Started && clock.Expired(Time.unscaledTimeAsDouble)) StopCapture("Requested source duration completed");
        }
        catch (Exception e) { Fail("Capture update failed (" + e.GetType().Name + ")."); }
    }

    private IEnumerator CaptureFrames()
    {
        var end = new WaitForEndOfFrame();
        while (configured)
        {
            yield return end;
            if (!active || writer == null || clock?.Started != true || orientation?.Ready != true || program == null || !texture) continue;
            try
            {
                if (!clock.Take(Time.unscaledTimeAsDouble)) continue;
                if (program.Generation != generation) { StopCapture("Scene changed; clip finalized before the next location"); continue; }
                if (pending || program.CurrentPresentation != FramePresentation.Live) { omitted++; continue; }
                byte[]? pixels = writer.Rent();
                if (pixels == null) { omitted++; writer.Drop(); continue; }
                try
                {
                    pendingStamp = program.CopyProgramFrame(texture, orientation.FlipCamera, orientation.FlipSlate);
                    if (pendingStamp.Generation != generation || pendingStamp.Presentation != FramePresentation.Live)
                    { writer.Return(pixels); pixels = null; omitted++; continue; }
                    DiagnosticPictureContext context = program.DiagnosticContext(pendingStamp, out int sourceFrame, out float sourceTime);
                    var row = new CameraTraceRecord("timelapse-sample", pendingStamp.CopiedAt)
                        .Field("session", writer.SessionId).Field("traceSession", ScenicTrace.SessionId)
                        .Field("generation", pendingStamp.Generation).Field("sourceFrame", sourceFrame)
                        .Field("sourceTime", sourceTime).Field("renderedFrame", pendingStamp.RenderedFrame)
                        .Field("missedIntervals", clock.MissedIntervals).Field("omittedIntervals", omitted);
                    context.Append(row); pendingMetadata = row.ToString();
                    pendingOwner = writer; pendingPixels = pixels; pending = true;
                    try { AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32, Complete); pixels = null; }
                    catch { pending = false; pendingOwner = null; pendingPixels = null; throw; }
                }
                finally { if (pixels != null) writer.Return(pixels); }
            }
            catch (Exception e) { Fail("Frame setup failed (" + e.GetType().Name + ")."); }
        }
    }

    private void Complete(AsyncGPUReadbackRequest request)
    {
        pending = false;
        TimelapseVideoWriter? owner = pendingOwner; byte[]? pixels = pendingPixels;
        pendingOwner = null; pendingPixels = null;
        try
        {
            if (owner == null || pixels == null) return;
            if (!active || owner != writer || program == null || pendingStamp.Generation != generation || !program.IsProgramFrameCurrent(pendingStamp))
            { owner.Return(pixels); pixels = null; omitted++; return; }
            if (request.hasError) throw new InvalidOperationException("GPU readback failed.");
            var data = request.GetData<byte>();
            if (data.Length != pixels.Length) throw new InvalidOperationException("Readback dimensions changed.");
            data.CopyTo(pixels); owner.Submit(pixels, pendingMetadata, pendingStamp.CopiedAt); pixels = null;
        }
        catch (Exception e) { Fail("Frame transfer failed (" + e.GetType().Name + ")."); }
        finally { if (pixels != null) owner?.Return(pixels); if (!active) FinishStop(); }
    }

    internal void StopCapture(string reason = "Operator stop")
    {
        if (!active && !closing) return;
        active = false; closing = true; stopReason = reason; orientation?.Dispose();
        // A GPU callback may be delayed or never arrive during device loss.
        // Finalize queued photographs now; any outstanding readback is canceled
        // logically and keeps its texture alive only until its callback retires.
        writer?.Complete(stopReason); FinishStop();
    }
    private void FinishStop()
    {
        if (active || pending) return;
        if (closing) closing = false;
        if (texture) { texture.Release(); Destroy(texture); } texture = null;
    }
    private void Fail(string reason)
    {
        failure = reason; active = false; closing = true; stopReason = "Failed";
        orientation?.Dispose(); writer?.Complete(stopReason); FinishStop();
        if (!reported) { reported = true; log?.Invoke("Timelapse stopped: " + reason + " Live broadcasting continues."); }
    }
    private void OnDisable() => StopCapture("Capture component disabled");
    private void OnDestroy() { configured = false; StopCapture("Capture component destroyed"); }
}
