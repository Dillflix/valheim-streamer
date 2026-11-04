using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin.Streaming;

internal sealed class StreamCapture : MonoBehaviour
{
    private EncoderSession? session;
    private GameAudioTap? tap;
    private AudioListener? listener;
    private RenderTexture? output;
    private FrameHold? program;
    private ProgramOrientationProbe? orientation;
    private StreamOptions? awaitingCalibration;
    private string preflightFailure = "", orientationDiagnostics = "Orientation has not been measured.";
    private bool preflightPermanent;
    private ProgramFrameStamp pendingFrame;
    private byte[] pixels = Array.Empty<byte>();
    private bool active, pending;
    private Vector3 normalPosition;
    private Quaternion normalRotation;
    private bool listenerMoved;
    private readonly CaptureCadence cadence = new CaptureCadence();
    private Action<string>? reportFailure;
    private bool failureReported;
    internal bool Active => active;
    internal bool IsPublishing => active && session?.IsPublishing == true;
    internal bool HasCompletedFailure => preflightFailure.Length != 0 || session?.IsClosed == true && session.FailureDetails.Length != 0;
    internal bool PermanentFailure => preflightPermanent || session?.PermanentFailure == true;
    internal bool CleanupBlocked => session?.CleanupBlocked == true;
    internal long Generation { get; private set; }
    internal bool CanStart => !active && !pending && orientation?.Pending != true && (session == null || session.IsClosed);
    internal string Status => awaitingCalibration != null ? "Calibrating video orientation" :
        preflightFailure.Length != 0 ? preflightFailure : session?.Status ?? "Stopped";
    internal string Diagnostics => (preflightFailure.Length != 0 ? preflightFailure : session == null ? "No encoder session." :
        session.FailureDetails.Length == 0 ? session.TimingDetails : session.FailureDetails) + "\n" + orientationDiagnostics +
        "\nCapture cadence: " + cadence.AdmittedSamples + " samples admitted; " + cadence.SkippedSlots + " render/readback slots skipped.";

    internal void Begin(StreamOptions options, AudioListener gameListener, FrameHold programSource, Action<string>? onFailure = null)
    {
        if (!CanStart) throw new InvalidOperationException("Wait for the previous encoder to stop.");
        options.Validate();
        if (!SystemInfo.supportsAsyncGPUReadback) throw new EncoderStartException("This GPU does not support asynchronous frame readback.");
        if (!gameListener || !gameListener.isActiveAndEnabled) throw new InvalidOperationException("The game audio listener is unavailable.");
        if (AudioSettings.speakerMode != AudioSpeakerMode.Stereo && AudioSettings.speakerMode != AudioSpeakerMode.Mono)
            throw new ArgumentException("Unsupported speaker mode: " + AudioSettings.speakerMode + ". Select mono/stereo audio output before starting the stream.");
        try
        {
            ReportFailure();
            session = null;
            Generation++;
            reportFailure = onFailure;
            failureReported = false;
            preflightFailure = ""; preflightPermanent = false;
            program = programSource ?? throw new ArgumentNullException(nameof(programSource));
            listener = gameListener;
            normalRotation = listener.transform.localRotation;
            pixels = new byte[options.Width * options.Height * 4];
            output = new RenderTexture(options.Width, options.Height, 0, RenderTextureFormat.ARGB32);
            if (!output.Create()) throw new InvalidOperationException("Streaming output texture could not be allocated.");
            cadence.Reset(options.Fps);
            active = true;
            orientation?.Dispose(); orientation = null;
            if (options.AutomaticOrientation)
            {
                orientation = new ProgramOrientationProbe();
                awaitingCalibration = options;
                orientationDiagnostics = orientation.Diagnostics;
            }
            else
            {
                orientationDiagnostics = "Manual output orientation; raw GPU rows; FFmpeg FlipVertical=" + options.FlipVertical + ".";
                StartEncoder(options);
            }
            StartCoroutine(CaptureFrames());
        }
        catch { StopOutput(); throw; }
    }

    // Runs after the original GameCamera.UpdateListner, which normally moves the
    // listener back to the observer's eyes even when our camera is following a target.
    internal void AlignListener(Camera? camera)
    {
        if (!active || !listener || !camera) return;
        normalPosition = listener.transform.position;
        listenerMoved = true;
        listener.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
    }

    private void AudioConfigurationChanged(bool deviceChanged) => session?.Fail("Audio device configuration changed; restart streaming.");

    private void StartEncoder(StreamOptions options)
    {
        if (!listener || !listener.isActiveAndEnabled) throw new InvalidOperationException("The game audio listener is unavailable.");
        session = new EncoderSession(options);
        AudioSettings.OnAudioConfigurationChanged += AudioConfigurationChanged;
        tap = listener.gameObject.AddComponent<GameAudioTap>();
        tap.Session = session;
    }

    private void Update()
    {
        ReportFailure();
        if (!active) return;
        if (awaitingCalibration != null)
        {
            if (!listener || !listener.isActiveAndEnabled)
            { FailPreflight("Game audio listener changed during video calibration; restart streaming.", false); return; }
            orientation!.Tick();
            orientationDiagnostics = orientation.Diagnostics;
            if (orientation.Failure.Length != 0) { FailPreflight(orientation.Failure, orientation.PermanentFailure); return; }
            if (!orientation.Ready) return;
            StreamOptions options = awaitingCalibration;
            awaitingCalibration = null;
            try { StartEncoder(options); }
            catch (EncoderStartException error) { FailPreflight(error.Message, true); return; }
            catch { FailPreflight("Game audio capture could not start after video calibration; restart streaming.", false); return; }
        }
        if (session == null) return;
        if (!listener || !listener.isActiveAndEnabled || !tap)
            session.Fail("Game audio listener changed; restart streaming.");
        else if (AudioSettings.outputSampleRate != session.SampleRate)
            session.Fail("Audio sample rate changed; restart streaming.");
        if (session.IsStopping) StopOutput();
    }

    private void ReportFailure()
    {
        if (preflightFailure.Length != 0 && !failureReported)
        { failureReported = true; reportFailure?.Invoke(preflightFailure); return; }
        if (session == null || !session.IsClosed || failureReported) return;
        failureReported = true;
        if (session.FailureDetails.Length != 0) reportFailure?.Invoke(session.FailureDetails);
    }

    private void FailPreflight(string reason, bool permanent)
    {
        preflightFailure = reason; preflightPermanent = permanent;
        StopOutput();
    }

    private IEnumerator CaptureFrames()
    {
        var end = new WaitForEndOfFrame();
        while (active)
        {
            yield return end;
            if (!active || awaitingCalibration != null || session == null) continue;
            if (!cadence.TryCapture(Time.unscaledTimeAsDouble, !pending)) continue;
            try { CaptureFrame(); }
            catch { session?.Fail("GPU frame capture failed; check the graphics backend and restart."); StopOutput(); }
        }
    }

    private void CaptureFrame()
    {
        if (program == null || !output) return;
        // Copy only the camera compositor's program output. Console, local
        // diagnostic banners and screen-space HUD/UI never become stream input.
        // Missing or stale compositor output supplies an intentional clean slate.
        pendingFrame = program.CopyProgramFrame(output, orientation?.FlipCamera ?? false, orientation?.FlipSlate ?? false);
        pending = true;
        try { AsyncGPUReadback.Request(output, 0, TextureFormat.RGBA32, ReadbackFinished); }
        catch { pending = false; throw; }
    }

    private void ReadbackFinished(AsyncGPUReadbackRequest request)
    {
        pending = false;
        try
        {
            if (!active || session == null || program == null || !program.IsProgramFrameCurrent(pendingFrame)) return;
            if (request.hasError) { session.Fail("GPU readback failed; restart with a supported graphics backend."); return; }
            request.GetData<byte>().CopyTo(pixels);
            pixels = session.SubmitVideo(pixels);
        }
        catch { session?.Fail("Frame transfer failed; restart streaming."); }
        finally { if (!active) ReleaseTextures(); }
    }

    internal void StopOutput()
    {
        active = false;
        awaitingCalibration = null;
        orientation?.Dispose();
        AudioSettings.OnAudioConfigurationChanged -= AudioConfigurationChanged;
        StopAllCoroutines();
        if (tap) { tap.Session = null; Destroy(tap); }
        tap = null;
        session?.Stop();
        if (listener && listenerMoved)
        {
            listener.transform.position = normalPosition;
            listener.transform.localRotation = normalRotation;
        }
        listenerMoved = false;
        listener = null;
        program = null;
        if (!pending) ReleaseTextures(); // An outstanding request owns these until its callback.
    }

    private void ReleaseTextures() { ReleaseTexture(ref output); pixels = Array.Empty<byte>(); }
    private static void ReleaseTexture(ref RenderTexture? texture)
    {
        if (texture) { texture.Release(); Destroy(texture); }
        texture = null;
    }
    private void OnDisable() => StopOutput();
    private void OnDestroy() => StopOutput();
}
