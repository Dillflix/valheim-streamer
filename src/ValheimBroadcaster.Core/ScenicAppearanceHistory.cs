using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>Coarse measured local appearance, not a forecast of remote weather.
/// Quantization avoids giving every tiny lighting interpolation novelty credit.</summary>
public readonly struct ScenicAppearanceKey : IEquatable<ScenicAppearanceKey>
{
    public readonly int Light, Sun, Fog;
    public readonly string Weather;
    public ScenicAppearanceKey(float lightIntensity, float sunHeight, float fogDensity, bool fogEnabled, string weather = "")
    {
        if (!Valid(lightIntensity) || lightIntensity < 0 || !Valid(sunHeight) || sunHeight < -1 || sunHeight > 1 ||
            !Valid(fogDensity) || fogDensity < 0 || weather == null || weather.Length > 96)
            throw new ArgumentOutOfRangeException(nameof(lightIntensity));
        Light = lightIntensity < .08f ? 0 : lightIntensity < .45f ? 1 : 2;
        Sun = sunHeight < -.1f ? 0 : sunHeight < .2f ? 1 : 2;
        Fog = !fogEnabled || fogDensity < .002f ? 0 : fogDensity < .012f ? 1 : 2;
        Weather = weather;
    }
    public bool Equals(ScenicAppearanceKey b) => Light == b.Light && Sun == b.Sun && Fog == b.Fog && (Weather ?? "") == (b.Weather ?? "");
    public override bool Equals(object? obj) => obj is ScenicAppearanceKey b && Equals(b);
    public override int GetHashCode() => ((Light * 397 ^ Sun) * 397 ^ Fog) * 397 ^ (Weather ?? "").GetHashCode();
    private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

/// <summary>Only sustained actual scenic footage records a visit's appearance.
/// All destination eligibility, biome/source balance and travel cooldowns remain
/// authoritative. This supplies only a bounded comparable-destination bonus.</summary>
public sealed class ScenicAppearanceHistory
{
    private const int Capacity = 64;
    private readonly Dictionary<string, Visit> visits = new Dictionary<string, Visit>(StringComparer.Ordinal);
    private string? current;
    private ScenicAppearanceKey pending;
    private double footage;
    private long serial;
    private sealed class Visit { internal ScenicAppearanceKey Appearance; internal long Order; }
    public void Observe(string regionId, ScenicAppearanceKey appearance, double dt, bool actualScenicPicture)
    {
        if (string.IsNullOrWhiteSpace(regionId) || regionId.Length > 160 || double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0)
            throw new ArgumentOutOfRangeException(nameof(regionId));
        if (regionId != current || !appearance.Equals(pending))
        { current = regionId; pending = appearance; footage = 0; }
        if (!actualScenicPicture) return;
        double before = footage; footage += Math.Min(1, dt);
        if (before >= 15 || footage < 15) return;
        if (visits.Count >= Capacity && !visits.ContainsKey(regionId))
        {
            string? oldest = null; long order = long.MaxValue;
            foreach (var pair in visits) if (pair.Value.Order < order) { oldest = pair.Key; order = pair.Value.Order; }
            if (oldest != null) visits.Remove(oldest);
        }
        visits[regionId] = new Visit { Appearance = appearance, Order = ++serial };
    }
    public double Preference(string regionId, ScenicAppearanceKey currentAppearance)
    {
        if (!visits.TryGetValue(regionId, out Visit? visit)) return 0;
        ScenicAppearanceKey prior = visit.Appearance;
        double difference = (prior.Light != currentAppearance.Light ? .06 : 0) +
            (prior.Sun != currentAppearance.Sun ? .04 : 0) + (prior.Fog != currentAppearance.Fog ? .04 : 0) +
            ((prior.Weather ?? "") != (currentAppearance.Weather ?? "") ? .04 : 0);
        return Math.Min(.18, difference);
    }
    public void Reset() { visits.Clear(); current = null; pending = default; footage = 0; serial = 0; }
}
