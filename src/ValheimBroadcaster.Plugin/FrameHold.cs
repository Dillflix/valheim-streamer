using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed class FrameHold
{
    private readonly FrameContinuity continuity = new FrameContinuity();
    private Func<bool> active = () => false, ready = () => false, hardFailure = () => true;
    private Action<string> warning = _ => { };
    private Camera? camera;
    private FrameHoldRenderer? renderer;
    private RenderTexture? frame;
    private bool captureFailed;
    private int renderedFrame = -1, reportedFrame = -1;
    private FramePresentation renderedPresentation = FramePresentation.Slate, lastPresentation = FramePresentation.Slate;
    private long holds, slates;
    private float testAt = -1;
    internal string LastFailure { get; set; } = "none";
    internal string Diagnostics => $"Presentation: {lastPresentation}; {holds} held frames, {slates} slate frames; last gap: {LastFailure}; camera compositor {(renderer && renderedFrame >= Time.frameCount - 1 ? "active" : "awaiting render")}; capture {(captureFailed ? "unavailable" : "available")}; graphics {SystemInfo.graphicsDeviceType}.";

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
                continuity.Captured(now);
            }
            if (mode == FramePresentation.Hold && (!frame || captureFailed)) mode = FramePresentation.Slate;
            // Live and Hold have the identical final texture-to-camera path. Never
            // reinterpret an end-of-frame screenshot through IMGUI coordinates.
            if (mode == FramePresentation.Slate) Blit(Texture2D.blackTexture, destination);
            else Blit(frame && !captureFailed ? frame : source, destination);
            renderedPresentation = mode;
        }
        catch (Exception error)
        {
            Release();
            if (!captureFailed) warning("Camera frame hold unavailable (" + error.GetType().Name + "); recovery slate remains enabled.");
            captureFailed = true;
            renderedPresentation = live && !hard ? FramePresentation.Live : FramePresentation.Slate;
            Blit(renderedPresentation == FramePresentation.Live ? source : Texture2D.blackTexture, destination);
        }
        renderedFrame = Time.frameCount;
    }

    // GUI adds text or an opaque fallback only. It never draws the held image.
    internal FramePresentation Present()
    {
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

    private static void Blit(Texture source, RenderTexture destination)
    {
        bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear && (!destination || destination.sRGB);
            Graphics.Blit(source, destination);
        }
        finally { GL.sRGBWrite = srgb; }
        // OnRenderImage must leave its output bound, unlike end-of-frame capture.
    }

    internal void LostRenderer(FrameHoldRenderer lost)
    {
        if (!ReferenceEquals(renderer, lost)) return;
        lost.Owner = null;
        // Retire a temporarily disabled camera's component as well; otherwise
        // reactivation would accumulate abandoned pass-through image effects.
        UnityEngine.Object.Destroy(lost);
        renderer = null; camera = null;
        Release(); renderedFrame = -1; testAt = -1;
    }

    internal void Reset()
    {
        Detach(); holds = slates = 0; captureFailed = false; reportedFrame = -1;
        lastPresentation = FramePresentation.Slate; LastFailure = "none";
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
    }

    private void Release()
    {
        continuity.Reset();
        if (frame) { frame.Release(); UnityEngine.Object.Destroy(frame); }
        frame = null;
    }
}
