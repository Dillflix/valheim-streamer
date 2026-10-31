using System.Collections.Generic;
using UnityEngine;
namespace ValheimBroadcaster.Plugin;

// Local presentation only. Never disables the player object, colliders, network view,
// or AI/simulation. Other clients continue to see an ordinary player.
internal sealed class ObserverVisualMask
{
    private readonly Dictionary<Renderer, bool> saved = new Dictionary<Renderer, bool>();
    private readonly List<Renderer> current = new List<Renderer>();
    private readonly Dictionary<Light, bool> lights = new Dictionary<Light, bool>();
    private readonly Dictionary<AudioSource, bool> audio = new Dictionary<AudioSource, bool>();
    private Player? owner;
    private float nextScan;
    internal void Apply(Player? player, bool hide, bool hideEffects = false)
    {
        if (!hide || !player) { Restore(); return; }
        if (owner != player) { Restore(); owner = player; nextScan = 0; }
        if (Time.unscaledTime >= nextScan)
        {
            // Equipment can replace renderers; discard destroyed objects each scan.
            var dead = new List<Renderer>();
            foreach (var item in saved) if (!item.Key) dead.Add(item.Key!);
            foreach (Renderer renderer in dead) saved.Remove(renderer);
            Prune(lights); Prune(audio);
            nextScan = Time.unscaledTime + 1f;
        }
        if (hideEffects)
        {
            foreach (Light light in player.GetComponentsInChildren<Light>(true))
            { if (!lights.ContainsKey(light)) lights[light] = light.enabled; light.enabled = false; }
            foreach (AudioSource source in player.GetComponentsInChildren<AudioSource>(true))
            { if (!audio.ContainsKey(source)) audio[source] = source.mute; source.mute = true; }
        }
        current.Clear();
        player.GetComponentsInChildren<Renderer>(true, current);
        foreach (Renderer renderer in current)
        {
            if (!saved.ContainsKey(renderer)) saved[renderer] = renderer.forceRenderingOff;
            renderer.forceRenderingOff = true;
        }
    }
    private static void Prune<T>(Dictionary<T, bool> entries) where T : Object
    {
        var dead = new List<T>();
        foreach (T item in entries.Keys) if (!item) dead.Add(item!);
        foreach (T item in dead) entries.Remove(item);
    }
    internal void Restore()
    {
        foreach (var item in saved) if (item.Key) item.Key.forceRenderingOff = item.Value;
        foreach (var item in lights) if (item.Key) item.Key.enabled = item.Value;
        foreach (var item in audio) if (item.Key) item.Key.mute = item.Value;
        lights.Clear(); audio.Clear(); saved.Clear(); current.Clear(); owner = null;
    }
}
