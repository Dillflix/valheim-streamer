using System;

namespace ValheimBroadcaster.Plugin.Streaming;

// Pure transport lifecycle policy. Clock and process availability come from the adapter.
// Retries continue at a bounded rate; configuration/capability errors require explicit on.
internal sealed class OutputRecoveryPolicy
{
    internal const double InitialDelaySeconds = 5;
    internal const double MaximumDelaySeconds = 60;
    internal const double BurstCooldownSeconds = 300;
    internal const int AttemptsPerBurst = 8;
    internal const double HealthyResetSeconds = 30;
    private double nextAttempt, publishingSince = -1;
    private bool stableReset;
    internal bool Desired { get; private set; }
    internal bool ManualSuppressed { get; private set; }
    internal bool Blocked { get; private set; }
    internal bool InFlight { get; private set; }
    internal int FailuresInBurst { get; private set; }
    internal long Attempts { get; private set; }
    internal string LastFailure { get; private set; } = "none";

    internal void ArmAutoStart(bool enabled, double now)
    {
        Time(now);
        if (enabled && !ManualSuppressed && !Desired) { Desired = true; nextAttempt = now; }
    }
    internal void RequestStart(double now)
    {
        Time(now);
        Desired = true; ManualSuppressed = Blocked = false;
        FailuresInBurst = 0; nextAttempt = now; publishingSince = -1; stableReset = false;
    }
    internal void RequestStop()
    {
        Desired = InFlight = Blocked = false;
        ManualSuppressed = true; publishingSince = -1; stableReset = false;
        FailuresInBurst = 0;
    }
    internal void Suspend()
    {
        InFlight = false; publishingSince = -1; stableReset = false;
    }
    internal bool TryBegin(double now, bool mayPublish, bool processAvailable)
    {
        Time(now);
        if (!Desired || Blocked || InFlight || !mayPublish || !processAvailable || now < nextAttempt) return false;
        InFlight = true; publishingSince = -1; stableReset = false;
        if (Attempts < long.MaxValue) Attempts++;
        return true;
    }
    internal void Failed(double now, bool permanent, bool autoRetry, string safeReason)
    {
        Time(now);
        InFlight = false; publishingSince = -1; stableReset = false;
        LastFailure = safeReason.Length > 512 ? safeReason.Substring(0, 512) : safeReason;
        if (!Desired) return;
        Blocked = permanent || !autoRetry;
        if (Blocked) return;
        if (FailuresInBurst >= AttemptsPerBurst) FailuresInBurst = 0;
        FailuresInBurst++;
        double delay = FailuresInBurst == AttemptsPerBurst ? BurstCooldownSeconds :
            Math.Min(MaximumDelaySeconds, InitialDelaySeconds * Math.Pow(2, FailuresInBurst - 1));
        nextAttempt = now + delay;
    }
    internal void Publishing(double now)
    {
        Time(now);
        if (!InFlight) return;
        if (publishingSince < 0 || now < publishingSince) publishingSince = now;
        if (!stableReset && now - publishingSince >= HealthyResetSeconds)
        { FailuresInBurst = 0; stableReset = true; }
    }
    internal double RetryIn(double now) { Time(now); return Math.Max(0, nextAttempt - now); }
    private static void Time(double now)
    { if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now)); }
}
