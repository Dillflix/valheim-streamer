using System;
using System.Globalization;

namespace ValheimBroadcaster.Core
{
    public enum StartupJoinState { Disabled, Waiting, Connecting, Backoff, Complete, Blocked, Cancelled }

    /// <summary>One explicit startup intent. Success requires a real authorized observer, never a timer.</summary>
    public sealed class StartupJoinPolicy
    {
        public StartupJoinState State { get; private set; }
        public int Attempts { get; private set; }
        public double Deadline { get; private set; }
        public double NextAttemptAt { get; private set; }
        public string Failure { get; private set; } = "none";
        public bool Pending => State == StartupJoinState.Waiting || State == StartupJoinState.Connecting || State == StartupJoinState.Backoff;
        public const int MaximumAttempts = 4;
        public const double AttemptSeconds = 90;
        private double lastClock;

        public StartupJoinPolicy(bool enabled) { State = enabled ? StartupJoinState.Waiting : StartupJoinState.Disabled; }
        public bool Begin(double now)
        {
            Clock(now);
            if ((State != StartupJoinState.Waiting && State != StartupJoinState.Backoff) || now < NextAttemptAt) return false;
            Attempts++; State = StartupJoinState.Connecting; Deadline = now + AttemptSeconds;
            return true;
        }
        public bool TimedOut(double now)
        {
            Clock(now);
            if (State != StartupJoinState.Connecting || now < Deadline) return false;
            if (Attempts >= MaximumAttempts) Block("attempts_exhausted");
            else { State = StartupJoinState.Backoff; NextAttemptAt = now + 5 * Math.Pow(2, Attempts - 1); Deadline = 0; }
            return true;
        }
        public void Complete()
        {
            if (State != StartupJoinState.Connecting) throw new InvalidOperationException("No startup join is awaiting authorization");
            State = StartupJoinState.Complete; Deadline = NextAttemptAt = 0;
        }
        public void Block(string reason) { State = StartupJoinState.Blocked; Failure = reason; Deadline = NextAttemptAt = 0; }
        public void Cancel()
        {
            if (State == StartupJoinState.Disabled || State == StartupJoinState.Complete) return;
            State = StartupJoinState.Cancelled; Deadline = NextAttemptAt = 0;
        }
        private void Clock(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || now < lastClock) throw new ArgumentOutOfRangeException(nameof(now));
            lastClock = now;
        }

        public static bool ValidProfile(string filename) => !string.IsNullOrWhiteSpace(filename) && filename.Length <= 128 &&
            filename == filename.Trim() && filename != "." && filename != ".." &&
            filename.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\r', '\n', '\0' }) < 0 &&
            !filename.EndsWith(".fch", StringComparison.OrdinalIgnoreCase);

        // Require an explicit port; never silently choose a default or interpret a Steam join code as an address.
        public static bool TryAddress(string address, out string host, out ushort port)
        {
            host = ""; port = 0;
            if (string.IsNullOrEmpty(address) || address.Length > 260 || address != address.Trim()) return false;
            int split = address.LastIndexOf(':');
            if (split <= 0 || !ushort.TryParse(address.Substring(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port == 0) return false;
            host = address.Substring(0, split);
            // This initial adapter supports native Steam DNS/IPv4 destinations. IPv6 needs its own verified launch path.
            UriHostNameType type = Uri.CheckHostName(host);
            return type == UriHostNameType.Dns || type == UriHostNameType.IPv4;
        }
    }
}
