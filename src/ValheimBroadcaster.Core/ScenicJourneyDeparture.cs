using System;

namespace ValheimBroadcaster.Core
{
    public enum ScenicJourneyDepartureState { None, Preparing, Ready, Admitted, Abandoned }

    /// <summary>A pending departure never owns the picture. Ownership starts
    /// only when a validated successor meets its still-current seed endpoint.
    /// An unready endpoint is abandoned immediately, leaving ordinary coverage
    /// free to select its already prepared replacement.</summary>
    public sealed class ScenicJourneyDeparture
    {
        public const double PreparationSeconds = ScenicJourneyPolicy.PreparationSeconds;
        public long SeedId { get; private set; }
        public long Epoch { get; private set; }
        public long LastAttemptedSeed { get; private set; }
        private long lastAttemptedEpoch;
        public double ElapsedSeconds { get; private set; }
        public ScenicJourneyDepartureState State { get; private set; }
        public string Reason { get; private set; } = "No departure requested";
        public bool Pending => State == ScenicJourneyDepartureState.Preparing || State == ScenicJourneyDepartureState.Ready;
        public bool OwnsCoverage => State == ScenicJourneyDepartureState.Admitted;

        public bool CanAttempt(long seed, long epoch) => seed > 0 && epoch > 0 && !Pending && !OwnsCoverage &&
            (seed != LastAttemptedSeed || epoch != lastAttemptedEpoch);

        public void Begin(long seed, long epoch)
        {
            if (!CanAttempt(seed, epoch)) throw new ArgumentException("Departure needs a fresh readable seed and scene.");
            SeedId = LastAttemptedSeed = seed; Epoch = lastAttemptedEpoch = epoch; ElapsedSeconds = 0;
            State = ScenicJourneyDepartureState.Preparing; Reason = "Preparing departure while ordinary Scenic coverage continues";
        }

        public ScenicJourneyDepartureState Observe(long seed, long epoch, bool endpoint, bool ready,
            bool held, bool available, double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!Pending) return State;
            if (seed != SeedId || epoch != Epoch || !available)
            { Abandon("Departure lost its current readable seed or scene"); return State; }
            if (held) return State;
            ElapsedSeconds += deltaSeconds;
            if (endpoint)
            {
                if (ready && ElapsedSeconds <= PreparationSeconds)
                { State = ScenicJourneyDepartureState.Admitted; Reason = "Validated departure admitted at the seed endpoint"; }
                else Abandon("Departure was not ready at the seed endpoint; ordinary coverage continues");
            }
            else if (ElapsedSeconds > PreparationSeconds)
                Abandon("Departure preparation expired; ordinary coverage continues");
            else
            {
                State = ready ? ScenicJourneyDepartureState.Ready : ScenicJourneyDepartureState.Preparing;
                Reason = ready ? "Departure ready; awaiting the existing shot endpoint" :
                    "Preparing departure while ordinary Scenic coverage continues";
            }
            return State;
        }

        public void Abandon(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A departure outcome needs a reason.", nameof(reason));
            State = ScenicJourneyDepartureState.Abandoned; Reason = reason;
        }

        public void Reset()
        {
            SeedId = Epoch = LastAttemptedSeed = lastAttemptedEpoch = 0; ElapsedSeconds = 0;
            State = ScenicJourneyDepartureState.None; Reason = "No departure requested";
        }
    }
}
