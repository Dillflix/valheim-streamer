using System;

namespace ValheimBroadcaster.Core;

/// <summary>Measured scenic rejection, kept distinct from unavailable evidence.</summary>
public enum ScenicRejectionKind
{
    None, InvalidPlan, Acquisition, Geometry, CarrierRange, CameraCollision,
    CorridorCollision, Focus, Foliage, SurveyIncomplete, SamplingBudget, CameraFormat, SurfaceClearance,
    InsideVegetation
}

/// <summary>Revokes permission immediately after failed proof renewal. A changing
/// sightline may be measured again briefly, but a physical contradiction cannot
/// be retried as if it were merely a preference. Retries never extend the window.</summary>
public sealed class JourneyProofRenewalPolicy
{
    public const double MaximumReadabilityRetrySeconds = 2;
    private double readabilityFailedAt = -1, observedAt = -1;
    private bool physicalContradiction, initialized;
    public bool Available { get; private set; }
    public bool RevalidatingReadability => readabilityFailedAt >= 0 && !physicalContradiction;

    // One evaluator slice can inspect several poses. Later missing/editorial
    // evidence must not hide an earlier measured physical contradiction.
    public static ScenicRejectionKind Stronger(ScenicRejectionKind previous, ScenicRejectionKind next) =>
        Strength(previous) > Strength(next) ? previous : next;

    private static int Strength(ScenicRejectionKind kind)
    {
        if (kind == ScenicRejectionKind.SurfaceClearance || kind == ScenicRejectionKind.CameraCollision ||
            kind == ScenicRejectionKind.CorridorCollision || kind == ScenicRejectionKind.InsideVegetation ||
            kind == ScenicRejectionKind.InvalidPlan || kind == ScenicRejectionKind.CameraFormat) return 3;
        if (kind == ScenicRejectionKind.Focus || kind == ScenicRejectionKind.Foliage) return 2;
        return kind == ScenicRejectionKind.None ? 0 : 1;
    }

    public void Begin(double validatedAt)
    {
        if (!Finite(validatedAt) || validatedAt < 0) throw new ArgumentOutOfRangeException(nameof(validatedAt));
        Reset(); initialized = true; observedAt = validatedAt; Available = true;
    }

    public bool MustRetire(double now) => !initialized || physicalContradiction || !Finite(now) || now < 0 || now < observedAt ||
        readabilityFailedAt >= 0 && now - readabilityFailedAt >= MaximumReadabilityRetrySeconds;

    public void Reject(ScenicRejectionKind kind, double now)
    {
        Available = false;
        if (MustRetire(now)) { physicalContradiction = true; return; }
        observedAt = now;
        if (kind == ScenicRejectionKind.Focus || kind == ScenicRejectionKind.Foliage)
        {
            if (readabilityFailedAt < 0) readabilityFailedAt = now;
        }
        else if (Strength(kind) == 3)
            physicalContradiction = true;
    }

    // Only the adapter's complete unchanged-plan validation may call this.
    public bool TryAccept(double now)
    {
        if (MustRetire(now)) { Available = false; return false; }
        readabilityFailedAt = -1; observedAt = now; Available = true; return true;
    }

    public void Reset()
    { Available = physicalContradiction = initialized = false; readabilityFailedAt = observedAt = -1; }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
