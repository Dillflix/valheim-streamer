using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Frame-local shared scene-query work. Background scenic planning
    /// cannot consume the allowance reserved for on-air actor prediction.</summary>
    public sealed class CameraObservationBudget
    {
        public const int TotalUnits = 60, BackgroundUnits = 36;
        private int frame;
        private bool initialized;
        public int Spent { get; private set; }
        public int BackgroundSpent { get; private set; }

        public void BeginFrame(int frameNumber)
        {
            if (initialized && frameNumber == frame) return;
            frame = frameNumber; initialized = true;
            Spent = BackgroundSpent = 0;
        }

        public bool TrySpend(bool onAir, int units)
        {
            if (units <= 0 || units > TotalUnits) throw new ArgumentOutOfRangeException(nameof(units));
            if (!initialized || units > TotalUnits - Spent || !onAir && units > BackgroundUnits - BackgroundSpent)
                return false;
            Spent += units;
            if (!onAir) BackgroundSpent += units;
            return true;
        }

        public void Reset() { initialized = false; frame = 0; Spent = BackgroundSpent = 0; }
    }
}
