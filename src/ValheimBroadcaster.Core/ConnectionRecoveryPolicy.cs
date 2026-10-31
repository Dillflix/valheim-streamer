using System;

namespace ValheimBroadcaster.Core
{
    public enum ConnectionRecoveryState { Disabled, Monitoring, Backoff, Connecting, Halted }

    /// <summary>In-process connection intent. Authentication never succeeds by timer or retry.</summary>
    public sealed class ConnectionRecoveryPolicy
    {
        private double lastClock, stableSince;
        private bool haveClock;
        private long worldId;
        private int burstAttempts;
        public ConnectionRecoveryState State { get; private set; }
        public bool Armed => State == ConnectionRecoveryState.Monitoring || Recovering;
        public bool Recovering => State == ConnectionRecoveryState.Backoff || State == ConnectionRecoveryState.Connecting;
        public long Attempts { get; private set; }
        public long WorldId => worldId;
        public double NextAttemptAt { get; private set; }
        public double AttemptDeadline { get; private set; }
        public string LastFailure { get; private set; } = "none";
        public const double AttemptSeconds = 90;
        public const double StableSeconds = 60;
        public const int BurstLimit = 8;
        public const double BurstCooldownSeconds = 900;

        public void Arm(double now, long authenticatedWorldId)
        {
            if (!Check(now)) return;
            if (authenticatedWorldId == 0) throw new ArgumentOutOfRangeException(nameof(authenticatedWorldId));
            worldId = authenticatedWorldId; Attempts = burstAttempts = 0;
            State = ConnectionRecoveryState.Monitoring; stableSince = now;
            NextAttemptAt = AttemptDeadline = 0; LastFailure = "none";
        }

        public bool Connected(double now, long authenticatedWorldId)
        {
            if (!Check(now) || !Armed) return false;
            if (authenticatedWorldId != worldId) { Halt("Connected world differs from the authorized session"); return false; }
            if (State != ConnectionRecoveryState.Monitoring)
            {
                State = ConnectionRecoveryState.Monitoring; stableSince = now;
                NextAttemptAt = AttemptDeadline = 0;
            }
            if (now - stableSince >= StableSeconds) burstAttempts = 0;
            return true;
        }

        public void Lost(double now, string reason)
        {
            if (!Check(now) || !Armed || State == ConnectionRecoveryState.Backoff) return;
            LastFailure = reason;
            State = ConnectionRecoveryState.Backoff;
            double delay = Math.Min(120, 5 * Math.Pow(2, Math.Min(burstAttempts, 5)));
            if (burstAttempts >= BurstLimit) { delay = BurstCooldownSeconds; burstAttempts = 0; }
            NextAttemptAt = now + delay; AttemptDeadline = 0;
        }

        public bool BeginAttempt(double now)
        {
            if (!Check(now) || State != ConnectionRecoveryState.Backoff || now < NextAttemptAt) return false;
            State = ConnectionRecoveryState.Connecting;
            burstAttempts++; Attempts = Math.Min(long.MaxValue - 1, Attempts) + 1;
            AttemptDeadline = now + AttemptSeconds;
            return true;
        }

        public bool TimedOut(double now)
        {
            if (!Check(now) || State != ConnectionRecoveryState.Connecting || now < AttemptDeadline) return false;
            Lost(now, "Connection or observer startup timed out");
            return true;
        }

        public void Cancel()
        {
            State = ConnectionRecoveryState.Disabled; NextAttemptAt = AttemptDeadline = 0;
            LastFailure = "Stopped by operator";
        }
        public void Halt(string reason)
        {
            State = ConnectionRecoveryState.Halted; NextAttemptAt = AttemptDeadline = 0;
            LastFailure = reason;
        }
        private bool Check(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (haveClock && now < lastClock) { Halt("Monotonic connection clock moved backwards"); return false; }
            haveClock = true; lastClock = now; return true;
        }
    }
}
