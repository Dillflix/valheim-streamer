using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public enum WildlifePlayerActivity { Unknown, Active, Idle }

    /// <summary>Source-observed quiet duration, never a client prediction of
    /// activity. FreshUntil includes the age of the original observation.</summary>
    public readonly struct WildlifePlayerEvidence
    {
        public readonly string Id;
        public readonly WildlifePlayerActivity Activity;
        public readonly double IdleSeconds, FreshUntil;
        public WildlifePlayerEvidence(string id, WildlifePlayerActivity activity, double idleSeconds, double freshUntil)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64) throw new ArgumentException("Invalid player identity.", nameof(id));
            if (!Enum.IsDefined(typeof(WildlifePlayerActivity), activity)) throw new ArgumentOutOfRangeException(nameof(activity));
            if (!Finite(idleSeconds) || idleSeconds < 0 || !Finite(freshUntil) || freshUntil < 0)
                throw new ArgumentOutOfRangeException(nameof(idleSeconds));
            Id = id; Activity = activity; IdleSeconds = idleSeconds; FreshUntil = freshUntil;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// Allows a bounded local creature cutaway only with affirmative, complete
    /// evidence that every connected player is idle. CoverageDirector retains
    /// creature selection, duration and shown/failure cooldowns. No travel or
    /// scenic selection is requested here.
    /// </summary>
    public sealed class WildlifeCutawayPolicy
    {
        public const double RequiredIdleSeconds = 60, PlayerCoverageSeconds = 60;
        public const int MaximumPlayers = 128;
        private readonly HashSet<string> roster = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> observedRoster = new HashSet<string>(StringComparer.Ordinal);
        private double lastNow = -1, rearmAt;
        private bool rosterEstablished, wasCutaway;
        public bool AllowCutaway { get; private set; }
        public double IdleSeconds { get; private set; }
        public double RearmRemainingSeconds { get; private set; }
        public string Reason { get; private set; } = "Awaiting complete player activity";

        public bool Update(double now, IReadOnlyList<WildlifePlayerEvidence> connectedPlayers,
            bool rosterComplete, bool suppressed, bool cutawayActive)
        {
            if (!Finite(now) || now < 0 || lastNow >= 0 && now < lastNow)
            { Reset(); throw new ArgumentOutOfRangeException(nameof(now)); }
            if (connectedPlayers == null) throw new ArgumentNullException(nameof(connectedPlayers));
            if (connectedPlayers.Count > MaximumPlayers) throw new ArgumentException("Player activity inventory exceeds its bound.");
            observedRoster.Clear();
            double minimumIdle = double.PositiveInfinity;
            bool known = true, active = false;
            foreach (WildlifePlayerEvidence player in connectedPlayers)
            {
                if (string.IsNullOrWhiteSpace(player.Id) || !observedRoster.Add(player.Id))
                    throw new ArgumentException("Missing or duplicate player activity identity.");
                if (player.Activity == WildlifePlayerActivity.Unknown || now >= player.FreshUntil) known = false;
                if (player.Activity == WildlifePlayerActivity.Active) active = true;
                minimumIdle = Math.Min(minimumIdle, player.IdleSeconds);
            }
            bool changed = rosterEstablished && !roster.SetEquals(observedRoster);
            if (wasCutaway && !cutawayActive || changed || suppressed || !rosterComplete ||
                connectedPlayers.Count == 0 || !known || active)
                rearmAt = Math.Max(rearmAt, now + PlayerCoverageSeconds);
            roster.Clear();
            foreach (string id in observedRoster) roster.Add(id);
            rosterEstablished = true;
            wasCutaway = cutawayActive;
            lastNow = now;
            IdleSeconds = rosterComplete && connectedPlayers.Count > 0 && known && !active ? minimumIdle : 0;
            RearmRemainingSeconds = Math.Max(0, rearmAt - now);
            // An old 59-second snapshot never becomes a 60-second observation.
            // A brief action between snapshots is visible through the source's
            // reset duration even when the actor is already still again.
            AllowCutaway = !suppressed && rosterComplete && connectedPlayers.Count > 0 && known && !active &&
                !changed && minimumIdle >= RequiredIdleSeconds && RearmRemainingSeconds <= 0;
            Reason = suppressed ? "Wildlife cutaways suppressed by the current operation" :
                connectedPlayers.Count == 0 ? "No connected players; normal idle coverage applies" :
                !rosterComplete || !known ? "Waiting for complete fresh player activity" :
                changed ? "Player membership changed; returning to player coverage" :
                active || minimumIdle < RequiredIdleSeconds ? "Players have not all been observed idle for sixty seconds" :
                RearmRemainingSeconds > 0 ? "Giving players coverage before another wildlife cutaway" :
                cutawayActive ? "Nearby wildlife while all connected players remain idle" :
                "All connected players are idle; a nearby wildlife cutaway is allowed";
            return AllowCutaway;
        }

        public void Reset()
        {
            roster.Clear(); observedRoster.Clear(); lastNow = -1; rearmAt = 0;
            rosterEstablished = wasCutaway = AllowCutaway = false;
            IdleSeconds = RearmRemainingSeconds = 0; Reason = "Awaiting complete player activity";
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
