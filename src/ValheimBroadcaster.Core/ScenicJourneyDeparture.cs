using System;

namespace ValheimBroadcaster.Core
{
    public enum ScenicJourneyDepartureState { None, Preparing, Ready, Admitted, Abandoned }

    /// <summary>A pending departure never owns or parks the picture. Legacy
    /// connected continuations require their matching seed endpoint. Independent
    /// cuts instead preserve preparation across ordinary edits and admit only
    /// with current route/ending proof at a useful editorial boundary.</summary>
    public sealed class ScenicJourneyDeparture
    {
        public const double PreparationSeconds = ScenicJourneyPolicy.PreparationSeconds;
        public long SeedId { get; private set; }
        public long Epoch { get; private set; }
        public long LastAttemptedSeed { get; private set; }
        private long lastAttemptedEpoch;
        private bool independentCut;
        public double ElapsedSeconds { get; private set; }
        public double PreparationLimitSeconds { get; private set; } = PreparationSeconds;
        public ScenicJourneyDepartureState State { get; private set; }
        public string Reason { get; private set; } = "No departure requested";
        public bool Pending => State == ScenicJourneyDepartureState.Preparing || State == ScenicJourneyDepartureState.Ready;
        public bool OwnsCoverage => State == ScenicJourneyDepartureState.Admitted;

        public bool CanAttempt(long seed, long epoch) => seed > 0 && epoch > 0 && !Pending && !OwnsCoverage &&
            (seed != LastAttemptedSeed || epoch != lastAttemptedEpoch);

        public void Begin(long seed, long epoch) => Begin(seed, epoch, false, PreparationSeconds);

        /// <summary>A separately validated departure is an editorial cut, not a
        /// continuation of the incidental ordinary shot that seeded its pose.</summary>
        public void BeginIndependent(long seed, long epoch, double preparationSeconds = PreparationSeconds) =>
            Begin(seed, epoch, true, preparationSeconds);

        private void Begin(long seed, long epoch, bool cut, double preparationSeconds)
        {
            if (double.IsNaN(preparationSeconds) || double.IsInfinity(preparationSeconds) || preparationSeconds <= 0 || preparationSeconds > 90)
                throw new ArgumentOutOfRangeException(nameof(preparationSeconds));
            if (!CanAttempt(seed, epoch)) throw new ArgumentException("Departure needs a fresh readable seed and scene.");
            independentCut = cut; PreparationLimitSeconds = preparationSeconds;
            SeedId = LastAttemptedSeed = seed; Epoch = lastAttemptedEpoch = epoch; ElapsedSeconds = 0;
            State = ScenicJourneyDepartureState.Preparing; Reason = "Preparing departure while ordinary Scenic coverage continues";
        }

        public ScenicJourneyDepartureState Observe(long seed, long epoch, bool endpoint, bool ready,
            bool held, bool available, double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!Pending) return State;
            if ((!independentCut && seed != SeedId) || epoch != Epoch || !available)
            { Abandon("Departure lost its current readable seed or scene"); return State; }
            if (held) return State;
            // Waiting for a normal editorial boundary does not spend route
            // computation time. The adapter separately bounds request lifetime
            // and the age of every route/ending proof.
            if (!independentCut || !ready) ElapsedSeconds += deltaSeconds;
            if (independentCut)
            {
                if (ElapsedSeconds > PreparationLimitSeconds)
                    Abandon("Departure preparation expired; ordinary coverage continues");
                else if (ready && endpoint)
                { State = ScenicJourneyDepartureState.Admitted; Reason = "Validated independent departure admitted at an editorial boundary"; }
                else
                {
                    State = ready ? ScenicJourneyDepartureState.Ready : ScenicJourneyDepartureState.Preparing;
                    Reason = ready ? "Departure ready; retaining the current shot's minimum useful dwell" :
                        "Preparing an independent departure while ordinary coverage continues";
                }
                return State;
            }
            if (endpoint)
            {
                if (ready && ElapsedSeconds <= PreparationLimitSeconds)
                { State = ScenicJourneyDepartureState.Admitted; Reason = "Validated departure admitted at the seed endpoint"; }
                else Abandon("Departure was not ready at the seed endpoint; ordinary coverage continues");
            }
            else if (ElapsedSeconds > PreparationLimitSeconds)
                Abandon("Departure preparation expired; ordinary coverage continues");
            else
            {
                State = ready ? ScenicJourneyDepartureState.Ready : ScenicJourneyDepartureState.Preparing;
                Reason = ready ? "Departure ready; awaiting the existing shot endpoint" :
                    "Preparing departure while ordinary Scenic coverage continues";
            }
            return State;
        }


        /// <summary>Use the ordinary role's established minimum dwell. A due
        /// replacement may cut immediately; an independent picture retains its
        /// own four-second minimum. This does not grant scene admission.</summary>
        public static bool CutReady(ScenicShotPlan current, double shownSeconds, bool editDue)
        {
            if (current == null || double.IsNaN(shownSeconds) || double.IsInfinity(shownSeconds) || shownSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(shownSeconds));
            return !current.IsPictureOnly && editDue || shownSeconds >= (current.IsPictureOnly ? ScenicPictureAvailability.MinimumPictureSeconds :
                ScenicPacingPolicy.MinimumSeconds(current.Role));
        }

        /// <summary>The observed pose is only a departure proposal. Strip the
        /// former shot's subject envelope and route rejection: a new forward
        /// route must earn its own complete geometry certificate.</summary>
        public static ScenicShotPlan Snapshot(ScenicShotPlan source, AutonomyPosition position, AutonomyPosition aim, float fov)
        {
            if (source == null || !source.IsDirected || source.Feature == null) throw new ArgumentException("A measured scenic pose is required.", nameof(source));
            ScenicFeature old = source.Feature;
            var direction = new AutonomyPosition(aim.X - position.X, 0, aim.Z - position.Z);
            ScenicFeatureKind kind = old.Kind == ScenicFeatureKind.Structure || old.Kind == ScenicFeatureKind.Rock
                ? ScenicFeatureKind.Landscape : old.Kind;
            var feature = new ScenicFeature(old.Id, kind, aim, direction, old.Radius, old.Height,
                old.Confidence, waterDirection: old.WaterDirection);
            return ScenicShotPlan.Picture(source.Id, feature, ScenicShotRole.Traverse, position, aim, fov, 0, -.04f).WithJourneyView();
        }

        /// <summary>Selected natural coordinates persist within a world even
        /// when a bounded destination page evicts them. A structural report has
        /// its existing age limit; absence alone is not a withdrawal message.
        /// The adapter separately requires a fresh same-world server feed.</summary>
        public static bool DestinationCurrent(bool natural, double reportedAge, double elapsed, bool retryRestricted)
        {
            if (double.IsNaN(reportedAge) || double.IsInfinity(reportedAge) || reportedAge < 0 ||
                double.IsNaN(elapsed) || double.IsInfinity(elapsed) || elapsed < 0) return false;
            return !retryRestricted && (natural || reportedAge + elapsed < WorldInterestProtocol.SiteSeconds);
        }

        public void Abandon(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A departure outcome needs a reason.", nameof(reason));
            State = ScenicJourneyDepartureState.Abandoned; Reason = reason;
        }

        public void Reset()
        {
            PreparationLimitSeconds = PreparationSeconds; independentCut = false; SeedId = Epoch = LastAttemptedSeed = lastAttemptedEpoch = 0; ElapsedSeconds = 0;
            State = ScenicJourneyDepartureState.None; Reason = "No departure requested";
        }
    }
}
