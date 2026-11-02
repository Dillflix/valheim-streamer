using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>A loaded player's measured relationship to the selected primary.</summary>
    public readonly struct CompanionObservation
    {
        public readonly string Id;
        public readonly AutonomyPosition Position;
        public readonly bool SameInterior, ClearConnection, SharedMotion;
        public CompanionObservation(string id, AutonomyPosition position, bool sameInterior, bool clearConnection, bool sharedMotion)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A companion requires an identity.", nameof(id));
            Id = id; Position = position; SameInterior = sameInterior;
            ClearConnection = clearConnection; SharedMotion = sharedMotion;
        }
    }

    /// <summary>Persistent, primary-anchored membership. Never grows a transitive chain of nearby players.</summary>
    public sealed class CompanionGroupPolicy
    {
        public const int MaximumCompanions = 3;
        public const double JoinDistance = 18, ExitDistance = 24, HardDistance = 30, MaximumSpan = 30;
        public const double JoinSeconds = 2, MovingJoinSeconds = 1, ExitGraceSeconds = 2;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly List<string> members = new List<string>(MaximumCompanions);
        private readonly List<string> remove = new List<string>();
        private readonly List<Entry> ordered = new List<Entry>();
        private string? primary;
        private double lastNow = -1;
        public IReadOnlyList<string> Members => members;

        /// <summary>Shift emphasis within an already observed coherent group.
        /// The adapter must still establish same world, live actor bindings and
        /// structural line of sight. New/unrelated primaries use ordinary Reset.</summary>
        public bool TryReanchor(double now, string nextPrimary, AutonomyPosition nextPosition,
            AutonomyPosition previousPosition, bool clearConnection)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || string.IsNullOrEmpty(nextPrimary))
                throw new ArgumentException("Reanchoring requires a finite time and primary identity.");
            if (primary == null || primary == nextPrimary || lastNow < 0 || now < lastNow || now - lastNow > 1 ||
                !clearConnection || !entries.TryGetValue(nextPrimary, out Entry? next) || !next.Member ||
                !next.Observation.SameInterior || !next.Observation.ClearConnection ||
                nextPosition.HorizontalDistance(previousPosition) > ExitDistance ||
                Math.Abs(nextPosition.Y - previousPosition.Y) > 3.5 ||
                nextPosition.HorizontalDistance(next.Observation.Position) > 5) return false;
            string previous = primary;
            entries.Remove(nextPrimary);
            entries[previous] = new Entry(new CompanionObservation(previous, previousPosition, true, true,
                next.Observation.SharedMotion), now - JoinSeconds) { Member = true };
            primary = nextPrimary; lastNow = now;
            members.Remove(nextPrimary);
            if (!members.Contains(previous)) members.Insert(0, previous);
            return true;
        }

        private sealed class Entry
        {
            internal CompanionObservation Observation;
            internal double JoinedSince, OutsideSince = -1;
            internal bool Member, Seen;
            internal Entry(CompanionObservation observation, double now) { Observation = observation; JoinedSince = now; }
        }

        public IReadOnlyList<string> Update(double now, string primaryId, AutonomyPosition primaryPosition,
            IReadOnlyList<CompanionObservation> observations)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || string.IsNullOrEmpty(primaryId))
                throw new ArgumentException("Membership requires a finite time and primary identity.");
            if (observations == null || observations.Count > 64) throw new ArgumentException("At most 64 loaded companions may be surveyed.");
            if (primary != primaryId || lastNow >= 0 && (now < lastNow || now - lastNow > 1)) Reset();
            primary = primaryId; lastNow = now;
            foreach (Entry entry in entries.Values) entry.Seen = false;
            foreach (CompanionObservation observation in observations)
            {
                if (observation.Id == primaryId) continue;
                double distance = primaryPosition.HorizontalDistance(observation.Position);
                bool hardInvalid = !observation.SameInterior || Math.Abs(primaryPosition.Y - observation.Position.Y) > 3.5 || distance > HardDistance;
                if (hardInvalid) { entries.Remove(observation.Id); continue; }
                if (!entries.TryGetValue(observation.Id, out Entry? entry))
                {
                    if (distance > JoinDistance || !observation.ClearConnection) continue;
                    entries[observation.Id] = entry = new Entry(observation, now);
                }
                if (entry.Seen) continue;
                entry.Observation = observation; entry.Seen = true;
                bool within = distance <= (entry.Member ? ExitDistance : JoinDistance) && observation.ClearConnection;
                if (within) entry.OutsideSince = -1;
                else
                {
                    if (entry.OutsideSince < 0) entry.OutsideSince = now;
                    if (!entry.Member) entry.JoinedSince = now;
                    if (now - entry.OutsideSince >= ExitGraceSeconds) entry.Member = false;
                }
            }
            remove.Clear(); ordered.Clear();
            foreach (KeyValuePair<string, Entry> pair in entries)
            {
                if (!pair.Value.Seen) remove.Add(pair.Key); // An unloaded/departed actor is never framed from stale coordinates.
                else ordered.Add(pair.Value);
            }
            foreach (string id in remove) entries.Remove(id);
            ordered.Sort((a, b) =>
            {
                int retained = b.Member.CompareTo(a.Member);
                if (retained != 0) return retained;
                int distance = primaryPosition.HorizontalDistance(a.Observation.Position).CompareTo(primaryPosition.HorizontalDistance(b.Observation.Position));
                return distance != 0 ? distance : StringComparer.Ordinal.Compare(a.Observation.Id, b.Observation.Id);
            });
            members.Clear();
            foreach (Entry entry in ordered)
            {
                CompanionObservation observation = entry.Observation;
                bool joinReady = entry.OutsideSince < 0 && now - entry.JoinedSince >= (observation.SharedMotion ? MovingJoinSeconds : JoinSeconds);
                bool fits = members.Count < MaximumCompanions;
                foreach (string member in members)
                    if (entries[member].Observation.Position.HorizontalDistance(observation.Position) > MaximumSpan) fits = false;
                if (fits && (entry.Member || joinReady)) { entry.Member = true; members.Add(observation.Id); }
                else entry.Member = false;
            }
            return members;
        }

        public void Reset()
        { entries.Clear(); members.Clear(); remove.Clear(); ordered.Clear(); primary = null; lastNow = -1; }
    }
}
