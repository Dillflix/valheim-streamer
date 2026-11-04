using System;

namespace ValheimBroadcaster.Core
{
    /// <summary>Scopes off-air preparation to one already loaded destination.
    /// This grants no presentation or native travel-completion permission.</summary>
    public sealed class ScenicArrivalPreparation
    {
        public const double MaximumSeconds = 20;
        public long Token { get; private set; }
        // Scene/carrier binding epoch, not mutable construction-observation revision.
        private long world, epoch;
        private AutonomyPosition destination;
        private double started;
        public bool Active => Token > 0;

        public bool MatchesRequest(long worldId, long token, AutonomyPosition point) =>
            Active && world == worldId && Token == token && destination.Distance(point) < .01;
        public bool Matches(long worldId, long token, AutonomyPosition point, long sceneEpoch) =>
            MatchesRequest(worldId, token, point) && epoch == sceneEpoch;

        public void Begin(long worldId, long token, AutonomyPosition point, long sceneEpoch, double now)
        {
            Time(now);
            if (worldId == 0 || token <= 0 || sceneEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(token));
            world = worldId; Token = token; destination = point; epoch = sceneEpoch; started = now;
        }

        public bool CanPrepare(long worldId, long token, AutonomyPosition point, long sceneEpoch, double now)
        {
            Time(now);
            return Matches(worldId, token, point, sceneEpoch) && now >= started && now - started <= MaximumSeconds;
        }

        public bool CanAdopt(long worldId, long token, AutonomyPosition point, long sceneEpoch,
            AutonomyPosition actualPosition, double now) =>
            CanPrepare(worldId, token, point, sceneEpoch, now) && actualPosition.Distance(destination) <= 5;

        public void Reset() { Token = world = epoch = 0; started = 0; destination = default; }
        private static void Time(double now)
        { if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now)); }
    }
}
