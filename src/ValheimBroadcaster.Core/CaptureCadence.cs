using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>A fixed-rate sample clock on a monotonic render timeline.
    /// A late render samples the latest due slot once; older slots are counted
    /// as skipped. Busy readback slots are consumed, never queued for a burst.</summary>
    public sealed class CaptureCadence
    {
        private int framesPerSecond;
        private bool started;
        private double epoch, lastNow;
        private long nextSlot;
        public long AdmittedSamples { get; private set; }
        public long SkippedSlots { get; private set; }
        public long ScheduledSlots => nextSlot;

        public void Reset(int rate)
        {
            if (rate < 1 || rate > 240) throw new ArgumentOutOfRangeException(nameof(rate));
            framesPerSecond = rate; started = false; epoch = lastNow = 0;
            nextSlot = AdmittedSamples = SkippedSlots = 0;
        }

        public bool TryCapture(double now, bool readbackAvailable)
        {
            if (framesPerSecond == 0) throw new InvalidOperationException("Set the capture rate before sampling.");
            AutonomousDirector.Time(now, nameof(now));
            if (started && now < lastNow) throw new ArgumentOutOfRangeException(nameof(now), "Reset before changing capture clocks.");
            if (!started) { epoch = now; started = true; }
            lastNow = now;
            // The tiny tolerance is in slots, not seconds. It only absorbs
            // floating-point rounding at an exact boundary; render jitter is
            // not accumulated into the sampling phase.
            double due = Math.Floor((now - epoch) * framesPerSecond + .000001);
            if (due >= long.MaxValue) throw new ArgumentOutOfRangeException(nameof(now));
            long slot = (long)due;
            if (slot < nextSlot) return false;
            long elapsedSlots = slot - nextSlot + 1;
            nextSlot = slot + 1;
            SkippedSlots += elapsedSlots - (readbackAvailable ? 1 : 0);
            if (!readbackAvailable) return false;
            AdmittedSamples++;
            return true;
        }
    }
}
