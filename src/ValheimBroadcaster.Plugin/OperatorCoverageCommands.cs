using System;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

public sealed partial class BroadcasterPlugin
{
    private void ModeCommand(string[] args, Terminal context)
    {
        const string usage = "broadcast mode auto|scenic|player|creature|status";
        if (args.Length != 2) { context?.AddString(usage); return; }
        if (args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        { context?.AddString(ModeStatus); return; }
        if (!CoveragePreferences.TryParse(args[1], out CoveragePreference preference))
        { context?.AddString(usage); return; }
        if (!director.IsEnabled || selfTest || probe.Active || !Player.m_localPlayer ||
            remote.Isolated && !remote.Approved)
        { context?.AddString("Start normal broadcasting first; mode selection is unavailable during self-follow or a probe."); return; }
        SelectCoverage(preference);
        context?.AddString(ModeStatus);
        if (observer.Phase == ObserverPhase.Travelling)
            context?.AddString("Current travel will finish normally; the requested coverage applies on arrival.");
    }

    private void SelectCoverage(CoveragePreference preference)
    {
        bool changed = coverage.Preference != preference;
        if (changed)
        {
            coverage.Preference = preference;
            wildlife.Reset();
            playerShots.Reset(); creatureAdmission.Reset();
            // Rebase once when explicitly leaving Player coverage. Keep an
            // existing Scenic picture and its scene observations when possible.
            if (!CoveragePreferences.AllowsPlayers(preference) && coverage.Mode == CoverageMode.Player &&
                Player.m_localPlayer && (!observer.Active || observer.Phase == ObserverPhase.Watching))
                idle.SetRegion(Player.m_localPlayer.transform.position);
            if (preference == CoveragePreference.Player || preference == CoveragePreference.Creature)
            {
                cameraFixtures?.CancelPending("Operator selected " + preference + " coverage", idle);
                idle.StopFixture(); idle.StopJourney();
                StopLiveTimelapse("Operator selected " + preference + " coverage");
            }
            nextCreatureRefresh = nextRosterRefresh = 0;
            ScenicTrace.Event("operator-mode", preference.ToString());
        }
        // Mode selection resumes direction after the previous manual-workaround
        // commands. It does not stop/restart RTMP or an active native teleport.
        if (observer.RemoteSession && remote.Approved && automatic != null)
        {
            if (automatic.RouteActive) automatic.StopRoute();
            if (!automatic.ControlsSelection) automatic.Enable(true);
        }
        SetHolding(false);
        RefreshRoster();
    }

    private string ModeStatus => "Requested mode: " + coverage.Preference + " | actual coverage: " + coverage.Mode +
        " | " + CoveragePreferences.Describe(coverage.Preference, coverage.Mode) +
        (observer.RemoteSession ? " | " + (automatic?.ControlsSelection == true
            ? settings.ScenicTravel.Value ? "Automatic acquisition/travel enabled" : "Automatic direction enabled; scenic travel disabled in settings"
            : "Automatic acquisition/travel paused") : " | Local coverage; no automatic remote travel");
}
