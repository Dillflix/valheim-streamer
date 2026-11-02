using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Selects the longest contiguous readable portion of an ordered
    /// sampled route. This identifies a proposal for slicing and full native
    /// revalidation; it never certifies unsampled or newly generated geometry.</summary>
    public sealed class ScenicRouteWindow
    {
        private double lastProgress = -1, lastDistance;
        private double runStart, runDistance;
        private bool previousReadable;

        public bool HasWindow => TravelMetres > 0 && End > Start;
        public double Start { get; private set; }
        public double End { get; private set; }
        public double TravelMetres { get; private set; }

        /// <param name="progress">Strictly increasing normalized route progress.</param>
        /// <param name="cumulativeDistance">Nondecreasing measured body travel
        /// along this same route, including its unreadable portions.</param>
        /// <param name="readable">Complete actual-pose evidence. Missing or
        /// uncertain geometry/visibility must be reported as false.</param>
        /// <param name="corridorFromPreviousClear">Complete corridor evidence
        /// between this sample and the immediately preceding sample. Unknown or
        /// obstructed corridors are false even when both endpoints are clear.</param>
        public void Observe(double progress, double cumulativeDistance, bool readable, bool corridorFromPreviousClear)
        {
            if (!Finite(progress) || progress < 0 || progress > 1 || progress <= lastProgress)
                throw new ArgumentOutOfRangeException(nameof(progress));
            if (!Finite(cumulativeDistance) || cumulativeDistance < 0 ||
                lastProgress >= 0 && cumulativeDistance < lastDistance)
                throw new ArgumentOutOfRangeException(nameof(cumulativeDistance));

            if (readable)
            {
                if (!previousReadable || !corridorFromPreviousClear)
                { runStart = progress; runDistance = cumulativeDistance; }
                double distance = cumulativeDistance - runDistance;
                // Keep the first equal-length run for stable deterministic
                // selection. A lone readable sample never becomes a route.
                if (distance > TravelMetres)
                { Start = runStart; End = progress; TravelMetres = distance; }
            }
            previousReadable = readable;
            lastProgress = progress; lastDistance = cumulativeDistance;
        }

        public void Reset()
        {
            lastProgress = -1; lastDistance = runStart = runDistance = 0;
            previousReadable = false; Start = End = TravelMetres = 0;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
