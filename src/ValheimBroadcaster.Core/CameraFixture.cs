using System;

namespace ValheimBroadcaster.Core;

/// <summary>A world-bound immutable camera input fixture. It records an authored
/// plan, not a promise that the world, actors or geometry will still match it.</summary>
public sealed class CameraFixture
{
    public const int SchemaVersion = 2;
    public long WorldId { get; }
    public string Name { get; }
    public string SourceVersion { get; }
    public string SettingsSummary { get; }
    public DateTime CreatedUtc { get; }
    public float MotionMultiplier { get; }
    public bool DynamicMovement { get; }
    public long SourceJourneyId { get; }
    public long SourcePredecessorId { get; }
    public ScenicShotPlan Plan { get; }

    public CameraFixture(long worldId, string name, string sourceVersion, string settingsSummary,
        DateTime createdUtc, float motionMultiplier, bool dynamicMovement, ScenicShotPlan plan,
        long? sourceJourneyId = null, long? sourcePredecessorId = null)
    {
        if (worldId == 0) throw new ArgumentOutOfRangeException(nameof(worldId), "A fixture requires the actual world UID.");
        if (plan == null || !plan.IsDirected || plan.IsPictureOnly || plan.Feature == null)
            throw new ArgumentException("Save an authored moving shot or finite pan; a recovery picture is not a camera fixture.", nameof(plan));
        if (float.IsNaN(motionMultiplier) || float.IsInfinity(motionMultiplier) || motionMultiplier < 0 || motionMultiplier > 1.5f)
            throw new ArgumentOutOfRangeException(nameof(motionMultiplier));
        if (createdUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Fixture timestamp must be UTC.", nameof(createdUtc));
        if (double.IsNaN(plan.DurationSeconds) || double.IsInfinity(plan.DurationSeconds) || plan.DurationSeconds < 1 || plan.DurationSeconds > 604800)
            throw new ArgumentOutOfRangeException(nameof(plan), "Fixture duration must be between one second and seven days.");
        WorldId = worldId; Name = NormalizeName(name); SourceVersion = Text(sourceVersion, 128, false);
        SettingsSummary = Text(settingsSummary, 4096, true); CreatedUtc = createdUtc;
        SourceJourneyId = sourceJourneyId ?? plan.JourneyId;
        SourcePredecessorId = sourcePredecessorId ?? plan.ContinuationOf;
        if (SourceJourneyId < 0 || SourcePredecessorId < 0 || SourceJourneyId == 0 && SourcePredecessorId != 0)
            throw new ArgumentException("Invalid source journey metadata.", nameof(sourceJourneyId));
        MotionMultiplier = motionMultiplier; DynamicMovement = dynamicMovement; Plan = plan.AsStandalone();
    }

    public void RequireWorld(long actualWorldId)
    {
        if (actualWorldId == 0 || actualWorldId != WorldId)
            throw new InvalidOperationException("This camera fixture belongs to a different world.");
    }

    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 48)
            throw new ArgumentException("Use a fixture name of 1–48 letters, digits, underscores or hyphens.", nameof(name));
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || i > 0 && (c == '_' || c == '-')))
                throw new ArgumentException("Use a fixture name of 1–48 letters, digits, underscores or hyphens, starting with a letter or digit.", nameof(name));
        }
        return name.ToLowerInvariant();
    }

    private static string Text(string value, int maximum, bool empty)
    {
        if (value == null || value.Length > maximum || !empty && string.IsNullOrWhiteSpace(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        foreach (char c in value) if (char.IsControl(c)) throw new ArgumentException("Fixture metadata must be single-line text.", nameof(value));
        return value;
    }
}
