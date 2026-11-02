using System;
using Unity.Cinemachine;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>
/// The first IModifierValueSource on a candidate. For orbit shots the upstream
/// OrbitalFollow value is authoritative; a rising Dolly supplies native distance
/// progress. This component owns no camera parameter and no pipeline stage.
/// </summary>
internal sealed class NativeElevationValueSource : MonoBehaviour, CinemachineFreeLookModifier.IModifierValueSource
{
    internal CinemachineOrbitalFollow? Orbit;
    internal bool Active, Scenic;
    internal float DistanceFraction;
    public float NormalizedModifierValue => !Active ? 0 : Scenic ? Mathf.Clamp01(DistanceFraction) :
        Orbit && Orbit.enabled ? Mathf.Clamp(((CinemachineFreeLookModifier.IModifierValueSource)Orbit).NormalizedModifierValue, -1, 1) : 0;
}

/// <summary>
/// Uses actual native FreeLook modifiers for lens, orbital damping and vertical
/// composition. Base placement is computed after Body, so native composition is
/// deferred until that baseline is available and restored at native Finalize.
/// </summary>
internal sealed class NativeFreeLookProfiles : IDisposable
{
    private readonly CinemachineCamera camera;
    private readonly CinemachineOrbitalFollow orbit;
    private readonly CinemachineRotationComposer aim;
    private readonly NativeElevationValueSource source;
    private readonly CinemachineFreeLookModifier extension;
    private readonly ConditionalLens lens = new ConditionalLens();
    private readonly ConditionalDamping damping = new ConditionalDamping();
    private readonly DeferredComposition composition = new DeferredComposition();
    private ElevationProfile profile;
    internal bool Enabled => extension && extension.enabled;
    internal string Description => !Enabled ? "FreeLook profile inactive" :
        $"FreeLook {(source.Scenic ? "rise" : "orbit")} {source.NormalizedModifierValue:F2}; " +
        $"lens {(profile.Lens ? "native" : "group-owned")}; composition {(profile.Composition ? "native" : "base")}";

    // Add this before OrbitalFollow: upstream caches the first matching interface
    // even if that component is disabled for another native body family.
    internal static NativeElevationValueSource AddSource(GameObject owner) => owner.AddComponent<NativeElevationValueSource>();

    internal NativeFreeLookProfiles(CinemachineCamera camera, CinemachineOrbitalFollow orbit,
        CinemachineRotationComposer aim, NativeElevationValueSource source)
    {
        this.camera = camera; this.orbit = orbit; this.aim = aim; this.source = source;
        source.Orbit = orbit;
        if (!ReferenceEquals(camera.GetComponent<CinemachineFreeLookModifier.IModifierValueSource>(), source))
            throw new InvalidOperationException("FreeLook value source must be attached before native body components.");
        extension = camera.gameObject.AddComponent<CinemachineFreeLookModifier>();
        extension.enabled = false;
        extension.Easing = .55f;
        extension.Modifiers.Add(lens);
        extension.Modifiers.Add(damping);
        extension.Modifiers.Add(composition);
        lens.RefreshCache(camera); damping.RefreshCache(camera); composition.RefreshCache(camera);
    }

    internal void Configure(bool enabled, bool orbital, bool scenic, bool rising, float scenicDistanceFraction,
        bool grouped, ShotScale scale, bool confined, bool recovering, float preferredFov, bool compositionEnabled)
    {
        Restore();
        profile = ElevationProfilePolicy.Resolve(enabled, orbital, scenic, rising, grouped, scale,
            confined, recovering, preferredFov, compositionEnabled);
        source.Scenic = scenic;
        source.DistanceFraction = float.IsNaN(scenicDistanceFraction) || float.IsInfinity(scenicDistanceFraction)
            ? 0 : Mathf.Clamp01(scenicDistanceFraction);
        source.Active = profile.Enabled;
        lens.Active = profile.Lens;
        damping.Active = profile.Damping;
        composition.Active = profile.Composition;
        var top = camera.Lens; var bottom = top;
        top.FieldOfView = profile.TopFov; bottom.FieldOfView = profile.BottomFov;
        lens.Top = top; lens.Bottom = bottom;
        Vector3 baseline = orbit.TrackerSettings.PositionDamping;
        damping.Damping = new CinemachineFreeLookModifier.TopBottomRigs<Vector3>
        { Top = baseline * profile.TopDampingScale, Bottom = baseline * profile.BottomDampingScale };
        extension.enabled = profile.Enabled;
    }

    /// <summary>Call after the rig writes its base RotationComposer composition
    /// in the Body callback and before the Aim stage runs.</summary>
    internal void ApplyComposition(CameraState state, float deltaTime)
    {
        if (!Enabled || !profile.Composition) return;
        composition.Composition = new CinemachineFreeLookModifier.TopBottomRigs<ScreenComposerSettings>
        {
            Top = ComposeEndpoint(aim.Composition, profile.TopScreenY),
            Bottom = ComposeEndpoint(aim.Composition, profile.BottomScreenY)
        };
        composition.Apply(camera, ref state, deltaTime);
    }

    /// <summary>Keep the same actual-image hard-limit rectangle when shifting
    /// desired placement. This supplies native modifier endpoints, not a second
    /// interpolation or pose correction.</summary>
    internal static ScreenComposerSettings ComposeEndpoint(ScreenComposerSettings baseline, float offsetY)
    {
        if (float.IsNaN(offsetY) || float.IsInfinity(offsetY)) return baseline;
        var result = baseline;
        float center = baseline.ScreenPosition.y;
        float delta = Mathf.Clamp(center + offsetY, Mathf.Min(-.2f, center), Mathf.Max(.2f, center)) - center;
        if (result.HardLimits.Enabled)
        {
            float gap = result.HardLimits.Size.y - result.EffectiveDeadZoneSize.y;
            if (gap <= .001f) delta = 0;
            else
            {
                // Offset saturation must limit the requested movement, not move
                // the actual-image hard-limit rectangle when Validate clamps it.
                delta = Mathf.Clamp(delta, (result.HardLimits.Offset.y - 1) * gap * .5f,
                    (result.HardLimits.Offset.y + 1) * gap * .5f);
                result.HardLimits.Offset.y -= 2 * delta / gap;
            }
        }
        result.ScreenPosition.y = center + delta;
        result.Validate();
        return result;
    }

    // Also call in a finally around the camera solve: native Finalize restores
    // normally, but a failed pipeline must not leave modified settings behind.
    internal void Restore()
    {
        CameraState unused = default;
        damping.AfterPipeline(camera, ref unused, 0, 0);
        composition.AfterPipeline(camera, ref unused, 0, 0);
    }

    internal void Reset()
    {
        Restore();
        source.Active = false; source.DistanceFraction = 0;
        lens.Active = damping.Active = composition.Active = false;
        if (extension) extension.enabled = false;
        profile = default;
    }

    public void Dispose()
    {
        Reset();
        if (extension) UnityEngine.Object.Destroy(extension);
        if (source) UnityEngine.Object.Destroy(source);
    }

    private sealed class ConditionalLens : CinemachineFreeLookModifier.LensModifier
    {
        internal bool Active;
        public override void BeforePipeline(CinemachineVirtualCameraBase vcam, ref CameraState state, float dt, float value)
        { if (Active) base.BeforePipeline(vcam, ref state, dt, value); }
    }

    private sealed class ConditionalDamping : CinemachineFreeLookModifier.PositionDampingModifier
    {
        internal bool Active;
        private bool applied;
        public override void BeforePipeline(CinemachineVirtualCameraBase vcam, ref CameraState state, float dt, float value)
        {
            if (!Active) return;
            base.BeforePipeline(vcam, ref state, dt, value); applied = true;
        }
        public override void AfterPipeline(CinemachineVirtualCameraBase vcam, ref CameraState state, float dt, float value)
        {
            if (!applied) return;
            base.AfterPipeline(vcam, ref state, dt, value); applied = false;
        }
    }

    private sealed class DeferredComposition : CinemachineFreeLookModifier.CompositionModifier
    {
        internal bool Active;
        private bool sampled, applied;
        private float easedValue;
        public override void BeforePipeline(CinemachineVirtualCameraBase vcam, ref CameraState state, float dt, float value)
        { sampled = Active; easedValue = value; }
        internal void Apply(CinemachineVirtualCameraBase vcam, ref CameraState state, float dt)
        {
            if (!sampled || applied) return;
            base.BeforePipeline(vcam, ref state, dt, easedValue); applied = true;
        }
        public override void AfterPipeline(CinemachineVirtualCameraBase vcam, ref CameraState state, float dt, float value)
        {
            if (applied) base.AfterPipeline(vcam, ref state, dt, value);
            sampled = applied = false;
        }
    }
}
