using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Structural measurements, never a claim that network scene data is loaded.</summary>
    public readonly struct RoomObservation
    {
        public readonly bool Complete, Dungeon, Opening;
        public readonly int RoofSamples, WallSamples;
        public readonly float CeilingHeight;
        public RoomObservation(bool complete, bool dungeon, int roofSamples, int wallSamples,
            float ceilingHeight, bool opening)
        {
            if (roofSamples < 0 || roofSamples > 5 || wallSamples < 0 || wallSamples > 8 ||
                float.IsNaN(ceilingHeight) || float.IsInfinity(ceilingHeight) || ceilingHeight < 0)
                throw new ArgumentException("Room measurements must be bounded, finite and nonnegative.");
            Complete = complete; Dungeon = dungeon; RoofSamples = roofSamples;
            WallSamples = wallSamples; CeilingHeight = ceilingHeight; Opening = opening;
        }
    }

    /// <summary>Hysteresis for aesthetic room constraints, independent of the game's dungeon layer.</summary>
    public sealed class RoomAwarenessPolicy
    {
        private double lastAt = -1, pendingSince, doorwayUntil;
        private ShotEnvironment pending = ShotEnvironment.Unknown;
        public ShotEnvironment Environment { get; private set; } = ShotEnvironment.Unknown;
        public float Confidence { get; private set; }
        public float CeilingHeight { get; private set; }
        public bool DoorwayTransition { get; private set; }

        public void Observe(double now, RoomObservation value)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if (lastAt >= 0 && (now < lastAt || now - lastAt > 1)) Reset();
            lastAt = now;
            if (!value.Complete)
            {
                Environment = pending = ShotEnvironment.Unknown; Confidence = 0;
                DoorwayTransition = false; CeilingHeight = 0; pendingSince = now; doorwayUntil = 0;
                return;
            }
            CeilingHeight = value.CeilingHeight;
            if (value.Opening) doorwayUntil = now + 1.5;
            DoorwayTransition = value.Opening || now < doorwayUntil;
            ShotEnvironment desired = value.Dungeon ? ShotEnvironment.Dungeon :
                DoorwayTransition ? ShotEnvironment.Doorway : value.RoofSamples >= 3 && value.WallSamples >= 2
                ? ShotEnvironment.Room : value.RoofSamples >= 2 ? ShotEnvironment.Sheltered : ShotEnvironment.Open;
            if (pending != desired) { pending = desired; pendingSince = now; }
            // An observed doorway needs restraint immediately; open-space freedom
            // must be earned by persistent measurements after leaving it.
            if (desired == ShotEnvironment.Dungeon || desired == ShotEnvironment.Doorway ||
                Environment == ShotEnvironment.Unknown || now - pendingSince >= (desired == ShotEnvironment.Open ? 1.25 : .75))
                Environment = desired;
            Confidence = Environment == desired ? (desired == ShotEnvironment.Open ? .75f :
                desired == ShotEnvironment.Dungeon ? 1 : desired == ShotEnvironment.Doorway ? .8f :
                Math.Min(.95f, .55f + value.RoofSamples * .05f + value.WallSamples * .025f)) : .45f;
        }

        public void Reset()
        {
            lastAt = -1; pendingSince = doorwayUntil = 0;
            Environment = pending = ShotEnvironment.Unknown; Confidence = CeilingHeight = 0; DoorwayTransition = false;
        }
    }

    /// <summary>Measured locomotion profiles with persistence; speed is an observation, not keyboard intent.</summary>
    public sealed class LocomotionObservationPolicy
    {
        private double lastAt = -1, pendingSince;
        private ShotLocomotion pending;
        public ShotLocomotion Locomotion { get; private set; } = ShotLocomotion.Stationary;
        public float Confidence { get; private set; }
        public void Observe(double now, float horizontalSpeed, float verticalSpeed, bool swimming, bool supportedByShip)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 ||
                float.IsNaN(horizontalSpeed) || float.IsInfinity(horizontalSpeed) || horizontalSpeed < 0 ||
                float.IsNaN(verticalSpeed) || float.IsInfinity(verticalSpeed)) throw new ArgumentOutOfRangeException(nameof(now));
            if (lastAt >= 0 && (now < lastAt || now - lastAt > 1)) Reset();
            ShotLocomotion desired = supportedByShip ? ShotLocomotion.Boat : swimming ? ShotLocomotion.Swim :
                Math.Abs(verticalSpeed) >= .65f && Math.Abs(verticalSpeed) >= horizontalSpeed * .22f ? ShotLocomotion.Vertical :
                horizontalSpeed >= (Locomotion == ShotLocomotion.Run ? 4.2f : 5f) ? ShotLocomotion.Run :
                horizontalSpeed >= (Locomotion == ShotLocomotion.Stationary ? .8f : .35f) ? ShotLocomotion.Walk : ShotLocomotion.Stationary;
            if (lastAt < 0) { pending = desired; pendingSince = now; }
            if (pending != desired) { pending = desired; pendingSince = now; }
            lastAt = now;
            if (now - pendingSince >= (desired == ShotLocomotion.Boat || desired == ShotLocomotion.Swim ? .25 : .6))
                Locomotion = desired;
            Confidence = Locomotion == desired ? (supportedByShip || swimming ? .9f : .8f) : .35f;
        }
        public void Reset() { lastAt = -1; pendingSince = 0; pending = Locomotion = ShotLocomotion.Stationary; Confidence = 0; }
    }
}
