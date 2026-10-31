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
        internal Target(string id, string name) { Identity = new PlayerTarget(id, name); }
    }

    internal readonly Dictionary<string, Target> Targets = new Dictionary<string, Target>(StringComparer.Ordinal);

    internal void Refresh(bool remote)
    {
        Targets.Clear();
        Player observer = Player.m_localPlayer;
        if (!observer || !ZNet.instance) return;
        string observerId = observer.GetZDOID().ToString();
        // The network roster and loaded scene objects are different inventories.
        // Only public positions are eligible to bootstrap a distant target.
        if (remote)
        {
            foreach (var info in ZNet.instance.GetPlayerList())
            {
                string id = info.m_characterID.ToString();
                if (id == observerId || info.m_characterID.IsNone() || !info.m_publicPosition || !Finite(info.m_position)) continue;
                Targets[id] = new Target(id, info.m_name) { PublicPosition = info.m_position };
            }
        }
        foreach (Player player in Player.GetAllPlayers())
        {
            if (!player || player == observer || player.GetZDOID().IsNone()) continue;
            string id = player.GetZDOID().ToString();
            if (!Targets.TryGetValue(id, out Target? target))
                Targets[id] = target = new Target(id, player.GetPlayerName());
            target.LoadedPlayer = player;
        }
    }

    internal IEnumerable<PlayerTarget> Identities()
    {
        foreach (Target target in Targets.Values) yield return target.Identity;
    }

    internal Target? Find(string? id) => id != null && Targets.TryGetValue(id, out Target? target) ? target : null;

    internal static bool Finite(Vector3 value) => Valid(value.x) && Valid(value.y) && Valid(value.z);
    private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
