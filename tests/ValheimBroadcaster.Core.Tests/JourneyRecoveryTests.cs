using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyRecoveryTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey continuation repair retains the same destination and measured progress instead of starting another journey", PreserveProgramme);
        yield return ("A repaired Journey reserve waits for useful readable ending coverage before resuming", UsefulEnding);
        yield return ("An expired ending yields picture authority while the same Journey remains available for local repair", EndingToCoverage);
        yield return ("Local Journey repair never overrides missing picture or revoked ownership", Authority);
        yield return ("Local Journey repair pauses during an operator hold without renewing its own deadline", HoldAndReset);
        yield return ("A changed local observation scene discards old proof identity without resetting Journey recovery time", SceneRebind);
        yield return ("A failed Journey queue tail preserves its certified prefix and cancels dependent preparation", PreserveQueuePrefix);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < .000001, $"Expected {expected}, got {actual}");
    private static JourneyRecoveryPolicy Policy() { var p = new JourneyRecoveryPolicy(); p.Begin(63, 77, 4); return p; }
    private static JourneyRecoveryState Step(JourneyRecoveryPolicy p, double seconds, bool ready = false,
        bool readable = true, bool held = false, bool authority = true) => p.Observe(63, 77, 4, seconds, readable, held, ready, authority);
    private static AutonomyPosition P(float x, float z = 0) => new AutonomyPosition(x, 20, z);
    private static ScenicShotPlan Section(long id, long predecessor, float start) => ScenicShotPlan.Spatial(id,
        new ScenicFeature("sea", ScenicFeatureKind.Coastline, P(start + 10, 20), P(1), 20, 5, 1), ScenicShotRole.Traverse,
        new[] { P(start), P(start + 10), P(start + 20) }, new[] { P(start + 28), P(start + 38), P(start + 48) }, 65)
        .AsJourney(63, predecessor, 20d / 12);

    private static void PreserveProgramme()
    {
        var journey = new ScenicJourneyPolicy(); journey.Start(63, 5000, 12, P(0), P(1, 0));
        var before = Section(75, 72, 0);
        journey.Observe(before, 0, 0, true, false, P(0));
        journey.Observe(before, .05, .03, true, false, P(.6f));
        double distance = journey.TravelMetres, shown = journey.ShownSeconds;
        Check(distance > 0, "The repair fixture did not establish rendered travel");
        var repair = Policy(); journey.BreakPresentation();
        Step(repair, 2); Check(Step(repair, 1.1, true) == JourneyRecoveryState.Resumed, "Certified local repair did not resume");
        Check(journey.Id == 63 && journey.Active && journey.TargetMetres == 5000 && journey.CruiseSpeed == 12,
            "Repair changed the original Journey command");
        Near(journey.TravelMetres, distance); Near(journey.ShownSeconds, shown);
        // The independent local cut gets no distance credit; subsequent shown
        // movement belongs to the original objective and continues its totals.
        var after = Section(90, 89, .6f).AsJourneyDeparture(63, 20d / 12);
        journey.Observe(after, 3.2, 0, true, false, P(.6f));
        Near(journey.TravelMetres, distance);
        journey.Observe(after, 3.25, .03, true, false, P(1.2f));
        Check(journey.TravelMetres > distance && journey.ShownSeconds > shown, "Resumed captured travel did not accumulate");
    }

    private static void UsefulEnding()
    {
        var repair = Policy();
        Check(Step(repair, 1, true) == JourneyRecoveryState.Ready, "A ready route immediately cut the ending");
        Step(repair, 2, true, readable: false);
        Near(repair.ReadableSeconds, 1);
        Check(Step(repair, 1.9, true) == JourneyRecoveryState.Ready, "Missing footage earned readable dwell");
        Check(Step(repair, .2, true) == JourneyRecoveryState.Resumed && !repair.Active, "Useful ending did not release repaired coverage");
        Check(Step(repair, 1, false) == JourneyRecoveryState.Resumed, "Completed recovery restarted itself");
    }

    private static void EndingToCoverage()
    {
        // The supplied 0.8.47 ending was admitted around180.4 and finished
        // around187.1. Keep local preparation alive over that finite pan,
        // rather than turning its normal endpoint into whole-Journey failure.
        var repair = Policy();
        Check(Step(repair, 6.7) == JourneyRecoveryState.Preparing, "Ending completion prematurely discarded the itinerary");
        Check(Step(repair, 2.3) == JourneyRecoveryState.Coverage && repair.Active && !repair.OwnsPreparedEnding,
            "Ending expiry killed the mission or retained picture authority");
        Check(repair.Observe(63, 90, 4, .1, true, false, true, true) == JourneyRecoveryState.Resumed,
            "Fresh independently validated coverage could not resume the original mission");
        var boundary = Policy(); Step(boundary, 9);
        Check(Step(boundary, 10, authority: false) == JourneyRecoveryState.Exhausted,
            "Mission runtime/destination revocation was ignored after yielding picture authority");
    }

    private static void Authority()
    {
        foreach (var change in new[] { (64L, 77L, 4L), (63L, 78L, 4L), (63L, 77L, 5L) })
        {
            var repair = Policy();
            Check(repair.Observe(change.Item1, change.Item2, change.Item3, 3, true, false, true, true) == JourneyRecoveryState.Exhausted,
                "Replacement identity admitted an obsolete local reserve");
        }
        Check(Step(Policy(), 3, true, authority: false) == JourneyRecoveryState.Exhausted, "Revoked authority was ignored");
        var unreadable = Policy();
        Check(Step(unreadable, 8, true, readable: false) == JourneyRecoveryState.Ready, "Reserve with no picture falsely resumed");
        Check(Step(unreadable, 1, true, readable: false) == JourneyRecoveryState.Coverage && !unreadable.OwnsPreparedEnding,
            "Missing picture retained ending authority");
        Check(Step(unreadable, .1, true, readable: false, authority: false) == JourneyRecoveryState.Exhausted,
            "Global missing-output recovery could not terminate an unusable mission");
    }

    private static void HoldAndReset()
    {
        var repair = Policy(); Step(repair, 2); Step(repair, 60, true, held: true);
        Near(repair.ElapsedSeconds, 2); Near(repair.ReadableSeconds, 2);
        bool rejected = false; try { repair.Begin(63, 77, 4); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Repeated Begin silently renewed an active deadline");
        Check(Step(repair, 1.1, true) == JourneyRecoveryState.Resumed, "Unheld repair did not resume");
        repair.Reset(); Check(!repair.Active && repair.State == JourneyRecoveryState.None && repair.Journey == 0 && repair.ElapsedSeconds == 0,
            "Scene cleanup retained local repair authority");
    }

    private static void SceneRebind()
    {
        var repair = Policy(); Step(repair, 4); repair.RebindScene(5);
        Check(repair.Active && !repair.OwnsPreparedEnding && repair.Journey == 63 && repair.Epoch == 5,
            "Local scene refresh reset the mission or retained the old ending");
        Near(repair.ElapsedSeconds, 4); Near(repair.ReadableSeconds, 0);
        Check(repair.Observe(63, 91, 5, 1, true, false, true, true) == JourneyRecoveryState.Coverage,
            "Old-scene dwell admitted a replacement without current readable coverage");
        Check(repair.Observe(63, 91, 5, 2.1, true, false, true, true) == JourneyRecoveryState.Resumed,
            "Current scene proof could not resume the retained mission");
        Check(!JourneyRecoveryPolicy.DifferentSeed(P(0), P(20), P(1), P(21)), "Relabelled near-identical view became a new route search");
        Check(JourneyRecoveryPolicy.DifferentSeed(P(0), P(20), P(5), P(25)), "Different observed local pose could not retry");
        Check(JourneyRecoveryPolicy.DifferentSeed(P(0), P(20), P(0), P(0, 20)), "Different view direction could not retry");
    }

    private sealed class Queued : IDisposable
    {
        internal readonly int Id;
        internal bool Disposed;
        internal Queued(int id) { Id = id; }
        public void Dispose() { Check(!Disposed, "Queue proof disposed twice"); Disposed = true; }
    }

    private static void PreserveQueuePrefix()
    {
        var first = new Queued(1); var failed = new Queued(2); var dependent = new Queued(3);
        var queue = new List<Queued> { first, failed, dependent };
        bool cancelled = false;
        JourneyQueueRetention.RetireFrom(queue, 1, () =>
        {
            Check(!failed.Disposed && !dependent.Disposed, "Dependent preparation outlived invalidation");
            cancelled = true;
        });
        Check(cancelled && queue.Count == 1 && ReferenceEquals(queue[0], first) && !first.Disposed && failed.Disposed && dependent.Disposed,
            "A failed tail discarded the valid prefix or retained descendants");
        JourneyQueueRetention.RetireFrom(queue, 0, () => { });
        Check(queue.Count == 0 && first.Disposed, "Invalid head left a traversable dependent queue");
        cancelled = false; JourneyQueueRetention.RetireFrom(queue, 0, () => cancelled = true);
        Check(cancelled, "An empty queue failed to cancel its in-flight proposal");
    }
}
