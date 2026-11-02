using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicReadinessTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Scenic continuation requires an actually presented matching plan", InitialAndIdentity);
        yield return ("Scenic readiness queries cannot prolong a stalled render", BoundedEvidence);
        yield return ("Scenic continuation rejects relocated focus and invalid clocks", Discontinuity);
        yield return ("Scenic readiness reset revokes the prior region binding", Reset);
    }
    private static AutonomyPosition P(float x = 0) => new AutonomyPosition(x, 30, 0);
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void InitialAndIdentity()
    {
        var policy = new ScenicReadinessPolicy();
        Check(!policy.CanContinue(1, P(), 5), "An unshown plan bypassed acquisition.");
        policy.Presented(1, P(), 5);
        Check(policy.CanContinue(1, P(.2f), 5.1), "A current scenic plan could not continue.");
        Check(!policy.CanContinue(2, P(), 5.1), "Replacement inherited the outgoing plan's proof.");
        Check(policy.CanContinue(1, P(), 5.1), "A standby query erased the live plan.");
    }
    private static void BoundedEvidence()
    {
        var policy = new ScenicReadinessPolicy(); policy.Presented(1, P(), 5);
        for (int i = 0; i <= 7; i++) Check(policy.CanContinue(1, P(), 5 + i * .1), "Recent proof unexpectedly expired.");
        Check(!policy.CanContinue(1, P(), 5.8), "Queries renewed stale live evidence.");
        policy.Presented(1, P(.2f), 6);
        Check(policy.CanContinue(1, P(.3f), 6.1), "Fresh acquired footage did not rearm continuation.");
    }
    private static void Discontinuity()
    {
        var policy = new ScenicReadinessPolicy(); policy.Presented(1, P(), 5);
        Check(!policy.CanContinue(1, P(17), 5.1), "A discontinuous focus inherited continuation.");
        Check(!policy.CanContinue(1, P(), 4.9) && !policy.CanContinue(1, P(), double.NaN), "Invalid clock admitted continuation.");
        try { policy.Presented(1, P(), 4.9); throw new Exception("Backwards proof accepted."); }
        catch (ArgumentException) { }
        Check(!policy.CanContinue(1, P(), 5.1), "Invalid proof left old readiness active.");
    }
    private static void Reset()
    {
        var policy = new ScenicReadinessPolicy(); policy.Presented(7, P(), 5); policy.Reset();
        Check(!policy.CanContinue(7, P(), 5.1), "A new world/region kept old readiness.");
        policy.Presented(8, P(100), 8);
        Check(policy.CanContinue(8, P(101), 8.1) && !policy.CanContinue(7, P(), 8.1), "New plan binding failed.");
    }
}
