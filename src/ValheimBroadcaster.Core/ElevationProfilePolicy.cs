using System;

namespace ValheimBroadcaster.Core
{
    public readonly struct ElevationProfile
    {
        public readonly bool Enabled, Lens, Damping, Composition;
        public readonly float TopFov, BottomFov, TopDampingScale, BottomDampingScale, TopScreenY, BottomScreenY;
        internal ElevationProfile(bool enabled, bool lens, bool damping, bool composition,
            float topFov, float bottomFov, float topDamping, float bottomDamping, float topY, float bottomY)
        {
            Enabled = enabled; Lens = lens; Damping = damping; Composition = composition;
            TopFov = topFov; BottomFov = bottomFov; TopDampingScale = topDamping;
            BottomDampingScale = bottomDamping; TopScreenY = topY; BottomScreenY = bottomY;
        }
    }

    /// <summary>
    /// Selects endpoint settings for native FreeLook modifiers. Cinemachine owns
    /// interpolation and the actual elevation source; this policy never solves
    /// a camera pose or substitutes geometry/visibility evidence.
    /// </summary>
    public static class ElevationProfilePolicy
    {
        public static ElevationProfile Resolve(bool enabled, bool orbital, bool scenic, bool rising,
            bool grouped, ShotScale scale, bool confined, bool recovering, float preferredFov, bool compositionEnabled)
        {
            ShotScalePolicy.Profile(scale);
            if (float.IsNaN(preferredFov) || float.IsInfinity(preferredFov) || preferredFov < 1 || preferredFov > 179)
                throw new ArgumentOutOfRangeException(nameof(preferredFov));
            bool active = enabled && !confined && !recovering && (orbital && !scenic || scenic && rising);
            float high = scenic ? 7 : scale == ShotScale.Wide ? 5 : scale == ShotScale.Medium ? 2 : 4;
            float low = scenic ? 0 : scale == ShotScale.Medium ? -1 : -2;
            return new ElevationProfile(active, active && !grouped, active && orbital && !scenic,
                active && compositionEnabled, Clamp(preferredFov + high, 35, 105), Clamp(preferredFov + low, 35, 105),
                1.3f, .9f, scenic ? .045f : scale == ShotScale.Medium ? .018f : .03f,
                scenic ? 0 : scale == ShotScale.Medium ? -.012f : -.02f);
        }

        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    }
}
