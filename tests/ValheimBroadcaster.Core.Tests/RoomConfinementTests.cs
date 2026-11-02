using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class RoomConfinementTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Room confinement requires separated compatible complete observations", Acquisition);
        yield return ("A missing structural survey retires confinement immediately", MissingSurvey);
        yield return ("Stale room evidence cannot constrain the next camera frame", Expiry);
        yield return ("Doorway and unsupported scene conditions disable confinement immediately", Doorway);
        yield return ("Subject departure retires the old room before reacquisition", SubjectDeparture);
        yield return ("World and scene changes cannot inherit room boundaries", SceneChange);
        yield return ("Changed or demolished room boundaries require fresh confirmation", ChangedBoundary);
        yield return ("Rotated room containment matches oriented world geometry", RotatedCell);
        yield return ("Confinement rejects unsafe blend endpoints in a convex room", BlendContainment);
        yield return ("Clock reset and teardown discard pending and active room evidence", Reset);
        yield return ("Incomplete default and unsupported room dimensions are rejected", InvalidVolume);
        yield return ("Stable boundary measurements remain world anchored while a subject moves", MovingSubject);
    }
    private static AutonomyPosition P(float x = 0, float y = 2, float z = 0) => new AutonomyPosition(x, y, z);
    private static RoomConfinementVolume Cell(float x = 0, float yaw = 0) =>
        new RoomConfinementVolume(P(x), new AutonomyPosition(4, 1.5f, 2), yaw);
    private static RoomConfinementPolicy Active()
    {
        var policy = new RoomConfinementPolicy();
        policy.Observe(0, 1, P(), Cell()); policy.Observe(.5, 1, P(), Cell());
        Check(policy.Active, "Compatible surveys did not acquire a cell.");
        return policy;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Acquisition()
    {
        var policy = new RoomConfinementPolicy();
        policy.Observe(0, 1, P(), Cell());
        policy.Observe(0, 1, P(), Cell());
        Check(!policy.Active, "Same-frame observations acquired confinement.");
        policy.Observe(.2, 1, P(), Cell()); Check(!policy.Active, "A transient hint acquired confinement.");
        policy.Observe(.5, 1, P(), Cell()); Check(policy.Active, "Persistent measured boundaries not admitted.");
    }
    private static void MissingSurvey()
    {
        var policy = Active(); policy.Observe(.6, 1, P(), null);
        Check(!policy.Active && policy.Contains(P(50)), "Missing geometry retained an old clamp.");
        policy.Observe(.7, 1, P(), Cell()); Check(!policy.Active, "Missing evidence retained acquisition credit.");
    }
    private static void Expiry()
    {
        var policy = Active(); policy.Tick(1.2, 1, P(), true); Check(policy.Active, "Fresh evidence expired early.");
        policy.Tick(1.26, 1, P(), true); Check(!policy.Active, "A stale room remained active.");
        policy.Observe(1.3, 1, P(), Cell()); Check(!policy.Active, "Expired room resumed without confirmation.");
    }
    private static void Doorway()
    {
        var policy = Active(); policy.Tick(.6, 1, P(), false);
        Check(!policy.Active, "Doorway movement retained confinement.");
        policy.Observe(.7, 1, P(), Cell()); Check(!policy.Active, "Doorway exit retained old confirmation.");
    }
    private static void SubjectDeparture()
    {
        var policy = Active(); policy.Tick(.6, 1, P(4.1f), true);
        Check(!policy.Active && policy.Contains(P(4.1f)), "A leaving actor remained trapped in old camera space.");
        policy.Observe(.7, 1, P(4.1f), Cell()); Check(!policy.Active, "Cell excluding actor was admitted.");
    }
    private static void SceneChange()
    {
        var policy = Active(); policy.Tick(.6, 2, P(), true); Check(!policy.Active, "New scene retained old room.");
        policy.Observe(.7, 2, P(), Cell()); Check(!policy.Active, "Coincident new-scene geometry inherited confidence.");
    }
    private static void ChangedBoundary()
    {
        var policy = Active(); policy.Observe(.8, 1, P(), Cell(1));
        Check(!policy.Active, "Changed enclosure retained the old valid flag.");
        policy.Observe(1.3, 1, P(), Cell(1)); Check(policy.Active && policy.Volume.Center.X == 1, "Changed enclosure failed to reacquire.");
    }
    private static void RotatedCell()
    {
        var cell = Cell(yaw: 90);
        Check(cell.Contains(P(1.9f, 2, 3.9f)), "Rotated inside point rejected.");
        Check(!cell.Contains(P(2.1f, 2, 0)) && !cell.Contains(P(0, 3.6f)), "Rotated/vertical outside point admitted.");
        Check(Cell(yaw: -270).Contains(P(1.9f, 2, 3.9f)), "Equivalent rotation changed containment.");
    }
    private static void BlendContainment()
    {
        var policy = Active();
        Check(policy.ValidateSegment(P(-3.9f, 1, -1.9f), P(3.9f, 3, 1.9f)), "Safe convex segment rejected.");
        Check(!policy.ValidateSegment(P(), P(4.2f)), "Outside destination accepted.");
        Check(!policy.ValidateSegment(P(-4.2f), P()), "Outside source allowed a crossing blend.");
    }
    private static void Reset()
    {
        var policy = Active(); policy.Tick(.1, 1, P(), true); Check(!policy.Active, "Clock rollback retained geometry.");
        policy.Observe(.2, 1, P(), Cell()); Check(!policy.Active, "Clock rollback retained confirmation.");
        policy.Observe(.7, 1, P(), Cell()); policy.Reset();
        Check(!policy.Active && policy.Contains(P(50)), "Reset retained confinement.");
    }
    private static void InvalidVolume()
    {
        var policy = new RoomConfinementPolicy();
        policy.Observe(0, 1, P(0, 0), default(RoomConfinementVolume));
        policy.Observe(.5, 1, P(0, 0), default(RoomConfinementVolume));
        Check(!policy.Active, "Default empty volume was admitted.");
        bool threw = false;
        try { _ = new RoomConfinementVolume(P(), new AutonomyPosition(30, 2, 2), 0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw, "Unbounded room volume accepted.");
        Check(!Cell().Contains(P(), float.NaN) && !Cell().Contains(P(), -1), "Invalid inset expanded containment.");
    }
    private static void MovingSubject()
    {
        var policy = Active(); policy.Tick(.6, 1, P(2), true);
        policy.Observe(1, 1, P(2), Cell());
        Check(policy.Active && policy.Volume.Center.X == 0, "Actor movement translated the physical room volume.");
    }
}
