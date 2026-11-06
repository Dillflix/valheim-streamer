using System;
using System.IO;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Local automatic evidence, independent of the operator overlay and
/// video encoder. Only the main thread supplies Unity observations.</summary>
internal static class ScenicTrace
{
    private static CameraTraceWriter? writer;
    private static Action<string>? log;
    private static string? directory;
    private static int retentionMiB;
    private static long sequence;
    private static bool reportedError;
    private static float nextPicture, nextWarning;
    private static bool lastUsable, lastOnAir;
    internal static bool Enabled => writer != null && writer.Error == null;
    internal static string SessionId => writer?.SessionId ?? "";
    internal static long Sequence => sequence;
    internal static string Status => writer == null ? "Scenic trace: disabled" :
        $"Scenic trace: {writer.Written} records, {writer.Pending} queued, {writer.Dropped} dropped; " +
        (writer.Error == null ? writer.DirectoryPath : "I/O stopped: " + writer.Error);

    internal static void Initialize(string root, Action<string> logger, int traceRetentionMiB)
    { directory = Path.Combine(root, "diagnostics", "ValheimBroadcaster"); log = logger; retentionMiB = traceRetentionMiB; Start(); }
    internal static void Start()
    {
        if (writer != null || directory == null) return;
        string header = new CameraTraceRecord("header", 0).Field("version", BroadcasterPlugin.PluginVersion)
            .Field("protocol", ObserverProtocol.Version).Field("unity", Application.unityVersion)
            .Field("utc", DateTime.UtcNow.ToString("O")).ToString();
        writer = new CameraTraceWriter(directory, header, retentionBytes: retentionMiB * 1024L * 1024); reportedError = false;
        log?.Invoke("Automatic scenic evidence capture enabled: " + directory + " (" + retentionMiB + " MiB rolling retention). No upload is performed.");
    }
    internal static CameraTraceRecord Row(string kind) => new CameraTraceRecord(kind, Time.unscaledTime)
        .Field("seq", ++sequence).Field("frame", Time.frameCount).Field("epoch", CameraSpatialScene.Shared.Epoch);
    internal static void Record(CameraTraceRecord record) { writer?.Record(record.ToString()); }
    internal static CameraTraceRecord Vector(CameraTraceRecord row, string name, Vector3 value) =>
        row.Field(name + "X", value.x).Field(name + "Y", value.y).Field(name + "Z", value.z);
    internal static void Event(string source, string reason, long plan = 0)
    { if (Enabled) Record(Row("event").Field("owner", source).Field("reason", reason).Field("plan", plan)); }
    internal static void Plan(ScenicShotPlan plan)
    {
        if (!Enabled) return;
        CameraTraceRecord row = Row("scenic-admission").Field("plan", plan.Id).Field("feature", plan.FeatureId)
            .Field("parent", plan.Feature?.ParentId).Field("scope", plan.Feature?.ScopeLabel)
            .Field("featureKind", plan.FeatureKind.ToString()).Field("role", plan.Role.ToString())
            .Field("duration", plan.DurationSeconds).Field("travel", plan.TravelMetres)
            .Field("explicitDuration", plan.HasExplicitDuration).Field("paced", plan.PacingProfile != null)
            .Field("pacingPoints", plan.PacingProfile?.Count ?? 0)
            .Field("fov", plan.FieldOfView).Field("spatial", plan.IsSpatial).Field("pan", plan.IsPan)
            .Field("picture", plan.IsPictureOnly).Field("journey", plan.JourneyId).Field("journeyView", plan.UsesJourneyView)
            .Field("journeyCruise", plan.JourneyCruiseSpeed)
            .Field("rejectedContinuation", plan.HasRejectedContinuation).Field("continuationReason", plan.ContinuationRejection);
        if (plan.IsSpatial)
        {
            row.Field("waypoints", plan.WaypointCount);
            for (int i = 0; i < plan.WaypointCount; i++)
            {
                AutonomyPosition camera = plan.CameraWaypoint(i), aim = plan.AimWaypoint(i);
                row.Field("camera" + i + "X", camera.X).Field("camera" + i + "Y", camera.Y).Field("camera" + i + "Z", camera.Z)
                    .Field("aim" + i + "X", aim.X).Field("aim" + i + "Y", aim.Y).Field("aim" + i + "Z", aim.Z);
            }
        }
        Record(row);
    }
    internal static void Picture(bool onAir, bool usable, bool held, long plan, float elapsed, bool expired, string reason)
    {
        if (!Enabled || !onAir && !lastOnAir) return;
        float now = Time.unscaledTime;
        if (onAir != lastOnAir || usable != lastUsable || now >= nextPicture)
        {
            Record(Row("picture").Field("onAir", onAir).Field("usable", usable).Field("held", held)
                .Field("plan", plan).Field("missingSeconds", elapsed).Field("expired", expired).Field("reason", reason));
            nextPicture = now + .25f;
        }
        lastOnAir = onAir; lastUsable = usable;
    }
    internal static void Tick()
    {
        if (writer?.Error != null && !reportedError)
        { reportedError = true; log?.Invoke(Status + "; camera operation continues without file recording."); }
    }
    internal static void Incomplete(string owner, ScenicCoverageGap gap, float distance, float radius, int omitted)
    {
        if (!Enabled || Time.unscaledTime < nextWarning) return;
        nextWarning = Time.unscaledTime + 15;
        log?.Invoke($"Scenic survey evidence: {owner}; missing {gap}; camera/survey distance {distance:F1}m; " +
            $"retained radius {radius:F1}m; omitted {omitted}. Details are captured automatically. {Status}");
    }
    internal static void Stop() { writer?.Dispose(); writer = null; }
}
