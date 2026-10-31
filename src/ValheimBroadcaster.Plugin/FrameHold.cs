using System;
using System.Collections;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed class FrameHold
{
    private readonly FrameContinuity continuity = new FrameContinuity();
    private RenderTexture? frame;
    private bool captureFailed;
    private int reportedFrame = -1;
    private long holds, slates;
    internal string LastFailure { get; set; } = "none";
    internal string Diagnostics => $"Presentation: {holds} held frames, {slates} slate frames; last gap: {LastFailure}; capture {(captureFailed ? "unavailable" : "available")}.";

    internal IEnumerator Capture(Func<bool> active, Func<bool> ready, Action<string> warning)
    {
        var end = new WaitForEndOfFrame();
        while (true)
        {
            yield return end;
            if (!active() || !ready() || captureFailed || Screen.width <= 0 || Screen.height <= 0) continue;
            try
            {
                if (!frame || frame.width != Screen.width || frame.height != Screen.height)
                {
                    Release();
                    frame = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                    if (!frame.Create()) throw new InvalidOperationException("Framebuffer allocation failed.");
                }
                // GPU copy of the existing completed frame, without a second render or CPU readback.
                ScreenCapture.CaptureScreenshotIntoRenderTexture(frame);
                continuity.Captured(Time.unscaledTime);
            }
            catch (Exception error)
            {
                Release(); captureFailed = true;
                warning("Frame hold unavailable (" + error.GetType().Name + "); recovery slate remains enabled.");
            }
        }
    }

    internal FramePresentation Present(bool ready, bool hardFailure)
    {
        if (frame && (frame.width != Screen.width || frame.height != Screen.height)) Release();
        FramePresentation result = continuity.Select(Time.unscaledTime, ready, hardFailure);
        if (Event.current.type == EventType.Repaint && reportedFrame != Time.frameCount)
        {
            reportedFrame = Time.frameCount;
            if (result == FramePresentation.Hold) holds++;
            if (result == FramePresentation.Slate) slates++;
        }
        if (result == FramePresentation.Hold && frame)
        {
            Color saved = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), frame, ScaleMode.StretchToFill, false);
            GUI.color = saved;
        }
        return result;
    }

    internal void Reset()
    {
        Release(); holds = slates = 0; captureFailed = false; reportedFrame = -1; LastFailure = "none";
    }
    private void Release()
    {
        continuity.Reset();
        if (frame) { frame.Release(); UnityEngine.Object.Destroy(frame); }
        frame = null;
    }
}
