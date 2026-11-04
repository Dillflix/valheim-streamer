using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Interval photography has two clocks: real source time and an
    /// explicitly selected output frame rate. Missing intervals are not filled
    /// with repeated images and a late render never produces a catch-up burst.</summary>
    public sealed class TimelapseClock
    {
        public const double MinimumIntervalSeconds = .25, MaximumIntervalSeconds = 3600;
        public const double MaximumCaptureSeconds = 7 * 24 * 60 * 60;
        public const int MaximumFrames = 216000;
        private double start = -1, last = -1;
        private long nextSlot;
        public double IntervalSeconds { get; }
        public double CaptureSeconds { get; }
        public int OutputFps { get; }
        public long OfferedFrames { get; private set; }
        public long MissedIntervals { get; private set; }
        public int PlannedFrames { get; }
        public double PlannedOutputSeconds => (double)PlannedFrames / OutputFps;
        public double SpeedMultiplier => IntervalSeconds * OutputFps;
        public bool Started => start >= 0;

        public TimelapseClock(double intervalSeconds, double captureSeconds, int outputFps)
        {
            if (!Finite(intervalSeconds) || intervalSeconds < MinimumIntervalSeconds || intervalSeconds > MaximumIntervalSeconds)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Interval must be 0.25–3600 seconds.");
            if (!Finite(captureSeconds) || captureSeconds < intervalSeconds || captureSeconds > MaximumCaptureSeconds)
                throw new ArgumentOutOfRangeException(nameof(captureSeconds), "Capture duration must cover an interval and be at most seven days.");
            if (outputFps < 1 || outputFps > 60) throw new ArgumentOutOfRangeException(nameof(outputFps), "Output FPS must be 1–60.");
            double frames = Math.Ceiling(captureSeconds / intervalSeconds);
            if (frames > MaximumFrames) throw new ArgumentOutOfRangeException(nameof(captureSeconds), "At most 216000 interval frames per clip.");
            IntervalSeconds = intervalSeconds; CaptureSeconds = captureSeconds; OutputFps = outputFps; PlannedFrames = (int)frames;
        }

        public void Start(double now)
        {
            ValidateTime(now); start = last = now; nextSlot = OfferedFrames = MissedIntervals = 0;
        }

        public bool Expired(double now)
        {
            ValidateTime(now);
            return Started && (now < start || now - start >= CaptureSeconds);
        }

        public double Remaining(double now)
        {
            ValidateTime(now);
            return Started ? Math.Max(0, CaptureSeconds - Math.Max(0, now - start)) : CaptureSeconds;
        }

        public bool Take(double now)
        {
            ValidateTime(now);
            if (!Started) throw new InvalidOperationException("Timelapse source clock is not started.");
            if (now < last) throw new InvalidOperationException("Timelapse source clock moved backwards.");
            last = now;
            if (Expired(now)) return false;
            double elapsed = now - start;
            long slot = (long)Math.Floor((elapsed + 1e-7) / IntervalSeconds);
            if (slot < nextSlot) return false;
            MissedIntervals += slot - nextSlot;
            nextSlot = slot + 1; OfferedFrames++;
            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void ValidateTime(double value)
        { if (!Finite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
    }
}
