using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class JourneyHandoffTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Journey endpoint gives a developing finite ending instead of the reported 32.7-second frozen frame", EndpointRegression);
        yield return ("Ordinary exit coverage survives every Journey admission and is ready when its ending completes", PreparedExitSurvives);
        yield return ("A successor appearing after braking commitment cannot accelerate the camera again", LateSuccessor);
        yield return ("Journey braking remains monotonic and reaches its prepared ending in bounded time", BoundedBraking);
        yield return ("A stalled or slowly rendered Journey ending expires without a 32-second endpoint hold", EndingDeadline);
        yield return ("A timely route successor preserves travel but an observed destination commits to its ending", SuccessorAndArrival);
        yield return ("Journey route and ending proofs expire by age or scene change and reject future or nonfinite time", ProofFreshness);
        yield return ("A polished partial Journey ending never turns 145 metres into a completed kilometre", PartialEndingIsNotArrival);
    }

    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static AutonomyPosition P(float x, float y = 20, float z = 0) => new AutonomyPosition(x, y, z);
    private static ScenicShotPlan Section(long id = 20) => ScenicShotPlan.Spatial(id,
        new ScenicFeature("headland", ScenicFeatureKind.Coastline, P(58, 17, 9), P(1, 0), 20, 5, .9f),
        ScenicShotRole.Traverse, new[] { P(0), P(24), P(48) },
        new[] { P(30, 17, 9), P(54, 17, 9), P(78, 17, 9) }, 66, .08f, -.04f)
        .AsJourney(19, 11, 6).WithJourneyRamps(false, false);

    private static void EndpointRegression()
    {
        const double endpointAt = 113.8937;
        foreach (int fps in new[] { 30, 60 })
        {
            ScenicShotPlan section = Section();
            ScenicShotPlan ending = ScenicJourneyEnding.Propose(section, 21, 1)!;
            var director = new ScenicDirector(); director.Accept(section, endpointAt - 6);
            director.Observe(section.Id, endpointAt, 1, true, true, false);
            Check(director.NeedsReplacement, "Completed section did not release its route clock.");
            director.Accept(ending, endpointAt);
            director.Observe(ending.Id, endpointAt, 0, true, true, false);
            double elapsed = 0;
            while (!JourneyHandoffPolicy.EndingComplete(elapsed, director.Progress))
            {
                elapsed += 1d / fps;
                double proposed = director.Propose(1d / fps);
                director.Observe(ending.Id, endpointAt + elapsed, proposed, true, true, false);
                if (elapsed > .1 && elapsed < 5.5)
                    Check(ending.Sample(director.Progress).AimPosition.Distance(ending.Sample(0).AimPosition) > .0001,
                        "Ending kept the final route frame static while preparing its successor.");
                Check(elapsed < 10, "Journey retained its endpoint while waiting for unavailable continuation.");
            }
            Check(elapsed >= 5.9 && elapsed <= 6.1 && endpointAt + elapsed < 146.6283,
                "Finite ending reproduced the captured 32.7-second endpoint hold.");
        }
    }

    private static void PreparedExitSurvives()
    {
        ScenicShotPlan section = Section();
        ScenicShotPlan ordinary = Section(30).AsStandalone();
        ScenicShotPlan ending = ScenicJourneyEnding.Propose(section, 21, 1)!;
        var director = new ScenicDirector(); director.Accept(Section(10).AsStandalone(), 0);
        director.Stage(ordinary);
        director.Accept(section, 10);
        Check(director.Prepared == ordinary, "Journey departure consumed ordinary exit preparation.");
        director.Accept(ending, 16);
        Check(director.Prepared == ordinary, "Journey ending consumed ordinary exit preparation.");
        director.Observe(ending.Id, 16, 0, true, true, false);
        for (int i = 1; i <= 181; i++)
            director.Observe(ending.Id, 16 + i / 30d, director.Propose(1d / 30), true, true, false);
        Check(JourneyHandoffPolicy.EndingComplete(181d / 30, director.Progress), "Ending did not yield.");
        ScenicShotPlan exit = director.TakePrepared()!;
        Check(exit == ordinary, "Validated ordinary exit vanished before the ending completed.");
        director.Accept(exit, 22.1);
        Check(director.Current == ordinary && !director.Current.IsJourney && director.Propose(1d / 30) == 0,
            "Exit skipped its exact first frame or retained Journey ownership.");
    }

    private static void LateSuccessor()
    {
        foreach (double speed in new[] { 2d, 6, 12 })
        {
            double remaining = JourneyHandoffPolicy.BrakingDistance(speed);
            bool committed = JourneyHandoffPolicy.ShouldBrake(false, remaining, speed, false, false);
            Check(committed, "Missing successor did not commit before the endpoint.");
            for (int i = 0; i < 30; i++)
            {
                remaining *= .9;
                committed = JourneyHandoffPolicy.ShouldBrake(committed, remaining, speed, true, false);
                Check(committed, "Late readiness cancelled braking and allowed a camera speed surge.");
            }
        }
    }

    private static void BoundedBraking()
    {
        foreach (double speed in new[] { 2d, 6, 12 })
        foreach (int fps in new[] { 30, 60 })
        {
            double remaining = JourneyHandoffPolicy.BrakingDistance(speed), elapsed = 0, previousSpeed = speed;
            while (remaining > .00001)
            {
                double velocity = speed * JourneyHandoffPolicy.BrakingScale(remaining, speed);
                Check(velocity > 0 && velocity <= previousSpeed + .00001, "Braking stalled or accelerated.");
                remaining = Math.Max(0, remaining - velocity / fps); elapsed += 1d / fps; previousSpeed = velocity;
                Check(elapsed < 5, "Braking asymptotically retained the final route frame.");
            }
            Check(elapsed > 3 && previousSpeed <= speed * .11, "Route ended at cruise speed without a meaningful slowdown.");
        }
    }

    private static void EndingDeadline()
    {
        foreach (int fps in new[] { 10, 30, 60 })
        {
            double elapsed = 0;
            while (!JourneyHandoffPolicy.EndingComplete(elapsed, .4))
            {
                elapsed += 1d / fps;
                Check(elapsed < 9.2, "Stalled output indefinitely prolonged Journey ownership.");
            }
            Check(elapsed >= 9 && elapsed <= 9.11, "Ending deadline did not allow its bounded finishing interval.");
        }
        Check(JourneyHandoffPolicy.EndingComplete(6, 1), "A fully presented ending waited unnecessarily.");
    }

    private static void SuccessorAndArrival()
    {
        foreach (double speed in new[] { 2d, 6, 12 })
        {
            double approach = JourneyHandoffPolicy.BrakingDistance(speed);
            Check(!JourneyHandoffPolicy.ShouldBrake(false, approach, speed, true, false),
                "Timely validated continuation forced an unnecessary stop.");
            Check(!JourneyHandoffPolicy.ShouldBrake(false, approach + 1, speed, false, false),
                "A speculative loading wait stopped the route before its actual braking point.");
            Check(JourneyHandoffPolicy.ShouldBrake(false, approach, speed, true, true),
                "A ready continuation overrode the selected destination arrival.");
        }
    }

    private static void ProofFreshness()
    {
        Check(JourneyHandoffPolicy.IsProofCurrent(7, 100, 7, 111.99), "Recent matching proof was discarded.");
        Check(!JourneyHandoffPolicy.IsProofCurrent(7, 100, 7, 112.01), "An old proof authorized a later journey cut.");
        Check(!JourneyHandoffPolicy.IsProofCurrent(7, 100, 8, 101), "A scene reset retained its old ending certificate.");
        Check(!JourneyHandoffPolicy.IsProofCurrent(0, 100, 0, 101), "An uninitialized observation epoch authorized a cut.");
        Check(!JourneyHandoffPolicy.IsProofCurrent(7, 100, 7, 99), "Future-dated proof authorized a cut.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1 })
        {
            Check(!JourneyHandoffPolicy.IsProofCurrent(7, invalid, 7, 101), "Invalid proof time was accepted.");
            Check(!JourneyHandoffPolicy.IsProofCurrent(7, 100, 7, invalid), "Invalid admission time was accepted.");
        }
    }

    private static void PartialEndingIsNotArrival()
    {
        var journey = new ScenicJourneyPolicy(); journey.Start(19, 1000, 6, P(0), P(1, 0));
        ScenicShotPlan section = Section(), ending = ScenicJourneyEnding.Propose(section, 21, 1)!;
        for (int frame = 0; frame <= 290; frame++)
            journey.Observe(section, frame / 12d, frame / 300d, true, false, P(frame * .5f));
        double beforeEnding = journey.TravelMetres;
        for (int frame = 1; frame <= 72; frame++)
        {
            journey.Observe(ending, (290 + frame) / 12d, frame / 72d, true, false, P(145));
            journey.ObserveArrival((290 + frame) / 12d, true, false, P(145), false);
        }
        Check(beforeEnding == 145 && journey.TravelMetres == 145 && !journey.TargetReached,
            "A finite abort ending invented unpresented route distance or destination success.");
        journey.Finish(ScenicJourneyOutcome.Completed, "Ending finished");
        Check(journey.Outcome == ScenicJourneyOutcome.Blocked, "A polished partial programme was mislabeled as arrival.");
    }
}
