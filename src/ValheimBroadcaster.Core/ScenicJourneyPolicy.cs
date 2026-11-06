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
        public const int MinimumCruiseSpeed = 2;
        public const int MaximumCruiseSpeed = 24;
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
        public ScenicJourneyItinerary? Itinerary { get; private set; }
        public double RouteProgressMetres { get; private set; }
        public double ArrivalShownSeconds { get; private set; }
        private bool continuousGuide;
        public bool AtArrival => Itinerary != null && previousLive &&
            previousPosition.HorizontalDistance(Itinerary.Destination.Position) <= Itinerary.ArrivalRadius &&
            (continuousGuide ? LastRenderedPosition.HasValue && TravelMetres > 0 :
                TravelMetres >= Math.Max(0, TargetMetres - Itinerary.ArrivalRadius * 2) * .9 &&
                creditedRouteProgress >= TargetMetres - Itinerary.ArrivalRadius * 2);
        public int Windows { get; private set; }
        public AutonomyPosition ObjectiveOrigin { get; private set; }
        public AutonomyPosition ObjectiveGoal { get; private set; }
        public AutonomyPosition Heading { get; private set; }
        public ScenicJourneyOutcome Outcome { get; private set; }
        public string Reason { get; private set; } = "No journey requested";
        public double RemainingMetres => Itinerary != null ? Math.Max(0, TargetMetres - RouteProgressMetres) :
            Math.Max(0, Math.Max(TargetMetres - TravelMetres,
            TargetMetres * ObjectiveFraction - Math.Min(ObjectiveAdvanceMetres, creditedAdvanceMetres)));
        public bool TargetReached => Itinerary != null ? AtArrival && ArrivalShownSeconds >= 3 :
            TargetMetres > 0 && TravelMetres >= TargetMetres - .5 &&
            Math.Min(ObjectiveAdvanceMetres, creditedAdvanceMetres) >= TargetMetres * ObjectiveFraction - .5;
        public bool BudgetExpired => TargetMetres > 0 && (ShownSeconds >= MaximumSeconds || Windows >= 1024 || TravelMetres >= TargetMetres * 1.5 + MinimumWindowMetres);
        public bool CanContinue => Active && !TargetReached && !BudgetExpired;
        public double AchievedMetresPerSecond => ShownSeconds > 0 ? TravelMetres / ShownSeconds : 0;
        public JourneyTrail Trail { get; } = new JourneyTrail();
        public AutonomyPosition? LastRenderedPosition { get; private set; }
        private long observedPlan;
        private double observedProgress, observedAt = -1, creditedAdvanceMetres;
        private double previousRouteProgress, creditedRouteProgress, arrivalObservedAt = -1;
        private bool previousArrivalLive;
        private bool previousLive;
        private AutonomyPosition previousPosition;

        public static bool Eligible(ScenicShotPlan? plan) => plan != null && plan.IsDirected && !plan.IsPictureOnly && !plan.IsPan && !plan.HasRejectedContinuation &&
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

        /// <summary>Production admission uses a named, observed destination and
        /// metadata itinerary. The legacy heading overload remains available to
        /// old mathematical fixtures; it is not destination discovery.</summary>
        public void Start(long id, ScenicJourneyItinerary itinerary, double cruiseSpeed)
        {
            if (id <= 0 || itinerary == null || itinerary.RouteMetres < 256 || itinerary.RouteMetres > 6750)
                throw new ArgumentOutOfRangeException(nameof(itinerary));
            Speed(cruiseSpeed);
            Reset(); Id = id; Itinerary = itinerary; TargetMetres = itinerary.RouteMetres; CruiseSpeed = cruiseSpeed;
            ObjectiveOrigin = itinerary.Origin; ObjectiveGoal = itinerary.Destination.Position;
            AutonomyPosition next = itinerary.SteeringGoal(itinerary.Origin);
            double magnitude = itinerary.Origin.HorizontalDistance(next);
            Heading = new AutonomyPosition((float)((next.X - ObjectiveOrigin.X) / magnitude), 0,
                (float)((next.Z - ObjectiveOrigin.Z) / magnitude));
            // Supported cruise may smoothly fall to its declared useful floor
            // when real preparation latency consumes the certified reserve.
            // Keep that legitimate travel inside the finite programme budget.
            MaximumSeconds = TargetMetres / MinimumCruiseSpeed * 1.5 + 45;
            Outcome = ScenicJourneyOutcome.Running; Reason = itinerary.Description;
        }

        public AutonomyPosition SteeringGoal(AutonomyPosition from, double lookaheadMetres = 48) =>
            Itinerary?.SteeringGoal(from, lookaheadMetres) ?? ObjectiveGoal;

        /// <summary>Arrival is an actual presented ending, not merely a nearby
        /// coordinate or a spent distance budget. A held/hidden/unidentified
        /// subject cannot earn completion, and returning to it restarts its dwell.</summary>
        public void ObserveArrival(double at, bool liveReadable, bool held,
            AutonomyPosition? actualPosition, bool arrivalSubjectVisible)
        {
            if (!Finite(at) || at < 0) throw new ArgumentOutOfRangeException(nameof(at));
            if (!Active || Itinerary == null || at <= arrivalObservedAt) return;
            double gap = arrivalObservedAt < 0 ? 0 : at - arrivalObservedAt;
            arrivalObservedAt = at;
            bool valid = AtArrival && liveReadable && !held && arrivalSubjectVisible && actualPosition.HasValue &&
                actualPosition.Value.HorizontalDistance(Itinerary.Destination.Position) <= Itinerary.ArrivalRadius;
            if (!valid)
            { previousArrivalLive = false; if (!held) ArrivalShownSeconds = 0; return; }
            if (previousArrivalLive && gap <= .25) ArrivalShownSeconds += gap;
            previousArrivalLive = true;
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
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.JourneyId != Id) return;
            ObservePose(plan.Id, at, progress, liveReadable, held, actualPosition, guided: false);
        }

        public void ObserveGuide(double at, bool liveReadable, bool held, AutonomyPosition actualPosition)
        {
            // The guide advances only one physical step per confirmed render.
            // Completion uses the actual destination radius; a metadata route
            // can curve or detour and is not a distance-credit requirement.
            continuousGuide = true;
            if (Active && liveReadable && at > observedAt &&
                actualPosition.Distance(LastRenderedPosition ?? ObjectiveOrigin) > CruiseSpeed * .15 + .2)
            {
                Finish(ScenicJourneyOutcome.Interrupted, "Rendered guide moved beyond a single physical step");
                return;
            }
            ObservePose(Id, at, 0, liveReadable, held, actualPosition, guided: true);
        }

        private void ObservePose(long shotId, double at, double progress, bool liveReadable, bool held,
            AutonomyPosition? actualPosition, bool guided)
        {
            if (!Finite(at) || at < 0 || !Finite(progress) || progress < 0 || progress > 1)
                throw new ArgumentOutOfRangeException(nameof(progress));
            if (!Active || at <= observedAt) return;
            double gap = observedAt < 0 ? 0 : at - observedAt;
            observedAt = at;
            if (!liveReadable || held || !actualPosition.HasValue)
            { previousLive = false; return; }
            if (observedPlan == shotId && progress + .000001 < observedProgress)
            { previousLive = false; return; }
            AutonomyPosition position = actualPosition.Value;
            if (previousLive && gap <= .25)
            {
                double step = position.Distance(previousPosition);
                if (step > (guided ? CruiseSpeed * gap + .2 : MaximumStepMetres + .05))
                { Finish(ScenicJourneyOutcome.Interrupted, "Camera position jumped beyond the validated journey step"); return; }
                TravelMetres += step;
                // A relocation hidden by held/missing footage cannot fulfill
                // the objective even if enough local looping travel preceded it.
                creditedAdvanceMetres += (position.X - previousPosition.X) * Heading.X +
                    (position.Z - previousPosition.Z) * Heading.Z;
                if (Itinerary != null)
                    creditedRouteProgress += Itinerary.ProjectProgress(position) - previousRouteProgress;
                ShownSeconds += gap;
            }
            Trail.Observe(position, previousLive && gap <= .25);
            LastRenderedPosition = position;
            if (observedPlan != shotId) { observedPlan = shotId; Windows++; }
            observedProgress = progress;
            previousPosition = position; previousLive = true;
            if (Itinerary != null)
            {
                previousRouteProgress = RouteProgressMetres = Itinerary.ProjectProgress(position);
                // This is actual route-relative development, not a dot product
                // against the first heading which would reject curved journeys.
                ObjectiveAdvanceMetres = RouteProgressMetres;
            }
            DisplacementMetres = ObjectiveOrigin.HorizontalDistance(position);
            if (Itinerary == null)
                ObjectiveAdvanceMetres = (position.X - ObjectiveOrigin.X) * Heading.X + (position.Z - ObjectiveOrigin.Z) * Heading.Z;
        }

        public void Finish(ScenicJourneyOutcome outcome, string reason)
        {
            if (outcome == ScenicJourneyOutcome.None || outcome == ScenicJourneyOutcome.Running)
                throw new ArgumentOutOfRangeException(nameof(outcome));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A journey outcome needs a reason.", nameof(reason));
            if (outcome == ScenicJourneyOutcome.Completed && !TargetReached)
            { outcome = ScenicJourneyOutcome.Blocked; reason = (Itinerary == null ? "Distance/forward objective was not reached: " :
                "Observed destination and readable arrival were not completed: ") + reason; }
            Outcome = outcome; Reason = reason; previousLive = false;
        }

        /// <summary>A missing capture breaks continuity without inventing a
        /// newer timestamp that would suppress the next real render sample.</summary>
        public void BreakPresentation()
        { previousLive = previousArrivalLive = false; }

        public void Reset()
        {
            Trail.Reset(); LastRenderedPosition = null; continuousGuide = false;
            Id = observedPlan = 0; ShownSeconds = TravelMetres = DisplacementMetres = ObjectiveAdvanceMetres = MaximumSeconds = TargetMetres = CruiseSpeed = observedProgress = creditedAdvanceMetres = 0;
            observedAt = -1; Windows = 0; previousLive = false; previousPosition = ObjectiveOrigin = ObjectiveGoal = Heading = default;
            Itinerary = null; RouteProgressMetres = ArrivalShownSeconds = previousRouteProgress = creditedRouteProgress = 0;
            arrivalObservedAt = -1; previousArrivalLive = false;
            Outcome = ScenicJourneyOutcome.None; Reason = "No journey requested";
        }
        private static void Speed(double value)
        { if (!Finite(value) || value < MinimumCruiseSpeed || value > MaximumCruiseSpeed) throw new ArgumentOutOfRangeException(nameof(value)); }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
