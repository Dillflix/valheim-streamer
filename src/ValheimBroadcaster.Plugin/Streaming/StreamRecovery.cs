using System;
using UnityEngine;

namespace ValheimBroadcaster.Plugin.Streaming;

// Main-thread coordinator; never starts a replacement until the old session and
// pending GPU readback are fully closed. Explicit operator stops defeat autostart.
internal sealed class StreamRecovery
{
    private readonly StreamCapture capture;
    private readonly Func<StreamOptions> options;
    private readonly Func<AudioListener?> listener;
    private readonly Action<string> log;
    private readonly OutputRecoveryPolicy policy = new OutputRecoveryPolicy();
    private long handledFailure = -1;
    private bool permitted;
    private string encoder = "not started";
    internal bool Desired => policy.Desired;
    internal bool ManualSuppressed => policy.ManualSuppressed;
    internal string Status => !policy.Desired ? "Stopped" : policy.Blocked ? "Blocked: " + policy.LastFailure :
        !permitted ? "Waiting for authorized broadcast scene" : capture.Active ? capture.Status : !capture.CanStart ? "Closing previous encoder" :
        "Retry in " + policy.RetryIn(Time.realtimeSinceStartup).ToString("F0") + "s (attempts " + policy.Attempts + ")";
    internal string Diagnostics => "RTMP: " + Status + "\nEncoder: " + encoder + "; desired " + policy.Desired +
        "; manual stop " + policy.ManualSuppressed + "; attempts " + policy.Attempts + "; failures in retry cycle " + policy.FailuresInBurst +
        "\nLast failure: " + policy.LastFailure;

    internal StreamRecovery(StreamCapture capture, Func<StreamOptions> options, Func<AudioListener?> listener, Action<string> log)
    { this.capture = capture; this.options = options; this.listener = listener; this.log = log; }
    internal void ArmAutoStart(bool enabled) => policy.ArmAutoStart(enabled, Time.realtimeSinceStartup);
    internal void RequestStart() => policy.RequestStart(Time.realtimeSinceStartup);
    internal void RequestStop()
    {
        policy.RequestStop(); capture.StopOutput(); handledFailure = capture.Generation;
    }
    internal void Suspend()
    {
        permitted = false; policy.Suspend(); capture.StopOutput(); handledFailure = capture.Generation;
    }
    internal void Tick(bool mayPublish, bool retryEnabled)
    {
        permitted = mayPublish;
        if (!mayPublish)
        {
            // A deliberate world transition is a pause, not an encoder failure.
            if (capture.Active || policy.InFlight) Suspend();
            return;
        }
        double now = Time.realtimeSinceStartup;
        if ((capture.HasCompletedFailure || capture.CleanupBlocked) && handledFailure != capture.Generation)
        {
            handledFailure = capture.Generation;
            policy.Failed(now, capture.PermanentFailure || capture.CleanupBlocked, retryEnabled, capture.Diagnostics);
            log("RTMP failure (redacted):\n" + capture.Diagnostics + "\n" + Status);
        }
        if (capture.IsPublishing) policy.Publishing(now);
        if (!policy.TryBegin(now, mayPublish, capture.CanStart)) return;
        try
        {
            StreamOptions selected = options();
            selected.Validate();
            encoder = selected.VideoEncoder.ToString();
            AudioListener? current = listener();
            if (!current || !current.isActiveAndEnabled) throw new InvalidOperationException("Game audio listener is not ready.");
            capture.Begin(selected, current);
            log("Starting RTMP using " + encoder + " and AAC (attempt " + policy.Attempts + ").");
        }
        catch (ArgumentException error) { StartFailed(now, true, retryEnabled, error.Message); }
        catch (EncoderStartException error) { StartFailed(now, true, retryEnabled, error.Message); }
        catch (InvalidOperationException) { StartFailed(now, false, retryEnabled, "Game capture or audio device is not ready."); }
        catch (Exception error) { StartFailed(now, false, retryEnabled, "Stream startup failed: " + error.GetType().Name); }
    }
    private void StartFailed(double now, bool permanent, bool retryEnabled, string safeReason)
    {
        policy.Failed(now, permanent, retryEnabled, safeReason);
        log("RTMP: " + safeReason + " " + Status);
    }
}
