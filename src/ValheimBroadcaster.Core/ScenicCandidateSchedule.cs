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

        public static int AttemptLimit(IReadOnlyList<ScenicFeature> features, int currentIndex, int searchRound)
        {
            Validate(features, currentIndex, searchRound);
            int roles = RolesPerPass(features, currentIndex, searchRound);
            return Math.Min(MaximumAttempts, roles * 8);
        }

        public static Candidate Select(IReadOnlyList<ScenicFeature> features, int currentIndex, int searchRound, int attempt)
        {
            int limit = AttemptLimit(features, currentIndex, searchRound);
            if (attempt < 0 || attempt >= limit) throw new ArgumentOutOfRangeException(nameof(attempt));
            int roles = RolesPerPass(features, currentIndex, searchRound), pass = attempt / roles, offset = attempt % roles;
            int count = Math.Min(MaximumFeaturesPerSearch, features.Count);
            for (int slot = 0; slot < count; slot++)
            {
                int feature = FeatureIndex(features.Count, currentIndex, searchRound, slot);
                int roleCount = RoleCount(features[feature].Kind);
                if (offset < roleCount)
                    return new Candidate(feature, offset, (searchRound + pass + offset * 2) % 8);
                offset -= roleCount;
            }
            throw new InvalidOperationException("Scenic candidate schedule is inconsistent.");
        }

        public static ScenicShotRole Role(ScenicFeatureKind kind, ScenicShotRole preferred, int index)
        {
            if (!Enum.IsDefined(typeof(ScenicShotRole), preferred) || index < 0 || index >= RoleCount(kind))
                throw new ArgumentOutOfRangeException(nameof(index));
            int seen = 0;
            for (int step = 0; step < 4; step++)
            {
                var role = (ScenicShotRole)(((int)preferred + step) % 4);
                if (role == ScenicShotRole.Detail && RoleCount(kind) == 3) continue;
                if (seen++ == index) return role;
            }
            throw new InvalidOperationException("No applicable scenic role.");
        }

        public static int RoleCount(ScenicFeatureKind kind) =>
            kind == ScenicFeatureKind.Rock || kind == ScenicFeatureKind.Structure ? 4 : 3;

        private static int RolesPerPass(IReadOnlyList<ScenicFeature> features, int currentIndex, int searchRound)
        {
            int roles = 0;
            for (int slot = 0; slot < Math.Min(MaximumFeaturesPerSearch, features.Count); slot++)
                roles += RoleCount(features[FeatureIndex(features.Count, currentIndex, searchRound, slot)].Kind);
            return roles;
        }

        private static int FeatureIndex(int count, int currentIndex, int searchRound, int slot)
        {
            if (currentIndex >= 0 && slot == 0) return currentIndex;
            int available = count - (currentIndex >= 0 ? 1 : 0);
            int ordinal = slot - (currentIndex >= 0 ? 1 : 0);
            int index = (searchRound * MaximumFeaturesPerSearch + ordinal) % available;
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
