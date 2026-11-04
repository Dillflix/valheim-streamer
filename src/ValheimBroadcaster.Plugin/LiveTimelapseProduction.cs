using System;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

public sealed partial class BroadcasterPlugin
{
    private readonly LiveScenicTimelapse liveTimelapse = new LiveScenicTimelapse();
    private double nextTimelapseSurvey;
    private long timelapseWorld;
    private string timelapseDetail = "Awaiting an outdoor composition";
    private string LiveTimelapseStatus => "Live timelapse: " + liveTimelapse.Reason + "; " + timelapseDetail;

    private void StopLiveTimelapse(string reason)
    {
        if (liveTimelapse.Active && idle.FixtureOwnerId == liveTimelapse.PlanId) idle.StopFixture();
        liveTimelapse.End(Time.realtimeSinceStartupAsDouble, reason);
        scenicEnvironment?.SetTimelapse(null);
        if (scenicEnvironment != null) scenicEnvironment.AutomaticPermissionRequested = false;
    }

    private void UpdateLiveTimelapse()
    {
        if (scenicEnvironment == null) return;
        double now = Time.realtimeSinceStartupAsDouble;
        long world = WorldIdentity.Current;
        if (timelapseWorld != world) { StopLiveTimelapse("World changed"); timelapseWorld = world; liveTimelapse.Reset(now); }
        bool context = settings.LiveTimelapse.Value && enabled && director.IsEnabled && !selfTest && !probe.Active &&
            settings.ProceduralPlayers.Value && settings.DynamicShots.Value && settings.ShotMotion.Value > 0 && observer.Active && observer.Phase == ObserverPhase.Watching &&
            activeWorld == ZNet.instance && Player.m_localPlayer && coverage.Mode == CoverageMode.Scenic &&
            (!remote.Isolated || remote.Approved) && scenicEnvironment.CanAutomatic;
        scenicEnvironment.AutomaticPermissionRequested = context;
        bool permitted = context && scenicEnvironment.Permitted(now);
        bool live = cameraReady && Time.frameCount - presentedFrame <= 2 &&
            frameHold.CurrentPresentation == FramePresentation.Live;
        long owned = liveTimelapse.PlanId;
        if (owned > 0)
        {
            liveTimelapse.Observe(now, permitted, idle.FixtureOwnerId, presentedScenicPlan,
                presentedScenicProgress, live && presentedIdleUsable && presentedMode == CoverageMode.Scenic && presentedRevision == coverage.Revision && !presentedScenicBlocked, director.IsHolding);
            if (!liveTimelapse.Active)
            {
                if (idle.FixtureOwnerId == owned) idle.StopFixture();
                scenicEnvironment.SetTimelapse(null);
            }
            else scenicEnvironment.SetTimelapse(liveTimelapse.OnAir ? liveTimelapse.Progress : (double?)null);
            return;
        }
        scenicEnvironment.SetTimelapse(null);
        bool available = permitted && live && presentedIdleUsable && presentedMode == CoverageMode.Scenic && presentedRevision == coverage.Revision && !director.IsHolding && !idle.JourneyActive && !idle.FixtureActive &&
            cameraFixtures?.PendingPlayback != true;
        if (!liveTimelapse.Opportunity(now, available))
        {
            timelapseDetail = !context ? "Disabled or another coverage/environment owner is active" : !permitted ?
                remote.EnvironmentStatus : "Spacing timelapses among ordinary Scenic shots";
            return;
        }
        ScenicShotPlan? source = idle.Plan;
        if (!LiveScenicTimelapse.Suitable(source) || presentedScenicPlan != source!.Id || presentedScenicProgress >= .65f)
        { timelapseDetail = "Waiting for an early, readable coast, ridge or clearing shot"; return; }
        if (now < nextTimelapseSurvey) return;
        if (!CameraWorkBudget.Shared.TryBegin(CameraWorkKind.Scenic, out var work)) return;
        using (work)
        {
            nextTimelapseSurvey = now + 2;
            // Restrict the already observed route to gentle travel. Full native
            // preflight still screens the regenerated spline before admission.
            double start = Math.Max(0, presentedScenicProgress);
            double end = Math.Min(1, start + Math.Min(.4, 6 / Math.Max(1, source.TravelMetres)));
            ScenicShotPlan candidate = source.Slice(start, end).AsStandalone().WithDuration(settings.LiveTimelapseSeconds.Value);
            if (candidate.TravelMetres > 8) { timelapseDetail = "Route is too expansive for restrained timelapse movement"; return; }
            for (int i = 0; i < 3; i++)
            {
                var p = candidate.Sample(i * .5).CameraPosition;
                if (Physics.Raycast(new Vector3(p.X, p.Y, p.Z), Vector3.up, 60, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                { timelapseDetail = "Roof or canopy limits visible lighting change; seeking an open view"; return; }
            }
            if (!idle.RequestFixture(candidate, out string reason, 1, true)) { timelapseDetail = reason; return; }
            liveTimelapse.Begin(idle.FixtureOwnerId, now);
            timelapseDetail = candidate.FeatureKind + "; " + settings.LiveTimelapseSeconds.Value.ToString("F0") + "s; one visual day; natural weather";
            Logger.LogInfo(LiveTimelapseStatus);
        }
    }

    private void LiveTimelapseCommand(string[] args, Terminal context)
    {
        const string usage = "broadcast timelapse on|off|start|stop|status|duration <45-180 seconds>; timelapse record start [interval] [duration] [fps]|stop|status";
        if (args.Length >= 3 && args[1].Equals("record", StringComparison.OrdinalIgnoreCase))
        { var recording = new string[args.Length - 1]; recording[0] = args[0]; Array.Copy(args, 2, recording, 1, args.Length - 2); TimelapseCommand(recording, context); return; }
        if (args.Length < 2) { context?.AddString(usage); return; }
        string action = args[1].ToLowerInvariant();
        if (action == "duration" && args.Length == 3 && CameraNumber(args[2], 45, 180, out float duration)) settings.LiveTimelapseSeconds.Value = duration;
        else if (args.Length != 2) { context?.AddString(usage); return; }
        else if (action == "on") settings.LiveTimelapse.Value = true;
        else if (action == "off") { settings.LiveTimelapse.Value = false; StopLiveTimelapse("Disabled by operator"); }
        else if (action == "stop") StopLiveTimelapse("Stopped by operator; automatic selection retains its cooldown");
        else if (action == "start") { settings.LiveTimelapse.Value = true; liveTimelapse.Request(); }
        else if (action != "status") { context?.AddString(usage); return; }
        context?.AddString(LiveTimelapseStatus);
    }
}
