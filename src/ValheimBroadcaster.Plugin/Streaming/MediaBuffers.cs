using System;
using System.Threading;

namespace ValheimBroadcaster.Plugin.Streaming;

// One audio-thread producer, one encoder-thread consumer. No locks, I/O or allocations
// in Push. Overflow is explicit: silently losing samples would conceal drift.
internal sealed class AudioRing
{
    private readonly float[] samples;
    private long written, read;
    internal AudioRing(int capacity) { samples = new float[capacity]; }
    internal bool Push(float[] data, int channels)
    {
        if ((channels != 1 && channels != 2) || data.Length % channels != 0) return false;
        int count = data.Length / channels * 2;
        long w = written;
        if (count > samples.Length - (w - Volatile.Read(ref read))) return false;
        for (int i = 0; i < count; i++) samples[(int)((w + i) % samples.Length)] = data[channels == 1 ? i / 2 : i];
        Volatile.Write(ref written, w + count);
        return true;
    }
    internal int Pop(float[] destination)
    {
        long r = read;
        int count = (int)Math.Min(destination.Length, Volatile.Read(ref written) - r);
        for (int i = 0; i < count; i++) destination[i] = samples[(int)((r + i) % samples.Length)];
        Array.Clear(destination, count, destination.Length - count);
        Volatile.Write(ref read, r + count);
        return count;
    }
}

// Three arrays: producer-owned, pending, consumer-owned. Only ownership swaps lock;
// neither copying pixels nor pipe writes occur under the lock. Stale pending frames
// are replaced, not queued. The consumer repeats its last frame when capture is slow.
internal sealed class VideoMailbox
{
    private readonly object gate = new object();
    private byte[] pending;
    private bool available;
    internal VideoMailbox(int length) { pending = new byte[length]; }
    internal byte[] Publish(byte[] frame)
    {
        lock (gate) { byte[] old = pending; pending = frame; available = true; return old; }
    }
    internal byte[] Take(byte[] previous)
    {
        lock (gate)
        {
            if (!available) return previous;
            byte[] next = pending; pending = previous; available = false; return next;
        }
    }
}
