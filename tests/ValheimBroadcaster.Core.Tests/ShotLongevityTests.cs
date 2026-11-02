using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ShotLongevityTests
{
    private const PredictedVisibility R = PredictedVisibility.Readable;
    private const PredictedVisibility O = PredictedVisibility.Obstructed;
    private const PredictedVisibility U = PredictedVisibility.Unknown;
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Longevity prefers a durable view over an imminent obstruction", DurableView);
        yield return ("Longevity discounts near obstruction more than distant obstruction", ObstructionTiming);
        yield return ("Unknown future support cannot count as an obstructed view", UnknownSupport);
        yield return ("Partial known forecasts do not invent their unknown continuation", PartialForecast);
        yield return ("Longevity cannot rescue currently unreadable footage", CurrentReadability);
        yield return ("New observations need a coherent course before prediction weighs in", InitialConfidence);
        yield return ("Sudden player reversal immediately invalidates old forecasts", Reversal);
        yield return ("Sudden stopping invalidates moving forecasts", Stop);
        yield return ("Sudden displacement invalidates a forecast with unchanged velocity", Displacement);
        yield return ("Observation gaps cannot reuse earlier motion confidence", ObservationGap);
        yield return ("Forecast freshness expires without candidate refresh", ForecastAge);
        yield return ("Later confidence cannot retroactively strengthen an uncertain forecast", SampleConfidence);
        yield return ("Repeated same-tick observations cannot manufacture confidence", SameTick);
        yield return ("Authored scenic forecasts still require known future geometry", ScenicForecast);
        yield return ("Candidate geometry invalidation is isolated and explicit", CandidateInvalidation);
        yield return ("Reset clears forecast and motion history for a new binding", Reset);
        yield return ("Prediction confidence reduces during measured acceleration", Acceleration);
        yield return ("Longer trajectory coverage preview is bounded and read-only", CoveragePreview);
        yield return ("Blocked trajectories stay blocked during longer coverage previews", BlockedCoveragePreview);
        yield return ("Longevity validates malformed timing and evidence", InvalidInputs);
    }

    private static AutonomyPosition P(float x = 0, float y = 0, float z = 0) => new AutonomyPosition(x, y, z);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static ShotLongevityPolicy Stable(float speed = 5)
    {
        var policy = new ShotLongevityPolicy(3);
        for (int i = 0; i <= 10; i++) policy.ObserveMotion(i * .1, P(i * speed * .1f), P(speed));
        return policy;
    }
    private static void DurableView()
    {
        var p = Stable();
        p.Record(0, 1, O, U, U); p.Record(1, 1, R, R, R);
        float current = p.AdjustScore(0, 1, .95f, true), alternative = p.AdjustScore(1, 1, .82f, true);
        Check(alternative > current + ProceduralShotPolicy.ImprovementMargin,
            "An imminent blocked corridor beat a continuously readable alternate.");
        var selection = new ProceduralShotPolicy();
        selection.Select(0, new[] { .95f, .82f }, new[] { true, true }, new[] { true, true });
        selection.Select(2.1, new[] { current, alternative }, new[] { true, true }, new[] { true, true });
        Check(selection.Select(2.7, new[] { current, alternative }, new[] { true, true }, new[] { true, true }) == 1,
            "Longevity scores failed to drive a sustained pre-obstruction replacement.");
    }
    private static void ObstructionTiming()
    {
        var p = Stable(); p.Record(0, 1, O, U, U); p.Record(1, 1, R, O, U); p.Record(2, 1, R, R, O);
        Check(p.AdjustScore(0, 1, .9f, true) < p.AdjustScore(1, 1, .9f, true) &&
            p.AdjustScore(1, 1, .9f, true) < p.AdjustScore(2, 1, .9f, true),
            "A longer clear interval had no longevity advantage.");
        Check(p.Evidence(2, 1).ReadableSeconds == 1, "Forecast duration crossed the first obstruction.");
    }
    private static void UnknownSupport()
    {
        var p = Stable(); p.Record(0, 1, U, O, O);
        Check(!p.Evidence(0, 1).Available && p.AdjustScore(0, 1, .9f, true) == .9f,
            "Unknown future delivery or foliage was interpreted as a known obstruction.");
    }
    private static void PartialForecast()
    {
        var p = Stable(); p.Record(0, 1, R, U, O);
        var e = p.Evidence(0, 1);
        Check(e.Available && !e.Blocked && e.ReadableSeconds == .5f && p.AdjustScore(0, 1, .9f, true) == .9f,
            "An unknown middle interval certified a later failure or whole-horizon success.");
    }
    private static void CurrentReadability()
    {
        var p = Stable(); p.Record(0, 1, R, R, R); p.Record(1, 1, O, U, U);
        Check(p.AdjustScore(0, 1, .1f, false) == .1f && p.AdjustScore(1, 1, .1f, false) == .1f,
            "Future estimates modified the score of an unreadable present view.");
        Check(p.AdjustScore(0, 1, .9f, true) <= .9f, "A prediction manufactured additional present picture quality.");
    }
    private static void InitialConfidence()
    {
        var p = new ShotLongevityPolicy(1); p.ObserveMotion(0, P(), P(5)); p.Record(0, 0, O, U, U);
        Check(!p.Evidence(0, 0).Available && p.AdjustScore(0, 0, .9f, true) == .9f,
            "First-frame velocity was trusted as an established course.");
    }
    private static void Reversal()
    {
        var p = Stable(); p.Record(0, 1, O, U, U);
        p.ObserveMotion(1.1, P(4.5f), P(-5));
        Check(p.MotionConfidence == 0 && !p.Evidence(0, 1.1).Available,
            "Old forward-course forecasts survived a reversal.");
        p.Record(0, 1.1, O, U, U);
        Check(p.AdjustScore(0, 1.1, .9f, true) == .9f, "Fresh samples bypassed reversal uncertainty.");
    }
    private static void Stop()
    {
        var p = Stable(); p.Record(0, 1, O, U, U); p.ObserveMotion(1.1, P(5), P());
        Check(p.MotionConfidence == 0 && p.AdjustScore(0, 1.1, .9f, true) == .9f,
            "A stop still penalized the view using earlier travel motion.");
    }
    private static void Displacement()
    {
        var p = Stable(); p.Record(0, 1, O, U, U); p.ObserveMotion(1.1, P(50), P(5));
        Check(p.MotionConfidence == 0 && !p.Evidence(0, 1.1).Available,
            "Teleportation retained the old view's lifetime estimate.");
    }
    private static void ObservationGap()
    {
        var p = Stable(); p.Record(0, 1, O, U, U); p.ObserveMotion(2, P(10), P(5));
        Check(p.MotionConfidence == 0 && !p.Evidence(0, 2).Available, "A long observation gap retained prediction confidence.");
    }
    private static void ForecastAge()
    {
        var p = Stable(); p.Record(0, 1, O, U, U);
        for (int i = 11; i <= 17; i++) p.ObserveMotion(i * .1, P(i * .5f), P(5));
        Check(!p.Evidence(0, 1.71).Available && p.AdjustScore(0, 1.71, .9f, true) == .9f,
            "A stale candidate forecast kept affecting a healthy live view.");
    }
    private static void SameTick()
    {
        var p = new ShotLongevityPolicy(1);
        for (int i = 0; i < 100; i++) p.ObserveMotion(0, P(), P(5));
        Check(p.MotionConfidence == 0, "Repeated same-frame calls manufactured a coherent course.");
    }
    private static void SampleConfidence()
    {
        var p = new ShotLongevityPolicy(1);
        p.ObserveMotion(0, P(), P(5)); p.Record(0, 0, O, U, U);
        for (int i = 1; i <= 5; i++) p.ObserveMotion(i * .1, P(i * .5f), P(5));
        Check(p.MotionConfidence >= .4f && !p.Evidence(0, .51).Available &&
            p.AdjustScore(0, .51, .9f, true) == .9f,
            "An uncertain old forecast gained confidence without resampling its geometry.");
    }
    private static void ScenicForecast()
    {
        var p = new ShotLongevityPolicy(2); p.ObserveMotion(0, P(), P(), true);
        p.Record(0, 0, R, O, U); p.Record(1, 0, U, R, R);
        Check(p.MotionConfidence == 1 && p.AdjustScore(0, 0, .9f, true) < .9f &&
            p.AdjustScore(1, 0, .9f, true) == .9f,
            "Authored scenic motion bypassed geometry confidence or was ignored.");
    }
    private static void CandidateInvalidation()
    {
        var p = Stable(); p.Record(0, 1, O, U, U); p.Record(1, 1, R, O, U); p.Invalidate(0);
        Check(!p.Evidence(0, 1).Available && p.Evidence(1, 1).Available,
            "One changed candidate discarded unrelated evidence or retained its old evidence.");
    }
    private static void Reset()
    {
        var p = Stable(); p.Record(0, 1, O, U, U); p.Reset();
        Check(!p.Evidence(0, 0).Available && p.MotionConfidence == 0, "A scene reset retained old evidence.");
    }
    private static void Acceleration()
    {
        var p = Stable(); p.ObserveMotion(1.1, P(5.55f), P(6)); p.Record(0, 1.1, O, U, U);
        Check(p.MotionConfidence > 0 && p.MotionConfidence < .7f,
            "Measured acceleration was treated as a fully predictable constant course.");
    }
    private static void CoveragePreview()
    {
        var p = new ShotTrajectoryPolicy();
        for (int i = 0; i <= 120; i++)
            p.Step(i / 60d, 1d / 60, P(i * .1f), P(i * .1f), P(6), CinematicActivity.Travel,
                ShotTrajectoryKind.Tracking, 1, 8, 0, 1);
        var start = p.Preview(0); var near = p.Preview(.75); var far = p.PreviewCoverage(1.5);
        Check(far.Anchor.X > near.Anchor.X && p.PreviewCoverage(100).Anchor.Distance(far.Anchor) < .0001,
            "Coverage preview did not extend the horizon or failed to bound it.");
        Check(p.Preview(100).Anchor.Distance(near.Anchor) < .0001 && p.Preview(0).Anchor.Distance(start.Anchor) < .0001,
            "Coverage preview changed live state or the original movement preview contract.");
    }
    private static void BlockedCoveragePreview()
    {
        var p = new ShotTrajectoryPolicy();
        p.Step(0, .1, P(), P(), P(5), CinematicActivity.Travel, ShotTrajectoryKind.Tracking, 1, 8, 0, 1);
        p.Synchronize(P(2), true);
        Check(p.PreviewCoverage(1.5).Anchor.Distance(P(2)) < .0001,
            "Blocked trajectory forecast escaped its accepted anchor.");
    }
    private static void InvalidInputs()
    {
        static void Throws(Action action)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Malformed prediction input was accepted.");
        }
        Throws(() => new ShotLongevityPolicy(0)); Throws(() => new ShotLongevityPolicy(33));
        var p = Stable();
        Throws(() => p.Record(3, 1, R, R, R)); Throws(() => p.Record(0, 1, (PredictedVisibility)5, R, R));
        Throws(() => p.AdjustScore(0, 1, float.NaN, true)); Throws(() => p.Evidence(0, .5));
        Throws(() => p.ObserveMotion(double.NaN, P(), P()));
        Check(p.MotionConfidence == 0, "Bad observation time left old confidence active.");
        Throws(() => new ShotTrajectoryPolicy().PreviewCoverage(-1));
    }
}
