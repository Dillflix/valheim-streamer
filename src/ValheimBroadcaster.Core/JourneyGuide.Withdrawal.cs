using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

public sealed partial class JourneyGuide
{
    // Recent manoeuvres are enough for local egress; the world itinerary owns
    // long-distance navigation. Every replayed movement still gets a live sweep.
    private const int TrailCapacity = 160;
    private readonly List<AutonomyPosition> recentPath = new List<AutonomyPosition>(TrailCapacity);
    private AutonomyPosition motionAnchor, failedPosition;
    private AutonomyPosition? escapeTarget, rearWaypoint;
    private double slowSeconds, escapeScan, lastBearing, withdrawn, lastEscapeSide;
    private bool trackMotion, frontier, returnBlocked, withdrawing;

    private void TrackEscapeProgress(double dt)
    {
        if (!trackMotion || Position.HorizontalDistance(motionAnchor) >= 1.5)
        {
            motionAnchor = Position;
            slowSeconds = 0;
            trackMotion = true;
        }
        else slowSeconds += dt;
    }

    private void RememberTravel(AutonomyPosition from, AutonomyPosition next)
    {
        if (recentPath.Count == 0) recentPath.Add(from);
        if (recentPath[recentPath.Count - 1].Distance(next) < .65) return;
        if (recentPath.Count == TrailCapacity) recentPath.RemoveAt(0);
        recentPath.Add(next);
    }

    private bool TryBeginWithdrawal(IJourneyGuideWorld world, AutonomyPosition target)
    {
        // Give normal steering and the existing local climb a chance first.
        // Waiting at the loading boundary must not send the guide back into
        // already travelled space. A covered dead end can forbid every bearing
        // in the forward fan, including the climb.
        if (slowSeconds < 3.5 || frontier || lifting && speedLimit > 0) return false;
        double bearing = Bearing(Position, target);
        if (!world.TryGetSurface(Along(Position, Direction(bearing, 0), 4), out _)) return false;
        failedPosition = Position;
        lastBearing = bearing;
        withdrawn = 0;
        escapeScan = 0;
        escapeTarget = rearWaypoint = null;
        returnBlocked = false;
        withdrawing = true;
        Speed = 0;
        return true;
    }

    private void FollowWithdrawal(double dt, IJourneyGuideWorld world)
    {
        escapeScan -= dt;
        if (escapeScan <= 0)
        {
            escapeScan = ScanInterval;
            // At a previously traversed fork, change course far enough to
            // clear the failed approach before resuming itinerary steering.
            bool noHistory = recentPath.Count == 0;
            if ((returnBlocked || noHistory || withdrawn >= 3) &&
                (TryResumeThroughOpening(world) || TryChooseExit(world))) return;
            // With no history (e.g. a departure in an existing cul-de-sac),
            // probe the unsampled rear direction. Take only a short verified
            // step, and never extend a blind withdrawal indefinitely.
            rearWaypoint = null;
            if (noHistory && withdrawn < 72)
            {
                var rear = Direction(lastBearing + 180, 0);
                double clear = SafeClearance(world, Position, Along(Position, rear, 5));
                if (clear > .5) rearWaypoint = Along(Position, rear, clear - Margin);
            }
        }
        while (recentPath.Count > 0 && Position.Distance(recentPath[recentPath.Count - 1]) < .015)
            recentPath.RemoveAt(recentPath.Count - 1);
        if (recentPath.Count == 0 && (!rearWaypoint.HasValue || Position.Distance(rearWaypoint.Value) < .015))
        {
            // Keep the failed approach in memory and recheck at scan cadence.
            // Leaving this state here would just re-enter the blind corridor.
            Speed = 0;
            Status = "Looking for an opening behind or beside the blocked approach";
            return;
        }

        AutonomyPosition point = recentPath.Count > 0 ? recentPath[recentPath.Count - 1] : rearWaypoint!.Value;
        double remaining = Position.Distance(point);
        Forward = new AutonomyPosition((float)((point.X - Position.X) / remaining),
            (float)((point.Y - Position.Y) / remaining), (float)((point.Z - Position.Z) / remaining));
        double speed = Math.Min(cruise, Speed + 4 * dt);
        AutonomyPosition next = speed * dt >= remaining ? point : Along(Position, Forward, speed * dt);
        double requested = Position.Distance(next);
        if (SafeClearance(world, Position, next) < requested - .00001)
        {
            Speed = 0;
            returnBlocked = true;
            Status = "Waiting for clearance on the return path";
            return;
        }
        returnBlocked = false;
        Position = next;
        Travelled += requested;
        withdrawn += requested;
        Speed = speed;
        Status = "Retracing the blocked approach to an alternative passage";
        CheckArrival();
    }

    private bool TryResumeThroughOpening(IJourneyGuideWorld world)
    {
        // A temporary obstacle may have cleared since withdrawal began. Verify
        // the chord across the failed position before returning to that course.
        var beyond = Along(failedPosition, Direction(lastBearing, 0), 8);
        double length = Position.Distance(beyond);
        if (length > 40 || length < .5 || SafeClearance(world, Position, beyond) < length - .01) return false;
        CommitExit(beyond, 0, "Continuing through a reopened passage");
        return true;
    }

    private bool TryChooseExit(IJourneyGuideWorld world)
    {
        // Steer across the approach before turning back toward the destination.
        // A short ray just inside the entrance can aim straight back into its
        // wall, so require a connected sweep for the entire lateral waypoint.
        double span = Clamp(8 + cruise * .7, 11, 17);
        double side = lastEscapeSide >= 0 ? -1 : 1;
        for (int i = 0; i < 4; i++)
        {
            double offset = (i < 2 ? 94 : 120) * (i % 2 == 0 ? side : -side);
            AutonomyPosition end = Along(Position, Direction(lastBearing + offset, 0), span);
            if (end.HorizontalDistance(failedPosition) < Position.HorizontalDistance(failedPosition) ||
                !world.TryGetSurface(end, out double surface) || !Finite(surface)) continue;
            end = new AutonomyPosition(end.X, (float)Clamp(Math.Max(Position.Y, surface + altitude), -20000, 20000), end.Z);
            if (SafeClearance(world, Position, end) < Position.Distance(end) - .01) continue;
            CommitExit(end, Math.Sign(offset), "Taking a lateral exit from the blocked approach");
            return true;
        }
        return false;
    }
    private void CommitExit(AutonomyPosition end, int side, string status)
    {
        escapeTarget = end;
        lastEscapeSide = side;
        withdrawing = trackMotion = false;
        slowSeconds = withoutProgress = extraHeight = 0;
        yaw = Bearing(Position, end);
        pitch = Math.Atan2(end.Y - Position.Y, Position.HorizontalDistance(end)) * 180 / Math.PI;
        Speed = scanIn = 0;
        Status = status;
    }

}
