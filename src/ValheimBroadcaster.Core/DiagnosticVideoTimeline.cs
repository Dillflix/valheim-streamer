using System;

namespace ValheimBroadcaster.Core;

/// <summary>Maps capture clock to constant-frame-rate diagnostic video without
/// speeding up dropped/irregular samples. Short repeats are diagnostic evidence,
/// not claims that the live program held. Long gaps start a new segment.</summary>
public sealed class DiagnosticVideoTimeline
{
    public const int FramesPerSecond = 12, MaximumSegmentFrames = 300;
    public const double MaximumRepeatGap = .25;
    private double origin, previous = -1;
    private int lastSlot = -1;
    private long generation;
    public DiagnosticVideoSlot Offer(double at, long sourceGeneration)
    {
        if (double.IsNaN(at) || double.IsInfinity(at) || at < 0) throw new ArgumentOutOfRangeException(nameof(at));
        double gap = previous < 0 ? 0 : at - previous;
        int slot = previous < 0 ? 0 : (int)Math.Floor(Math.Min(int.MaxValue, Math.Max(0, at - origin) * FramesPerSecond + 1e-6));
        bool begin = previous < 0 || at < previous || sourceGeneration != generation ||
            gap > MaximumRepeatGap || slot >= MaximumSegmentFrames;
        if (!begin && slot <= lastSlot) return new DiagnosticVideoSlot(false, false, lastSlot, 0, gap);
        if (begin) { origin = at; slot = 0; lastSlot = -1; }
        int repeat = slot - lastSlot - 1;
        previous = at; generation = sourceGeneration; lastSlot = slot;
        return new DiagnosticVideoSlot(true, begin, slot, repeat, gap);
    }
}

public readonly struct DiagnosticVideoSlot
{
    public readonly bool Accepted, NewSegment;
    public readonly int Slot, Repeats;
    public readonly double SampleGap;
    internal DiagnosticVideoSlot(bool accepted, bool begin, int slot, int repeats, double gap)
    { Accepted = accepted; NewSegment = begin; Slot = slot; Repeats = repeats; SampleGap = gap; }
}
