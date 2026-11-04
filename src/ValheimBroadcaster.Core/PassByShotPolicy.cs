using System;

namespace ValheimBroadcaster.Core
{
    public readonly struct PassByShotPlan
    {
        public readonly AutonomyPosition Camera, Origin, Direction;
        public readonly float Lead, Clearance;
        public PassByShotPlan(AutonomyPosition camera, AutonomyPosition origin, AutonomyPosition direction, float lead, float clearance)
        { Camera = camera; Origin = origin; Direction = direction; Lead = lead; Clearance = clearance; }
        public float Along(AutonomyPosition point) => (point.X - Camera.X) * Direction.X + (point.Z - Camera.Z) * Direction.Z;
    }

    /// <summary>A finite roadside observation: planning may follow the route while
    /// on standby, but the accepted camera point never follows the subject.
    /// Geometry and picture safety are supplied by the adapter, never inferred here.</summary>
    public sealed class PassByShotPolicy
    {
        public const double CooldownSeconds = 35;
        public PassByShotPlan Plan { get; private set; }
        public bool Active { get; private set; }
        public bool Offered { get; private set; }
        public string Reason { get; private set; } = "awaiting sustained open travel";
        private double lastAt = -1, steadySince = -1, startedAt, cooldownUntil, stoppedAt = -1;
        private long scene;
        private AutonomyPosition previous, heading;
        private int side = 1;

        public bool Update(double now, long sceneId, AutonomyPosition subject, AutonomyPosition velocity,
            bool allowed, bool displayed, float preferredSide = 1)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
            { Reset(); Reason = "invalid observation clock"; return false; }
            bool gap = lastAt >= 0 && (now < lastAt || now - lastAt > 1 || scene != sceneId || previous.Distance(subject) > 16);
            if (gap) { Reset(); cooldownUntil = now + 3; Reason = "scene or movement discontinuity"; }
            scene = sceneId; lastAt = now; previous = subject;
            double speed = Math.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            if (!allowed || speed > 9 || Math.Abs(velocity.Y) > 3)
            { End(now, "pass-by yielded to activity or constrained movement"); return false; }
            if (Active)
            {
                if (!displayed) { End(now, "pass-by coverage ended"); return false; }
                if (speed < .8)
                {
                    if (stoppedAt < 0) stoppedAt = now;
                    if (now - stoppedAt >= .5) { End(now, "route stopped"); return false; }
                }
                else stoppedAt = -1;
                double direction = speed > .001 ? (velocity.X * Plan.Direction.X + velocity.Z * Plan.Direction.Z) / speed : 1;
                double sideDrift = Math.Abs((subject.X - Plan.Origin.X) * Plan.Direction.Z - (subject.Z - Plan.Origin.Z) * Plan.Direction.X);
                if (direction < .7 || sideDrift > 3 || Math.Abs(subject.Y - Plan.Origin.Y) > 3 || subject.HorizontalDistance(Plan.Camera) < 4)
                { End(now, "route changed or pass clearance lost"); return false; }
                if (now - startedAt > 12 || Plan.Along(subject) > Math.Max(6, Plan.Lead * .7f))
                { End(now, "departure complete"); return false; }
                Offered = true;
                Reason = Plan.Along(subject) < -2 ? "pass-by: approach" : Plan.Along(subject) > 2 ? "pass-by: departure" : "pass-by: crossing";
                return true;
            }
            Offered = false;
            if (speed < 1.2 || now < cooldownUntil)
            { steadySince = -1; Reason = now < cooldownUntil ? "pass-by rearm" : "awaiting sustained open travel"; return false; }
            var directionNow = new AutonomyPosition((float)(velocity.X / speed), 0, (float)(velocity.Z / speed));
            if (steadySince < 0 || directionNow.X * heading.X + directionNow.Z * heading.Z < .94f)
            { steadySince = now; heading = directionNow; }
            if (now - steadySince < 2) { Reason = "measuring travel direction"; return false; }
            float lead = (float)Math.Max(8, speed * 2), clearance = (float)Math.Max(6, speed * 1.8);
            float chosenSide = preferredSide < 0 ? -side : side;
            float x = subject.X + directionNow.X * lead + directionNow.Z * clearance * chosenSide;
            float z = subject.Z + directionNow.Z * lead - directionNow.X * clearance * chosenSide;
            if (Math.Abs(x) > 20000 || Math.Abs(z) > 20000 || Math.Abs(subject.Y + 2) > 20000)
            { Reason = "pass-by outside supported world"; return false; }
            Plan = new PassByShotPlan(new AutonomyPosition(x, subject.Y + 2, z), subject, directionNow, lead, clearance);
            Offered = true; Reason = "pass-by candidate ready"; return true;
        }

        public void Commit(double now)
        {
            if (!Offered || Active || double.IsNaN(now) || double.IsInfinity(now) || now < lastAt || now - lastAt > .5) return;
            Active = true; startedAt = now; stoppedAt = -1; Reason = "pass-by: approach";
        }
        public void Reject(double now, string reason) => End(now, reason);
        private void End(double now, string reason)
        {
            if (Active || Offered) { cooldownUntil = now + CooldownSeconds; side = -side; }
            Active = Offered = false; steadySince = stoppedAt = -1; Reason = reason;
        }
        public void Reset()
        {
            Plan = default; Active = Offered = false; lastAt = steadySince = stoppedAt = -1;
            startedAt = cooldownUntil = 0; scene = 0; side = 1; previous = heading = default;
            Reason = "awaiting sustained open travel";
        }
    }
}
