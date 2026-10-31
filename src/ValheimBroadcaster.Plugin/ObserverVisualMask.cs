using System.Collections.Generic;
using UnityEngine;
namespace ValheimBroadcaster.Plugin;

// Local presentation only. Never disables the player object, colliders, network view,
// or AI/simulation. Other clients continue to see an ordinary player.
internal sealed class ObserverVisualMask
{
    private readonly Dictionary<Renderer, bool> saved = new Dictionary<Renderer, bool>();
    private readonly List<Renderer> current = new List<Renderer>();
    private Player? owner;
    private float nextScan;
    internal void Apply(Player? player, bool hide)
    {
        if (!hide || !player) { Restore(); return; }
        if (owner != player) { Restore(); owner = player; nextScan = 0; }
        if (Time.unscaledTime >= nextScan)
        {
            // Equipment can replace renderers; discard destroyed objects each scan.
            var dead = new List<Renderer>();
            foreach (var item in saved) if (!item.Key) dead.Add(item.Key!);
            foreach (Renderer renderer in dead) saved.Remove(renderer);
            nextScan = Time.unscaledTime + 1f;
        }
        current.Clear();
        player.GetComponentsInChildren<Renderer>(true, current);
        foreach (Renderer renderer in current)
        {
            if (!saved.ContainsKey(renderer)) saved[renderer] = renderer.forceRenderingOff;
            renderer.forceRenderingOff = true;
        }
    }
    internal void Restore()
    {
        foreach (var item in saved) if (item.Key) item.Key.forceRenderingOff = item.Value;
        saved.Clear(); current.Clear(); owner = null;
    }
}
