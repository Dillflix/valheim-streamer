using System;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

/// <summary>Renews a queued section in place. Its route and ending remain the
/// same immutable plans, so renewal cannot orphan any downstream certificate.</summary>
internal sealed class ScenicJourneySectionLease : IDisposable
{
    private readonly ScenicJourneyEndingLease route = new ScenicJourneyEndingLease();
    private readonly ScenicJourneyEndingLease ending = new ScenicJourneyEndingLease();
    internal bool CanAdmit => route.CanAdmit && ending.CanAdmit && route.Epoch == ending.Epoch;
    internal bool Contradicted => route.Contradicted || ending.Contradicted;
    internal bool RevalidatingReadability => route.RevalidatingReadability || ending.RevalidatingReadability;
    internal bool RefreshDue => route.RefreshDue || ending.RefreshDue;
    internal long Epoch => route.Epoch == ending.Epoch ? route.Epoch : 0;
    internal float PreparedAt => Math.Min(route.ValidatedAt, ending.ValidatedAt);
    internal float EndingValidatedAt => ending.ValidatedAt;
    internal string Reason => !route.CanAdmit || route.IsRefreshing ? route.Reason : ending.Reason;
    internal void Begin(ScenicShotPlan plan, ScenicShotPlan finiteEnding, long epoch, float routeAt,
        float endingAt, float aspect, float nearClip, bool moveCarrier)
    {
        route.BeginRoute(plan, epoch, routeAt, aspect, nearClip, moveCarrier);
        ending.Begin(finiteEnding, epoch, endingAt, aspect, nearClip, moveCarrier);
    }
    internal void Step()
    {
        // Finish each expensive renewal before starting another. A newer
        // ending cannot make an expired route fresh, or vice versa.
        if (ending.IsRefreshing) ending.Step();
        else if (route.IsRefreshing || !route.CanAdmit || route.RefreshDue) route.Step();
        else ending.Step();
    }
    public void Dispose() { route.Dispose(); ending.Dispose(); }
}
