using System;
namespace ValheimBroadcaster.Core
{
    public enum ProbeState { Idle, Acquiring, Viewing, TimedOut, Completed, Stopped, Failed }
    /// <summary>Bounded solo scene acquisition; readiness never implies full scene fidelity.</summary>
    public sealed class SceneProbe
    {
        public ProbeState State { get; private set; }
        public bool Active => State == ProbeState.Acquiring || State == ProbeState.Viewing;
        public string? FailureReason { get; private set; }
        public void Fail(string reason) { State = ProbeState.Failed; FailureReason = reason; }
        public double Elapsed { get; private set; }
        public double? FirstReadySeconds { get; private set; }
        private double waiting;
        public void Start() { State = ProbeState.Acquiring; Elapsed = waiting = 0; FirstReadySeconds = null; FailureReason = null; }
        public void Stop() { if (Active) State = ProbeState.Stopped; }
        public void Tick(double dt, bool ready)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (!Active) return;
            Elapsed += dt;
            if (Elapsed >= 120) { State = ProbeState.Completed; return; }
            if (ready)
            {
                FirstReadySeconds ??= Elapsed;
                waiting = 0; State = ProbeState.Viewing;
            }
            else
            {
                waiting += dt;
                State = waiting >= 30 ? ProbeState.TimedOut : ProbeState.Acquiring;
            }
        }
    }
}
