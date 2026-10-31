using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimBroadcaster.Plugin.Streaming;

internal sealed class StreamCapture : MonoBehaviour
{
    private EncoderSession? session;
    private GameAudioTap? tap;
    private AudioListener? listener;
    private RenderTexture? screen, output;
    private byte[] pixels = Array.Empty<byte>();
    private bool active, pending;
    private Vector3 normalPosition;
    private Quaternion normalRotation;
    private bool listenerMoved;
    private float nextFrame;
    private int fps;
    internal bool Active => active;
    internal bool CanStart => !active && !pending && (session == null || session.IsClosed);
    internal string Status => session?.Status ?? "Stopped";

    internal void Begin(StreamOptions options, AudioListener gameListener)
    {
        if (!CanStart) throw new InvalidOperationException("Wait for the previous encoder to stop.");
        options.Validate();
        if (!SystemInfo.supportsAsyncGPUReadback) throw new InvalidOperationException("This GPU does not support asynchronous frame readback.");
        if (!gameListener || !gameListener.isActiveAndEnabled) throw new InvalidOperationException("The game audio listener is unavailable.");
        if (AudioSettings.speakerMode != AudioSpeakerMode.Stereo && AudioSettings.speakerMode != AudioSpeakerMode.Mono)
            throw new InvalidOperationException("Select stereo audio output before starting the stream.");
        try
        {
            session = new EncoderSession(options);
            listener = gameListener;
            normalRotation = listener.transform.localRotation;
            AudioSettings.OnAudioConfigurationChanged += AudioConfigurationChanged;
            tap = listener.gameObject.AddComponent<GameAudioTap>();
            tap.Session = session;
            pixels = new byte[options.Width * options.Height * 4];
            output = new RenderTexture(options.Width, options.Height, 0, RenderTextureFormat.ARGB32);
            output.Create();
            fps = options.Fps;
            active = true;
            nextFrame = 0;
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

    private void Update()
    {
        if (!active || session == null) return;
        if (!listener || !listener.isActiveAndEnabled || !tap)
            session.Fail("Game audio listener changed; restart streaming.");
        else if (AudioSettings.outputSampleRate != session.SampleRate)
            session.Fail("Audio sample rate changed; restart streaming.");
        if (session.IsStopping) StopOutput();
    }

    private IEnumerator CaptureFrames()
    {
        var end = new WaitForEndOfFrame();
        while (active)
        {
            yield return end;
            if (!active || pending || Time.unscaledTime < nextFrame) continue;
            nextFrame = Time.unscaledTime + 1f / fps;
            try { CaptureFrame(); }
            catch { session?.Fail("GPU frame capture failed; check the graphics backend and restart."); StopOutput(); }
        }
    }

    private void CaptureFrame()
    {
        if (Screen.width <= 0 || Screen.height <= 0) return;
        if (!screen || screen.width != Screen.width || screen.height != Screen.height)
        {
            ReleaseTexture(ref screen);
            screen = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            screen.Create();
        }
        // Copy the already-rendered framebuffer, including slate/overlay. No second
        // camera render, ReadPixels main-thread stall, or desktop capture.
        ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);
        Graphics.Blit(screen, output);
        pending = true;
        try { AsyncGPUReadback.Request(output, 0, TextureFormat.RGBA32, ReadbackFinished); }
        catch { pending = false; throw; }
    }

    private void ReadbackFinished(AsyncGPUReadbackRequest request)
    {
        pending = false;
        try
        {
            if (!active || session == null) return;
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
        if (!pending) ReleaseTextures(); // An outstanding request owns these until its callback.
    }

    private void ReleaseTextures() { ReleaseTexture(ref screen); ReleaseTexture(ref output); pixels = Array.Empty<byte>(); }
    private static void ReleaseTexture(ref RenderTexture? texture)
    {
        if (texture) { texture.Release(); Destroy(texture); }
        texture = null;
    }
    private void OnDisable() => StopOutput();
    private void OnDestroy() => StopOutput();
}
