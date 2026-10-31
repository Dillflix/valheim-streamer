using BepInEx.Configuration;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

internal sealed class BroadcastSettings
{
    internal readonly ConfigEntry<float> Distance, Height, FieldOfView, Azimuth, Smoothing, Dwell, Timeout;
    internal readonly ConfigEntry<float> ScenicDwell, CreatureDwell, CreatureRadius, AcquisitionCooldown;
    internal readonly ConfigEntry<bool> RemoteTargets, HideHud, ShowStatus, ObserverConnection;
    internal readonly ConfigEntry<KeyboardShortcut> Toggle, Next, Previous, Hold, SelfTest, StreamToggle;
    internal readonly ConfigEntry<string> FFmpegPath, RtmpUrl;
    internal readonly ConfigEntry<int> StreamWidth, StreamHeight, StreamFps, StreamBitrate;
    internal readonly ConfigEntry<Streaming.VideoEncoderKind> StreamEncoder;
    internal readonly ConfigEntry<int> NvencGpu;
    internal readonly ConfigEntry<bool> FlipVertical;

    internal BroadcastSettings(ConfigFile config)
    {
        ObserverConnection = config.Bind("Multiplayer", "ObserverConnection", false, "Select before joining: require the server companion and remain a read-only observer until disconnect. Dedicated Steam servers only; ordinary gameplay is disabled for this connection.");
        FFmpegPath = config.Bind("Streaming", "FFmpegPath", "ffmpeg", "Path to FFmpeg with the selected H.264 encoder, AAC and RTMP support. Not bundled.");
        StreamEncoder = config.Bind("Streaming", "VideoEncoder", Streaming.VideoEncoderKind.libx264, "libx264: software CPU encoding. h264_nvenc: NVIDIA GPU encoding, requiring a compatible driver and NVENC-enabled FFmpeg on this client. No automatic fallback.");
        NvencGpu = config.Bind("Streaming", "NvencGpu", -1, "NVENC device index: -1 selects automatically; 0 selects the first NVIDIA GPU. Used only with h264_nvenc.");
        RtmpUrl = config.Bind("Streaming", "RtmpUrl", "", "Full rtmp:// or rtmps:// receiver URL including stream key. Keep this config private.");
        StreamWidth = config.Bind("Streaming", "Width", 1280, "Output width: even, 320–1920.");
        StreamHeight = config.Bind("Streaming", "Height", 720, "Output height: even, 180–1080. Match the game aspect ratio.");
        StreamFps = config.Bind("Streaming", "FPS", 30, "Output frames per second: 10–60.");
        StreamBitrate = config.Bind("Streaming", "VideoBitrateKbps", 4000, "H.264 video bitrate: 250–20000 kbps. Stereo AAC adds 128 kbps.");
        FlipVertical = config.Bind("Streaming", "FlipVertical", false, "Flip output if the receiver shows an upside-down image with your graphics backend.");
        StreamToggle = config.Bind("Controls", "StreamToggle", new KeyboardShortcut(KeyCode.F11), "Start/stop RTMP video and game audio. Requires active broadcast camera.");
        ScenicDwell = config.Bind("Idle", "ScenicSeconds", 45f, Range("Scenic moving-shot duration before choosing another path or fresh creature.", 5f, 600f));
        CreatureDwell = config.Bind("Idle", "CreatureSeconds", 25f, Range("Creature follow duration before returning to scenery.", 5f, 300f));
        CreatureRadius = config.Bind("Idle", "CreatureRadius", 50f, Range("Loaded-creature search radius around the survey origin in metres.", 5f, 100f));
        AcquisitionCooldown = config.Bind("Director", "AcquisitionCooldownSeconds", 30f, Range("Local-interest cooldown between experimental distant loading attempts.", 5f, 300f));
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
        SelfTest = config.Bind("Controls", "SelfTest", new KeyboardShortcut(KeyCode.F10), "Start self-follow camera testing with your own character; F8 stops.");
        Next = config.Bind("Controls", "Next", new KeyboardShortcut(KeyCode.PageDown), "Select next target.");
        Previous = config.Bind("Controls", "Previous", new KeyboardShortcut(KeyCode.PageUp), "Select previous target.");
        Hold = config.Bind("Controls", "Hold", new KeyboardShortcut(KeyCode.F9), "Hold current target / resume automatic cycling.");
    }

    private static ConfigDescription Range(string text, float min, float max) =>
        new ConfigDescription(text, new AcceptableValueRange<float>(min, max));
}
