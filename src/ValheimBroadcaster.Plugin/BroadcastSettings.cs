using BepInEx.Configuration;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

internal sealed class BroadcastSettings
{
    internal readonly ConfigEntry<float> Distance, Height, FieldOfView, Azimuth, Smoothing, Dwell, Timeout;
    internal readonly ConfigEntry<bool> RemoteTargets, HideHud, ShowStatus;
    internal readonly ConfigEntry<KeyboardShortcut> Toggle, Next, Previous, Hold;

    internal BroadcastSettings(ConfigFile config)
    {
        Distance = config.Bind("Camera", "Distance", 18f, Range("Horizontal distance from target in metres.", 2f, 80f));
        Height = config.Bind("Camera", "Height", 10f, Range("Height above target in metres.", 1f, 60f));
        FieldOfView = config.Bind("Camera", "FieldOfView", 75f, Range("Vertical camera field of view in degrees.", 30f, 110f));
        Azimuth = config.Bind("Camera", "Azimuth", 225f, Range("Fixed world-space viewing direction in degrees.", 0f, 360f));
        Smoothing = config.Bind("Camera", "Smoothing", 5f, Range("Exponential follow response per second; larger is faster.", 0.1f, 30f));
        Dwell = config.Bind("Director", "DwellSeconds", 30f, Range("Seconds of ready footage before advancing.", 3f, 600f));
        Timeout = config.Bind("Director", "TargetTimeoutSeconds", 20f, Range("Seconds to wait before skipping a target that cannot load.", 3f, 120f));
        RemoteTargets = config.Bind("Experimental", "RemoteTargets", false,
            "Include distant players sharing a public map position and change network reference position. " +
            "UNVERIFIED: may affect streaming, simulation ownership, and the parked observer. Test on a disposable world.");
        HideHud = config.Bind("Display", "HideHud", true, "Hide the normal HUD while broadcasting; restore its prior state on stop.");
        ShowStatus = config.Bind("Display", "ShowStatus", true, "Show target and director status in the upper-left corner.");
        Toggle = config.Bind("Controls", "Toggle", new KeyboardShortcut(KeyCode.F8), "Start/stop broadcasting.");
        Next = config.Bind("Controls", "Next", new KeyboardShortcut(KeyCode.PageDown), "Select next target.");
        Previous = config.Bind("Controls", "Previous", new KeyboardShortcut(KeyCode.PageUp), "Select previous target.");
        Hold = config.Bind("Controls", "Hold", new KeyboardShortcut(KeyCode.F9), "Hold current target / resume automatic cycling.");
    }

    private static ConfigDescription Range(string text, float min, float max) =>
        new ConfigDescription(text, new AcceptableValueRange<float>(min, max));
}
