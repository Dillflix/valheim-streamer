using System;

namespace ValheimBroadcaster.Core;

// Resumable procedural sampling. A caller charges every height query to its own
// budget; no inference is made from unobserved points. All offsets stay inside
// their original 64m zone, whose full delivery neighborhood is checked outside.
public sealed class LandscapeSurvey
{
    public const int SampleCount = 9;
    private static readonly float[] X = { 24, -24, 0, 0, 24, 24, -24, -24 }, Z = { 0, 0, 24, -24, 24, -24, 24, -24 };
    private readonly float[] heights = new float[SampleCount];
    private int sampled;
    public int NextSample => sampled;
    public bool Complete => sampled == SampleCount;
    public static ObserverPoint Offset(int index)
    {
        if (index < 0 || index >= SampleCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (index == 0) return default;
        int n = index - 1;
        // Centre first, then compass points and diagonals.
        return new ObserverPoint(X[n], 0, Z[n]);
    }
    public void Observe(float height)
    {
        if (Complete) throw new InvalidOperationException("Survey already complete.");
        heights[sampled++] = height; // NaN stays unknown; never becomes dry/wet evidence.
    }
    public bool TryDestination(float water, out ObserverPoint offset, out ScenicLandscape landscape)
    {
        offset = default; landscape = ScenicLandscape.Unknown;
        if (!Complete || float.IsNaN(water) || float.IsInfinity(water)) return false;
        bool wet = false, completeEvidence = true; int selected = -1; float best = float.PositiveInfinity;
        for (int i = 0; i < SampleCount; i++)
        {
            float h = heights[i]; if (float.IsNaN(h) || float.IsInfinity(h) || h < -1000 || h >= 990) { completeEvidence = false; continue; }
            if (h <= water) wet = true;
        }
        for (int i = 0; i < SampleCount; i++)
        {
            float h = heights[i]; if (float.IsNaN(h) || float.IsInfinity(h) || h <= water + 2 || h >= 990) continue;
            // A coast approach needs observed dry ground. Minimize distance to
            // an observed wet sample without putting the carrier underwater.
            float score = i == 0 ? 0 : 1;
            if (wet)
            {
                score = float.PositiveInfinity; ObserverPoint a = Offset(i);
                for (int j = 0; j < SampleCount; j++)
                    if (!float.IsNaN(heights[j]) && !float.IsInfinity(heights[j]) && heights[j] >= -1000 && heights[j] <= water)
                    {
                        ObserverPoint b = Offset(j); float dx = a.X - b.X, dz = a.Z - b.Z;
                        score = Math.Min(score, dx * dx + dz * dz);
                    }
            }
            if (score < best) { selected = i; best = score; }
        }
        if (selected < 0) return false;
        ObserverPoint o = Offset(selected); offset = new ObserverPoint(o.X, heights[selected] + 8, o.Z);
        landscape = wet ? ScenicLandscape.Coast : !completeEvidence ? ScenicLandscape.Unknown : heights[selected] >= water + 80 ? ScenicLandscape.Highland : ScenicLandscape.Inland;
        return true;
    }
}
