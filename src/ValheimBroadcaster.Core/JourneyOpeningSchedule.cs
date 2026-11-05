using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>Fair service of persistent, independently observed departure
/// origins. This owns service order, not graph lifetimes or world evidence.
/// A work slice ending or an unavailable observation never removes an origin.
/// The caller removes genuinely exhausted origins or resets changed context.</summary>
public sealed class JourneyOpeningSchedule
{
    private readonly List<long> origins = new List<long>();
    private readonly int maximumSlots, discoveryEvery;
    private int cursor, serviced;
    private const int ProofBurst = 3;
    private long proofOwner = -1, lastSelected = -1;
    private int proofSlices;
    private bool firstDiscovery = true;
    public int Count => origins.Count;

    public JourneyOpeningSchedule(int maximumSlots = 8, int discoveryEvery = 4)
    {
        if (maximumSlots < 1 || maximumSlots > 8) throw new ArgumentOutOfRangeException(nameof(maximumSlots));
        if (discoveryEvery < 1 || discoveryEvery > 16) throw new ArgumentOutOfRangeException(nameof(discoveryEvery));
        this.maximumSlots = maximumSlots; this.discoveryEvery = discoveryEvery;
    }

    public bool Register(long id)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (origins.Count >= maximumSlots || origins.Contains(id)) return false;
        origins.Add(id); return true;
    }

    public bool Contains(long id) => origins.Contains(id);

    public bool Remove(long id)
    {
        int index = origins.IndexOf(id);
        if (index < 0) return false;
        origins.RemoveAt(index);
        if (proofOwner == id) { proofOwner = -1; proofSlices = 0; }
        if (index < cursor) cursor--;
        if (cursor >= origins.Count) cursor = 0;
        return true;
    }

    public bool TryNext(out long id)
    {
        if (origins.Count == 0) { id = 0; return false; }
        if (proofOwner >= 0 && proofSlices > 0)
        { id = proofOwner; proofSlices--; }
        else
        {
            id = origins[cursor]; cursor = (cursor + 1) % origins.Count;
            // One ordinary neighbour gets work between bounded proof bursts.
            // Keep the productive proof owner so several simultaneous renewals
            // cannot dilute every path's freshness below its five-second bound.
            if (id == proofOwner && origins.Count > 1)
            { id = origins[cursor]; cursor = (cursor + 1) % origins.Count; }
            if (proofOwner >= 0) proofSlices = ProofBurst;
        }
        lastSelected = id;
        serviced = Math.Min(discoveryEvery, serviced + 1); return true;
    }

    /// <summary>Only measured progress earns selected-proof priority. Missing
    /// observations or a stalled query immediately yield its burst; completion
    /// returns all work to ordinary round-robin and discovery scheduling.</summary>
    public void ReportProofProgress(long id, bool proofPending, bool madeProgress, bool waiting)
    {
        if (id != lastSelected || !origins.Contains(id)) return;
        if (proofOwner == id)
        {
            if (!proofPending || !madeProgress || waiting) { proofOwner = -1; proofSlices = 0; }
            return;
        }
        if (proofOwner < 0 && proofPending && madeProgress && !waiting)
        { proofOwner = id; proofSlices = ProofBurst; }
    }

    /// <summary>Discovery itself may span many observation slices. Each turn
    /// advances that existing work; it does not restart or silently complete it.
    /// Known origins continue receiving service while more origins are surveyed.</summary>
    public bool TakeDiscoveryTurn(bool pendingOpening, bool catalogueExhausted)
    {
        if (origins.Count >= maximumSlots || catalogueExhausted && !pendingOpening) return false;
        if (!firstDiscovery && origins.Count > 0 && serviced < discoveryEvery) return false;
        firstDiscovery = false; serviced = 0; return true;
    }

    public void Reset()
    { origins.Clear(); cursor = serviced = proofSlices = 0; proofOwner = lastSelected = -1; firstDiscovery = true; }
}
