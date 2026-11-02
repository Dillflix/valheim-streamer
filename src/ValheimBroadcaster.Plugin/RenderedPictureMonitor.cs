using System;
using UnityEngine;
using UnityEngine.Rendering;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal readonly struct RenderedPictureContext
{
    internal readonly RenderedPictureKey Key;
    internal readonly PictureRegion Primary;
    internal readonly bool HasPrimary;
    internal readonly int Frame;
    internal readonly float ObservedAt;
    internal RenderedPictureContext(RenderedPictureKey key, PictureRegion primary, bool hasPrimary, int frame, float observedAt)
    { Key = key; Primary = primary; HasPrimary = hasPrimary; Frame = frame; ObservedAt = observedAt; }
    internal bool Valid => Key.IsValid && Frame >= 0 && !float.IsNaN(ObservedAt) && !float.IsInfinity(ObservedAt) && ObservedAt >= 0;
}

/// <summary>Samples the actual clean camera output at 2 Hz independently of
/// RTMP. Holds, slates and operator UI never contribute picture evidence.</summary>
internal sealed class RenderedPictureMonitor : IDisposable
{
    private const int Width = 96, Height = 54;
    private readonly RenderedPicturePolicy policy = new RenderedPicturePolicy();
    private readonly byte[] rgba = new byte[Width * Height * 4];
    private RenderedPictureContext context, requestedContext;
    private RenderTexture? sampleTexture;
    private bool pending, disposed, failed, measured;
    private long epoch, requestedEpoch, requestedGeneration, latestGeneration = -1;
    private int sourceWidth, sourceHeight;
    private float nextSample;
    private string state = "Awaiting a clean live picture";
    internal Func<long, bool>? IsProgramGenerationCurrent { get; set; }
    internal string Diagnostics => "Rendered picture: " + state +
        (measured && (!context.Valid || !policy.Feedback(context.Key, Time.unscaledTime).IsFresh)
            ? "; sample stale, no pixel penalty" : "");

    internal void SetContext(RenderedPictureContext value)
    {
        if (disposed) return;
        if (!value.Valid) { Suspend(); return; }
        if (!context.Valid || !context.Key.Equals(value.Key))
        {
            epoch++; nextSample = 0; measured = false;
            state = "Awaiting pixels for the displayed shot";
        }
        context = value;
    }

    internal void Offer(RenderTexture cleanLive, long programGeneration, int renderedFrame)
    {
        if (disposed || failed || !context.Valid || context.Frame != renderedFrame ||
            !cleanLive || !cleanLive.IsCreated() || IsProgramGenerationCurrent == null ||
            !IsProgramGenerationCurrent(programGeneration)) return;
        float now = Time.unscaledTime;
        if (now < context.ObservedAt || now - context.ObservedAt > .25f) return;
        if (latestGeneration != programGeneration || sourceWidth != cleanLive.width || sourceHeight != cleanLive.height)
        {
            epoch++; policy.Reset(); latestGeneration = programGeneration; measured = false;
            sourceWidth = cleanLive.width; sourceHeight = cleanLive.height; nextSample = 0;
        }
        if (pending || now < nextSample) return;
        if (!SystemInfo.supportsAsyncGPUReadback)
        { state = "Unavailable: asynchronous GPU readback unsupported; geometric evaluation remains active"; return; }
        nextSample = now + .5f;
        RenderTexture previous = RenderTexture.active;
        bool srgb = GL.sRGBWrite;
        try
        {
            if (!sampleTexture)
            {
                sampleTexture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { name = "Broadcaster picture measurement", useMipMap = false, autoGenerateMips = false, filterMode = FilterMode.Bilinear };
                if (!sampleTexture.Create()) throw new InvalidOperationException("Picture sample allocation failed.");
            }
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear && sampleTexture.sRGB;
            Graphics.Blit(cleanLive, sampleTexture);
            requestedContext = context; requestedEpoch = epoch; requestedGeneration = programGeneration;
            pending = true;
            AsyncGPUReadback.Request(sampleTexture, 0, TextureFormat.RGBA32, Complete);
        }
        catch
        {
            pending = false; failed = true; policy.Reset();
            state = "Unavailable: picture sampling failed; geometric evaluation remains active";
            Release();
        }
        finally { RenderTexture.active = previous; GL.sRGBWrite = srgb; }
    }

    private void Complete(AsyncGPUReadbackRequest request)
    {
        pending = false;
        try
        {
            if (disposed || requestedEpoch != epoch || !context.Valid || !context.Key.Equals(requestedContext.Key) ||
                IsProgramGenerationCurrent == null || !IsProgramGenerationCurrent(requestedGeneration)) return;
            if (request.hasError)
            { policy.Reset(); failed = true; state = "Unavailable: GPU picture readback failed; geometric evaluation remains active"; return; }
            var data = request.GetData<byte>();
            if (data.Length != rgba.Length)
            { policy.Reset(); failed = true; state = "Unavailable: unexpected picture buffer; geometric evaluation remains active"; return; }
            data.CopyTo(rgba);
            RenderedPictureMeasurement result = RenderedPictureQuality.Measure(rgba, Width, Height,
                requestedContext.Primary, requestedContext.HasPrimary);
            if (policy.Observe(requestedContext.Key, requestedContext.ObservedAt, Time.unscaledTime, result))
            {
                measured = true;
                RenderedPictureFeedback feedback = policy.Feedback(context.Key, Time.unscaledTime);
                state = $"luma {result.MeanLuma:0.00}; dark {result.DarkFraction:P0}; clipped {result.ClippedFraction:P0}; " +
                    (result.HasSubjectContrast ? $"subject contrast {result.SubjectSeparation:0.00} (conservative mirrored ROI); " : "subject contrast unavailable; ") +
                    $"penalty {feedback.Penalty:0.00}; {feedback.Reason}";
            }
        }
        catch
        { policy.Reset(); failed = true; state = "Unavailable: picture measurement failed; geometric evaluation remains active"; }
        finally { if (disposed || failed) Release(); }
    }

    internal RenderedPictureFeedback Feedback(RenderedPictureKey key, float now)
    {
        if (disposed || failed || !context.Valid || !context.Key.Equals(key) || latestGeneration < 0 ||
            IsProgramGenerationCurrent == null || !IsProgramGenerationCurrent(latestGeneration))
            return RenderedPictureFeedback.Unavailable(state);
        return policy.Feedback(key, now);
    }

    internal void Clear()
    {
        if (disposed) return;
        if (context.Valid || latestGeneration >= 0) epoch++;
        context = default; latestGeneration = -1; policy.Reset(); nextSample = 0; failed = false; measured = false;
        state = "Awaiting a clean live picture";
        // An in-flight request owns sampleTexture until its callback finishes.
    }

    internal void Suspend()
    {
        if (disposed) return;
        if (context.Valid) epoch++;
        context = default; policy.Suspend(Time.unscaledTime); nextSample = 0; measured = false;
        if (!failed) state = "Awaiting a clean live picture; previous pixel evidence suspended";
        // Keep the compositor generation and policy's correction deadline. A
        // hard reset or next Offer with a different generation still clears both.
        // The old callback is invalidated, but continues owning its sample RT.
    }

    public void Dispose()
    {
        if (disposed) return;
        epoch++; disposed = true; context = default; policy.Reset();
        if (!pending) Release();
    }

    private void Release()
    {
        if (sampleTexture) { sampleTexture.Release(); UnityEngine.Object.Destroy(sampleTexture); }
        sampleTexture = null;
    }
}
