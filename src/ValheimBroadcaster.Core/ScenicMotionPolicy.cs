using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Brief smooth acceleration/deceleration with a steady cruise.
    /// Whole-shot quintic easing spends much of a long landscape nearly still.</summary>
    public static class ScenicMotionProfile
    {
        public const double RampFraction = .08;
        public static double Progress(double fraction)
        {
            if (double.IsNaN(fraction) || double.IsInfinity(fraction))
                throw new ArgumentOutOfRangeException(nameof(fraction));
            double t = Math.Max(0, Math.Min(1, fraction));
            if (t < RampFraction) return Ramp(t);
            if (t > 1 - RampFraction) return 1 - Ramp(1 - t);
            return (t - RampFraction * .5) / (1 - RampFraction);
        }
        private static double Ramp(double t)
        {
            double u = t / RampFraction;
            return RampFraction * (u * u * u - .5 * u * u * u * u) / (1 - RampFraction);
        }
    }

    /// <summary>Consumes presentation progress separately from motion progress.
    /// Pauses and blocked movement discard elapsed input instead of storing a
    /// catch-up move, while the safe live picture can still earn viewing time.</summary>
    public sealed class ScenicMotionClock
    {
        private float observed;
        public float Progress { get; private set; }
        public void Reset(float fraction = 0)
        {
            Validate(fraction);
            Progress = observed = Math.Max(0, Math.Min(1, fraction));
        }
        public float Update(float fraction, float speed, bool canAdvance)
        {
            Validate(fraction); Validate(speed);
            if (speed < 0 || speed > 150) throw new ArgumentOutOfRangeException(nameof(speed));
            fraction = Math.Max(0, Math.Min(1, fraction));
            float elapsed = Math.Max(0, fraction - observed);
            observed = Math.Max(observed, fraction);
            if (canAdvance) Progress = Math.Min(1, Progress + elapsed * speed);
            return Progress;
        }
        private static void Validate(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value)); }
    }
}
