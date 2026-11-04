using System;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin.Streaming;

// Calibrates the actual Graphics.Blit -> RGBA readback route before opening
// FFmpeg/audio. Both source families are measured separately: held camera RT
// and the CPU-authored recovery Texture2D. No probe pixels enter the program.
internal sealed class ProgramOrientationProbe : IDisposable
{
    private Texture2D? pattern;
    private RenderTexture? cameraPattern, output;
    private readonly byte[] bytes = new byte[ProgramOrientationPolicy.ProbeWidth * ProgramOrientationPolicy.ProbeHeight * 4];
    private readonly float started = Time.realtimeSinceStartup;
    private bool disposed;
    private int stage;
    internal bool Pending { get; private set; }
    internal bool Ready => !disposed && Failure.Length == 0 && stage == 4;
    internal bool FlipCamera { get; private set; }
    internal bool FlipSlate { get; private set; }
    internal string Failure { get; private set; } = "";
    internal bool PermanentFailure { get; private set; }
    private ProgramRowOrder cameraOrder, slateOrder;
    internal string Diagnostics => Ready ? "Automatic orientation verified; camera " + cameraOrder +
        ", slate " + slateOrder + "; encoded rows TopFirst; legacy FlipVertical ignored." :
        Failure.Length != 0 ? Failure : "Calibrating automatic output orientation (" + stage + "/4).";

    internal ProgramOrientationProbe()
    {
        try
        {
            int width = ProgramOrientationPolicy.ProbeWidth, height = ProgramOrientationPolicy.ProbeHeight;
            pattern = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = "Broadcaster orientation probe", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            byte[] data = ProgramOrientationPolicy.CreateBottomUpProbe();
            var colors = new Color32[width * height];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(data[i * 4], data[i * 4 + 1], data[i * 4 + 2], data[i * 4 + 3]);
            pattern.SetPixels32(colors); pattern.Apply(false, true);
            cameraPattern = CreateTexture("Broadcaster camera orientation probe");
            output = CreateTexture("Broadcaster output orientation probe");
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try { FrameHold.Blit(pattern, cameraPattern); }
            finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
        }
        catch { Dispose(); throw; }
    }

    private static RenderTexture CreateTexture(string name)
    {
        var result = new RenderTexture(ProgramOrientationPolicy.ProbeWidth, ProgramOrientationPolicy.ProbeHeight,
            0, RenderTextureFormat.ARGB32) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        if (!result.Create()) { UnityEngine.Object.Destroy(result); throw new InvalidOperationException("Orientation probe allocation failed."); }
        return result;
    }

    internal void Tick()
    {
        if (disposed || Ready || Failure.Length != 0) return;
        if (Time.realtimeSinceStartup < started || Time.realtimeSinceStartup - started > ProgramOrientationPolicy.CalibrationTimeoutSeconds)
        {
            Failure = "Output orientation calibration timed out; no unverified video was sent. Waiting for GPU cleanup before retry; restart the broadcaster client if cleanup remains blocked.";
            return;
        }
        if (Pending) return;
        RenderTexture previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            bool camera = stage < 2, correction = (stage & 1) != 0;
            FrameHold.Blit(camera ? cameraPattern! : pattern!, output!, correction && (camera ? FlipCamera : FlipSlate));
            Pending = true;
            AsyncGPUReadback.Request(output!, 0, TextureFormat.RGBA32, Complete);
        }
        catch { Pending = false; Failure = "Output orientation calibration failed; no unverified video was sent. Restart streaming."; }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }

    private void Complete(AsyncGPUReadbackRequest request)
    {
        Pending = false;
        try
        {
            if (disposed || Failure.Length != 0) return;
            if (request.hasError) { Failure = "GPU orientation readback failed; no unverified video was sent. Restart streaming."; return; }
            var data = request.GetData<byte>();
            if (data.Length != bytes.Length)
            { PermanentFailure = true; Failure = "GPU orientation readback had an unexpected size; streaming was not started."; return; }
            data.CopyTo(bytes);
            ProgramRowOrder order = ProgramOrientationPolicy.Detect(bytes, ProgramOrientationPolicy.ProbeWidth, ProgramOrientationPolicy.ProbeHeight);
            if (order == ProgramRowOrder.Unknown || (stage & 1) != 0 && order != ProgramRowOrder.TopFirst)
            {
                PermanentFailure = true;
                Failure = "GPU orientation could not be verified; streaming was not started. Check the graphics backend or use explicit manual orientation.";
                return;
            }
            if (stage == 0) { cameraOrder = order; FlipCamera = order == ProgramRowOrder.BottomFirst; }
            if (stage == 2) { slateOrder = order; FlipSlate = order == ProgramRowOrder.BottomFirst; }
            stage++;
        }
        catch { Failure = "GPU orientation measurement failed; streaming was not started."; }
        finally { if (disposed || Failure.Length != 0 || Ready) Release(); }
    }

    public void Dispose()
    {
        disposed = true;
        if (!Pending) Release();
    }

    private void Release()
    {
        if (pattern) UnityEngine.Object.Destroy(pattern);
        pattern = null;
        Release(ref cameraPattern); Release(ref output);
    }
    private static void Release(ref RenderTexture? texture)
    {
        if (texture) { texture.Release(); UnityEngine.Object.Destroy(texture); }
        texture = null;
    }
}
