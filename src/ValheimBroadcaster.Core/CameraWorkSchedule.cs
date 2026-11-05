using System;

namespace ValheimBroadcaster.Core;

public enum CameraWorkKind { Live, Candidate, Scenic, Observation }

/// <summary>Measured, cooperative main-thread work scheduling. Live validation
/// is never rejected; alternatives share a finite time allowance. A native
/// operation is indivisible, so overruns are recorded and defer later work.</summary>
public sealed class CameraWorkSchedule
{
    public static CameraWorkKind FoliageQueryKind(bool withinLive) => withinLive ? CameraWorkKind.Live : CameraWorkKind.Observation;
    public const double BackgroundMilliseconds = 3;
    private readonly double[] spent = new double[4];
    private readonly int[] starts = new int[4];
    private int frame = -1;
    public int Deferred { get; private set; }
    public double LiveMilliseconds => spent[0];
    public double BackgroundSpent => spent[1] + spent[2] + spent[3];
    public double ObservationMilliseconds => spent[3];
    public double LargestOperation { get; private set; }

    public void BeginFrame(int value)
    {
        if (value == frame) return;
        frame = value; Array.Clear(spent, 0, spent.Length); Array.Clear(starts, 0, starts.Length);
        Deferred = 0; LargestOperation = 0;
    }

    public bool TryStart(CameraWorkKind kind)
    {
        int index = Index(kind);
        int maximum = kind == CameraWorkKind.Candidate ? 12 : 32;
        double allowance = kind == CameraWorkKind.Observation ? 1.5 : 2;
        if (kind != CameraWorkKind.Live && (BackgroundSpent >= BackgroundMilliseconds ||
            spent[index] >= allowance || !WithinSharedAllowance(kind) || starts[index] >= maximum))
        { Deferred++; return false; }
        starts[index]++; return true;
    }

    public bool CanContinue(CameraWorkKind kind)
    {
        int index = Index(kind);
        return kind == CameraWorkKind.Live || WithinSharedAllowance(kind) &&
            spent[index] < (kind == CameraWorkKind.Observation ? 1.5 : 2);
    }

    private bool WithinSharedAllowance(CameraWorkKind kind)
    {
        // Standby solving cannot starve discovery needed by the live picture.
        double reserved = kind == CameraWorkKind.Observation ? 0 : Math.Max(0, .75 - ObservationMilliseconds);
        return BackgroundSpent < BackgroundMilliseconds - reserved;
    }

    public void Charge(CameraWorkKind kind, double milliseconds)
    {
        if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        spent[Index(kind)] += milliseconds;
        LargestOperation = Math.Max(LargestOperation, milliseconds);
    }
    private static int Index(CameraWorkKind kind)
    {
        int index = (int)kind;
        if (index < 0 || index > 3) throw new ArgumentOutOfRangeException(nameof(kind));
        return index;
    }
}
