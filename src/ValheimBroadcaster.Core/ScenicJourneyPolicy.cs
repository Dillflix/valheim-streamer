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

    public enum ScenicJourneyOutcome { None, Running, Completed, Interrupted, Blocked, BudgetExpired }

    /// <summary>A distance-directed journey. Only consecutive matching, actually
    /// presented camera positions earn distance. Authored path length, preparation,
    /// decorative motion settings and held output cannot claim achieved travel.</summary>
    public sealed class ScenicJourneyPolicy
    {
        public const double WindowMetres = 64;
        public const double MinimumWindowMetres = 8;
        public const double PreparationSeconds = 12;
        public const double MaximumStepMetres = .75;
        public const double ObjectiveFraction = .65;
        public long Id { get; private set; }
        public bool Active => Id > 0 && Outcome == ScenicJourneyOutcome.Running;
        public double TargetMetres { get; private set; }
        public double CruiseSpeed { get; private set; }
        public double ShownSeconds { get; private set; }
        public double TravelMetres { get; private set; }
        /// <summary>Horizontal displacement from the first journey seam.</summary>
        public double DisplacementMetres { get; private set; }
        public double ObjectiveAdvanceMetres { get; private set; }
        public double MaximumSeconds { get; private set; }
        public int Windows { get; private set; }
        public AutonomyPosition ObjectiveOrigin { get; private set; }
        public AutonomyPosition ObjectiveGoal { get; private set; }
        public AutonomyPosition Heading { get; private set; }
        public ScenicJourneyOutcome Outcome { get; private set; }
        public string Reason { get; private set; } = "No journey requested";
        public double RemainingMetres => Math.Max(0, Math.Max(TargetMetres - TravelMetres,
            TargetMetres * ObjectiveFraction - Math.Min(ObjectiveAdvanceMetres, creditedAdvanceMetres)));
        public bool TargetReached => TargetMetres > 0 && TravelMetres >= TargetMetres - .5 &&
            Math.Min(ObjectiveAdvanceMetres, creditedAdvanceMetres) >= TargetMetres * ObjectiveFraction - .5;
        public bool BudgetExpired => TargetMetres > 0 && (ShownSeconds >= MaximumSeconds || Windows >= 1024 || TravelMetres >= TargetMetres * 1.5 + MinimumWindowMetres);
        public bool CanContinue => Active && !TargetReached && !BudgetExpired;
        public double AchievedMetresPerSecond => ShownSeconds > 0 ? TravelMetres / ShownSeconds : 0;
        private long observedPlan;
        private double observedProgress, observedAt = -1, creditedAdvanceMetres;
        private bool previousLive;
        private AutonomyPosition previousPosition;

        public static bool Eligible(ScenicShotPlan? plan) => plan != null && plan.IsDirected && !plan.IsPictureOnly && !plan.IsPan &&
            plan.Feature != null && plan.Feature.SubjectKind == ScenicSubjectKind.Environment && plan.Feature.MeasuredBounds == null &&
            plan.Feature.Kind != ScenicFeatureKind.Structure && plan.Feature.Kind != ScenicFeatureKind.Rock && plan.TravelMetres >= 8;

        public void Start(long id, double targetMetres, double cruiseSpeed, AutonomyPosition origin, AutonomyPosition heading)
        {
            if (id <= 0 || !Finite(targetMetres) || targetMetres < 256 || targetMetres > 5000)
                throw new ArgumentOutOfRangeException(nameof(targetMetres));
            Speed(cruiseSpeed);
            double magnitude = Math.Sqrt((double)heading.X * heading.X + (double)heading.Z * heading.Z);
            if (magnitude < .001) throw new ArgumentException("A nonzero horizontal journey heading is required.", nameof(heading));
            var direction = new AutonomyPosition((float)(heading.X / magnitude), 0, (float)(heading.Z / magnitude));
            var goal = new AutonomyPosition((float)(origin.X + direction.X * targetMetres), origin.Y,
                (float)(origin.Z + direction.Z * targetMetres));
            Reset(); Id = id; TargetMetres = targetMetres; CruiseSpeed = cruiseSpeed;
            ObjectiveOrigin = origin; ObjectiveGoal = goal; Heading = direction;
            MaximumSeconds = targetMetres / cruiseSpeed * 1.5 + 30;
            Outcome = ScenicJourneyOutcome.Running; Reason = "Developing a distance-directed scenic journey";
        }

        /// <summary>One locally certifiable section. At high speed multiple
        /// prepared sections are needed to satisfy the time-based horizon.</summary>
        public static double WindowMetresForSpeed(double speed)
        { Speed(speed); return Math.Max(32, Math.Min(WindowMetres, speed * 8)); }
        public static double RequiredLookaheadMetres(double speed)
        { Speed(speed); return speed * PreparationSeconds; }
        public static double SafeDeltaSeconds(double seconds, double speed)
        {
            if (!Finite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            Speed(speed); return Math.Min(seconds, Math.Min(.1, MaximumStepMetres / speed));
        }

        /// <summary>Subtract the already committed untraversed route before
        /// planning more. The final short remainder still receives a complete
        /// minimum-size validated section, allowing a small bounded overshoot.</summary>
        public double NextWindowMetres(double committedRemainingMetres = 0)
        {
            if (!Finite(committedRemainingMetres) || committedRemainingMetres < 0)
                throw new ArgumentOutOfRangeException(nameof(committedRemainingMetres));
            if (!CanContinue) return 0;
            double outstanding = RemainingMetres - committedRemainingMetres;
            if (outstanding <= .5) return 0;
            double budget = TargetMetres * 1.5 + MinimumWindowMetres - TravelMetres - committedRemainingMetres;
            if (budget < MinimumWindowMetres) return 0;
            return Math.Min(budget, Math.Max(MinimumWindowMetres, Math.Min(WindowMetresForSpeed(CruiseSpeed), outstanding)));
        }

        public void Observe(ScenicShotPlan plan, double at, double progress, bool liveReadable, bool held,
            AutonomyPosition? actualPosition = null)
        {
            if (plan == null || !Finite(at) || at < 0 || !Finite(progress) || progress < 0 || progress > 1)
                throw new ArgumentOutOfRangeException(nameof(progress));
            if (!Active || plan.JourneyId != Id || at <= observedAt) return;
            double gap = observedAt < 0 ? 0 : at - observedAt;
            observedAt = at;
            if (!liveReadable || held || !actualPosition.HasValue)
            { previousLive = false; return; }
            if (observedPlan == plan.Id && progress + .000001 < observedProgress)
            { previousLive = false; return; }
            AutonomyPosition position = actualPosition.Value;
            if (previousLive && gap <= .25)
            {
                double step = position.Distance(previousPosition);
                if (step > MaximumStepMetres + .05)
                { Finish(ScenicJourneyOutcome.Interrupted, "Camera position jumped beyond the validated journey step"); return; }
                TravelMetres += step;
                // A relocation hidden by held/missing footage cannot fulfill
                // the objective even if enough local looping travel preceded it.
                creditedAdvanceMetres += (position.X - previousPosition.X) * Heading.X +
                    (position.Z - previousPosition.Z) * Heading.Z;
                ShownSeconds += gap;
            }
            if (observedPlan != plan.Id) { observedPlan = plan.Id; Windows++; }
            observedProgress = progress;
            previousPosition = position; previousLive = true;
            DisplacementMetres = ObjectiveOrigin.HorizontalDistance(position);
            ObjectiveAdvanceMetres = (position.X - ObjectiveOrigin.X) * Heading.X + (position.Z - ObjectiveOrigin.Z) * Heading.Z;
        }

        public void Finish(ScenicJourneyOutcome outcome, string reason)
        {
            if (outcome == ScenicJourneyOutcome.None || outcome == ScenicJourneyOutcome.Running)
                throw new ArgumentOutOfRangeException(nameof(outcome));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A journey outcome needs a reason.", nameof(reason));
            if (outcome == ScenicJourneyOutcome.Completed && !TargetReached)
            { outcome = ScenicJourneyOutcome.Blocked; reason = "Distance/forward objective was not reached: " + reason; }
            Outcome = outcome; Reason = reason; previousLive = false;
        }

        public void Reset()
        {
            Id = observedPlan = 0; ShownSeconds = TravelMetres = DisplacementMetres = ObjectiveAdvanceMetres = MaximumSeconds = TargetMetres = CruiseSpeed = observedProgress = creditedAdvanceMetres = 0;
            observedAt = -1; Windows = 0; previousLive = false; previousPosition = ObjectiveOrigin = ObjectiveGoal = Heading = default;
            Outcome = ScenicJourneyOutcome.None; Reason = "No journey requested";
        }
        private static void Speed(double value)
        { if (!Finite(value) || value < 2 || value > 12) throw new ArgumentOutOfRangeException(nameof(value)); }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
