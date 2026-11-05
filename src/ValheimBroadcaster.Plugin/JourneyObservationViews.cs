using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>One bounded projection per unfinished navigation observation.
/// Projections share ScenicFoliageScene's catalogue and mesh geometry; only
/// their incremental local selection and retained bounds are independent.</summary>
internal sealed class JourneyObservationViews
{
    private readonly JourneyObservationOwners owners;
    private readonly ScenicVisibility[] views;
    private readonly long[] generations;
    private readonly string owner;
    internal string Reason => owners.Reason;

    internal JourneyObservationViews(string owner, int capacity)
    {
        this.owner = owner;
        owners = new JourneyObservationOwners(capacity);
        views = new ScenicVisibility[capacity];
        generations = new long[capacity];
        for (int i = 0; i < capacity; i++) views[i] = new ScenicVisibility(owner + "-view-" + i);
    }

    internal bool TryGet(long token, long epoch, Vector3 camera, out ScenicVisibility view)
    {
        if (!owners.TryAcquire(token, epoch, new AutonomyPosition(camera.x, camera.y, camera.z),
            out JourneyObservationLease lease, out bool reset))
        { view = null!; return false; }
        view = views[lease.Slot];
        bool reassigned = generations[lease.Slot] != lease.Generation;
        bool discardedPending = reassigned && view.ProjectionUpdating;
        if (reset || discardedPending) view.Reset();
        generations[lease.Slot] = lease.Generation;
        if (ScenicTrace.Enabled && reassigned)
            ScenicTrace.Record(ScenicTrace.Vector(ScenicTrace.Row("journey-observation-owner")
                .Field("owner", owner).Field("token", token).Field("slot", lease.Slot)
                .Field("generation", lease.Generation).Field("sceneEpoch", epoch)
                .Field("capacity", owners.Capacity).Field("active", owners.ActiveCount)
                .Field("cacheRetained", !reset && !discardedPending).Field("discardedPending", discardedPending), "camera", camera));
        return true;
    }

    internal void Release(long token) => owners.Release(token);

    internal void Reset()
    {
        owners.Reset();
        for (int i = 0; i < views.Length; i++) views[i].Reset();
    }
}
