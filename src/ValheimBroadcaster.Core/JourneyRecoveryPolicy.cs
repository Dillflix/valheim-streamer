using System;

namespace ValheimBroadcaster.Core;

public enum JourneyRecoveryState { None, Preparing, Ready, Coverage, Resumed, Exhausted }

/// <summary>A local interruption does not discard a still-valid itinerary.
/// The exact prepared ending supplies finite coverage while a replacement
/// departure earns the same two-section reserve as the original departure.
/// This owner never supplies geometry authority or resets Journey progress.</summary>
public sealed class JourneyRecoveryPolicy
{
    public const double MinimumReadableSeconds = 3;
    public const double MaximumSeconds = JourneyHandoffPolicy.EndingSeconds + JourneyHandoffPolicy.MaximumSettledSeconds;
    public long Journey { get; private set; }
    public long Ending { get; private set; }
    public long Epoch { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public double ReadableSeconds { get; private set; }
    public JourneyRecoveryState State { get; private set; }
    public bool Active => State == JourneyRecoveryState.Preparing || State == JourneyRecoveryState.Ready || State == JourneyRecoveryState.Coverage;
    public bool OwnsPreparedEnding => Active && Ending > 0;
    public string Reason { get; private set; } = "No local Journey repair";

    public void Begin(long journey, long ending, long epoch)
    {
        if (journey <= 0 || ending < 0 || epoch <= 0) throw new ArgumentOutOfRangeException(nameof(journey));
        if (Active) throw new InvalidOperationException("An active local repair cannot renew its own deadline.");
        Journey = journey; Ending = ending; Epoch = epoch;
        ElapsedSeconds = ReadableSeconds = 0; State = ending > 0 ? JourneyRecoveryState.Preparing : JourneyRecoveryState.Coverage;
        Reason = "Repairing the local route while retaining the destination and prepared ending";
    }

    public JourneyRecoveryState Observe(long journey, long ending, long epoch, double deltaSeconds,
        bool readable, bool held, bool reserveReady, bool canContinue)
    {
        if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!Active) return State;
        if (journey != Journey || OwnsPreparedEnding && ending != Ending || epoch != Epoch || !canContinue)
            return Exhaust("Local Journey repair lost its scene, ending, destination or runtime authority");
        if (held) return State;
        ElapsedSeconds += deltaSeconds;
        if (readable) ReadableSeconds += deltaSeconds;
        if (OwnsPreparedEnding && ElapsedSeconds >= MaximumSeconds)
        {
            // An ending's editorial lifetime is not the mission lifetime.
            // Give picture selection back to independent local coverage while
            // retaining the original destination and external runtime limit.
            Ending = 0; State = JourneyRecoveryState.Coverage;
        }
        if (reserveReady && readable && ReadableSeconds >= MinimumReadableSeconds)
        { State = JourneyRecoveryState.Resumed; Reason = "Validated local departure resumes the same Journey and destination"; }
        else
        {
            State = OwnsPreparedEnding ? reserveReady ? JourneyRecoveryState.Ready : JourneyRecoveryState.Preparing : JourneyRecoveryState.Coverage;
            Reason = reserveReady ? "Local route repaired; retaining useful readable coverage before resuming" :
                OwnsPreparedEnding ? "Preparing a validated local departure and connected reserve; destination retained" :
                "Local coverage owns the picture while the same Journey destination is repaired";
        }
        return State;
    }

    private JourneyRecoveryState Exhaust(string reason)
    { State = JourneyRecoveryState.Exhausted; Reason = reason; return State; }

    /// <summary>A local observation generation invalidates its proofs, not the
    /// world-scoped destination. The adapter must verify world/catalogue
    /// authority before rebinding and discard every old-scene proposal.</summary>
    public void RebindScene(long epoch)
    {
        if (epoch <= 0 || !Active) throw new ArgumentOutOfRangeException(nameof(epoch));
        Epoch = epoch; Ending = 0; ReadableSeconds = 0; State = JourneyRecoveryState.Coverage;
        Reason = "Rebuilding local Journey proofs in the current scene; destination retained";
    }

    /// <summary>Repeatedly relabelled views do not justify repeating a failed
    /// spatial search. A genuinely different local pose can propose a new exit.</summary>
    public static bool DifferentSeed(AutonomyPosition oldPosition, AutonomyPosition oldAim,
        AutonomyPosition position, AutonomyPosition aim)
    {
        if (oldPosition.Distance(position) >= 4) return true;
        double ax = oldAim.X - oldPosition.X, az = oldAim.Z - oldPosition.Z;
        double bx = aim.X - position.X, bz = aim.Z - position.Z;
        double denominator = Math.Sqrt((ax * ax + az * az) * (bx * bx + bz * bz));
        return denominator > .000001 && (ax * bx + az * bz) / denominator < .9396926207859084;
    }

    public void Reset()
    {
        Journey = Ending = Epoch = 0; ElapsedSeconds = ReadableSeconds = 0;
        State = JourneyRecoveryState.None; Reason = "No local Journey repair";
    }
}
