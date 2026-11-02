using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// Loaded wildlife is only a discovery input. Preview one candidate at a time,
// without changing the output camera, before offering a cut to coverage.
internal sealed class CreatureShotAdmission
{
    private readonly Dictionary<string, Evidence> evidence = new Dictionary<string, Evidence>(StringComparer.Ordinal);
    private readonly List<string> offered = new List<string>(32);
    private float nextPreview;
    private int nextCandidate;
    private readonly struct Evidence
    {
        internal readonly Vector3 Position;
        internal readonly float At;
        internal readonly bool Readable;
        internal Evidence(Vector3 position, float at, bool readable) { Position = position; At = at; Readable = readable; }
    }

    internal IReadOnlyList<string> Offer(IdleShots idle, string? activeCreature,
        Func<Character, bool> preview)
    {
        IReadOnlyList<string> loaded = idle.ReadyIds();
        float now = Time.unscaledTime;
        if (activeCreature == null && loaded.Count > 0 && now >= nextPreview)
        {
            nextPreview = now + .5f;
            string id = loaded[nextCandidate++ % loaded.Count];
            Character? actor = idle.Find(id);
            if (actor)
            {
                if (evidence.Count >= 32 && !evidence.ContainsKey(id)) evidence.Clear();
                evidence[id] = new Evidence(actor!.transform.position, now, preview(actor));
            }
            if (nextCandidate > 1000000) nextCandidate = 0;
        }
        offered.Clear();
        foreach (string id in loaded)
        {
            // An on-air subject is validated by the live solver each frame;
            // expiry of its off-air preview cannot itself cut away from it.
            if (id == activeCreature) { offered.Add(id); continue; }
            Character? actor = idle.Find(id);
            if (actor && evidence.TryGetValue(id, out Evidence proof) && proof.Readable &&
                now >= proof.At && now - proof.At <= 2f &&
                Vector3.Distance(actor!.transform.position, proof.Position) <= 3f)
                offered.Add(id);
        }
        return offered;
    }

    internal void Reset() { evidence.Clear(); offered.Clear(); nextPreview = 0; nextCandidate = 0; }
}
