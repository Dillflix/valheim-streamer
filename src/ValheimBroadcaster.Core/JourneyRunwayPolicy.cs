using System;

namespace ValheimBroadcaster.Core;

/// <summary>Matches useful travel to the certified physical runway and its
/// observed producer. All metres supplied here must already have fresh,
/// seam-connected route and ending proofs. A loading horizon sizes proposals;
/// it never grants movement authority or counts as certified travel.</summary>
public sealed class JourneyRunwayPolicy
{
    public const int HistoryCapacity = 8, StartupSections = 1, MaximumBufferedSections = 8;
    public const double LocalReachMetres = 85;
    public const double SchedulingMarginSeconds = 1;
    public const double Acceleration = 1, Deceleration = 1.5;
    public const double ReactionSeconds = .25, StopMarginMetres = .5;
    public const double MinimumUsefulSeconds = 2, StoppedSpeed = .1;
    private readonly double[] preparation = new double[HistoryCapacity];
    private readonly double[] produced = new double[HistoryCapacity];
    private int count, next;
    private double lastCompletion = -1;
    public bool Started { get; private set; }
    public int Samples => count;
    public double EffectiveCruise { get; private set; }
    public bool Stopped => Started && EffectiveCruise == 0;

    /// <summary>Successful producer time is a scheduling estimate, never a
    /// startup requirement. Failed searches and proof maintenance are not jobs.
    /// A bounded rolling mean retires old stalls as new complete work arrives.</summary>
    public double PreparationSeconds
    {
        get
        {
            if (count == 0) return 4;
            double total = 0;
            for (int i = 0; i < count; i++) total += preparation[i];
            return Math.Max(2, total / count * 1.25 + .5);
        }
    }

    /// <summary>Only measured complete route+ending jobs supply throughput.
    /// Legacy duration-only observations remain useful for scheduling but do
    /// not invent how much connected route a job produced.</summary>
    public double ProductionMetresPerSecond
    {
        get
        {
            double metres = 0, seconds = 0;
            for (int i = 0; i < count; i++)
                if (produced[i] > 0) { metres += produced[i]; seconds += preparation[i]; }
            return seconds > 0 ? metres / seconds : 0;
        }
    }
    public double RequiredRunwaySeconds
    {
        get
        {
            double speed = Math.Max(2, EffectiveCruise);
            return PreparationSeconds + SchedulingMarginSeconds + StoppingDistance(speed) / speed;
        }
    }

    public void Reset()
    {
        Array.Clear(preparation, 0, preparation.Length); Array.Clear(produced, 0, produced.Length);
        count = next = 0; lastCompletion = -1; Started = false; EffectiveCruise = 0;
    }

    /// <summary>Compatibility observation for a successfully certified route
    /// and ending when its distance was not recorded. Never call for failure.</summary>
    public void ObservePreparation(double seconds)
    {
        Number(seconds, nameof(seconds)); Record(Math.Max(.001, seconds), 0);
    }

    public bool ObservePreparation(double startedAt, double completedAt) =>
        ObserveComplete(0, startedAt, completedAt);

    /// <summary>Record only a successfully certified route and safe ending.
    /// Start timing at this candidate's preparation, not the first failed search
    /// in its retry episode. Include its genuine scheduler/loading waits; omit
    /// proof renewal of geometry which was already counted.</summary>
    public bool ObservePreparation(double certifiedMetres, double startedAt, double completedAt)
    {
        Number(certifiedMetres, nameof(certifiedMetres));
        if (certifiedMetres == 0) throw new ArgumentOutOfRangeException(nameof(certifiedMetres));
        return ObserveComplete(certifiedMetres, startedAt, completedAt);
    }

    private bool ObserveComplete(double metres, double startedAt, double completedAt)
    {
        Number(startedAt, nameof(startedAt)); Number(completedAt, nameof(completedAt));
        if (completedAt < startedAt || completedAt <= lastCompletion) return false;
        Record(Math.Max(.001, completedAt - startedAt), metres); lastCompletion = completedAt;
        return true;
    }

    private void Record(double seconds, double metres)
    {
        preparation[next] = seconds; produced[next] = metres;
        next = (next + 1) % HistoryCapacity; count = Math.Min(count + 1, HistoryCapacity);
    }

    public bool TryStart(int connectedFreshSections, double validatedMetres, double requestedCruise, out string reason)
    {
        Number(validatedMetres, nameof(validatedMetres)); Speed(requestedCruise);
        if (connectedFreshSections < 0) throw new ArgumentOutOfRangeException(nameof(connectedFreshSections));
        if (connectedFreshSections == 0)
        { reason = "Preparing a fresh connected Journey route and safe ending"; return false; }
        // Two pieces of the same length do not create more physical runway.
        // Reserve useful movement and a real stopping distance, not an assumed
        // number of future jobs or their possibly unattainable latency horizon.
        double supported = MaximumSpeed(validatedMetres, ReactionSeconds + MinimumUsefulSeconds);
        if (supported + .000001 < MinimumCruise(requestedCruise))
        { reason = "Certified Journey runway is too short for useful movement and a safe stop"; return false; }
        EffectiveCruise = Math.Min(DesiredCruise(requestedCruise), supported); Started = true;
        reason = EffectiveCruise + .000001 >= requestedCruise
            ? "Certified physical runway supports departure at the requested cruise"
            : "Certified physical runway supports departure at an adapted useful cruise";
        return true;
    }

    /// <summary>Continuous speed control, including a real zero-speed stop.
    /// A contracted buffer can brake below the useful launch floor; it cannot
    /// force endless minimum-speed travel into an unproved successor. The
    /// mission owner decides how long to wait/replan and whether to end. This
    /// method supplies no movement authority if a proof is revoked.</summary>
    public double StepSpeed(double requestedCruise, double validatedMetres, double deltaSeconds,
        bool held = false, bool braking = false)
    {
        Speed(requestedCruise); Number(validatedMetres, nameof(validatedMetres)); Number(deltaSeconds, nameof(deltaSeconds));
        if (!Started) throw new InvalidOperationException("A certified Journey reserve must be admitted before advancing cruise.");
        if (held || deltaSeconds == 0) return EffectiveCruise;
        double desired = braking ? 0 : Math.Min(DesiredCruise(requestedCruise), MaximumSafeSpeed(validatedMetres));
        // A stopped camera resumes only when there is enough certified room for
        // useful travel. A few newly observed centimetres cannot cause creeping.
        if (Stopped && validatedMetres + .000001 < MinimumLaunchMetres(requestedCruise)) desired = 0;
        // Close the final centimetres of the speed envelope with a bounded
        // deceleration to zero, rather than asymptotically approaching a stop.
        if (desired <= StoppedSpeed) desired = 0;
        double change = (desired > EffectiveCruise ? Acceleration : Deceleration) * Math.Min(.1, deltaSeconds);
        EffectiveCruise += Math.Max(-change, Math.Min(change, desired - EffectiveCruise));
        EffectiveCruise = Math.Min(requestedCruise, EffectiveCruise);
        if (EffectiveCruise < .000001) EffectiveCruise = 0;
        return EffectiveCruise;
    }

    public double DesiredCruise(double requestedCruise)
    {
        Speed(requestedCruise);
        double throughput = ProductionMetresPerSecond;
        return throughput > 0 ? Math.Max(MinimumCruise(requestedCruise), Math.Min(requestedCruise, throughput * .9)) : requestedCruise;
    }

    public static double MinimumCruise(double requestedCruise)
    { Speed(requestedCruise); return 2; }

    public static double MinimumLaunchMetres(double requestedCruise)
    {
        double speed = MinimumCruise(requestedCruise);
        return StoppingDistance(speed) + speed * MinimumUsefulSeconds;
    }

    /// <summary>Physical stop reserve plus one bounded reaction interval.
    /// This is independent of whether the route is stored in one or eight pieces.</summary>
    public static double StoppingDistance(double speed)
    { Number(speed, nameof(speed)); return StopMarginMetres + speed * ReactionSeconds + speed * speed / (2 * Deceleration); }

    public static double MaximumSafeSpeed(double validatedMetres) => MaximumSpeed(validatedMetres, ReactionSeconds);

    private static double MaximumSpeed(double metres, double responseSeconds)
    {
        Number(metres, nameof(metres));
        double available = Math.Max(0, metres - StopMarginMetres);
        // Rationalized quadratic avoids cancellation for very short runway.
        return 2 * available / (Math.Sqrt(responseSeconds * responseSeconds + 2 * available / Deceleration) + responseSeconds);
    }

    /// <summary>Distance target for background work, bounded by the observed
    /// camera-and-gaze proposal horizon. It is not a launch or proof requirement.</summary>
    public double TargetRunwayMetres(double requestedCruise, double loadedRouteMetres)
    {
        Speed(requestedCruise); Number(loadedRouteMetres, nameof(loadedRouteMetres));
        double cruise = DesiredCruise(requestedCruise);
        double target = StoppingDistance(cruise) + cruise * (PreparationSeconds + SchedulingMarginSeconds);
        return Math.Min(loadedRouteMetres, Math.Max(MinimumLaunchMetres(requestedCruise), target));
    }

    public bool NeedsPreparation(double validatedMetres, double requestedCruise, double loadedRouteMetres)
    {
        Number(validatedMetres, nameof(validatedMetres));
        return validatedMetres + .5 < TargetRunwayMetres(requestedCruise, loadedRouteMetres);
    }

    /// <summary>Memory/scheduling hint only. Actual replenishment is driven by
    /// metres and current proof identity; a section count never certifies a path.</summary>
    public int TargetSectionCount(double requestedCruise, double loadedRouteMetres, double typicalSectionMetres)
    {
        Number(typicalSectionMetres, nameof(typicalSectionMetres));
        double target = TargetRunwayMetres(requestedCruise, loadedRouteMetres);
        if (target == 0 || typicalSectionMetres == 0) return 0;
        return (int)Math.Min(MaximumBufferedSections, Math.Max(1,
            Math.Ceiling(target / Math.Max(ScenicJourneyPolicy.MinimumWindowMetres, typicalSectionMetres))));
    }

    public static double GazeReach(double requestedCruise)
    { Speed(requestedCruise); return Math.Max(14, Math.Min(28, requestedCruise * 3.5)); }

    /// <summary>The full future picture needs loaded geometry, including the
    /// inherited seam aim while it develops toward the travelling gaze. This
    /// bounds a proposal, never substitutes for full route/ending preflight.</summary>
    public static double WindowMetres(double requestedCruise, double seamDistance, double desiredMetres,
        double loadedReachMetres = LocalReachMetres, double inheritedGazeMetres = 0)
    {
        Speed(requestedCruise); Number(seamDistance, nameof(seamDistance)); Number(desiredMetres, nameof(desiredMetres));
        Number(loadedReachMetres, nameof(loadedReachMetres)); Number(inheritedGazeMetres, nameof(inheritedGazeMetres));
        double reach = Math.Max(0, loadedReachMetres - seamDistance - Math.Max(GazeReach(requestedCruise), inheritedGazeMetres));
        double metres = Math.Min(ScenicJourneyPolicy.WindowMetres, Math.Min(desiredMetres, reach));
        return metres + .000001 >= ScenicJourneyPolicy.MinimumWindowMetres ? metres : 0;
    }

    public static double RunwaySeconds(double validatedMetres, double cruise)
    {
        Number(validatedMetres, nameof(validatedMetres)); Number(cruise, nameof(cruise));
        return cruise > 0 ? validatedMetres / cruise : validatedMetres == 0 ? 0 : double.PositiveInfinity;
    }

    /// <summary>An unrelated or stale queue head cannot postpone braking. Its
    /// exact predecessor and Journey identity are part of admission evidence.</summary>
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
