using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Recent successful player footage permits continuation of that exact
    /// character binding. Scene/foundation checks remain the adapter's responsibility.</summary>
    public sealed class PlayerFollowContinuity
    {
        public const double MaximumGapSeconds = 1;
        public const double MaximumStepMetres = 16;
        private string? subject, binding;
        private AutonomyPosition position;
        private double presentedAt;

        // Candidate probes are read-only: examining another player must not discard
        // the current shot, and successful probes must not renew presentation age.
        public bool CanContinue(string subjectId, string bindingId, AutonomyPosition target, double now)
            => subject != null && ValidId(subjectId) && ValidId(bindingId) && ValidPosition(target) && ValidTime(now) &&
               string.Equals(subject, subjectId, StringComparison.Ordinal) &&
               string.Equals(binding, bindingId, StringComparison.Ordinal) &&
               now >= presentedAt && now - presentedAt <= MaximumGapSeconds &&
               position.Distance(target) <= MaximumStepMetres;

        // The adapter calls this only after the camera and carrier operation has
        // succeeded. It may establish a new binding after full acquisition checks.
        public void Presented(string subjectId, string bindingId, AutonomyPosition target, double now)
        {
            if (!ValidId(subjectId) || !ValidId(bindingId) || !ValidPosition(target) || !ValidTime(now) ||
                subject != null && now < presentedAt)
            {
                Reset();
                throw new ArgumentException("Presented footage requires valid identities, coordinates and monotonic time.");
            }
            subject = subjectId; binding = bindingId; position = target; presentedAt = now;
        }

        public void Reset() { subject = binding = null; position = default; presentedAt = 0; }
        private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value);
        private static bool ValidTime(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool ValidPosition(AutonomyPosition value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
