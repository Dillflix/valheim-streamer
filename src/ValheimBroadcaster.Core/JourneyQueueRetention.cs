using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>Queued proofs form an ordered dependency chain. A failed tail does
/// not revoke the earlier certified prefix; every dependent tail and pending
/// continuation must be retired together.</summary>
public static class JourneyQueueRetention
{
    public static void RetireFrom<T>(List<T> queue, int firstInvalid, Action cancelPreparation) where T : IDisposable
    {
        if (queue == null || cancelPreparation == null) throw new ArgumentNullException(nameof(queue));
        if (firstInvalid < 0 || firstInvalid > queue.Count) throw new ArgumentOutOfRangeException(nameof(firstInvalid));
        cancelPreparation();
        for (int i = firstInvalid; i < queue.Count; i++) queue[i].Dispose();
        queue.RemoveRange(firstInvalid, queue.Count - firstInvalid);
    }
}
