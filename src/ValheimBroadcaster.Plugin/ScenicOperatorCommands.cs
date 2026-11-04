using System;
using System.Globalization;
using System.Reflection;
using System.Linq;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

public sealed partial class BroadcasterPlugin
{
    private void JourneyCommand(string[] args, Terminal context)
    {
        const string usage = "broadcast journey start|stop|on|off|status|distance <256-5000 metres>|duration <seconds>|speed <2-12 metres/second> — distance selects the desired scale of a known-destination itinerary";
        if (args.Length < 2) { context?.AddString(usage); return; }
        string action = args[1].ToLowerInvariant();
        if (action == "start" && args.Length == 2)
        {
            if (!settings.ProceduralPlayers.Value)
            { context?.AddString("Enable broadcast camera procedural before requesting a journey."); return; }
            if (!director.IsEnabled || !observer.Active || observer.Phase != ObserverPhase.Watching ||
                coverage.Mode != CoverageMode.Scenic || selfTest || probe.Active || remote.Isolated && !remote.Approved)
            { context?.AddString("Start a protected observer's Scenic coverage before requesting a journey."); return; }
            StopLiveTimelapse("Operator requested a journey");
            settings.ScenicJourneys.Value = true;
            SetHolding(false);
            context?.AddString(idle.RequestJourney());
            return;
        }
        if ((action == "on" || action == "off") && args.Length == 2)
        {
            settings.ScenicJourneys.Value = action == "on";
            if (action == "off") idle.StopJourney();
        }
        else if (action == "stop" && args.Length == 2) idle.StopJourney();
        else if (action == "distance" && args.Length == 3 && CameraNumber(args[2], 256, 5000, out float metres))
            settings.ScenicJourneyDistance.Value = metres;
        else if (action == "duration" && args.Length == 3 && CameraNumber(args[2], 1, 2500, out float seconds) && seconds * settings.ScenicJourneySpeed.Value >= 256 && seconds * settings.ScenicJourneySpeed.Value <= 5000)
            settings.ScenicJourneyDistance.Value = seconds * settings.ScenicJourneySpeed.Value;
        else if (action == "speed" && args.Length == 3 && CameraNumber(args[2], 2, 12, out float speed))
            settings.ScenicJourneySpeed.Value = speed;
        else if (action != "status" || args.Length != 2) { context?.AddString(usage); return; }
        context?.AddString(idle.JourneyStatus);
        context?.AddString($"Automatic journeys {settings.ScenicJourneys.Value}; desired itinerary scale {settings.ScenicJourneyDistance.Value:F0}m; independent cruise {settings.ScenicJourneySpeed.Value:F1}m/s. Actual route length and arrival depend on the selected known destination and local clearance. Settings apply to the next journey.");
    }

    private void FixtureCommand(string[] args, Terminal context)
    {
        if (cameraFixtures == null) { context?.AddString("Camera fixtures are unavailable."); return; }
        if (args.Length >= 2 && args[1].Equals("play", StringComparison.OrdinalIgnoreCase) && !settings.ProceduralPlayers.Value)
        { context?.AddString("Enable broadcast camera procedural before playing a camera fixture."); return; }
        if (args.Length >= 2 && args[1].Equals("save", StringComparison.OrdinalIgnoreCase) &&
            (!director.IsEnabled || coverage.Mode != CoverageMode.Scenic || !cameraReady ||
             frameHold.CurrentPresentation != FramePresentation.Live || presentedScenicPlan != idle.PlanId))
        { context?.AddString("Save a fixture while its Scenic shot is actually on air."); return; }
        bool playback = director.IsEnabled && observer.Active && observer.Phase == ObserverPhase.Watching &&
            coverage.Mode == CoverageMode.Scenic && !selfTest && !probe.Active &&
            (!remote.Isolated || remote.Approved);
        if (args.Length >= 2 && args[1].Equals("play", StringComparison.OrdinalIgnoreCase)) StopLiveTimelapse("Operator requested a saved fixture");
        try
        {
            context?.AddString(cameraFixtures.Command(args, WorldIdentity.Current,
                playback, idle, settings, typeof(BroadcasterPlugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? PluginVersion,
                start =>
                {
                    if (!playback || !observer.Active || observer.Phase != ObserverPhase.Watching) return false;
                    observer.TravelTo(start); cameraReady = false; rig.ResetPose();
                    return observer.Phase == ObserverPhase.Travelling;
                }));
            if (args.Length >= 2 && args[1].Equals("play", StringComparison.OrdinalIgnoreCase) && cameraFixtures.PendingPlayback)
                SetHolding(false);
        }
        catch (ArgumentException error) { context?.AddString(error.Message); }
        catch (InvalidOperationException error) { context?.AddString(error.Message); }
    }

    private void EnvironmentCommand(string[] args, Terminal context)
    {
        const string usage = "broadcast environment off|status|list|cycle <0 or 30-86400 seconds>|weather <environment name|normal>";
        if (args.Length < 2 || scenicEnvironment == null) { context?.AddString(usage); return; }
        string action = args[1].ToLowerInvariant();
        if (action == "list" && args.Length == 2)
        {
            context?.AddString(EnvMan.instance ? string.Join(", ", EnvMan.instance.m_environments
                .Where(item => item != null).Select(item => item.m_name).OrderBy(name => name)) : "Join a world to list available environments.");
            return;
        }
        if (action != "status" && action != "list") StopLiveTimelapse("Operator changed environment controls");
        if (action == "off" && args.Length == 2)
        {
            settings.EnvironmentCycleSeconds.Value = 0;
            settings.EnvironmentWeather.Value = "";
            scenicEnvironment.Stop();
        }
        else if (action == "cycle" && args.Length == 3 && CameraNumber(args[2], 0, 86400, out float seconds) && (seconds == 0 || seconds >= 30))
            settings.EnvironmentCycleSeconds.Value = seconds;
        else if (action == "weather" && args.Length >= 3)
        {
            string name = string.Join(" ", args, 2, args.Length - 2).Trim();
            if (name.Length > 64 || name.Any(char.IsControl))
            { context?.AddString("Use a single existing Valheim environment name, at most 64 characters."); return; }
            if (!name.Equals("normal", StringComparison.OrdinalIgnoreCase) && EnvMan.instance &&
                !EnvMan.instance.m_environments.Any(item => item != null && item.m_name == name))
            { context?.AddString("Unknown environment. Use broadcast environment list for available names."); return; }
            settings.EnvironmentWeather.Value = name.Equals("normal", StringComparison.OrdinalIgnoreCase) ? "" : name;
        }
        else if (action != "status" || args.Length != 2) { context?.AddString(usage); return; }
        scenicEnvironment.Configure(settings.EnvironmentCycleSeconds.Value, settings.EnvironmentWeather.Value);
        context?.AddString(scenicEnvironment.Status);
        context?.AddString("Visual environment controls require fresh empty-server permission and live Scenic coverage; normal settings restore when either ends.");
    }

    private void TimelapseCommand(string[] args, Terminal context)
    {
        const string usage = "broadcast timelapse record start [capture interval seconds] [real duration seconds] [output fps] | stop | status";
        if (args.Length < 2 || timelapseCapture == null) { context?.AddString(usage); return; }
        string action = args[1].ToLowerInvariant();
        if (action == "start" && args.Length >= 2 && args.Length <= 5)
        {
            if (!director.IsEnabled || !Player.m_localPlayer || !ZNet.instance || activeWorld != ZNet.instance ||
                probe.Active || remote.Isolated && !remote.Approved)
            { context?.AddString("Start a valid broadcast session before recording a timelapse."); return; }
            float interval = 2, duration = 600;
            int fps = 30;
            if (args.Length >= 3 && !CameraNumber(args[2], .25f, 3600, out interval) ||
                args.Length >= 4 && !CameraNumber(args[3], .25f, 604800, out duration) ||
                args.Length == 5 && (!int.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out fps) || fps < 1 || fps > 60))
            { context?.AddString(usage); return; }
            try { context?.AddString(timelapseCapture.StartCapture(interval, duration, fps)); }
            catch (ArgumentException error) { context?.AddString(error.Message); }
            catch (InvalidOperationException error) { context?.AddString(error.Message); }
            return;
        }
        if (action == "stop" && args.Length == 2) timelapseCapture.StopCapture("Operator stop");
        else if (action != "status" || args.Length != 2) { context?.AddString(usage); return; }
        context?.AddString(timelapseCapture.Status);
    }
}
