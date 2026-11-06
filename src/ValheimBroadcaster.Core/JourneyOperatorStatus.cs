using System;
using System.Globalization;

namespace ValheimBroadcaster.Core;

/// <summary>Operator facts about a travel mission, independent of whether the
/// programme is a fresh stationary render, a held texture or an ordinary shot.
/// Cruise is a controller setting, not a claim of actual instantaneous speed.</summary>
public readonly struct JourneyOperatorStatus
{
    public bool Visible { get; }
    public string Summary { get; }
    public string Work { get; }
    public string LastRejection { get; }
    public string PictureLabel { get; }

    public JourneyOperatorStatus(bool ownsTravel, JourneyTravelState phase,
        bool journeyPicture, bool replanning, bool held, bool canAdvance,
        double effectiveCruise, double requestedCruise, double measuredMetres, double targetMetres,
        double noProgressSeconds, double freshRunwayMetres, long routeId, int queuedSections,
        string? destination, string? work, string? lastRejection, bool continuousGuide = false)
    {
        Visible = ownsTravel;
        Summary = Work = LastRejection = PictureLabel = "";
        if (!ownsTravel) return;
        bool advancing = Advancing(phase, replanning, held, canAdvance, effectiveCruise, measuredMetres, noProgressSeconds);
        string state = held ? "Operator paused" : replanning ? "Replanning" :
            phase == JourneyTravelState.None || phase == JourneyTravelState.Departure ? "Preparing departure" :
            phase == JourneyTravelState.Arriving ? "Arrival" : advancing ? "Travelling" : "Stationary";
        Summary = targetMetres <= 0 ? state + " | selecting destination" : state + " to " + Text(destination, "selected destination", 48) +
            " | " + Number(measuredMetres) + "/" + Number(targetMetres) + " m" +
            " | cruise " + Number(effectiveCruise) + "/" + Number(requestedCruise) + " m/s" +
            " | no advance " + Number(noProgressSeconds) + "s";
        Work = Text(work, "Awaiting navigation evidence", 150) + (continuousGuide ? "" :
            " | certified " + Number(freshRunwayMetres) + "m; route " + Math.Max(0, routeId).ToString(CultureInfo.InvariantCulture) +
            "; queued " + Math.Max(0, queuedSections).ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(lastRejection) && !string.Equals(lastRejection, "none", StringComparison.OrdinalIgnoreCase) &&
            (!advancing || phase == JourneyTravelState.Departure))
            LastRejection = Text(lastRejection, "", 220);
        PictureLabel = DescribePicture(phase, journeyPicture, replanning, held, canAdvance,
            effectiveCruise, measuredMetres, noProgressSeconds);
    }

    // Capture metadata runs per rendered frame. It needs only this label, not
    // the operator's formatted destination/work/runway strings or queue scan.
    public static string DescribePicture(JourneyTravelState phase, bool journeyPicture, bool replanning,
        bool held, bool canAdvance, double effectiveCruise, double measuredMetres, double noProgressSeconds) =>
        !journeyPicture ? "" : held ? "Journey stationary view (operator paused)" :
        replanning ? "Journey stationary view (replanning)" :
        phase == JourneyTravelState.Arriving ? "Journey arrival" :
        Advancing(phase, replanning, held, canAdvance, effectiveCruise, measuredMetres, noProgressSeconds)
            ? "Journey advancing" : "Journey stationary view";

    private static bool Advancing(JourneyTravelState phase, bool replanning, bool held, bool canAdvance,
        double effectiveCruise, double measuredMetres, double noProgressSeconds) =>
        !held && !replanning && canAdvance && effectiveCruise > .1 &&
        measuredMetres > .01 && noProgressSeconds < .5 && phase == JourneyTravelState.Travelling;

    private static string Number(double value) => double.IsNaN(value) || double.IsInfinity(value)
        ? "unknown" : Math.Max(0, value).ToString("F1", CultureInfo.InvariantCulture);

    private static string Text(string? value, string fallback, int limit)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string text = value!.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return text.Length <= limit ? text : text.Substring(0, limit - 3) + "...";
    }
}
