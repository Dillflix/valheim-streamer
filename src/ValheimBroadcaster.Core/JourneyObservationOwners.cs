using System;

namespace ValheimBroadcaster.Core;

/// <summary>An unfinished spatial query owns one projection slot at one exact
/// camera position. This identity allocates work; it never certifies geometry.</summary>
public readonly struct JourneyObservationLease
{
    public readonly int Slot;
    public readonly long Token, Epoch, Generation;
    public readonly AutonomyPosition Camera;
    internal JourneyObservationLease(int slot, long token, long epoch, long generation, AutonomyPosition camera)
    { Slot = slot; Token = token; Epoch = epoch; Generation = generation; Camera = camera; }
}

/// <summary>Bounded ownership of incremental foliage projections. Active
/// owners cannot be evicted or recentered by another pending query. Released
/// observations remain cached and the nearest idle projection is reused, with
/// least-recently-used selection breaking equal-distance ties.
/// The adapter owns the actual projection instances and shared scene catalogue.</summary>
public sealed class JourneyObservationOwners
{
    public const int MaximumCapacity = 9;
    private readonly Entry[] entries;
    private long epoch, clock, nextGeneration;
    public int Capacity => entries.Length;
    public int ActiveCount { get; private set; }
    public string Reason { get; private set; } = "No observation owner requested";

    private struct Entry
    {
        internal JourneyObservationLease Lease;
        internal bool Active;
        internal long LastUsed;
    }

    public JourneyObservationOwners(int capacity)
    {
        if (capacity < 1 || capacity > MaximumCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        entries = new Entry[capacity];
    }

    public bool TryAcquire(long token, long sceneEpoch, AutonomyPosition camera,
        out JourneyObservationLease lease, out bool resetProjection)
    {
        if (token <= 0 || sceneEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(token));
        if (!Finite(camera.X) || !Finite(camera.Y) || !Finite(camera.Z)) throw new ArgumentOutOfRangeException(nameof(camera));
        if (epoch != sceneEpoch) { Reset(); epoch = sceneEpoch; }
        lease = default; resetProjection = false;
        int retained = -1, available = -1, empty = -1;
        long oldest = long.MaxValue;
        double nearest = double.PositiveInfinity;
        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            if (entry.Active)
            {
                if (entry.Lease.Token != token) continue;
                if (!Same(entry.Lease.Camera, camera))
                { Reason = "Release the completed query phase before changing its requested camera"; return false; }
                entries[i].LastUsed = ++clock; lease = entry.Lease;
                Reason = "Resuming the same unfinished observation owner"; return true;
            }
            if (entry.Lease.Token == token && entry.Lease.Epoch == sceneEpoch && Same(entry.Lease.Camera, camera)) retained = i;
            if (entry.Lease.Generation == 0) { if (empty < 0) empty = i; continue; }
            double distance = entry.Lease.Camera.Distance(camera);
            if (distance < nearest || distance == nearest && entry.LastUsed < oldest)
            { nearest = distance; oldest = entry.LastUsed; available = i; }
        }
        int selected = retained >= 0 ? retained : available >= 0 ? available : empty;
        if (selected < 0)
        { Reason = "All observation slots belong to unfinished queries"; return false; }
        Entry before = entries[selected];
        // A completed projection is a reusable spatial cache, not a proof for
        // the new query. Its normal freshness/coverage checks remain mandatory.
        // The adapter must separately discard any still-updating projection
        // when changing owners, so an old partial job cannot retarget this one.
        resetProjection = before.Lease.Generation == 0 || before.Lease.Epoch != sceneEpoch;
        lease = new JourneyObservationLease(selected, token, sceneEpoch, ++nextGeneration, camera);
        entries[selected] = new Entry { Lease = lease, Active = true, LastUsed = ++clock };
        ActiveCount++; Reason = resetProjection ? "Assigned a bounded observation slot" : "Reusing a completed spatial cache under a new query lease";
        return true;
    }

    public bool IsCurrent(JourneyObservationLease lease) => lease.Generation > 0 && lease.Epoch == epoch &&
        lease.Slot >= 0 && lease.Slot < entries.Length && entries[lease.Slot].Active &&
        entries[lease.Slot].Lease.Generation == lease.Generation && entries[lease.Slot].Lease.Token == lease.Token &&
        Same(entries[lease.Slot].Lease.Camera, lease.Camera);

    public bool Release(JourneyObservationLease lease)
    {
        if (!IsCurrent(lease)) return false;
        entries[lease.Slot].Active = false; entries[lease.Slot].LastUsed = ++clock; ActiveCount--;
        return true;
    }

    public bool Release(long token)
    {
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].Active && entries[i].Lease.Token == token) return Release(entries[i].Lease);
        return false;
    }

    public void Reset()
    {
        Array.Clear(entries, 0, entries.Length); epoch = clock = 0; ActiveCount = 0;
        // Generations do not restart: delayed work from a preceding graph or
        // world can never match the first allocation in its replacement.
        Reason = "Observation ownership reset";
    }

    private static bool Same(AutonomyPosition a, AutonomyPosition b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
