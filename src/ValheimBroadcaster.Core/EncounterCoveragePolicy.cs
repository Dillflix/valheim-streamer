using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public enum EncounterShotPurpose { Player, Exchange, Overview }

    /// <summary>One observed actor in a bounded cast. Indexes refer to the
    /// observation, never to a camera's possibly smaller target list.</summary>
    public readonly struct EncounterCastMember
    {
        public readonly string Id;
        public readonly AutonomyPosition Position;
        public readonly float Radius, Height;
        public readonly bool Opponent, Key;
        public EncounterCastMember(string id, AutonomyPosition position, float radius, float height, bool opponent, bool key)
        { Id = id; Position = position; Radius = radius; Height = height; Opponent = opponent; Key = key; }
    }

    /// <summary>Screen-footprint planning, not a visibility certificate. A
    /// compact large cast can fit; a sparse small cast can need separate views.
    /// Actual native camera/framing/occlusion validation follows this proposal.</summary>
    public static class EncounterCastPolicy
    {
        public const int Capacity = 16;
        public static void Select(IReadOnlyList<EncounterCastMember> observed, EncounterShotPurpose purpose,
            float heading, float aspect, bool confined, IList<int> result)
        {
            if (observed == null || result == null || observed.Count > Capacity || !Finite(heading) ||
                !Finite(aspect) || aspect <= 0 || aspect > 10 || (int)purpose < 0 || (int)purpose > 2)
                throw new ArgumentOutOfRangeException(nameof(observed));
            result.Clear();
            if (observed.Count == 0) return;
            for (int i = 0; i < observed.Count; i++)
                if (string.IsNullOrEmpty(observed[i].Id) || !Finite(observed[i].Radius) || observed[i].Radius <= 0 ||
                    !Finite(observed[i].Height) || observed[i].Height <= 0)
                    throw new ArgumentOutOfRangeException(nameof(observed));
            result.Add(0);
            if (purpose == EncounterShotPurpose.Player) return;
            int key = -1;
            for (int i = 1; i < observed.Count; i++) if (observed[i].Key) { key = i; break; }
            if (key >= 0 && Fits(observed, result, key, purpose, heading, aspect, confined)) result.Add(key);
            // Deterministic spatial admission. Alternate allies and opponents
            // when similarly useful; no fixed quota erases either population.
            while (result.Count < observed.Count)
            {
                int best = -1;
                double bestScore = double.NegativeInfinity;
                int foes = 0, allies = 0, sectors = 0;
                foreach (int i in result)
                    if (i != 0)
                    { if (observed[i].Opponent) { foes++; sectors |= Sector(observed[0].Position, observed[i].Position); } else allies++; }
                for (int i = 1; i < observed.Count; i++)
                {
                    if (result.Contains(i) || !Fits(observed, result, i, purpose, heading, aspect, confined)) continue;
                    double distance = observed[i].Position.HorizontalDistance(observed[0].Position);
                    double relationship = observed[i].Opponent ? foes <= allies ? 1 : 0 : allies < foes ? 1 : 0;
                    double representation = purpose == EncounterShotPurpose.Overview && observed[i].Opponent &&
                        (sectors & Sector(observed[0].Position, observed[i].Position)) == 0 ? 1.5 : 0;
                    double score = relationship + representation + (observed[i].Key ? 10 : 0) - distance * .04;
                    if (best < 0 || score > bestScore + .0001 || Math.Abs(score - bestScore) < .0001 &&
                        string.CompareOrdinal(observed[i].Id, observed[best].Id) < 0)
                    { best = i; bestScore = score; }
                }
                if (best < 0) break;
                result.Add(best);
            }
        }

        private static bool Fits(IReadOnlyList<EncounterCastMember> actors, IList<int> selected, int next,
            EncounterShotPurpose purpose, float heading, float aspect, bool confined)
        {
            double angle = heading * Math.PI / 180, rightX = Math.Cos(angle), rightZ = -Math.Sin(angle),
                forwardX = Math.Sin(angle), forwardZ = Math.Cos(angle);
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity,
                minY = double.PositiveInfinity, maxY = double.NegativeInfinity,
                minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            for (int n = 0; n <= selected.Count; n++)
            {
                EncounterCastMember member = actors[n == selected.Count ? next : selected[n]];
                double x = member.Position.X - actors[0].Position.X, y = member.Position.Y - actors[0].Position.Y,
                    z = member.Position.Z - actors[0].Position.Z;
                double lateral = x * rightX + z * rightZ, depth = x * forwardX + z * forwardZ;
                minX = Math.Min(minX, lateral - member.Radius); maxX = Math.Max(maxX, lateral + member.Radius);
                minY = Math.Min(minY, y - member.Height * .5); maxY = Math.Max(maxY, y + member.Height * .5);
                minZ = Math.Min(minZ, depth - member.Radius); maxZ = Math.Max(maxZ, depth + member.Radius);
            }
            // Preserve a useful primary scale. Native DollyThenZoom may achieve
            // this envelope only when the real geometry permits it.
            double minimumHeight = purpose == EncounterShotPurpose.Overview ? .075 : .14;
            if (confined) minimumHeight = Math.Max(minimumHeight, .18);
            double verticalSpan = actors[0].Height / minimumHeight;
            double depthSpan = maxZ - minZ;
            double widthBudget = verticalSpan * aspect * .8 - depthSpan * .35;
            double heightBudget = verticalSpan * .8 - depthSpan * .2;
            return maxX - minX <= Math.Max(1, widthBudget) && maxY - minY <= Math.Max(1, heightBudget) &&
                depthSpan <= verticalSpan * (confined ? .45 : purpose == EncounterShotPurpose.Overview ? .9 : .7);
        }

        public static int Sector(AutonomyPosition origin, AutonomyPosition actor)
        {
            double x = actor.X - origin.X, z = actor.Z - origin.Z;
            if (x * x + z * z < .25) return 0;
            return 1 << EncounterSituationPolicy.Sector((float)x, (float)z);
        }
        public static int BitCount(int value)
        { int count = 0; for (uint bits = (uint)value; bits != 0; bits &= bits - 1) count++; return count; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct EncounterCoverage
    {
        public readonly EncounterShotPurpose Purpose;
        public readonly bool Fulfilled;
        public readonly int VisibleOpponents, VisibleCompanions, RepresentedSectors;
        public readonly string Reason;
        public EncounterCoverage(EncounterShotPurpose purpose, bool fulfilled, int opponents, int companions, int sectors, string reason)
        { Purpose = purpose; Fulfilled = fulfilled; VisibleOpponents = opponents; VisibleCompanions = companions; RepresentedSectors = sectors; Reason = reason; }
    }

    public static class EncounterCoveragePolicy
    {
        public static EncounterCoverage Measure(EncounterShotPurpose purpose, bool primaryReadable, bool keyVisible,
            int observedOpponents, int observedCompanions, int observedSectors, int visibleOpponents, int visibleCompanions, int visibleSectors)
        {
            bool exchange = primaryReadable && keyVisible;
            int neededSectors = (int)Math.Ceiling(EncounterCastPolicy.BitCount(observedSectors) * .67);
            bool overview = exchange && visibleOpponents >= Math.Min(3, observedOpponents) &&
                (observedCompanions == 0 || visibleCompanions > 0) &&
                EncounterCastPolicy.BitCount(visibleSectors & observedSectors) >= neededSectors;
            bool fulfilled = purpose == EncounterShotPurpose.Player ? primaryReadable :
                purpose == EncounterShotPurpose.Exchange ? exchange : overview;
            return new EncounterCoverage(purpose, fulfilled, visibleOpponents, visibleCompanions,
                EncounterCastPolicy.BitCount(visibleSectors), fulfilled ? purpose + " coverage satisfied" :
                !primaryReadable ? "Player readability unavailable" : !keyVisible ? "Player view retained; principal opponent not represented" :
                "Partial encounter view; surrounding groups not sufficiently represented");
        }

        /// <summary>Only readable candidates enter editorial bands. If no
        /// encounter purpose is fulfilled, ordinary picture ranking is intact.</summary>
        public static float Rank(float score, bool readable, EncounterCoverage coverage,
            EncounterShotPurpose wanted, bool anyFulfilled)
        {
            if (!readable || !anyFulfilled || wanted == EncounterShotPurpose.Player) return score;
            if (coverage.Fulfilled && coverage.Purpose == wanted) return .82f + score * .18f;
            if (coverage.Fulfilled && coverage.Purpose != EncounterShotPurpose.Player) return .48f + score * .16f;
            return score * .30f;
        }
    }

    /// <summary>Protect an action beat, but do not let continuous attack flags
    /// indefinitely preserve a picture that never explains its opponent.</summary>
    public sealed class EncounterPurposeProtection
    {
        private double last = -1, pendingSince = -1;
        private long scene;
        private string target = "";
        public bool Allow(double now, long sceneId, string targetId, bool protectedAction,
            bool currentReadable, bool currentExplainsEncounter, bool fulfilledAlternative)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (scene != sceneId || target != targetId || last >= 0 && (now < last || now - last > 1)) Reset();
            last = now; scene = sceneId; target = targetId;
            if (!protectedAction || !currentReadable) { pendingSince = -1; return true; }
            if (currentExplainsEncounter || !fulfilledAlternative || sceneId <= 0 || string.IsNullOrEmpty(targetId))
            { pendingSince = -1; return false; }
            if (pendingSince < 0) pendingSince = now;
            return now - pendingSince >= 3;
        }
        public void Reset() { last = pendingSince = -1; scene = 0; target = ""; }
    }
}
