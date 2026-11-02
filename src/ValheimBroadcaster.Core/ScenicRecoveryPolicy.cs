using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Bounds time spent searching without a usable live scenic picture.
    /// This lifetime belongs to on-air coverage, not a candidate, search sweep or
    /// accepted plan. Loaded geometry and successful planning never renew it.</summary>
    public sealed class ScenicRecoveryPolicy
    {
        public const double MaximumSearchSeconds = 10;
        private double lastAt = -1;
        private bool previouslyHeld;

        public bool Active { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public double RemainingSeconds => Math.Max(0, MaximumSearchSeconds - ElapsedSeconds);
        public bool Expired => Active && ElapsedSeconds >= MaximumSearchSeconds;

        /// <param name="usableLivePicture">Proof of an actually rendered, readable
        /// live scenic frame. A retained preceding plan may supply that proof;
        /// a held texture, ready scene or newly accepted route may not.</param>
        public void Observe(double now, bool scenicOnAir, bool usableLivePicture, bool held = false)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || lastAt >= 0 && now < lastAt)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (!scenicOnAir || usableLivePicture)
            {
                Active = false; ElapsedSeconds = 0; lastAt = now; previouslyHeld = held;
                return;
            }
            // Count wall time, including slow frames: the application failing to
            // update quickly must not grant unlimited recovery. At entry/resume,
            // do not charge an interval which belonged to other coverage or hold.
            if (Active && lastAt >= 0 && !held && !previouslyHeld)
                ElapsedSeconds += now - lastAt;
            Active = true; lastAt = now; previouslyHeld = held;
        }

        /// <summary>Use only for a region/session boundary, never a route retry.</summary>
        public void Reset()
        { Active = previouslyHeld = false; ElapsedSeconds = 0; lastAt = -1; }
    }
}
