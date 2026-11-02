using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Owns the lifetime of one accepted scenic plan, separately from
    /// candidate motion and region availability. A readable retained picture
    /// cancels recovery work, but never cancels a scheduled novelty search.</summary>
    public sealed class ScenicPresentationPolicy
    {
        public const double MissingPictureSeconds = .4, PoorPictureSeconds = 2, EstablishSeconds = 2;
        private double acceptedAt = -1, lastAt = -1, failedAt = -1;
        private bool previousReadable, missing, qualifiedFailure, searchFailed;
        private double previousElapsed;
        public bool ReplacementRequested { get; private set; }
        public bool Scheduled { get; private set; }
        public bool RecoveryExhausted { get; private set; }
        public bool Readable { get; private set; }
        public double ShownSeconds { get; private set; }
        public double Progress { get; private set; }

        public void Accept(double now)
        {
            Time(now); Reset(); acceptedAt = now;
        }

        public void ScheduleReplacement()
        {
            Scheduled = ReplacementRequested = true; RecoveryExhausted = false;
        }

        /// <returns>True when an obstruction-driven search became unnecessary.</returns>
        public bool Observe(double now, bool livePicture, bool readable)
        {
            Time(now);
            if (acceptedAt < 0 || lastAt >= 0 && now < lastAt) Accept(now);
            double dt = lastAt >= 0 && now - lastAt <= .25 ? now - lastAt : 0;
            bool gap = lastAt >= 0 && now - lastAt > .25;
            lastAt = now; Readable = livePicture && readable;
            if (Readable)
            {
                if (previousReadable) ShownSeconds += dt;
                previousReadable = true; failedAt = -1; RecoveryExhausted = qualifiedFailure = searchFailed = false;
                bool cancel = ReplacementRequested && !Scheduled;
                if (cancel) ReplacementRequested = false;
                return cancel;
            }
            previousReadable = false; ShownSeconds = 0;
            // Missing output and a live but poorly composed picture have
            // different recovery budgets. Time without observations proves none.
            if (gap || failedAt < 0 || missing != !livePicture) failedAt = now;
            missing = !livePicture;
            double budget = missing ? MissingPictureSeconds : PoorPictureSeconds;
            if (now - failedAt >= budget && (missing || now - acceptedAt >= EstablishSeconds))
                ReplacementRequested = qualifiedFailure = true;
            // A scheduled novelty search can become genuine recovery if its
            // retained picture subsequently fails. Scheduling is not permission
            // to keep an independently failed picture indefinitely.
            if (qualifiedFailure && searchFailed) RecoveryExhausted = true;
            return false;
        }

        public void SearchCompleted(bool foundReadableReplacement)
        {
            searchFailed = !foundReadableReplacement;
            RecoveryExhausted = ReplacementRequested && qualifiedFailure && !Readable && searchFailed;
        }

        public double AdvancePlan(double elapsed, double duration, bool suspended)
        {
            Time(elapsed);
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            double advance = elapsed >= previousElapsed ? (elapsed - previousElapsed) / duration : 0;
            previousElapsed = elapsed;
            if (!suspended) Progress = Math.Min(1, Progress + advance);
            return Progress;
        }

        public void Reset()
        {
            acceptedAt = lastAt = failedAt = -1; previousReadable = missing = qualifiedFailure = searchFailed = false;
            ReplacementRequested = Scheduled = RecoveryExhausted = Readable = false; ShownSeconds = 0;
            previousElapsed = Progress = 0;
        }

        private static void Time(double now)
        { if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now)); }
    }
}
