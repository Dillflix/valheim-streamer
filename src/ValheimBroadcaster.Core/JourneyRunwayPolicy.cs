using System;

namespace ValheimBroadcaster.Core;

/// <summary>Schedules speed against measured production latency and certified
/// travel. This class grants no geometry authority and earns no travel credit:
/// the adapter supplies a fresh, seam-connected route/ending inventory and still
/// commits a finite ending if the producer cannot replenish it in time.</summary>
public sealed class JourneyRunwayPolicy
{
    public const int HistoryCapacity = 8, StartupSections = 2;
    public const double LocalReachMetres = 85;
    public const double BrakeSeconds = 2, SchedulingMarginSeconds = 1;
    public const double Acceleration = 1, Deceleration = 1.5;
    private readonly double[] preparation = new double[HistoryCapacity];
    private int count, next;
    private double lastCompletion = -1;
    public bool Started { get; private set; }
    public int Samples => count;
    public double EffectiveCruise { get; private set; }
    public double PreparationSeconds
    {
        get
        {
            // A recent slow successful preparation is a better planning bound
            // than a mean which hides the stalls that consume the runway.
            double longest = 0;
            for (int i = 0; i < count; i++) longest = Math.Max(longest, preparation[i]);
            return Math.Max(2, longest * 1.25 + .5);
        }
    }
    public double RequiredRunwaySeconds => PreparationSeconds + BrakeSeconds + SchedulingMarginSeconds;

    public void Reset()
    {
        Array.Clear(preparation, 0, preparation.Length); count = next = 0;
        lastCompletion = -1; Started = false; EffectiveCruise = 0;
    }

    /// <summary>Record one complete route plus independently certified ending,
    /// including real unheld scheduler/loading waits. Do not record only the
    /// route portion or maintenance of an already prepared proof.</summary>
    public void ObservePreparation(double seconds)
    {
        Number(seconds, nameof(seconds));
        preparation[next] = seconds; next = (next + 1) % HistoryCapacity;
        count = Math.Min(count + 1, HistoryCapacity);
    }

    /// <summary>Timestamp variant rejects repeated or reversed completions so
    /// polling the same finished work cannot replace slower history entries.</summary>
    public bool ObservePreparation(double startedAt, double completedAt)
    {
        Number(startedAt, nameof(startedAt)); Number(completedAt, nameof(completedAt));
        if (completedAt < startedAt || completedAt <= lastCompletion) return false;
        ObservePreparation(completedAt - startedAt); lastCompletion = completedAt;
        return true;
    }

    public bool TryStart(int connectedFreshSections, double validatedMetres, double requestedCruise, out string reason)
    {
        Number(validatedMetres, nameof(validatedMetres)); Speed(requestedCruise);
        if (connectedFreshSections < 0) throw new ArgumentOutOfRangeException(nameof(connectedFreshSections));
        if (connectedFreshSections < StartupSections)
        { reason = "Preparing a second validated connected Journey section before departure"; return false; }
        double supported = validatedMetres / RequiredRunwaySeconds;
        if (supported + .000001 < MinimumCruise(requestedCruise))
        {
            reason = "Validated Journey runway cannot sustain the minimum cruise at the measured preparation latency";
            return false;
        }
        EffectiveCruise = Math.Min(requestedCruise, supported); Started = true;
        reason = EffectiveCruise + .000001 >= requestedCruise
            ? "Connected Journey reserve supports the requested cruise"
            : "Connected Journey reserve supports a reduced cruise; requested speed remains the ceiling";
        return true;
    }

    /// <summary>Respond before the emergency braking point. A low buffer can
    /// reduce cruise only to a useful floor, never an indefinite near-zero crawl.
    /// The caller owns expiry, actual displacement and committed braking.</summary>
    public double StepSpeed(double requestedCruise, double validatedMetres, double deltaSeconds,
        bool held = false, bool braking = false)
    {
        Speed(requestedCruise); Number(validatedMetres, nameof(validatedMetres)); Number(deltaSeconds, nameof(deltaSeconds));
        if (!Started) throw new InvalidOperationException("A certified Journey reserve must be admitted before advancing cruise.");
        if (held || deltaSeconds == 0) return EffectiveCruise;
        double desired = Math.Max(MinimumCruise(requestedCruise),
            Math.Min(requestedCruise, validatedMetres / RequiredRunwaySeconds));
        if (braking) desired = Math.Min(desired, EffectiveCruise);
        double change = (desired > EffectiveCruise ? Acceleration : Deceleration) * Math.Min(.1, deltaSeconds);
        EffectiveCruise += Math.Max(-change, Math.Min(change, desired - EffectiveCruise));
        // Settings normally remain latched for a Journey. This guard also
        // preserves the explicit ceiling if a caller lowers it between ticks.
        EffectiveCruise = Math.Min(requestedCruise, EffectiveCruise);
        return EffectiveCruise;
    }

    public static double MinimumCruise(double requestedCruise)
    { Speed(requestedCruise); return Math.Max(2, requestedCruise * .5); }

    public static double GazeReach(double requestedCruise)
    { Speed(requestedCruise); return Math.Max(14, Math.Min(28, requestedCruise * 3.5)); }

    /// <summary>The full future picture needs loaded geometry, including its
    /// forward focus. Shorten a section to the remaining reach rather than wait
    /// for the entire nominal window. This is only a proposal-size bound.</summary>
    public static double WindowMetres(double requestedCruise, double seamDistance, double desiredMetres)
    {
        Speed(requestedCruise); Number(seamDistance, nameof(seamDistance)); Number(desiredMetres, nameof(desiredMetres));
        double metres = Math.Min(ScenicJourneyPolicy.WindowMetres,
            Math.Min(desiredMetres, Math.Max(0, LocalReachMetres - seamDistance - GazeReach(requestedCruise))));
        return metres + .000001 >= ScenicJourneyPolicy.MinimumWindowMetres ? metres : 0;
    }

    public static double RunwaySeconds(double validatedMetres, double cruise)
    { Number(validatedMetres, nameof(validatedMetres)); Speed(cruise); return validatedMetres / cruise; }

    /// <summary>An unrelated or stale queue head cannot postpone braking. Its
    /// exact predecessor and Journey identity are part of admission evidence.
    /// Zero is the separately certified independent departure predecessor;
    /// subsequent sections must match their nonzero predecessor exactly.</summary>
    public static bool HeadMatches(ScenicShotPlan? plan, long predecessor, long journey,
        long proofEpoch, double proofAt, long currentEpoch, double now) =>
        plan != null && plan.IsJourney && predecessor >= 0 && journey > 0 &&
        plan.ContinuationOf == predecessor && plan.JourneyId == journey &&
        JourneyHandoffPolicy.IsProofCurrent(proofEpoch, proofAt, currentEpoch, now);

    private static void Speed(double speed)
    {
        if (double.IsNaN(speed) || double.IsInfinity(speed) || speed < 2 || speed > 12)
            throw new ArgumentOutOfRangeException(nameof(speed));
    }
    private static void Number(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
