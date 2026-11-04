using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Passive samples of Valheim's current interpolated lighting/fog.
/// This describes the currently observed environment, never the weather at a
/// remote destination. The director uses it only as a bounded revisit hint.</summary>
internal sealed class ScenicAppearanceSampler
{
    private double next;
    private ScenicAppearanceKey current;

    internal ScenicAppearanceKey Sample(double now)
    {
        if (now < next) return current;
        next = now + 2;
        EnvMan? environment = EnvMan.instance;
        Light? sun = environment ? environment.m_dirLight : RenderSettings.sun;
        float intensity = sun && sun.isActiveAndEnabled ? Mathf.Max(0, sun.intensity) : 0;
        float sunHeight = sun ? Mathf.Clamp(-sun.transform.forward.y, -1, 1) : 0;
        float fogDensity = Mathf.Max(0, RenderSettings.fogDensity);
        // EnvMan.SetEnv writes this density for Valheim's environment rendering;
        // the engine's legacy fog enable switch is not its only fog consumer.
        bool fog = RenderSettings.fog || environment && fogDensity > 0;
        string weather = environment ? environment.GetCurrentEnvironment()?.m_name ?? "" : "";
        current = new ScenicAppearanceKey(intensity, sunHeight, fogDensity, fog, weather);
        return current;
    }

    internal void Reset() { next = 0; current = default; }
}
