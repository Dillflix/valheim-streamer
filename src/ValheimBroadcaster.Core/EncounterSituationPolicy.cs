using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public enum EncounterPressure { None, Presence, Developing, Engaged }

    /// <summary>Local hostile geometry and observed motion, not aggro, intent,
    /// damage prediction or proof that the player is in danger.</summary>
    public readonly struct EncounterSituation
    {
        public readonly EncounterPressure Pressure;
        public readonly int HostileCount, ApproachingCount, CombatantCount, SectorMask, OccupiedSectors;
        public readonly bool Encircled;
        public readonly float Confidence, Spread, DirectionX, DirectionZ;
        internal EncounterSituation(EncounterPressure pressure, int hostile, int approaching, int combatants,
            int sectors, bool encircled, float spread, float x, float z)
        {
            Pressure = pressure; HostileCount = hostile; ApproachingCount = approaching; CombatantCount = combatants;
            SectorMask = sectors; OccupiedSectors = SectorCount(sectors); Encircled = encircled; Spread = spread;
            Confidence = hostile == 0 ? 0 : pressure == EncounterPressure.Engaged ? .8f : pressure == EncounterPressure.Developing ? .7f : .65f;
            double length = Math.Sqrt(x * x + z * z);
            DirectionX = length > .01 ? (float)(x / length) : 0;
            DirectionZ = length > .01 ? (float)(z / length) : 0;
        }
        private static int SectorCount(int mask)
        { int count = 0; for (int i = 0; i < 8; i++) if ((mask & (1 << i)) != 0) count++; return count; }
    }

    /// <summary>Uses the entire bounded observation shortlist, independently of
    /// how many participants a camera can frame. No actor is spawned or awakened.</summary>
    public sealed class EncounterSituationPolicy
    {
        private double last = -1, pendingSince;
        private EncounterPressure pressure, pending;
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        public EncounterSituation Current { get; private set; }

        public EncounterSituation Observe(double now, IReadOnlyList<EncounterObservation> observations, bool groupAttack)
        {
            if (!Finite(now) || now < 0 || observations == null || observations.Count > EncounterPolicy.MaximumCandidates)
                throw new ArgumentException("Encounter situation requires finite time and a bounded survey.");
            if (last >= 0 && (now < last || now - last > 1)) Reset();
            last = now; seen.Clear();
            int hostile = 0, approaching = 0, combatants = 0, sectors = 0, near = 0;
            bool prominent = false, immediate = false;
            float spread = 0, x = 0, z = 0;
            foreach (EncounterObservation value in observations)
            {
                if (!value.IsValid || !value.Hostile || !value.ClearConnection || value.GroupDistance > 40 || !seen.Add(value.Id)) continue;
                EncounterCandidate classified = EncounterPolicy.Classify(value, false, groupAttack);
                if (classified.Kind == EncounterKind.Combat) combatants++;
                // Distant unengaged hostiles are observations, not a reason to
                // repeatedly pull the programme away from the player's activity.
                if (value.GroupDistance > 28 && classified.Kind != EncounterKind.Combat) continue;
                hostile++;
                if (value.GroupDistance <= 20) near++;
                if (value.MotionReliable && value.ClosingSpeed >= .75f && value.GroupDistance <= 28) approaching++;
                prominent |= value.Prominence >= .5f && value.GroupDistance <= 20;
                immediate |= value.GroupDistance <= 6;
                spread = Math.Max(spread, value.Distance);
                float weight = 1f / Math.Max(4, value.Distance);
                x += value.OffsetX * weight; z += value.OffsetZ * weight;
                if (value.Distance <= 24 && value.OffsetX * value.OffsetX + value.OffsetZ * value.OffsetZ >= 1)
                    sectors |= 1 << Sector(value.OffsetX, value.OffsetZ);
            }
            bool surrounded = Encircled(sectors, near);
            EncounterPressure desired = hostile == 0 ? EncounterPressure.None : combatants > 0 ? EncounterPressure.Engaged :
                surrounded || near >= 3 || approaching > 0 || prominent || immediate ? EncounterPressure.Developing : EncounterPressure.Presence;
            if (desired != pending) { pending = desired; pendingSince = now; }
            // Loss of relevant observations never keeps ghost pressure alive.
            // Motion/crowd promotion must persist; confirmed action is immediate.
            if (desired == EncounterPressure.None || desired == EncounterPressure.Engaged || pressure == EncounterPressure.None ||
                desired == pressure || now - pendingSince >= (desired > pressure ? .75 : 1.5))
                pressure = desired == EncounterPressure.Developing && pressure == EncounterPressure.None
                    ? EncounterPressure.Presence : desired;
            return Current = new EncounterSituation(pressure, hostile, approaching, combatants, sectors, surrounded, spread, x, z);
        }

        public static int Sector(float x, float z)
        {
            if (!Finite(x) || !Finite(z)) return -1;
            double angle = Math.Atan2(x, z) + Math.PI / 8;
            if (angle < 0) angle += 2 * Math.PI;
            return ((int)Math.Floor(angle / (Math.PI / 4))) & 7;
        }
        private static bool Encircled(int mask, int count)
        {
            if (count < 3) return false;
            int occupied = 0, gap = 0, longest = 0;
            for (int i = 0; i < 8; i++) if ((mask & (1 << i)) != 0) occupied++;
            if (occupied < 3) return false;
            for (int i = 0; i < 16; i++)
            { if ((mask & (1 << (i & 7))) == 0) longest = Math.Max(longest, ++gap); else gap = 0; }
            // Coarse eight-sector enclosure; three nearby mobs all on one side
            // cannot satisfy it. This is geometry, never a tactical guarantee.
            return longest < 3;
        }
        public void Reset() { last = -1; pendingSince = 0; pressure = pending = EncounterPressure.None; Current = default; seen.Clear(); }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
