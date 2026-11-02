using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ScenicPresentationTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("A readable stationary scenic picture never requests replacement", ReadableHold);
        yield return ("Initial scenic composition settling cannot cause one-second plan churn", Settling);
        yield return ("Missing live scenic output retains fast bounded recovery", MissingOutput);
        yield return ("Recovered scenic picture cancels obsolete obstruction search", RecoveredSearch);
        yield return ("Scheduled scenic novelty search survives healthy presentation", ScheduledSearch);
        yield return ("Scheduled search exhaustion escalates only after independent picture failure", ScheduledPictureFailure);
        yield return ("A failed scheduled search still honors missing-output recovery timing", ScheduledMissingOutput);
        yield return ("Only exhausted picture recovery marks a scenic region exhausted", Exhaustion);
        yield return ("Held images and observation gaps earn no scenic shown history", PresentationEvidence);
        yield return ("Replacement scenic plan cannot inherit failed picture evidence", ReplacementReset);
        yield return ("Scenic search pause discards path-time debt", ProgressPause);
        yield return ("Retained scenic plan never rewinds at coverage-clock reset", ProgressReset);
        yield return ("Changing scenic dwell alters future speed without repositioning", DurationChange);
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(double a, double b, string reason) => Check(Math.Abs(a - b) < .00001, reason);
    private static ScenicPresentationPolicy Plan() { var p = new ScenicPresentationPolicy(); p.Accept(0); return p; }
    private static void Observe(ScenicPresentationPolicy p, int start, int end, bool live, bool readable)
    { for (int i = start; i <= end; i++) p.Observe(i / 10d, live, readable); }

    private static void ReadableHold()
    {
        var p = Plan(); Observe(p, 0, 600, true, true);
        Check(!p.ReplacementRequested && !p.RecoveryExhausted, "No camera translation was mistaken for missing footage.");
        Near(p.ShownSeconds, 60, "A clear held world position lost actual coverage credit.");
    }
    private static void Settling()
    {
        var p = Plan(); Observe(p, 0, 10, true, false);
        Check(!p.ReplacementRequested, "Live initial composition was discarded after one second.");
        Observe(p, 11, 19, true, true);
        Check(!p.ReplacementRequested, "Recovered initial composition inherited a failure.");
        Observe(p, 20, 39, true, false);
        Check(!p.ReplacementRequested, "Soft picture failure ignored its persistence budget.");
        p.Observe(4, true, false);
        Check(p.ReplacementRequested && !p.Scheduled, "Persistent poor scenery never requested recovery.");
    }
    private static void MissingOutput()
    {
        var p = Plan(); Observe(p, 0, 3, false, true);
        Check(!p.ReplacementRequested, "A single absent frame caused a new plan.");
        p.Observe(.4, false, true);
        Check(p.ReplacementRequested && !p.Readable, "A held framebuffer was accepted as live scenery.");
    }
    private static void RecoveredSearch()
    {
        var p = Plan(); Observe(p, 0, 20, true, false);
        Check(p.ReplacementRequested, "Recovery setup failed.");
        Check(p.Observe(2.1, true, true), "Recovered current plan did not cancel obsolete search.");
        Check(!p.ReplacementRequested && !p.RecoveryExhausted, "Recovered plan kept its replacement request.");
        Check(!p.Observe(2.2, true, true), "Recovery cancellation repeated every frame.");
    }
    private static void ScheduledSearch()
    {
        var p = Plan(); p.ScheduleReplacement(); Observe(p, 0, 20, true, true);
        Check(p.Scheduled && p.ReplacementRequested, "Healthy old picture cancelled intended fresh scenery.");
        p.SearchCompleted(false);
        Check(!p.RecoveryExhausted, "Exhausted novelty was treated as an unavailable world.");
    }
    private static void Exhaustion()
    {
        var p = Plan(); Observe(p, 0, 20, true, false);
        p.SearchCompleted(false);
        Check(p.RecoveryExhausted, "Failed bounded recovery did not expose exhaustion.");
        Observe(p, 21, 30, true, false);
        Check(p.RecoveryExhausted, "Another recovery survey cleared the failure evidence.");
        p.Observe(3.1, true, true);
        Check(!p.RecoveryExhausted, "Actual restored picture could not clear exhaustion.");
    }
    private static void ScheduledPictureFailure()
    {
        var p = Plan(); Observe(p, 0, 20, true, true); p.ScheduleReplacement();
        Observe(p, 21, 30, true, false); p.SearchCompleted(false);
        Check(!p.RecoveryExhausted, "Scheduled exhaustion bypassed the soft picture failure budget.");
        Observe(p, 31, 42, true, false);
        Check(p.RecoveryExhausted && p.Scheduled, "Scheduled label trapped coverage on a failed picture after its search was exhausted.");
        p.Observe(4.3, true, true);
        Check(!p.RecoveryExhausted && p.ReplacementRequested && p.Scheduled,
            "Recovered picture did not clear exhaustion while retaining the scheduled novelty request.");
    }
    private static void ScheduledMissingOutput()
    {
        var p = Plan(); Observe(p, 0, 20, true, true); p.ScheduleReplacement();
        Observe(p, 21, 23, false, false); p.SearchCompleted(false);
        Check(!p.RecoveryExhausted, "Scheduled search treated a short output gap as exhausted recovery.");
        Observe(p, 24, 26, false, false);
        Check(p.RecoveryExhausted, "A missing picture remained available merely because replacement was scheduled.");
        p.Accept(3);
        Check(!p.RecoveryExhausted && !p.Scheduled, "New accepted plan retained the scheduled failure.");
    }
    private static void PresentationEvidence()
    {
        var p = Plan(); Observe(p, 0, 10, true, true);
        p.Observe(1.1, false, true); Near(p.ShownSeconds, 0, "Held image retained sustained live history.");
        p.Observe(8, true, true); Near(p.ShownSeconds, 0, "Missing observations counted as shown seconds.");
        p.Observe(8.1, true, true); Near(p.ShownSeconds, .1, "Fresh consecutive output lost history.");
    }
    private static void ReplacementReset()
    {
        var p = Plan(); Observe(p, 0, 20, true, false); p.SearchCompleted(false); p.Accept(3);
        Check(!p.ReplacementRequested && !p.RecoveryExhausted && p.ShownSeconds == 0,
            "Accepted replacement inherited failed plan lifetime.");
        p.Observe(3.1, true, false);
        Check(!p.ReplacementRequested, "Fresh replacement was immediately rejected.");
    }
    private static void ProgressPause()
    {
        var p = Plan(); Near(p.AdvancePlan(1, 10, false), .1, "Initial progress failed.");
        Near(p.AdvancePlan(5, 10, true), .1, "Recovery search moved retained path.");
        Near(p.AdvancePlan(6, 10, false), .2, "Resuming repaid frozen search time in a camera surge.");
    }
    private static void ProgressReset()
    {
        var p = Plan(); p.AdvancePlan(8, 10, false);
        Near(p.AdvancePlan(0, 10, true), .8, "New coverage interval rewound retained path.");
        Near(p.AdvancePlan(1, 10, false), .9, "Retained path could not continue after interval reset.");
        p.Accept(5); Near(p.AdvancePlan(0, 10, false), 0, "New accepted path inherited old progress.");
    }
    private static void DurationChange()
    {
        var p = Plan(); p.AdvancePlan(4, 10, false);
        Near(p.AdvancePlan(4, 20, false), .4, "Changing dwell repositioned current picture.");
        Near(p.AdvancePlan(6, 20, false), .5, "New dwell did not affect only future increments.");
    }
}
