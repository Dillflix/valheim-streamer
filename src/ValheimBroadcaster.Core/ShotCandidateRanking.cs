using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core
{
    /// <summary>Ranks already evaluated candidates without moving a camera,
    /// advancing its solver or deciding when a cut is safe to display.</summary>
    public interface IShotCandidateRanker
    {
        int Best(IReadOnlyList<float> scores, IReadOnlyList<bool> eligible,
            IReadOnlyList<bool>? readable, int incumbent);
    }

    public static class ShotCandidateRanking
    {
        /// <summary>Optional wildlife coverage must earn admission with a
        /// readable subject. Physical support alone remains sufficient for the
        /// existing player camera's local correction/search behavior.</summary>
        public static bool Admissible(ShotSceneKind kind, bool supported, bool readable) =>
            supported && (kind != ShotSceneKind.Creature || readable);

        /// <summary>Disjoint quality bands preserve readable-first ordering in
        /// native Cinemachine ClearShot's scalar ShotQuality comparison.</summary>
        public static float NativeQuality(float score, bool readable)
        {
            if (float.IsNaN(score) || float.IsInfinity(score) || score < 0 || score > 1)
                throw new ArgumentOutOfRangeException(nameof(score));
            return score + (readable ? 2 : 0);
        }

        public static int Best(IReadOnlyList<float> scores, IReadOnlyList<bool> eligible,
            IReadOnlyList<bool>? readable, int incumbent, IShotCandidateRanker? ranker = null)
        {
            if (ranker != null)
            {
                int result = ranker.Best(scores, eligible, readable, incumbent);
                if (result < -1 || result >= scores.Count || result >= 0 && !eligible[result])
                    throw new InvalidOperationException("Camera ranker returned an ineligible candidate.");
                // A native adapter cannot turn image-quality ordering into
                // permission to use an unsafe camera or suppress all coverage.
                for (int i = 0; i < scores.Count; i++)
                    if (eligible[i] && (result < 0 || readable != null && readable[i] && !readable[result]))
                        throw new InvalidOperationException("Camera ranker lost supported readable coverage.");
                return result;
            }
            // The pure policy can still run without Unity (and supports the
            // explicit native-off comparison). Production supplies ClearShot.
            int best = incumbent >= 0 && incumbent < scores.Count && eligible[incumbent] ? incumbent : -1;
            for (int i = 0; i < scores.Count; i++)
                if (eligible[i] && (best < 0 || readable != null && readable[i] && !readable[best] ||
                    (readable == null || readable[i] == readable[best]) && scores[i] > scores[best])) best = i;
            return best;
        }
    }
}
