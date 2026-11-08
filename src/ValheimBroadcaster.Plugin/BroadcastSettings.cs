using BepInEx.Configuration;
using UnityEngine;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin.Streaming;

namespace ValheimBroadcaster.Plugin;

internal sealed class BroadcastSettings
{
    internal readonly ConfigEntry<float> Distance, Height, FieldOfView, Azimuth, Smoothing, Dwell, Timeout;
    internal readonly ConfigEntry<float> ScenicDwell, CreatureDwell, CreatureRadius, AcquisitionCooldown;
    internal readonly ConfigEntry<bool> WildlifeCutaways;
    internal readonly ConfigEntry<bool> ScenicJourneys, LiveTimelapse;
    internal readonly ConfigEntry<float> LiveTimelapseSeconds;
    internal readonly ConfigEntry<float> ScenicJourneyDistance, ScenicJourneySpeed;
    internal readonly ConfigEntry<float> EnvironmentCycleSeconds;
    internal readonly ConfigEntry<string> EnvironmentWeather;
    internal readonly ConfigEntry<bool> RemoteTargets, HideHud, ShowStatus, DebugOverlay, DebugOverlayShowTrace, ReviewMarker, ObserverConnection, ProceduralPlayers;
    internal readonly ConfigEntry<bool> SimpleFollow;
    internal readonly ConfigEntry<float> SimpleFollowDistance;
    internal readonly ConfigEntry<bool> DynamicShots, GroupFraming, DeliberateComposition;
    internal readonly ConfigEntry<bool> PredictiveShots, ShotSequences, ShotScales, RenderedPictureEvaluation;
    internal readonly ConfigEntry<bool> NativeConfiner, NativeSplineDolly, NativeClearShot;
    internal readonly ConfigEntry<bool> NativePositionComposer, NativeGroupDolly, NativeFreeLook, NativePassBy;
    internal readonly ConfigEntry<float> PositionDamping, AimDamping, DeadZone, ShotMotion;
    internal readonly ConfigEntry<KeyboardShortcut> Toggle, Next, Previous, Hold, SelfTest, StreamToggle;
    internal readonly ConfigEntry<string> FFmpegPath, RtmpUrl;
    internal readonly ConfigEntry<int> StreamWidth, StreamHeight, StreamFps, StreamBitrate, DebugOverlayFontSize;
    internal readonly ConfigEntry<bool> FlipVertical, AutomaticOrientation;
    internal readonly ConfigEntry<bool> DiagnosticVideo;
    internal readonly ConfigEntry<int> TraceRetentionMiB, VideoRetentionMiB;
    internal readonly ConfigEntry<VideoEncoderKind> VideoEncoder;
    internal readonly ConfigEntry<int> NvencGpu;
    internal readonly ConfigEntry<bool> AutoStream, RetryStream, Automation, ScenicTravel, Unattended;
    internal readonly ConfigEntry<float> RegionSeconds, RegionRevisitSeconds, AcquireSeconds, TargetRetrySeconds;
    internal readonly ConfigEntry<float> MinimumRegionDistance, MaximumRegionDistance;
    internal readonly ConfigEntry<bool> StartupEnabled;
    internal readonly ConfigEntry<string> StartupCharacter, StartupCharacterSource, StartupServer, StartupPasswordFile;

    internal BroadcastSettings(ConfigFile config)
    {
        ObserverConnection = config.Bind("Multiplayer", "ObserverConnection", false, "Select before joining: require the server companion and remain a read-only observer until disconnect. Dedicated Steam servers only; ordinary gameplay is disabled for this connection.");
        Automation = config.Bind("Automation", "Enabled", true, "Automatically acquire connected players in an authorized read-only observer session. Ordinary F8/F10 and offline observer tests are unchanged.");
        ScenicTravel = config.Bind("Automation", "ScenicTravel", true, "Travel among previously generated land regions when no player can be acquired. Does not generate new regions or simulate dormant creatures.");
        RegionSeconds = config.Bind("Automation", "RegionSeconds", 120f, Range("Seconds of usable idle coverage before selecting a fresh region.", 20f, 1800f));
        RegionRevisitSeconds = config.Bind("Automation", "RegionRevisitSeconds", 1800f, Range("Minimum interval before revisiting a scenic region; exhaustion waits instead of immediately repeating.", 60f, 86400f));
        AcquireSeconds = config.Bind("Automation", "AcquireSeconds", 65f, Range("Total acquisition budget including native travel and actor readiness.", 50f, 180f));
        TargetRetrySeconds = config.Bind("Automation", "RetrySeconds", 30f, Range("Cooldown after an unreachable player or scenic destination.", 5f, 600f));
        MinimumRegionDistance = config.Bind("Automation", "MinimumRegionDistance", 256f, Range("Minimum distance for a fresh scenic region, in metres.", 128f, 2000f));
        MaximumRegionDistance = config.Bind("Automation", "MaximumRegionDistance", 10000f, Range("Maximum distance of an autonomous scenic transfer, in metres. Player acquisition is not constrained by this limit.", 256f, 20000f));
        Unattended = config.Bind("Unattended", "Enabled", false, "Opt in to reconnecting the same server/character after unexpected loss of an authorized observer session. Initial join is manual unless Startup.Enabled is configured; credentials are retained in memory. Explicit off/logout cancels recovery. Keeps the game running in the background.");
        StartupEnabled = config.Bind("Startup", "Enabled", false, "Join the explicitly configured existing character and dedicated Steam server at game startup. Requires Unattended.Enabled and Multiplayer.ObserverConnection. Manual interaction cancels startup for this process.");
        StartupCharacter = config.Bind("Startup", "CharacterFile", "", "Exact existing character filename without .fch, not the display name. Use broadcast startup profiles at the main menu. No character is created or selected by fallback.");
        StartupCharacterSource = config.Bind("Startup", "CharacterSource", "Local", "Exact character save source: Local or Cloud. Automatic source selection and legacy fallback are not allowed.");
        StartupServer = config.Bind("Startup", "ServerAddress", "", "Dedicated Steam server DNS name or IPv4 address and explicit port, for example server.example:2456. Crossplay/join codes are not supported.");
        StartupPasswordFile = config.Bind("Startup", "PasswordFile", "", "Absolute path to a private local file containing the server password on one line. Empty means passwordless server. Never put the password in this setting or launch arguments.");
        FFmpegPath = config.Bind("Streaming", "FFmpegPath", "ffmpeg", "Path to an FFmpeg executable with libx264 and AAC encoders. Not bundled.");
        TraceRetentionMiB = config.Bind("Diagnostics", "TraceRetentionMiB", 2048, new ConfigDescription(
            "Rolling storage for automatic scenic traces in MiB. Oldest trace segments expire first.",
            new AcceptableValueRange<int>(32, 16384)));
        VideoRetentionMiB = config.Bind("Diagnostics", "VideoRetentionMiB", 2048, new ConfigDescription(
            "Rolling storage for clean diagnostic video and its frame mappings in MiB. Oldest pairs expire together.",
            new AcceptableValueRange<int>(32, 16384)));
        DiagnosticVideo = config.Bind("Diagnostics", "VideoCapture", true, "Record bounded local 640x360/12fps clean-program diagnostic video and synchronized frame metadata while broadcasting and trace capture is enabled. Uses a separate low-cost FFmpeg encoder; no audio or upload. Can be disabled with broadcast capture off.");
        VideoEncoder = config.Bind("Streaming", "VideoEncoder", VideoEncoderKind.libx264, "H.264 encoder: libx264 for CPU or h264_nvenc for NVIDIA GPU. Requires encoder support in the actual FFmpeg executable and a compatible driver.");
        NvencGpu = config.Bind("Streaming", "NvencGpu", -1, "NVIDIA encoder device index, or -1 for automatic selection.");
        AutoStream = config.Bind("Streaming", "AutoStart", false, "Start publishing automatically after an authorized observer camera becomes active. Explicit stream off suppresses auto-start until manually started again.");
        RetryStream = config.Bind("Streaming", "AutoRetry", true, "Retry interrupted output with bounded backoff while publishing is requested. Invalid configuration/capability failures require correction and explicit stream on.");
        RtmpUrl = config.Bind("Streaming", "RtmpUrl", "", "Full rtmp:// or rtmps:// receiver URL including stream key. Keep this config private.");
        StreamWidth = config.Bind("Streaming", "Width", 1280, "Output width: even, 320–1920.");
        StreamHeight = config.Bind("Streaming", "Height", 720, "Output height: even, 180–1080. Match the game aspect ratio.");
        StreamFps = config.Bind("Streaming", "FPS", 30, "Output frames per second: 10–60.");
        StreamBitrate = config.Bind("Streaming", "VideoBitrateKbps", 4000, "H.264 video bitrate: 250–20000 kbps. Stereo AAC adds 128 kbps.");
        AutomaticOrientation = config.Bind("Streaming", "AutomaticOrientation", true, "Calibrate the actual GPU readback path and normalize clean live, held and slate video to upright rows automatically. Ignores the legacy FlipVertical workaround. Restart streaming after changing this setting.");
        FlipVertical = config.Bind("Streaming", "FlipVertical", false, "Manual output flip, used only when AutomaticOrientation=false. Existing values are preserved but ignored in automatic mode. Restart streaming after changing this setting.");
        StreamToggle = config.Bind("Controls", "StreamToggle", new KeyboardShortcut(KeyCode.F11), "Start/stop RTMP video and game audio. Requires active broadcast camera.");
        ScenicDwell = config.Bind("Idle", "ScenicSeconds", 45f, Range("Seconds of usable scenic footage between wildlife opportunities. The dedicated scenic director sets individual shot duration from each route.", 5f, 600f));
        LiveTimelapse = config.Bind("ScenicTimelapse", "Enabled", true, "Autonomously select restrained live daylight-passage shots on an authorized empty server. No recording or simulation acceleration.");
        LiveTimelapseSeconds = config.Bind("ScenicTimelapse", "DurationSeconds", 90f, Range("Live shot duration for one visual day, returning to current natural time.", 45f, 180f));
        ScenicJourneys = config.Bind("Idle", "ScenicJourneys", true, "Offer continuous camera flights to known scenic destinations. The observer follows the guide as it steers through loaded terrain. Player coverage can interrupt a journey.");
        ScenicJourneyDistance = config.Bind("Idle", "JourneyDistanceMetres", 1000f, Range("Desired itinerary scale when selecting a known scenic destination. The selected route may be shorter or longer. Completion requires observed route progress and a readable destination arrival; a partial attempt is not completed.", 256f, 5000f));
        ScenicJourneySpeed = config.Bind("Idle", "JourneyCruiseMetresPerSecond", 6f, Range("Independent journey cruise speed. Decorative Camera.ShotMotion does not scale travel. Runtime may be slower if valid frames cannot sustain the requested speed.", ScenicJourneyPolicy.MinimumCruiseSpeed, ScenicJourneyPolicy.MaximumCruiseSpeed));
        EnvironmentCycleSeconds = config.Bind("ScenicEnvironment", "DayCycleSeconds", 0f, Range("Optional visual day-cycle duration, 30–86400 real seconds; zero preserves normal time. Requires the companion's fresh empty-server permission. Changes lighting presentation only, never simulation time.", 0f, 86400f));
        EnvironmentWeather = config.Bind("ScenicEnvironment", "Weather", "", "Optional existing Valheim environment name for scenic filming while the companion confirms no ordinary players are connected. Empty preserves normal weather. Owned overrides restore when permission or Scenic coverage ends.");
        CreatureDwell = config.Bind("Idle", "CreatureSeconds", 25f, Range("Seconds of usable creature footage. Wildlife cutaways return to players and also use this as a wall-time limit.", 5f, 300f));
        WildlifeCutaways = config.Bind("Idle", "WildlifeCutaways", true, "Allow nearby wildlife cutaways after every connected ordinary player has been observed idle for at least 60 seconds. Requires the optional companion activity feed; no spawning, simulation, or travel is enabled.");
        CreatureRadius = config.Bind("Idle", "CreatureRadius", 50f, Range("Loaded-creature search radius around the survey origin in metres.", 5f, 100f));
        AcquisitionCooldown = config.Bind("Director", "AcquisitionCooldownSeconds", 30f, Range("Local-interest cooldown between experimental distant loading attempts.", 5f, 300f));
        SimpleFollow = config.Bind("Camera", "SimpleFollow", true, "Minimal single-camera follow baseline: one native camera follows the covered player directly, framed slightly center-left and settling behind their facing. Disables the directed multi-camera editorial system until the baseline is trusted.");
        SimpleFollowDistance = config.Bind("Camera", "SimpleFollowDistance", 6f, Range("Simple follow orbit distance from the subject in metres.", 2f, 30f));
        ProceduralPlayers = config.Bind("Camera", "ProceduralPlayers", true, "Use the shared Cinemachine pipeline for player, creature and scenic coverage. False selects legacy fixed framing for comparison. This existing config key is retained for compatibility.");
        DynamicShots = config.Bind("Camera", "DynamicShots", true, "Direct shot duration and deliberate motion according to activity, available space and recent camera corrections.");
        PredictiveShots = config.Bind("Camera", "PredictiveShots", true, "Prefer presently readable viewpoints expected to remain useful during observed movement. Prediction is bounded, uncertain evidence stays neutral, and it never requests travel.");
        ShotSequences = config.Bind("Camera", "ShotSequences", true, "Develop short evidence-based sequences across player, creature and scenic coverage. Progress requires usable live footage; manual hold and steady mode pause editorial development.");
        ShotScales = config.Bind("Camera", "ShotScales", true, "Use deliberate wide, full-body and qualified medium compositions. Medium framing validates an intentional upper-body envelope and retains full-body alternatives when inappropriate.");
        RenderedPictureEvaluation = config.Bind("Camera", "RenderedPictureEvaluation", true, "Supplement geometric visibility with low-frequency analysis of the clean live picture for sustained poor exposure/contrast. No extra world render, automatic exposure changes or remote image upload.");
        NativeConfiner = config.Bind("Camera", "Confiner3D", true, "Use native Cinemachine Confiner 3D inside verified local interior volumes. Uncertain or open spaces retain the existing visibility and physical-clearance checks; no gameplay collider is added to the world.");
        NativeSplineDolly = config.Bind("Camera", "SplineDolly", true, "Use native Cinemachine Spline Dolly for finite procedural scenic paths, retaining validated movement, independent aim and presentation-qualified progress.");
        NativeClearShot = config.Bind("Camera", "ClearShot", true, "Use native Cinemachine ClearShot to rank evaluated camera views. Readability, actual-footage pacing, continuity and final pose validation remain required.");
        NativePositionComposer = config.Bind("Camera", "PositionComposer", true, "Offer observational actor shots with a fixed viewing direction and native screen/depth dead zones. The subject can move across the frame or toward/away from the camera before it responds; readable alternatives remain available.");
        NativeGroupDolly = config.Bind("Camera", "GroupDolly", true, "Let native group framing move within validated open-space limits before widening the lens. Constrained or obstructed paths retain zoom-only framing and primary-subject alternatives.");
        NativeFreeLook = config.Bind("Camera", "FreeLookModifier", true, "Use native FreeLook Modifier to coordinate lens, damping and composition with elevation on eligible orbiting/rising shots. Group framing retains exclusive lens ownership for group shots.");
        NativePassBy = config.Bind("Camera", "PassByShots", true, "Offer finite roadside shots during steady open-space player travel. The camera holds a world position and pans as players pass; encounters, course changes and unreadable views retain tracking alternatives.");
        DeliberateComposition = config.Bind("Camera", "DeliberateComposition", true, "Choose persistent actor or environmental screen placement from shot intent, movement and observed activity. False retains a centered comparison preference with the same damping and safety checks.");
        GroupFraming = config.Bind("Camera", "GroupFraming", true, "Include persistent nearby companions and relevant opponents when a readable group composition is available.");
        PositionDamping = config.Bind("Camera", "PositionDamping", 0.35f, Range("Cinemachine position settling time in seconds (approximately 99% convergence). Shot trajectory planning is separate; larger values soften final body response.", 0.1f, 3f));
        AimDamping = config.Bind("Camera", "AimDamping", 0.15f, Range("Cinemachine aim settling time in seconds (approximately 99% convergence); larger values soften aiming response to subject movement. The broadcast-tight default corrects almost immediately so the subject stays pinned to the composition target.", 0.1f, 3f));
        DeadZone = config.Bind("Camera", "DeadZone", 0.015f, Range("Screen fraction in which subjects can move without the camera immediately recentering them. Near-zero pins the subject to the composition target (broadcast-tight framing); zero removes the dead zone entirely.", 0f, 0.3f));
        ShotMotion = config.Bind("Camera", "ShotMotion", 1f, Range("Strength of deliberate shot movement. Zero disables decorative movement while retaining tracking, damping and shot selection.", 0f, 1.5f));
        Distance = config.Bind("Camera", "Distance", 18f, Range("Horizontal distance from target in metres.", 2f, 80f));
        Height = config.Bind("Camera", "Height", 2f, Range("Maximum height above target in metres. Deliberate shot scales rest the follow camera near the subject's shoulder line; this cap mainly governs wide and legacy coverage.", 1f, 60f));
        FieldOfView = config.Bind("Camera", "FieldOfView", 75f, Range("Vertical camera field of view in degrees.", 30f, 110f));
        Azimuth = config.Bind("Camera", "Azimuth", 225f, Range("Preferred world-space viewing direction in degrees; procedural cameras may reframe around obstacles.", 0f, 360f));
        Smoothing = config.Bind("Camera", "Smoothing", 5f, Range("Exponential follow response per second; larger is faster.", 0.1f, 30f));
        Dwell = config.Bind("Director", "DwellSeconds", 30f, Range("Seconds of ready footage before advancing.", 3f, 600f));
        Timeout = config.Bind("Director", "TargetTimeoutSeconds", 20f, Range("Seconds to wait before skipping a target that cannot load.", 3f, 120f));
        RemoteTargets = config.Bind("Experimental", "RemoteTargets", false,
            "Include distant players sharing a public map position and change network reference position. " +
            "UNVERIFIED: may affect streaming, simulation ownership, and the parked observer. Test on a disposable world.");
        HideHud = config.Bind("Display", "HideHud", true, "Hide the normal HUD while broadcasting; restore its prior state on stop.");
        ReviewMarker = config.Bind("Streaming", "ReviewMarker", false, "Include a machine-readable capture reference in the RTMP picture for the private review player.");
        DebugOverlayFontSize = config.Bind("Streaming", "DebugOverlayFontSize", 14,
            new ConfigDescription("Debug text size in pixels at 1080p. Scales with output height, with an 8-pixel minimum.",
                new AcceptableValueRange<int>(10, 20)));
        DebugOverlayShowTrace = config.Bind("Streaming", "DebugOverlayShowTrace", true,
            "Include the human-readable capture reference on the debug panel. The review marker works independently.");
        DebugOverlay = config.Bind("Streaming", "DebugOverlay", false, "Burn the director status and capture reference into the RTMP video sent to every viewer.");
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
