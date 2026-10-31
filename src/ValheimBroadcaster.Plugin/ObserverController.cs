using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

// Local character carrier; multiplayer publication/authority is isolated by the connection adapters.
internal sealed class ObserverController
{
    private static readonly MethodInfo? TeleportUpdate = AccessTools.Method(typeof(Player), "UpdateTeleport", new[] { typeof(float) });
    private static readonly FieldInfo? Teleporting = AccessTools.Field(typeof(Player), "m_teleporting");
    private static readonly FieldInfo? TeleportTimer = AccessTools.Field(typeof(Player), "m_teleportTimer");
    private static readonly FieldInfo? TeleportCooldown = AccessTools.Field(typeof(Player), "m_teleportCooldown");
    private readonly ObserverTravel travel = new ObserverTravel();
    private Player? owner;
    private ZNetView? view;
    private ZNet? world;
    private Rigidbody? body;
    private bool god, ghost, gravity, collisions;
    private RigidbodyConstraints constraints;
    private Vector3 origin, destination;
    private Quaternion originRotation;
    private float? cameraGap;
    private bool remoteSession;
    internal Func<bool> RemoteApproved { get; set; } = () => false;
    private bool CanOperate => remoteSession ? RemoteApproved() : Offline;
    internal bool RemoteSession => remoteSession;
    internal void Resume() => travel.Resume();
    internal bool Active => travel.Active;
    internal ObserverPhase Phase => travel.Phase;
    internal string Status => $"{(remoteSession ? "READ-ONLY" : "OFFLINE")} OBSERVER | {travel.Phase}" +
        (travel.Failure == null ? "" : " | " + travel.Failure);
    internal string Diagnostics => Status + $" | travel {travel.ElapsedSeconds:F1}s" +
        (owner ? $" | position {owner.transform.position.ToString("F1")} | origin {Vector3.Distance(owner.transform.position, origin):F1}m | destination {Vector3.Distance(owner.transform.position, destination):F1}m" : "") +
        (cameraGap.HasValue ? $" | last camera gap {cameraGap.Value:F1}m" : "");
    internal ZDO? NetworkObject => view ? view.GetZDO() : null;
    internal bool Owns(Character player) => Active && owner && owner == player;
    internal bool InWorld(ZNet candidate) => Active && world == candidate;
    private bool HasOwnership => view && view.IsOwner() && view.GetZDO() != null;
    internal static bool Offline => ZNet.instance && ObserverTravel.CanActivate(ZNet.instance.IsServer(), ZNet.IsOpenServer(), ZNet.instance.GetPeerConnections());

    internal void Begin(Player player, bool remote = false)
    {
        if (!(remote ? RemoteApproved() : Offline)) throw new InvalidOperationException("Observer requires a closed local world or a server-authorized read-only connection.");
        if (Active || !player || player.IsDead() || player.IsTeleporting() || player.IsAttached())
            throw new InvalidOperationException("Observer must be alive, unattached, and not teleporting.");
        if ((!remote && (!player.IsOnGround() || player.IsSwimming())) || player.InAttack() || player.InDodge() || player.IsSleeping())
            throw new InvalidOperationException("Stand still on dry ground and finish any attack, dodge, or sleep before starting observer mode.");
        if (TeleportUpdate == null || TeleportUpdate.ReturnType != typeof(void) ||
            Teleporting?.FieldType != typeof(bool) || TeleportTimer?.FieldType != typeof(float) || TeleportCooldown?.FieldType != typeof(float))
            throw new InvalidOperationException("Observer teleport hooks do not match this game build.");
        Rigidbody next = player.GetComponent<Rigidbody>();
        if (!next || next.isKinematic) throw new InvalidOperationException("Expected an ordinary dynamic player Rigidbody.");
        ZNetView nextView = player.GetComponent<ZNetView>();
        if (!nextView || !nextView.IsOwner() || nextView.GetZDO() == null)
            throw new InvalidOperationException("Observer must own its live network object.");
        if (!TargetRoster.Finite(player.transform.position) || !IdleShots.Ready(player.transform.position))
            throw new InvalidOperationException("Wait for the starting area to load.");
        remoteSession = remote;
        owner = player; world = ZNet.instance; body = next; view = nextView;
        origin = destination = player.transform.position; originRotation = player.transform.rotation;
        god = player.InGodMode(); ghost = player.InGhostMode();
        gravity = body.useGravity; collisions = body.detectCollisions; constraints = body.constraints;
        // Clear control flags before suppressing input updates. This does not claim
        // to cancel actions queued by other mods or an attack already in progress.
        player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false, false);
        travel.Start();
        try { Enforce(); SyncInterest(); }
        catch { Release(); throw; }
    }

    private void Enforce()
    {
        if (!owner || !body) return;
        owner.SetGodMode(true); owner.SetGhostMode(true);
        body.useGravity = false; body.detectCollisions = false; body.constraints = RigidbodyConstraints.FreezeAll;
        body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
    }

    internal void TravelTo(Vector3 target)
    {
        if (!CanOperate || travel.Phase != ObserverPhase.Watching || !owner || !TargetRoster.Finite(target))
            throw new InvalidOperationException("Start observer mode and finish any current travel first.");
        if (!HasOwnership) { Suspend("Observer network ownership lost; exit the world."); return; }
        if (!owner.TeleportTo(target, originRotation, true)) throw new InvalidOperationException("Game refused teleport (possibly cooldown); wait and retry.");
        destination = target;
        cameraGap = null;
        travel.Travel();
    }

    internal void RequestReturn()
    {
        if (!Active || Phase == ObserverPhase.Returning || Phase == ObserverPhase.Parked || !owner || world != ZNet.instance) return;
        if (!CanOperate) { Suspend("Observer authorization or world isolation lost. Exit the world."); return; }
        if (!HasOwnership) { Suspend("Observer network ownership lost; exit the world."); return; }
        CancelOwnedTeleport();
        destination = origin;
        cameraGap = null;
        travel.Return(remoteSession);
        if (!owner.TeleportTo(origin, originRotation, true)) Suspend("Return teleport refused; use broadcast observer return to retry.");
    }

    private void CancelOwnedTeleport()
    {
        if (!owner) return;
        Teleporting!.SetValue(owner, false);
        TeleportTimer!.SetValue(owner, 0f);
        TeleportCooldown!.SetValue(owner, 2f);
    }

    // Replaces only this player's FixedUpdate while active. Explicitly advances the
    // real teleport routine; does not run attacks, pickup, stats, or attached controls.
    internal static void AdvancePendingTeleport(Player player, float dt)
    {
        if (TeleportUpdate == null) throw new MissingMethodException("Player.UpdateTeleport");
        TeleportUpdate.Invoke(player, new object[] { dt });
    }

    internal void FixedTick(float dt)
    {
        if (!Active || !owner || owner != Player.m_localPlayer || world != ZNet.instance) return;
        Enforce();
        if (!CanOperate) { Suspend("Observer authorization or world isolation lost. Exit the world."); return; }
        if (Phase != ObserverPhase.Suspended)
        {
            TeleportUpdate!.Invoke(owner, new object[] { dt });
            if (body) { body.position = owner.transform.position; body.rotation = owner.transform.rotation; }
        }
        SyncInterest();
    }

    internal bool Tick(float dt)
    {
        if (!Active) return false;
        if (!owner || owner != Player.m_localPlayer || world != ZNet.instance) { Release(); return true; }
        if (!CanOperate) Suspend("Observer authorization or world isolation lost. Exit the world.");
        if (Phase == ObserverPhase.Suspended || Phase == ObserverPhase.Watching || Phase == ObserverPhase.Parked) return false;
        bool wasActive = Active;
        travel.Tick(dt, owner.IsTeleporting(), IdleShots.Ready(destination),
            (owner.transform.position - destination).sqrMagnitude <= 25f);
        if (wasActive && !Active) { Release(); return true; }
        if (Phase == ObserverPhase.Suspended) CancelOwnedTeleport();
        return false;
    }

    internal bool FollowCamera(Vector3 cameraPosition)
    {
        if (Phase != ObserverPhase.Watching || !owner || !body || !CanOperate) return false;
        Vector3 position = cameraPosition + Vector3.down * 2f;
        if (!TargetRoster.Finite(position) || (owner.transform.position - position).sqrMagnitude > 10000f || !IdleShots.Ready(position))
        { Suspend("Camera destination requires controlled travel; use broadcast observer return."); return false; }
        owner.transform.position = position;
        body.position = position;
        SyncInterest();
        cameraGap = Vector3.Distance(owner.transform.position, cameraPosition);
        return Phase == ObserverPhase.Watching;
    }

    internal void Suspend(string reason)
    {
        travel.Suspend(reason);
        if (Active) CancelOwnedTeleport();
    }

    private void SyncInterest()
    {
        if (!owner || world != ZNet.instance) return;
        // Update owned object position before the reference moves. Scene retention
        // therefore sees player and interest in the same region, including teleport.
        if (!HasOwnership) { Suspend("Observer network ownership lost; exit the world."); return; }
        view!.GetZDO().SetPosition(owner.transform.position);
        world.SetReferencePosition(owner.transform.position);
    }

    internal void Release()
    {
        if (owner)
        {
            if (Active) CancelOwnedTeleport();
            owner.SetGodMode(god); owner.SetGhostMode(ghost);
        }
        if (body)
        {
            body.useGravity = gravity; body.detectCollisions = collisions; body.constraints = constraints;
            body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }
        owner = null; body = null; view = null; world = null; cameraGap = null; remoteSession = false; travel.Reset();
    }
}
