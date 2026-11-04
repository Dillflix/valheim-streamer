using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Operator-owned fixture commands. Playback goes through the scenic
/// production evaluator; loading a file never moves a camera or character.</summary>
internal sealed class CameraFixtureController : IDisposable
{
    internal const string Usage = "broadcast fixture list|status | save <name> [replace] | play <name> [seconds] | stop | delete <name> | preview on|off|live|<name>";
    private readonly CameraFixtureStore store;
    private readonly CameraFixturePreview preview = new CameraFixturePreview();
    private CameraFixture? selected;
    private CameraFixture? pending;
    private float pendingAt;
    private bool playbackRequested;
    private long worldId;
    private string last = "No fixture selected";
    internal bool PreviewEnabled { get; private set; }
    internal bool PendingPlayback => pending != null || playbackRequested;
    internal bool AwaitingStart => pending != null;
    internal string Status => "Camera fixtures: " + last + "; preview " + (PreviewEnabled ? selected?.Name ?? "live plan" : "off");

    internal CameraFixtureController(string configPath)
    { store = new CameraFixtureStore(Path.Combine(configPath, "ValheimBroadcaster", "CameraFixtures")); }

    internal void SetWorld(long actualWorldId)
    {
        if (actualWorldId == worldId) return;
        Reset(); worldId = actualWorldId;
    }

    internal string Command(string[] args, long actualWorldId, bool allowPlayback, IdleShots idle,
        BroadcastSettings settings, string sourceVersion, Func<Vector3, bool>? acquire = null)
    {
        SetWorld(actualWorldId);
        if (args == null || args.Length < 2) return Usage;
        string operation = args[1].ToLowerInvariant();
        if (operation == "status" && args.Length == 2) return Status + "; " + idle.FixtureStatus;
        if (operation == "stop" && args.Length == 2)
        {
            bool travelling = pending != null;
            CancelPending("Fixture playback stopped; normal direction resumes", idle);
            return Status + (travelling ? ". Already-issued observer travel may finish; use broadcast observer return to return and park." : "");
        }
        if (operation == "preview" && args.Length == 3 && args[2].Equals("off", StringComparison.OrdinalIgnoreCase))
        { PreviewEnabled = false; return Status; }
        if (actualWorldId == 0) return "Join a world before accessing its camera fixtures.";
        try
        {
            if (operation == "list" && args.Length == 2)
            {
                var names = store.List(worldId);
                return names.Count == 0 ? "No camera fixtures saved for this world." : "Camera fixtures in this world: " + string.Join(", ", names);
            }
            if (operation == "save" && (args.Length == 3 || args.Length == 4 && args[3].Equals("replace", StringComparison.OrdinalIgnoreCase)))
            {
                ScenicShotPlan? plan = idle.Plan;
                if (plan == null || !plan.IsDirected || plan.IsPictureOnly)
                    return "Wait for an authored Scenic moving shot or pan before saving a fixture.";
                if (!idle.TryGetFixturePlayback(plan.Id, out float effectiveMotion, out bool effectiveDynamic))
                { effectiveMotion = settings.ShotMotion.Value; effectiveDynamic = settings.DynamicShots.Value; }
                var fixture = new CameraFixture(worldId, args[2], sourceVersion, Settings(settings, effectiveMotion, effectiveDynamic), DateTime.UtcNow,
                    effectiveMotion, effectiveDynamic, plan);
                store.Save(fixture, args.Length == 4);
                selected = fixture; preview.Reset();
                last = "Saved " + fixture.Name + (fixture.SourceJourneyId > 0 ? " (current journey window)" : "") +
                    $"; {plan.TravelMetres:F1}m, {plan.DurationSeconds:F1}s at motion 1; source {fixture.SourceVersion}";
                return Status;
            }
            if (operation == "play" && (args.Length == 3 || args.Length == 4))
            {
                if (!allowPlayback) return "Fixture playback requires the active, authorized observer in Scenic coverage; player coverage retains priority.";
                CameraFixture fixture = store.Load(worldId, args[2]);
                if (args.Length == 4)
                {
                    if (!double.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) ||
                        double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 1 || seconds > 604800)
                        return "Fixture duration must be between one second and seven days.";
                    // An explicit duration is presentation time at normal
                    // playback speed. It should not silently double because
                    // the fixture was recorded with a half-speed setting.
                    fixture = new CameraFixture(fixture.WorldId, fixture.Name, fixture.SourceVersion, fixture.SettingsSummary,
                        fixture.CreatedUtc, fixture.MotionMultiplier > 0 ? 1 : 0, fixture.DynamicMovement,
                        fixture.Plan.WithDuration(seconds), fixture.SourceJourneyId, fixture.SourcePredecessorId);
                }
                Vector3 start = V(fixture.Plan.Sample(0).CameraPosition);
                bool nearby = Player.m_localPlayer && (Player.m_localPlayer.transform.position - start).sqrMagnitude <= 64f * 64f;
                if (!nearby || !IdleShots.Ready(start))
                {
                    if (acquire == null)
                        return FormattableString.Invariant($"Fixture starts at ({start.x:F1}, {start.y:F1}, {start.z:F1}); acquire that area through observer travel before playback.");
                    if (!acquire(start)) return "Observer travel could not acquire this fixture's starting area; current playback was retained.";
                    pending = selected = fixture; pendingAt = Time.unscaledTime; playbackRequested = true; preview.Reset();
                    last = "Acquiring " + fixture.Name + " start; fixture validation begins after observer arrival";
                    return Status;
                }
                if (!idle.RequestFixture(fixture.Plan, out string reason, fixture.MotionMultiplier, fixture.DynamicMovement))
                    return "Fixture not queued: " + reason;
                pending = null; playbackRequested = true;
                selected = fixture; preview.Reset();
                last = "Queued " + fixture.Name + " for current-world geometry validation; recorded with " + fixture.SourceVersion;
                return Status + ". " + reason;
            }
            if (operation == "delete" && args.Length == 3)
            {
                string name = CameraFixture.NormalizeName(args[2]);
                bool removed = store.Delete(worldId, name);
                if (selected?.Name == name) { selected = null; preview.Reset(); }
                last = removed ? "Deleted " + name : "No saved fixture named " + name;
                return Status;
            }
            if (operation == "preview" && args.Length == 3)
            {
                string argument = args[2].ToLowerInvariant();
                if (argument == "live") { selected = null; preview.Reset(); }
                else if (argument != "on") { selected = store.Load(worldId, argument); preview.Reset(); }
                PreviewEnabled = true;
                return Status + ". Cyan: route; amber: aim; green: selected subject. Preview is not a clearance certificate.";
            }
            return Usage;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
            error is ArgumentException || error is InvalidOperationException || error is NotSupportedException)
        {
            last = "Fixture command failed: " + error.Message;
            return last;
        }
    }

    internal void Update(long actualWorldId, bool allowResume, IdleShots idle)
    {
        SetWorld(actualWorldId);
        if (pending == null)
        {
            if (playbackRequested && !idle.FixtureActive)
            { playbackRequested = false; last = "Fixture playback finished or interrupted; " + idle.FixtureStatus; }
            return;
        }
        if (Time.unscaledTime < pendingAt || Time.unscaledTime - pendingAt >= 90)
        { CancelPending("Fixture start acquisition exceeded 90 seconds; normal direction resumes", idle); return; }
        if (!allowResume || !IdleShots.Ready(V(pending.Plan.Sample(0).CameraPosition))) return;
        CameraFixture fixture = pending;
        if (idle.RequestFixture(fixture.Plan, out string reason, fixture.MotionMultiplier, fixture.DynamicMovement))
        { pending = null; playbackRequested = true; last = "Validating " + fixture.Name + "; " + reason; }
        else { CancelPending("Fixture could not begin after acquisition: " + reason, idle); }
    }

    internal void CancelPending(string reason, IdleShots? idle = null)
    { pending = null; playbackRequested = false; pendingAt = 0; idle?.StopFixture(); last = reason; }

    internal void Draw(Camera camera, ScenicShotPlan? activePlan, float progress)
    {
        if (!PreviewEnabled || !camera || worldId == 0 || !ZNet.instance || ZNet.instance.GetWorldUID() != worldId) return;
        ScenicShotPlan? plan = selected?.Plan ?? activePlan;
        if (plan == null || !plan.IsDirected) return;
        preview.Draw(camera, plan, selected == null ? progress : -1,
            selected == null ? "Live Scenic path" : "Fixture " + selected.Name + " — awaiting current geometry validation");
    }

    internal void Reset()
    { selected = pending = null; playbackRequested = false; pendingAt = 0; PreviewEnabled = false; worldId = 0; last = "No fixture selected"; preview.Reset(); }
    public void Dispose() => preview.Dispose();

    private static string Settings(BroadcastSettings s, float effectiveMotion, bool effectiveDynamic) =>
        "motion=" + effectiveMotion.ToString("R", CultureInfo.InvariantCulture) +
        ";dynamic=" + effectiveDynamic + ";composition=" + s.DeliberateComposition.Value +
        ";nativeDolly=" + s.NativeSplineDolly.Value + ";confiner=" + s.NativeConfiner.Value +
        ";clearShot=" + s.NativeClearShot.Value + ";pictureEvaluation=" + s.RenderedPictureEvaluation.Value +
        ";positionDamping=" + s.PositionDamping.Value.ToString("R", CultureInfo.InvariantCulture) +
        ";aimDamping=" + s.AimDamping.Value.ToString("R", CultureInfo.InvariantCulture) +
        ";deadZone=" + s.DeadZone.Value.ToString("R", CultureInfo.InvariantCulture) +
        ";viewport=" + Screen.width + "x" + Screen.height +
        ";aspect=" + (Camera.main ? Camera.main.aspect.ToString("R", CultureInfo.InvariantCulture) : "unknown") +
        ";nearClip=" + (Camera.main ? Camera.main.nearClipPlane.ToString("R", CultureInfo.InvariantCulture) : "unknown");
    private static Vector3 V(AutonomyPosition value) => new Vector3(value.X, value.Y, value.Z);
}
