using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class WaterClearanceEnvelopeTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey water clearance survives wave phase and normal weather changes", WavesAndWeather);
        yield return ("Journey water envelope retains loaded base offset depth and flat volumes", LoadedVolume);
        yield return ("Journey water envelope rejects unknown measurements instead of inventing sea level", Unknown);
        yield return ("Journey ending retains its route's stable water clearance contract", Ending);
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Near(double a, double b) => Check(Math.Abs(a - b) < .0001, $"Expected {b}, got {a}");
    private static void WavesAndWeather()
    {
        // Independent phase components span the measured native amplitude range.
        // A picture near a trough can pass once and fail as the crest advances:
        // that is the supplied 0.8.47 queued-proof failure, not missing terrain.
        Check(ScenicSurfacePolicy.EvaluateSample(36.5f, true, 5, true, 33, true).Clear, "Trough fixture invalid");
        Check(!ScenicSurfacePolicy.EvaluateSample(36.5f, true, 5, true, 35.3f, true).Clear, "Crest did not invalidate old clearance");
        Check(WaterClearanceEnvelope.TryHeight(30, 0, 1, .1, true, out float upper), "Loaded ocean envelope unavailable");
        float body = upper + ScenicSurfacePolicy.BodyWaterClearance + .5f;
        double[] amplitudes = { 8, 6, 4, 2, 1, .8, .6, .4, .3, .2 };
        for (int phase = 0; phase < 1000; phase++)
        for (int wind = 0; wind <= 10; wind++)
        {
            double sum = 0;
            for (int i = 0; i < amplitudes.Length; i++)
            {
                double a = .5 + .5 * Math.Sin(phase * .13 + i * .61);
                double b = .5 + .5 * Math.Sin(phase * .031 - i * .17);
                sum += (a * b - .2) * amplitudes[i];
            }
            float water = (float)(30 + sum * wind / 10d);
            Check(water <= upper && ScenicSurfacePolicy.EvaluateSample(body, true, 5, true, water, true).Clear,
                "A changing supported wave/weather phase invalidated the prepared envelope");
        }
        // The independently attainable all-crest bound must also fit, not just
        // a selected set of sinusoidal observations.
        Check(30 + 23.3 * .8 <= upper, "Envelope omitted native wave amplitudes");
        Check(WaterClearanceEnvelope.TryHeight(30, 0, 1, 1, true, out float storm) && storm == upper,
            "Ordinary weather change moved the planned surface");
    }
    private static void LoadedVolume()
    {
        Check(WaterClearanceEnvelope.TryHeight(42, 3, .25, 1, true, out float shallow), "Shallow volume rejected");
        Near(shallow, 45 + 18.64 * .25 + .05);
        Check(WaterClearanceEnvelope.TryHeight(42, -2, 1, 1, false, out float flat), "Flat volume rejected"); Near(flat, 40);
        Check(WaterClearanceEnvelope.TryHeight(30, 0, 0, 1, true, out float dry), "Dry-depth volume rejected"); Near(dry, 30);
        Check(WaterClearanceEnvelope.TryHeight(30, 0, 2, 2, true, out float stronger), "Observed stronger wind rejected");
        Near(stronger, 30 + 18.64 * 2 + .05);
    }
    private static void Unknown()
    {
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Check(!WaterClearanceEnvelope.TryHeight(bad, 0, 1, 1, true, out _), "Unknown base accepted");
            Check(!WaterClearanceEnvelope.TryHeight(30, bad, 1, 1, true, out _), "Unknown offset accepted");
            Check(!WaterClearanceEnvelope.TryHeight(30, 0, bad, 1, true, out _), "Unknown depth accepted");
            Check(!WaterClearanceEnvelope.TryHeight(30, 0, 1, bad, true, out _), "Unknown wind accepted");
        }
    }
    private static void Ending()
    {
        var body = new[] { new AutonomyPosition(0, 52, 0), new AutonomyPosition(16, 52, 0), new AutonomyPosition(32, 52, 0) };
        var aim = new[] { new AutonomyPosition(20, 50, 0), new AutonomyPosition(36, 50, 0), new AutonomyPosition(52, 50, 0) };
        var feature = new ScenicFeature("ocean", ScenicFeatureKind.Coastline, aim[1], new AutonomyPosition(1, 0, 0), 12, 5, 1);
        var section = ScenicShotPlan.Spatial(2, feature, ScenicShotRole.Traverse, body, aim, 75).AsJourney(1, 1, 3);
        var ending = ScenicJourneyEnding.Propose(section, 3, 1);
        Check(ending != null && ending.StableWaterClearance && !ending.IsJourney,
            "Finite ending forgot the future surface used to admit its seam");
        Check(ending!.RaiseCamera(1).StableWaterClearance, "Fitting discarded the ending surface contract");
    }
}
