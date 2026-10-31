using System;
using System.Collections.Generic;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed class TargetRoster
{
    internal sealed class Target
    {
        internal readonly PlayerTarget Identity;
        internal Player? LoadedPlayer;
        internal Vector3? PublicPosition;
        internal string CharacterId;
        internal bool FromDiscovery;
        internal Target(string id, string name) { Identity = new PlayerTarget(id, name); CharacterId = id; }
    }

    internal readonly Dictionary<string, Target> Targets = new Dictionary<string, Target>(StringComparer.Ordinal);

    internal int ConnectedOthers { get; private set; }

    internal void Clear() { Targets.Clear(); ConnectedOthers = 0; }

    internal void Refresh(bool remote, bool selfTest = false, ObserverDiscoverySnapshot? discovery = null)
    {
        Clear();
        Player observer = Player.m_localPlayer;
        if (!observer || !ZNet.instance) return;
        string observerId = observer.GetZDOID().ToString();
        if (selfTest)
        {
            Targets[observerId] = new Target(observerId, observer.GetPlayerName()) { LoadedPlayer = observer };
            return; // No remote roster or public position is used in this mode.
        }
        var characterIds = new Dictionary<string, Target>(StringComparer.Ordinal);
        if (discovery != null)
        {
            ConnectedOthers = discovery.Players.Count;
            foreach (ObserverPlayer info in discovery.Players)
            {
                if (info.CharacterId == observerId) continue;
                var target = new Target(info.Id, info.Name) { CharacterId = info.CharacterId, FromDiscovery = true };
                if (info.Position.HasValue)
                {
                    ObserverPoint p = info.Position.Value;
                    target.PublicPosition = new Vector3(p.X, p.Y, p.Z);
                }
                Targets[info.Id] = target;
                if (!string.IsNullOrEmpty(info.CharacterId)) characterIds[info.CharacterId] = target;
            }
        }
        // The network roster and loaded scene objects are different inventories.
        // Only public positions are eligible to bootstrap a distant target.
        foreach (var info in ZNet.instance.GetPlayerList())
        {
            if (discovery != null) break;
            string id = info.m_characterID.ToString();
            if (id == observerId) continue;
            ConnectedOthers++; // Presence includes peers whose character has not spawned/loaded.
            if (!remote || info.m_characterID.IsNone() || !info.m_publicPosition || !Finite(info.m_position)) continue;
            Targets[id] = new Target(id, info.m_name) { PublicPosition = info.m_position };
        }
        int loadedOthers = 0;
        foreach (Player player in Player.GetAllPlayers())
        {
            if (!player || player == observer || player.GetZDOID().IsNone()) continue;
            loadedOthers++;
            string id = player.GetZDOID().ToString();
            if (!characterIds.TryGetValue(id, out Target? target) && !Targets.TryGetValue(id, out target))
            {
                // The authenticated roster is authoritative for autonomous sessions;
                // a leftover character object must not resurrect a departed target.
                if (discovery != null) continue;
                Targets[id] = target = new Target(id, player.GetPlayerName());
            }
            target.LoadedPlayer = player;
        }
        ConnectedOthers = Math.Max(ConnectedOthers, loadedOthers);
    }

    internal IEnumerable<PlayerTarget> Identities()
    {
        foreach (Target target in Targets.Values) yield return target.Identity;
    }

    internal Target? Find(string? id) => id != null && Targets.TryGetValue(id, out Target? target) ? target : null;

    internal static bool Finite(Vector3 value) => Valid(value.x) && Valid(value.y) && Valid(value.z);
    private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
