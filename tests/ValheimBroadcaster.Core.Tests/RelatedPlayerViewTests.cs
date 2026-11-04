using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class RelatedPlayerViewTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Related player view pauses dwell and cannot authorize travel", PauseDwell);
        yield return ("Related player view cannot acquire an unseen owner", NoAcquisition);
        yield return ("Related view cannot retain a missing owner or exceed its wall limit", Boundaries);
    }
    private static readonly AutonomyPosition Position = new AutonomyPosition(0, 30, 0);
    private static readonly AutonomyPlayer[] Players = { new AutonomyPlayer("p", "Player", Position, 100, true) };
    private static AutonomousDirector New()
    { var result = new AutonomousDirector { DwellSeconds = 30 }; result.SetEnabled(true); return result; }
    private static void Tick(AutonomousDirector d, double at, bool related, string? presented = "p", AutonomyPlayer[]? players = null) =>
        d.Tick(at, .25, players ?? Players, Array.Empty<ScenicRegion>(), Position, presented, false,
            usablePlayerFootage: !related, relatedPlayerView: related);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void PauseDwell()
    {
        var d = New(); Tick(d, 0, false); Tick(d, .25, false);
        double dwell = d.ReadyDwellSeconds;
        for (int i = 2; i < 12; i++) Tick(d, i * .25, true);
        Check(d.State == AutonomyState.Following && d.TargetId == "p" && d.PendingTravel == null,
            "Related view broke the established story owner");
        Check(d.ReadyDwellSeconds == dwell, "Opponent picture earned player footage");
        Tick(d, 3, false);
        Check(d.ReadyDwellSeconds > dwell, "Actual player footage did not resume credit");
    }
    private static void NoAcquisition()
    {
        var d = New(); Tick(d, 0, true);
        Check(d.State == AutonomyState.Acquiring && d.ReadyDwellSeconds == 0,
            "A related-subject frame falsely acquired its player");
    }
    private static void Boundaries()
    {
        var d = New(); Tick(d, 0, false);
        Tick(d, .25, true); Tick(d, 4.5, true);
        Check(d.State != AutonomyState.Following && d.ReadyDwellSeconds <= .25,
            "An unbounded related view kept the player acquired indefinitely");
        var gone = New(); Tick(gone, 0, false);
        Tick(gone, .25, true, "p", Array.Empty<AutonomyPlayer>());
        Check(gone.TargetId == null && gone.PendingTravel == null,
            "A disconnected player survived as the story owner");
    }
}
