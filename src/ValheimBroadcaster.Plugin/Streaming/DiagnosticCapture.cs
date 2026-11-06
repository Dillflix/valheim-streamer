using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin.Streaming;

/// <summary>Samples the same clean compositor as RTMP, including holds/slates.
/// It owns its readback and encoder so diagnostics cannot stop live transport.</summary>
internal sealed class DiagnosticCapture : MonoBehaviour
{
    private FrameHold? program;
    private Func<bool>? shouldCapture;
    private Func<string>? executable;
    private Action<string>? log;
    private string? directory;
    private int retentionMiB;
    private DiagnosticVideoWriter? writer;
    private ProgramOrientationProbe? orientation;
    private RenderTexture? texture;
    private bool active, pending, configured, reportedFailure, retryRequested;
    private float nextFrame;
    private string failure = "";
    private ProgramFrameStamp pendingStamp;
    private string pendingMetadata = "";
    private DiagnosticVideoWriter? pendingOwner;
    private byte[]? pendingPixels;
    internal int EncoderProcessId => writer?.ProcessId ?? 0;
    internal bool EncoderClosed => writer == null || writer.Closed;
    internal string Status => "Footage capture: " + (failure.Length != 0 ? failure :
        active ? orientation?.Ready != true ? "calibrating orientation" :
        $"640x360 / 12 fps / no audio; {writer?.Written ?? 0} frames, {writer?.Dropped ?? 0} dropped; {directory}" :
        writer?.Closed == false ? "finalizing local video" : "stopped");

    internal void Configure(FrameHold source, Func<bool> enabled, Func<string> ffmpegPath, string root, Action<string> logger, int videoRetentionMiB)
    {
        if (configured) throw new InvalidOperationException("Diagnostic capture is already configured.");
        program = source; shouldCapture = enabled; executable = ffmpegPath; log = logger;
        directory = Path.Combine(root, "diagnostics", "ValheimBroadcaster", "footage");
        retentionMiB = videoRetentionMiB;
        configured = true; StartCoroutine(CaptureFrames());
    }

    private void Update()
    {
        if (!configured) return;
        if (retryRequested)
        {
            // Error belongs to the old writer until its worker and pending GPU
            // callbacks have retired. Clearing failure first would immediately
            // re-latch that same writer.Error and make the retry ineffective.
            StopCapture();
            if (pending || orientation?.Pending == true || writer?.Closed == false) return;
            writer = null; orientation = null;
            failure = ""; reportedFailure = false; retryRequested = false;
        }
        if (writer?.Error != null && failure.Length == 0) Fail(writer.Error);
        bool wanted;
        try { wanted = shouldCapture?.Invoke() == true; }
        catch { Fail("Diagnostic capture condition unavailable."); return; }
        if (!wanted) { StopCapture(); return; }
        if (failure.Length != 0) return;
        try
        {
            if (!active)
            {
                if (pending || orientation?.Pending == true || writer?.Closed == false) return;
                writer = null;
                if (!SystemInfo.supportsAsyncGPUReadback) { Fail("GPU readback unavailable; video capture disabled."); return; }
                texture = new RenderTexture(DiagnosticVideoWriter.Width, DiagnosticVideoWriter.Height, 0, RenderTextureFormat.ARGB32)
                    { name = "Broadcaster diagnostic video", filterMode = FilterMode.Bilinear };
                if (!texture.Create()) { Fail("Diagnostic output texture unavailable."); return; }
                orientation?.Dispose(); orientation = new ProgramOrientationProbe();
                active = true; nextFrame = 0;
            }
            orientation!.Tick();
            if (orientation.Failure.Length != 0) { Fail("Diagnostic orientation calibration failed; no unverified video recorded."); return; }
            if (orientation.Ready && writer == null)
            {
                writer = new DiagnosticVideoWriter(directory!, executable!(), BroadcasterPlugin.PluginVersion, retentionMiB * 1024L * 1024);
                log?.Invoke("Synchronized diagnostic footage enabled: " + directory +
                    " (640x360/12fps, no audio; " + retentionMiB + " MiB rolling retention). No upload is performed.");
            }
        }
        catch (Exception e) { Fail("Diagnostic initialization failed (" + e.GetType().Name + ")."); }
    }

    private IEnumerator CaptureFrames()
    {
        var end = new WaitForEndOfFrame();
        while (configured)
        {
            yield return end;
            if (!active || orientation?.Ready != true || writer == null || program == null || !texture || Time.unscaledTime < nextFrame) continue;
            nextFrame += 1f / DiagnosticVideoWriter.Fps;
            if (Time.unscaledTime - nextFrame > .5f) nextFrame = Time.unscaledTime + 1f / DiagnosticVideoWriter.Fps;
            if (pending) { writer.Drop(); continue; }
            byte[]? buffer = writer.Rent();
            if (buffer == null) { writer.Drop(); continue; }
            try
            {
                pendingStamp = program.CopyProgramFrame(texture, orientation.FlipCamera, orientation.FlipSlate);
                DiagnosticPictureContext context = program.DiagnosticContext(pendingStamp, out int sourceFrame, out float sourceTime);
                CameraTraceRecord row = ScenicTrace.Row("video-sample").Field("traceSession", ScenicTrace.SessionId)
                    .Field("videoSession", writer.SessionId).Field("generation", pendingStamp.Generation)
                    .Field("presentation", pendingStamp.Presentation.ToString()).Field("sourceFrame", sourceFrame)
                    .Field("sourceTime", sourceTime).Field("renderedFrame", pendingStamp.RenderedFrame);
                context.Append(row);
                pendingMetadata = row.ToString();
                ScenicTrace.Record(row);
                pendingPixels = buffer; pendingOwner = writer; pending = true;
                try { AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32, Complete); }
                catch { pending = false; pendingOwner = null; pendingPixels = null; throw; }
            }
            catch (Exception e)
            { writer.Return(buffer); Fail("Diagnostic readback setup failed (" + e.GetType().Name + ")."); }
        }
    }

    private void Complete(AsyncGPUReadbackRequest request)
    {
        pending = false;
        DiagnosticVideoWriter? owner = pendingOwner;
        byte[]? pixels = pendingPixels;
        pendingOwner = null; pendingPixels = null;
        try
        {
            if (owner == null || pixels == null) return;
            if (!active || owner != writer || program == null || !program.IsProgramFrameCurrent(pendingStamp))
            { owner.Return(pixels); pixels = null; owner.Drop(); return; }
            if (request.hasError) throw new InvalidOperationException("Diagnostic readback failed.");
            var data = request.GetData<byte>();
            if (data.Length != pixels.Length) throw new InvalidOperationException("Diagnostic readback size changed.");
            data.CopyTo(pixels);
            owner.Submit(pixels, pendingMetadata, pendingStamp.CopiedAt, pendingStamp.Generation); pixels = null;
        }
        catch (Exception e) { Fail("Diagnostic frame transfer failed (" + e.GetType().Name + ")."); }
        finally
        {
            if (pixels != null) owner?.Return(pixels);
            if (!active) ReleaseTexture();
        }
    }

    private void Fail(string reason)
    {
        failure = reason; StopCapture();
        if (!reportedFailure) { reportedFailure = true; log?.Invoke(Status + " Broadcasting continues; use broadcast capture on to retry."); }
    }

    internal void Retry()
    {
        if (failure.Length == 0 && writer?.Error == null) return;
        retryRequested = true;
        StopCapture();
    }
    internal void StopCapture()
    {
        active = false;
        orientation?.Dispose(); writer?.Dispose();
        if (!pending) ReleaseTexture();
    }
    private void ReleaseTexture()
    {
        if (texture) { texture.Release(); Destroy(texture); }
        texture = null;
    }
    private void OnDisable() => StopCapture();
    private void OnDestroy() { configured = false; StopCapture(); }
}
