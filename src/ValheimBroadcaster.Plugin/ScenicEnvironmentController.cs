using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Owns only the observer's EnvMan presentation overrides. The server
/// permits them only in an empty session; world/network time is never changed.</summary>
internal sealed class ScenicEnvironmentController : IDisposable
{
    private readonly RemoteObserverClient remote;
    private readonly Action<string> log;
    private readonly ScenicEnvironmentClock clock = new ScenicEnvironmentClock();
    private static readonly FieldInfo? SmoothDayFraction = AccessTools.Field(typeof(EnvMan), "m_smoothDayFraction");
    private static readonly MethodInfo? RescaleDayFraction = AccessTools.Method(typeof(EnvMan), "RescaleDayFraction", new[] { typeof(float) });
    private EnvMan? owner;
    private ZNet? world;
    private double cycleSeconds;
    private string weather = "", configurationError = "";
    private bool savedDebugTime, ownsTime, ownsWeather;
    private float savedTime, savedFraction, appliedTime;
    private string savedWeather = "", appliedWeather = "";
    private bool disposed, suspendedForConflict;
    internal string Status { get; private set; } = "Environment: natural time/weather";
    internal bool Active => owner && (ownsTime || ownsWeather);
    internal double DayFraction => clock.Fraction;

    internal ScenicEnvironmentController(RemoteObserverClient remote, Action<string> log)
    { this.remote = remote ?? throw new ArgumentNullException(nameof(remote)); this.log = log; remote.EnvironmentRevoked += Stop; }

    internal void Configure(double dayCycleSeconds, string weatherName)
    {
        if (disposed) return;
        string name = (weatherName ?? "").Trim();
        if (cycleSeconds == dayCycleSeconds && weather == name && configurationError.Length == 0) return;
        try
        {
            ScenicEnvironmentClock.Cycle(dayCycleSeconds);
            if (name.Length > 64 || name.Any(char.IsControl)) throw new ArgumentException("Invalid environment name.");
            bool weatherChanged = weather != name, cycleTurnedOff = cycleSeconds > 0 && dayCycleSeconds == 0;
            // Preserve phase through speed edits, but release a removed override.
            if (weatherChanged || cycleTurnedOff) Restore();
            cycleSeconds = dayCycleSeconds; weather = name; configurationError = ""; suspendedForConflict = false;
        }
        catch (ArgumentException)
        {
            Restore(); cycleSeconds = 0; weather = "";
            configurationError = "Environment: invalid settings; cycle must be 0 or 30–86400 seconds and weather a valid name";
        }
    }

    internal void Tick(bool scenicOnAir, double now)
    {
        if (disposed) return;
        bool requested = configurationError.Length == 0 && !suspendedForConflict && (cycleSeconds > 0 || weather.Length > 0);
        remote.EnvironmentRequested = requested && scenicOnAir;
        bool permitted = remote.Isolated ? remote.EnvironmentAllowed(now) : ObserverController.Offline;
        if (!requested || !scenicOnAir || !permitted || !EnvMan.instance || !ZNet.instance)
        {
            Restore();
            Status = configurationError.Length != 0 ? configurationError : suspendedForConflict ?
                "Environment: another writer changed the override; change settings to resume" : !requested ?
                "Environment: natural time/weather" : !scenicOnAir ? "Environment: armed; waiting for live Scenic footage" :
                remote.Isolated ? remote.EnvironmentStatus : "Environment: requires a closed local world or empty-server permission";
            return;
        }
        if (!ReferenceEquals(owner, null) && (!owner || owner != EnvMan.instance || world != ZNet.instance)) Restore();
        if (!owner)
        {
            EnvMan environment = EnvMan.instance;
            if (cycleSeconds > 0 && (SmoothDayFraction?.FieldType != typeof(float) || RescaleDayFraction?.ReturnType != typeof(float)))
            { Status = "Environment: visual time restoration hooks do not match this game build"; return; }
            if (weather.Length > 0 && !environment.m_environments.Any(item => item != null && item.m_name == weather))
            { Status = "Environment: unknown weather '" + weather + "'; use an installed EnvMan environment name"; return; }
            owner = environment; world = ZNet.instance;
            savedDebugTime = owner.m_debugTimeOfDay; savedTime = owner.m_debugTime; savedWeather = owner.m_debugEnv;
            savedFraction = owner.GetDayFraction();
            if (float.IsNaN(savedFraction) || float.IsInfinity(savedFraction))
            { Restore(); Status = "Environment: current visual time is invalid"; return; }
            clock.Reset(Math.Max(0, Math.Min(1, savedFraction)), now);
        }
        // Do not silently take over state another mod/operator modified after us.
        if (ownsTime && (!owner.m_debugTimeOfDay || owner.m_debugTime != appliedTime) ||
            ownsWeather && owner.m_debugEnv != appliedWeather)
        {
            Restore(); suspendedForConflict = true; remote.EnvironmentRequested = false;
            Status = "Environment: external override detected; cinematic control suspended"; log(Status); return;
        }
        float phase = (float)clock.Advance(now, cycleSeconds, true);
        if (cycleSeconds > 0)
        { owner.m_debugTimeOfDay = true; owner.m_debugTime = appliedTime = phase; ownsTime = true; }
        if (weather.Length > 0)
        { owner.m_debugEnv = appliedWeather = weather; ownsWeather = true; }
        Status = "Environment: cinematic " + (cycleSeconds > 0 ? "day cycle " + cycleSeconds.ToString("F0") + "s" : "natural time") +
            (weather.Length > 0 ? "; weather " + weather : "; natural weather") + "; shared simulation clock unchanged";
    }

    internal void Stop()
    { Restore(); remote.EnvironmentRequested = false; Status = "Environment: previous time/weather settings restored"; }

    private void Restore()
    {
        if (owner)
        {
            // Restore only values still owned by us. Respect a concurrent writer.
            if (ownsTime && owner.m_debugTimeOfDay && owner.m_debugTime == appliedTime)
            {
                owner.m_debugTimeOfDay = savedDebugTime; owner.m_debugTime = savedTime;
                // Native EnvMan otherwise lerps from our last artificial phase
                // for several seconds after revocation. Reset its presentation
                // cache to current natural server time, never the server clock.
                try
                {
                    float natural = savedFraction;
                    if (savedDebugTime) natural = savedTime;
                    else if (world && world == ZNet.instance && owner.m_dayLengthSec > 0 && RescaleDayFraction != null)
                    {
                        double fraction = (world.GetTimeSeconds() % owner.m_dayLengthSec) / owner.m_dayLengthSec;
                        if (RescaleDayFraction.Invoke(owner, new object[] { Mathf.Clamp01((float)fraction) }) is float rescaled)
                            natural = rescaled;
                    }
                    if (!float.IsNaN(natural) && !float.IsInfinity(natural)) SmoothDayFraction?.SetValue(owner, natural);
                }
                catch (Exception error) { log("Environment settings restored; native visual phase will settle after " + error.GetType().Name + "."); }
            }
            if (ownsWeather && owner.m_debugEnv == appliedWeather) owner.m_debugEnv = savedWeather;
        }
        owner = null; world = null; ownsTime = ownsWeather = false;
    }

    public void Dispose()
    { if (disposed) return; Stop(); remote.EnvironmentRevoked -= Stop; disposed = true; }
}
