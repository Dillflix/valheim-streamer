using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// A successful, actually rendered environmental plan can keep using its loaded
// terrain while unrelated network objects instantiate nearby. New plans still
// require full area acquisition; a stale binding never bypasses that boundary.
internal sealed class ScenicShotReadiness
{
    private readonly ScenicReadinessPolicy continuity = new ScenicReadinessPolicy();
    private ZNet? world;
    private Player? carrier;
    private bool interior;

    internal bool CanContinue(long planId, Vector3 focus)
    {
        if (!world || world != ZNet.instance || !carrier || carrier != Player.m_localPlayer ||
            !TargetRoster.Finite(focus) || Character.InInterior(focus) != interior)
        { Reset(); return false; }
        return continuity.CanContinue(planId, Point(focus), Time.unscaledTime) &&
            PlayerShotReadiness.PositionReady(focus, true);
    }

    internal void Presented(long planId, Vector3 focus)
    {
        if (!ZNet.instance || !Player.m_localPlayer || !TargetRoster.Finite(focus) ||
            !PlayerShotReadiness.PositionReady(focus, true)) { Reset(); return; }
        if (world != ZNet.instance || carrier != Player.m_localPlayer) Reset();
        world = ZNet.instance; carrier = Player.m_localPlayer; interior = Character.InInterior(focus);
        continuity.Presented(planId, Point(focus), Time.unscaledTime);
    }
    internal void Reset() { continuity.Reset(); world = null; carrier = null; interior = false; }
    private static AutonomyPosition Point(Vector3 p) => new AutonomyPosition(p.x, p.y, p.z);
}
