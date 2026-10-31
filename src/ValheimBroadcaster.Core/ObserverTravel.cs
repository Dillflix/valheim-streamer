using System;

namespace ValheimBroadcaster.Core
{
    public enum ObserverPhase { Off, Watching, Travelling, Returning, Suspended, Parked }

    /// <summary>Bounded travel policy; Unity adapter owns movement and reversible state.</summary>
    public sealed class ObserverTravel
    {
        public ObserverPhase Phase { get; private set; }
        public bool Active => Phase != ObserverPhase.Off;
        public string? Failure { get; private set; }
        private bool parkAfterReturn;
        public const double TimeoutSeconds = 45;
        public double ElapsedSeconds { get; private set; }
        public static bool CanActivate(bool server, bool open, int peers) => server && !open && peers == 0;

        public void Start()
        {
            if (Active) throw new InvalidOperationException("Observer already active.");
            Phase = ObserverPhase.Watching;
            Failure = null;
            ElapsedSeconds = 0;
        }

        public void Travel()
        {
            if (Phase != ObserverPhase.Watching) throw new InvalidOperationException("Finish current travel first.");
            Phase = ObserverPhase.Travelling;
            ElapsedSeconds = 0;
        }

        public void Return(bool park = false)
        {
            // Repeated stop commands must not restart the timer or native teleport.
            if (!Active || Phase == ObserverPhase.Returning || Phase == ObserverPhase.Parked) return;
            parkAfterReturn = park;
            Phase = ObserverPhase.Returning;
            Failure = null;
            ElapsedSeconds = 0;
        }

        public void Suspend(string reason)
        {
            if (!Active || Phase == ObserverPhase.Suspended) return;
            Phase = ObserverPhase.Suspended;
            Failure = reason;
        }

        public void Tick(double dt, bool teleporting, bool ready, bool atDestination)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (Phase != ObserverPhase.Travelling && Phase != ObserverPhase.Returning) return;
            ElapsedSeconds += dt;
            if (!teleporting && ready && atDestination)
            {
                Phase = Phase == ObserverPhase.Returning ? (parkAfterReturn ? ObserverPhase.Parked : ObserverPhase.Off) : ObserverPhase.Watching;
                ElapsedSeconds = 0;
                return;
            }
            if (ElapsedSeconds >= TimeoutSeconds)
                Suspend("Travel did not complete within 45 unpaused seconds; use broadcast observer return.");
        }

        public void Resume()
        {
            if (Phase != ObserverPhase.Parked) throw new InvalidOperationException("Observer is not parked.");
            Phase = ObserverPhase.Watching;
        }

        public void Reset()
        {
            Phase = ObserverPhase.Off;
            parkAfterReturn = false;
            Failure = null;
            ElapsedSeconds = 0;
        }
    }
}
