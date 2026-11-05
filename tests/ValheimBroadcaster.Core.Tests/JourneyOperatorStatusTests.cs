using System;
using System.Collections.Generic;
using System.Globalization;
using ValheimBroadcaster.Core;

internal static class JourneyOperatorStatusTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Captured Journey repair reports stationary travel and work despite a fresh retained picture", RecordedRepair);
        yield return ("Journey advancement labels require actual recent progress rather than a nonzero cruise setting", ActualProgress);
        yield return ("Ordinary fallback changes picture ownership without hiding Journey repair", Fallback);
        yield return ("Journey hold and arrival distinguish mission intent from held output textures", HoldAndArrival);
        yield return ("New Journey selection cannot claim a prior destination or zero-distance travel", Pending);
        yield return ("Journey operator numbers stay stable across locales and bound external text", Formatting);
    }

    private static JourneyOperatorStatus View(JourneyTravelState phase = JourneyTravelState.Travelling,
        bool owns = true, bool picture = true, bool repair = false, bool held = false, bool canAdvance = true,
        double speed = 6, double distance = 20, double noProgress = 0, double runway = 35,
        string? work = "Preparing continuation", string? rejection = "none", double target = 1000) =>
        new JourneyOperatorStatus(owns, phase, picture, repair, held, canAdvance, speed, 12,
            distance, target, noProgress, runway, 18, 0, "BlackForest", work, rejection);

    private static void RecordedRepair()
    {
        // Observed Journey13/plan18 at162.6857757568s. A current live render
        // remained available, with no movement since125.15865s and plan29 staged.
        var view = View(JourneyTravelState.Replanning, repair: true, canAdvance: false,
            speed: 0, distance: 34.586807262476135, noProgress: 37.527122497558594,
            runway: 0, target: 1992.5841478058442,
            work: "Replanning the same Journey: Fitting scenic route above loaded terrain and water",
            rejection: "Continuation unavailable: Journey corridor search expired; last evidence: Scenic feature is outside the frame or hidden");
        Check(view.Visible && view.Summary.Contains("Replanning to BlackForest"));
        Check(view.Summary.Contains("34.6/1992.6 m") && view.Summary.Contains("cruise 0.0/12.0 m/s"));
        Check(view.Summary.Contains("no advance 37.5s") && view.Work.Contains("certified 0.0m; route 18"));
        Check(view.LastRejection.Contains("Scenic feature is outside the frame or hidden"));
        Check(view.PictureLabel == "Journey stationary view (replanning)");
        Check(!view.PictureLabel.Contains("HELD") && !view.PictureLabel.Contains("moving route"));
    }

    private static void ActualProgress()
    {
        Check(View().PictureLabel == "Journey advancing");
        Check(View(noProgress: 1).PictureLabel == "Journey stationary view");
        Check(View(canAdvance: false).PictureLabel == "Journey stationary view");
        Check(View(speed: 0).PictureLabel == "Journey stationary view");
        Check(View(distance: 0).PictureLabel == "Journey stationary view");
        Check(View(noProgress: 1, rejection: "Earlier failed route").LastRejection.Length > 0);
        Check(View(rejection: "Earlier failed route").LastRejection.Length == 0,
            "Historical rejection presented as an active advancing-route blocker");
    }

    private static void Fallback()
    {
        var fallback = View(JourneyTravelState.Replanning, repair: true, picture: false,
            canAdvance: false, speed: 0, noProgress: 60);
        Check(fallback.Visible && fallback.Summary.Contains("Replanning"));
        Check(fallback.PictureLabel.Length == 0, "Ordinary fallback mislabeled as a stationary Journey frame");
        var inactive = View(owns: false);
        Check(!inactive.Visible && inactive.Summary.Length == 0 && inactive.PictureLabel.Length == 0);
    }

    private static void HoldAndArrival()
    {
        var held = View(held: true);
        Check(held.Summary.StartsWith("Operator paused") && held.PictureLabel.Contains("operator paused"));
        var arrival = View(JourneyTravelState.Arriving, speed: 0, noProgress: 5);
        Check(arrival.Summary.StartsWith("Arrival") && arrival.PictureLabel == "Journey arrival");
    }

    private static void Pending()
    {
        var pending = View(JourneyTravelState.Departure, picture: false, distance: 0, target: 0, speed: 0);
        Check(pending.Summary == "Preparing departure | selecting destination");
        Check(!pending.Summary.Contains("BlackForest") && !pending.Summary.Contains("0.0/0.0"));
    }

    private static void Formatting()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var view = View(noProgress: 37.5, work: "line1\nline2\rline3\t" + new string('a', 400),
                rejection: new string('b', 400));
            Check(view.Summary.Contains("37.5s"));
            Check(!view.Work.Contains("\n") && !view.Work.Contains("\r") && !view.Work.Contains("\t"));
            Check(view.Work.Length <= 210 && view.LastRejection.Length <= 220);
            Check(View(speed: double.NaN).Summary.Contains("cruise unknown/12.0"));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    private static void Check(bool value, string reason = "Journey operator status did not reflect observed state")
    { if (!value) throw new Exception(reason); }
}
