using System;

namespace ValheimBroadcaster.Core
{
    public static class ScenicJourneyMotion
    {
        private const double Ramp = .08;
        public static double CruiseFraction(bool easeIn, bool easeOut) => 1 - Ramp * .5 * ((easeIn ? 1 : 0) + (easeOut ? 1 : 0));
        public static double Progress(double progress, bool easeIn, bool easeOut)
        {
            if (double.IsNaN(progress) || double.IsInfinity(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
            double t = Math.Max(0, Math.Min(1, progress));
            double integral = t;
            if (easeIn) integral -= Loss(Math.Min(t, Ramp));
            if (easeOut && t > 1 - Ramp) integral -= ExitLoss(t - (1 - Ramp));
            return Math.Max(0, Math.Min(1, integral / CruiseFraction(easeIn, easeOut)));
        }
        private static double ExitLoss(double distance)
        { double u = distance / Ramp; return Ramp * (u * u * u - .5 * u * u * u * u); }
        private static double Loss(double distance)
        { double u = distance / Ramp; return Ramp * (u - u * u * u + .5 * u * u * u * u); }
    }

    /// <summary>Finite ownership of a continuous natural-scene journey. Window
    /// preparation earns no distance or airtime; only matching readable output
    /// commits progress. Player acquisition and picture recovery remain external.</summary>
    public sealed class ScenicJourneyPolicy
    {
        public const double WindowMetres = 32;
        public const double MinimumWindowMetres = 8;
        public const double PreparationSeconds = 12;
        public long Id { get; private set; }
        public bool Active => Id > 0;
        public double ShownSeconds { get; private set; }
        public double TravelMetres { get; private set; }
        public double MaximumSeconds { get; private set; }
        public int Windows { get; private set; }
        private long observedPlan;
        private double observedProgress, observedAt = -1;
        public bool CanContinue => Active && ShownSeconds < MaximumSeconds && Windows < 64 && TravelMetres < 1500;

        public static bool Eligible(ScenicShotPlan? plan) => plan != null && plan.IsDirected && !plan.IsPictureOnly && !plan.IsPan &&
            plan.Feature != null && plan.Feature.SubjectKind == ScenicSubjectKind.Environment && plan.Feature.MeasuredBounds == null &&
            plan.Feature.Kind != ScenicFeatureKind.Structure && plan.Feature.Kind != ScenicFeatureKind.Rock && plan.TravelMetres >= 8;

        public void Start(long id, double maximumSeconds)
        {
            if (id <= 0 || !Finite(maximumSeconds) || maximumSeconds < 30 || maximumSeconds > 900)
                throw new ArgumentOutOfRangeException(nameof(maximumSeconds));
            Reset(); Id = id; MaximumSeconds = maximumSeconds;
        }

        public double NextWindowMetres(double metresPerSecond, double motionMultiplier)
        {
            if (!Finite(metresPerSecond) || metresPerSecond < .25 || metresPerSecond > 2 ||
                !Finite(motionMultiplier) || motionMultiplier < 0 || motionMultiplier > 1.5)
                throw new ArgumentOutOfRangeException(nameof(metresPerSecond));
            return CanContinue ? Math.Min(WindowMetres, Math.Max(0, MaximumSeconds - ShownSeconds) * metresPerSecond * motionMultiplier) : 0;
        }

        public void Observe(ScenicShotPlan plan, double at, double progress, bool liveReadable, bool held)
        {
            if (plan == null || !Finite(at) || at < 0 || !Finite(progress) || progress < 0 || progress > 1)
                throw new ArgumentOutOfRangeException(nameof(progress));
            if (!Active || plan.JourneyId != Id) return;
            if (at <= observedAt) return;
            double gap = observedAt < 0 ? 0 : at - observedAt;
            observedAt = at;
            if (!liveReadable || held) return;
            if (observedPlan != plan.Id) { observedPlan = plan.Id; observedProgress = 0; Windows++; }
            if (gap <= .25) ShownSeconds += gap;
            TravelMetres += Math.Max(0, progress - observedProgress) * plan.TravelMetres;
            observedProgress = Math.Max(observedProgress, progress);
        }

        public void Reset()
        { Id = observedPlan = 0; ShownSeconds = TravelMetres = MaximumSeconds = observedProgress = 0; observedAt = -1; Windows = 0; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
