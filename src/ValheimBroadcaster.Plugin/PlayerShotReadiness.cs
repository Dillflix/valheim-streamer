using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Full area readiness establishes a shot. Once actually presented, unrelated
// object creation in its 3x3-zone neighbourhood need not interrupt that shot.
internal sealed class PlayerShotReadiness
{
    private readonly PlayerFollowContinuity continuity = new PlayerFollowContinuity();
    private ZNet? world;
    private Player? carrier, actor;
    private ZDOID characterId;
    private string? subjectId;
    private string bindingId = "";
    private bool interior;
    private static int? geometryMask;
    private static readonly RaycastHit[] supportHits = new RaycastHit[16];
    internal string Diagnostics { get; private set; } = "Player shot: awaiting acquisition";

    private static bool Live(Player? player) => player && player.isActiveAndEnabled && !player.IsDead() &&
        !player.IsTeleporting() && !player.GetZDOID().IsNone() && TargetRoster.Finite(player.transform.position);

    internal bool CanContinue(TargetRoster.Target? target)
    {
        if (world != ZNet.instance || carrier != Player.m_localPlayer) Reset();
        if (target == null || target.Identity.Id != subjectId) return false;
        Player? current = target.LoadedPlayer;
        if (!Live(current) || current != actor || current!.GetZDOID() != characterId ||
            Character.InInterior(current.transform.position) != interior)
        { Reset(); return false; }
        Vector3 point = current.transform.position;
        bool valid = continuity.CanContinue(target.Identity.Id, bindingId,
            new AutonomyPosition(point.x, point.y, point.z), Time.realtimeSinceStartup);
        if (!valid) Reset(); // This binding expired or relocated; require fresh acquisition.
        return valid;
    }

    internal bool TargetReady(TargetRoster.Target? target, out string reason)
    {
        bool continuing = CanContinue(target);
        if (target == null || !Live(target.LoadedPlayer) || !ZNetScene.instance)
        { reason = "Living player instance unavailable locally"; return false; }
        bool ready = PositionReady(target.LoadedPlayer!.transform.position, continuing);
        reason = continuing ? ready ? "Following established shot; local geometry ready" : "Target geometry unavailable locally" :
            ready ? "Initial player area ready" : "Acquiring player area; awaiting local objects";
        return ready;
    }

    internal void Presented(TargetRoster.Target target, bool continued)
    {
        Player? current = target.LoadedPlayer;
        if (!Live(current) || !ZNet.instance || !Player.m_localPlayer) { Reset(); return; }
        if (current != actor || current!.GetZDOID() != characterId || world != ZNet.instance)
            bindingId = current!.GetInstanceID() + ":" + current.GetZDOID();
        actor = current; characterId = current!.GetZDOID(); subjectId = target.Identity.Id;
        world = ZNet.instance; carrier = Player.m_localPlayer;
        Vector3 point = current.transform.position;
        interior = Character.InInterior(point);
        continuity.Presented(subjectId, bindingId, new AutonomyPosition(point.x, point.y, point.z), Time.realtimeSinceStartup);
        Diagnostics = continued ? "Player shot: continuous follow; local geometry ready" : "Player shot: acquired with full area readiness";
    }

    internal void Reset()
    {
        continuity.Reset(); world = null; carrier = actor = null; subjectId = null; bindingId = "";
        Diagnostics = "Player shot: awaiting acquisition";
    }

    internal bool ValidatePreparedReturn(TargetRoster.Target target)
    {
        if (!CanContinue(target) || !Live(target.LoadedPlayer)) return false;
        Vector3 point = target.LoadedPlayer!.transform.position;
        if (!PositionReady(point, true) || !continuity.ValidatePreparedReturn(target.Identity.Id, bindingId,
            new AutonomyPosition(point.x, point.y, point.z), Time.realtimeSinceStartup)) return false;
        Diagnostics = "Player scene retained for a bounded complementary view; return camera validated, no player-footage credit";
        return true;
    }

    internal static bool PositionReady(Vector3 point, bool continuing)
    {
        if (!continuing) return IdleShots.Ready(point);
        if (!TargetRoster.Finite(point) || !ZNetScene.instance) return false;
        if (Character.InInterior(point))
        {
            // Heightmaps ignore altitude: terrain thousands of metres below an
            // interior is not room geometry. Require nearby static support there.
            geometryMask ??= LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 2f, Vector3.down, supportHits, 80f,
                geometryMask.Value, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = supportHits[i];
                if (hit.collider && !hit.collider.attachedRigidbody && Character.InInterior(hit.point) &&
                    !hit.collider.GetComponentInParent<Character>()) return true;
            }
            return false;
        }
        Heightmap map = Heightmap.FindHeightmap(point);
        if (!map || !map.isActiveAndEnabled) return false;
        MeshCollider collider = map.GetComponent<MeshCollider>();
        MeshFilter mesh = map.GetComponent<MeshFilter>();
        return collider && collider.enabled && collider.sharedMesh && collider.sharedMesh.vertexCount > 0 &&
               mesh && mesh.sharedMesh && mesh.sharedMesh.vertexCount > 0;
    }
}
