using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    public readonly struct EncounterParticipant
    {
        public readonly string Id;
        public readonly AutonomyPosition Position;
        public readonly bool Opponent, Primary, KeyOpponent, Retained;
        public readonly float Relevance;
        public EncounterParticipant(string id, AutonomyPosition position, bool opponent = false,
            bool primary = false, bool keyOpponent = false, bool retained = false, float relevance = 0)
        { Id = id; Position = position; Opponent = opponent; Primary = primary; KeyOpponent = keyOpponent; Retained = retained; Relevance = relevance; }
    }

    /// <summary>A bounded observational cast, not the number of subjects every
    /// shot must include. Reserve the story owner and principal relationship,
    /// then retain both sides and spatially representative connected actors.</summary>
    public sealed class EncounterParticipantPolicy
    {
        public const int MaximumParticipants = 16;
        private readonly List<EncounterParticipant> candidates = new List<EncounterParticipant>(128);
        private readonly List<string> selected = new List<string>(MaximumParticipants);
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        public IReadOnlyList<string> Select(IReadOnlyList<EncounterParticipant> observations)
        {
            if (observations == null || observations.Count > 128) throw new ArgumentException("Participant observations must be bounded.");
            candidates.Clear(); selected.Clear(); seen.Clear();
            foreach (EncounterParticipant candidate in observations)
                if (!string.IsNullOrEmpty(candidate.Id) && candidate.Id.Length <= 128 &&
                    Finite(candidate.Relevance) && seen.Add(candidate.Id)) candidates.Add(candidate);
            candidates.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
            int primary = candidates.FindIndex(c => c.Primary);
            if (primary < 0) return selected;
            if (candidates.FindLastIndex(c => c.Primary) != primary) throw new ArgumentException("An observational cast has one primary.");
            EncounterParticipant owner = candidates[primary]; SelectAt(primary);
            int key = candidates.FindIndex(c => c.Opponent && c.KeyOpponent);
            int opponentSectors = 0, companionSectors = 0;
            if (key >= 0)
            {
                EncounterParticipant principal = candidates[key];
                opponentSectors |= 1 << EncounterSituationPolicy.Sector((float)(principal.Position.X - owner.Position.X), (float)(principal.Position.Z - owner.Position.Z));
                SelectAt(key);
            }
            // Alternate available sides instead of allowing a larger mob count
            // to erase every companion or many companions to erase all foes.
            bool wantOpponent = false;
            while (selected.Count < MaximumParticipants && candidates.Count > 0)
            {
                int best = -1;
                double bestScore = double.NegativeInfinity;
                bool matchingSide = candidates.Exists(c => c.Opponent == wantOpponent);
                for (int i = 0; i < candidates.Count; i++)
                {
                    EncounterParticipant candidate = candidates[i];
                    if (candidate.Primary || matchingSide && candidate.Opponent != wantOpponent) continue;
                    double dx = candidate.Position.X - owner.Position.X, dz = candidate.Position.Z - owner.Position.Z;
                    int sector = EncounterSituationPolicy.Sector((float)dx, (float)dz);
                    int mask = candidate.Opponent ? opponentSectors : companionSectors;
                    double distance = Math.Sqrt(dx * dx + dz * dz);
                    double score = Math.Min(1, Math.Max(0, candidate.Relevance / 6)) + (candidate.Retained ? .3 : 0) +
                        ((mask & (1 << sector)) == 0 ? .35 : 0) + .25 * (1 - Math.Min(1, distance / 40));
                    if (score > bestScore) { best = i; bestScore = score; }
                }
                if (best < 0) break;
                EncounterParticipant chosen = candidates[best];
                int chosenSector = EncounterSituationPolicy.Sector((float)(chosen.Position.X - owner.Position.X), (float)(chosen.Position.Z - owner.Position.Z));
                if (chosen.Opponent) opponentSectors |= 1 << chosenSector; else companionSectors |= 1 << chosenSector;
                SelectAt(best); wantOpponent = !wantOpponent;
            }
            return selected;
        }
        private void SelectAt(int index) { selected.Add(candidates[index].Id); candidates.RemoveAt(index); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
