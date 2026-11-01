using System;

namespace ValheimBroadcaster.Core
{
    public enum FramePresentation { Live, Hold, Slate }

    /// <summary>Hold only a recent, actually captured good frame during a brief loading gap.</summary>
    public sealed class FrameContinuity
    {
        public const double MaximumHoldSeconds = 0.75;
        private double? capturedAt;
        public void Captured(double now) { Validate(now); capturedAt = now; }
        public FramePresentation Select(double now, bool ready, bool hardFailure)
        {
            Validate(now);
            if (hardFailure)
            {
                // A travel/recovery boundary invalidates the previous scene,
                // even if it becomes ready again within the brief hold window.
                capturedAt = null;
                return FramePresentation.Slate;
            }
            if (ready) return FramePresentation.Live;
            return capturedAt.HasValue && now >= capturedAt.Value && now - capturedAt.Value < MaximumHoldSeconds
                ? FramePresentation.Hold : FramePresentation.Slate;
        }
        public void Reset() => capturedAt = null;
        private static void Validate(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        }
    }
}
