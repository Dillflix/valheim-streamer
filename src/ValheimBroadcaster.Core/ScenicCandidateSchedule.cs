using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Give every applicable shot scale a chance before spending the
    /// bounded search on additional angles. A busy survey must not consume its
    /// entire budget on wide establishing shots.</summary>
    public static class ScenicCandidateSchedule
    {
        public const int MaximumAttempts = 96;
        private const int MaximumFeaturesPerSearch = MaximumAttempts / 4;

        public readonly struct Candidate
        {
            public readonly int FeatureIndex, RoleIndex, Viewpoint;
            internal Candidate(int feature, int role, int viewpoint)
            { FeatureIndex = feature; RoleIndex = role; Viewpoint = viewpoint; }
        }

        /// <summary>Editorial scores are relative and may be negative. Once
        /// geometry/readability has admitted a candidate, bounded comparison
        /// must not become an exhaustive search because continuity penalized it.</summary>
        public static bool ComparisonComplete(int attempts, bool hasValidatedCandidate)
        {
            if (attempts < 0) throw new ArgumentOutOfRangeException(nameof(attempts));
            return hasValidatedCandidate && attempts >= 4;
        }

        public static int AttemptLimit(IReadOnlyList<ScenicFeature> features, int currentIndex, int searchRound)
        {
            Validate(features, currentIndex, searchRound);
            int roles = RolesPerPass(features, currentIndex, searchRound);
            return Math.Min(MaximumAttempts, roles * 8);
        }

        /// <summary>Spatial search explores viewpoints inside each role itself;
        /// repeat azimuth passes would only repeat the same measured search.</summary>
        public static int SpatialAttemptLimit(IReadOnlyList<ScenicFeature> features, int currentIndex, int searchRound)
        { Validate(features, currentIndex, searchRound); return RolesPerPass(features, currentIndex, searchRound); }

        public static Candidate Select(IReadOnlyList<ScenicFeature> features, int currentIndex, int searchRound, int attempt)
        {
            int limit = AttemptLimit(features, currentIndex, searchRound);
            if (attempt < 0 || attempt >= limit) throw new ArgumentOutOfRangeException(nameof(attempt));
            int roles = RolesPerPass(features, currentIndex, searchRound), pass = attempt / roles, offset = attempt % roles;
            int count = Math.Min(MaximumFeaturesPerSearch, features.Count);
            for (int slot = 0; slot < count; slot++)
            {
                int feature = FeatureIndex(features.Count, currentIndex, searchRound, slot);
                int roleCount = RoleCount(features[feature]);
                if (offset < roleCount)
                    return new Candidate(feature, offset, (searchRound + pass + offset * 2) % 8);
                offset -= roleCount;
            }
            throw new InvalidOperationException("Scenic candidate schedule is inconsistent.");
        }

        /// <summary>Give each observed subject its preferred role before
        /// spending the spatial survey budget on another role of one subject.
        /// A failing aggregate must not consume recovery with four consecutive
        /// searches while a nearby building or coastline remains unexamined.</summary>
        public static Candidate SelectSpatial(IReadOnlyList<ScenicFeature> features, int currentIndex, int searchRound, int attempt, bool developing = false)
        {
            int limit = SpatialAttemptLimit(features, currentIndex, searchRound);
            if (attempt < 0 || attempt >= limit) throw new ArgumentOutOfRangeException(nameof(attempt));
            int count = Math.Min(MaximumFeaturesPerSearch, features.Count), offset = attempt;
            // Healthy prefetch must see alternate scale/movement before its
            // early acceptance threshold (four attempts). Urgent acquisition
            // keeps the broad subject-first schedule below.
            int leadFeature = FeatureIndex(features.Count, currentIndex, searchRound, 0);
            bool interleave = developing && RoleCount(features[leadFeature]) > 1;
            if (interleave)
            {
                if (attempt == 0) return new Candidate(leadFeature, 0, 0);
                if (count > 1 && attempt == 1) return new Candidate(FeatureIndex(features.Count, currentIndex, searchRound, 1), 0, 0);
                int secondRoleAt = count > 1 ? 2 : 1;
                if (attempt == secondRoleAt) return new Candidate(leadFeature, 1, 0);
                // The other subject also gets a different role before the
                // bounded search can accept four same-scale candidates.
                if (count > 1 && RoleCount(features[FeatureIndex(features.Count, currentIndex, searchRound, 1)]) > 1)
                {
                    if (attempt == 3) return new Candidate(FeatureIndex(features.Count, currentIndex, searchRound, 1), 1, 0);
                    offset = attempt - 4;
                }
                else offset = attempt - secondRoleAt - 1;
            }
            for (int role = 0; role < 4; role++)
            for (int slot = 0; slot < count; slot++)
            {
                int feature = FeatureIndex(features.Count, currentIndex, searchRound, slot);
                if (role >= RoleCount(features[feature])) continue;
                if (interleave && (slot == 0 || slot == 1) && role < 2) continue;
                if (offset-- == 0) return new Candidate(feature, role, 0);
            }
            throw new InvalidOperationException("Spatial candidate schedule is inconsistent.");
        }

        public static ScenicShotRole Role(ScenicFeatureKind kind, ScenicShotRole preferred, int index)
            => RoleForCount(RoleCount(kind), preferred, index);

        public static ScenicShotRole Role(ScenicFeature feature, ScenicShotRole preferred, int index)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (feature.SubjectKind == ScenicSubjectKind.Npc || feature.ScopeLabel == "Opening")
            {
                if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
                return ScenicShotRole.Detail;
            }
            return RoleForCount(RoleCount(feature), preferred, index);
        }

        private static ScenicShotRole RoleForCount(int count, ScenicShotRole preferred, int index)
        {
            if (!Enum.IsDefined(typeof(ScenicShotRole), preferred) || index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(index));
            int seen = 0;
            for (int step = 0; step < 4; step++)
            {
                var role = (ScenicShotRole)(((int)preferred + step) % 4);
                if (role == ScenicShotRole.Detail && count == 3) continue;
                if (seen++ == index) return role;
            }
            throw new InvalidOperationException("No applicable scenic role.");
        }

        public static int RoleCount(ScenicFeatureKind kind) =>
            kind == ScenicFeatureKind.Rock || kind == ScenicFeatureKind.Structure ? 4 : 3;

        public static int RoleCount(ScenicFeature feature)
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (feature.SubjectKind == ScenicSubjectKind.Npc || feature.ScopeLabel == "Opening") return 1;
            return feature.ScopeLabel == "Settlement" || feature.ScopeLabel == "District"
                ? 3 : RoleCount(feature.Kind);
        }

        private static int RolesPerPass(IReadOnlyList<ScenicFeature> features, int currentIndex, int searchRound)
        {
            int roles = 0;
            for (int slot = 0; slot < Math.Min(MaximumFeaturesPerSearch, features.Count); slot++)
                roles += RoleCount(features[FeatureIndex(features.Count, currentIndex, searchRound, slot)]);
            return roles;
        }

        private static int FeatureIndex(int count, int currentIndex, int searchRound, int slot)
        {
            if (currentIndex >= 0 && slot == 0) return currentIndex;
            int available = count - (currentIndex >= 0 ? 1 : 0);
            int ordinal = slot - (currentIndex >= 0 ? 1 : 0);
            // A pinned current/child subject occupies one slot. Advance only
            // by the remaining batch width, otherwise one omitted subject is
            // skipped again at every boundary between observation sweeps.
            int stride = MaximumFeaturesPerSearch - (currentIndex >= 0 ? 1 : 0);
            int index = (searchRound * stride + ordinal) % available;
            return currentIndex >= 0 && index >= currentIndex ? index + 1 : index;
        }

        private static void Validate(IReadOnlyList<ScenicFeature> features, int currentIndex, int searchRound)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));
            if (currentIndex < -1 || currentIndex >= features.Count || searchRound < 0)
                throw new ArgumentOutOfRangeException(nameof(currentIndex));
        }
    }
}
