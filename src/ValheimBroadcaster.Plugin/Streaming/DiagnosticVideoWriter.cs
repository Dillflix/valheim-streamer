using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin.Streaming;

/// <summary>Independent, bounded local video recorder. Never shares a pipe,
/// encoder, audio tap or failure state with the live broadcast.</summary>
internal sealed class DiagnosticVideoWriter : IDisposable
{
    internal const int Width = 640, Height = 360, Fps = 12, FramesPerSegment = 300;
    internal const int MaximumQueued = 8, MaximumSegments = 8;
    internal const long MaximumSegmentBytes = 8 * 1024 * 1024, MaximumBytes = 96 * 1024 * 1024;
    private readonly ConcurrentQueue<Item> queue = new ConcurrentQueue<Item>();
    private readonly ConcurrentQueue<byte[]> buffers = new ConcurrentQueue<byte[]>();
    private readonly AutoResetEvent wake = new AutoResetEvent(false);
    private readonly object gate = new object();
    private readonly Thread worker;
    private readonly System.Threading.Timer watchdog;
    private readonly string directory, executable, version;
    private readonly long? retentionBytes;
    private volatile Process? process;
    private int processId;
    internal int ProcessId => Volatile.Read(ref processId);
    private SegmentState? sink;
    private int stopped, pending, segment;
    private long written, dropped, lastWriteTicks, stopTicks;
    private string? error;
    internal string SessionId { get; } = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    internal string? Error => Volatile.Read(ref error);
    internal bool Closed => !worker.IsAlive;
    internal long Written => Interlocked.Read(ref written);
    internal long Dropped => Interlocked.Read(ref dropped);
    internal int Pending => Volatile.Read(ref pending);
    internal string DirectoryPath => directory;

    private readonly struct Item
    {
        internal readonly byte[] Pixels;
        internal readonly string Metadata;
        internal readonly double At;
        internal readonly long Generation;
        internal Item(byte[] pixels, string metadata, double at, long generation)
        { Pixels = pixels; Metadata = metadata; At = at; Generation = generation; }
    }

    private sealed class SegmentState
    {
        internal int EncodedFrames, Ended;
        internal string? Error;
        internal Thread? Worker;
    }

    internal DiagnosticVideoWriter(string directory, string executable, string version, long? retentionBytes = null)
    {
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
        this.executable = executable ?? throw new ArgumentNullException(nameof(executable));
        if (retentionBytes.HasValue && retentionBytes.Value < MaximumSegmentBytes + 3 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(retentionBytes));
        this.retentionBytes = retentionBytes;
        this.version = version ?? "unknown";
        // A fixed pool bounds CPU memory and avoids allocating a megapixel byte
        // array for every frame. One readback may own a buffer outside the queue.
        for (int i = 0; i < MaximumQueued + 2; i++) buffers.Enqueue(new byte[Width * Height * 4]);
        worker = new Thread(Write) { IsBackground = true, Name = "Broadcaster diagnostic video" };
        watchdog = new System.Threading.Timer(Watch, null, 1000, 1000);
        worker.Start();
    }

    internal byte[]? Rent() => Volatile.Read(ref stopped) == 0 && Error == null && buffers.TryDequeue(out byte[]? result) ? result : null;
    internal void Return(byte[] buffer) { if (buffer.Length == Width * Height * 4) buffers.Enqueue(buffer); }
    internal void Drop() => Interlocked.Increment(ref dropped);
    internal bool Submit(byte[] pixels, string metadata, double at, long generation)
    {
        lock (gate)
        {
        if (double.IsNaN(at) || double.IsInfinity(at) || at < 0 || pixels.Length != Width * Height * 4 || metadata.Length > 8192 || !metadata.EndsWith("}", StringComparison.Ordinal) ||
            Volatile.Read(ref stopped) != 0 || Error != null)
        {
            Return(pixels); Drop(); return false;
        }
        if (Interlocked.Increment(ref pending) > MaximumQueued)
        { Interlocked.Decrement(ref pending); Return(pixels); Drop(); return false; }
        queue.Enqueue(new Item(pixels, metadata, at, generation)); wake.Set(); return true;
        }
    }

    private void Write()
    {
        StreamWriter? map = null;
        byte[]? retained = null;
        string retainedMetadata = "";
        int frame = 0;
        long mapBytes = 0;
        var timeline = new DiagnosticVideoTimeline();
        try
        {
            while (Volatile.Read(ref stopped) == 0 || !queue.IsEmpty)
            {
                if (!queue.TryDequeue(out Item item)) { map?.Flush(); wake.WaitOne(100); continue; }
                Interlocked.Decrement(ref pending);
                bool keep = false;
                try
                {
                    DiagnosticVideoSlot slot = timeline.Offer(item.At, item.Generation);
                    if (!slot.Accepted) { Drop(); continue; }
                    if (slot.NewSegment)
                    {
                        CloseSegment(frame);
                        map?.WriteLine(new CameraTraceRecord("video-end", 0).Field("complete", true).Field("frames", frame).ToString());
                        map?.Dispose(); map = null;
                        Directory.CreateDirectory(directory); Prune(directory, retentionBytes);
                        string path = Path.Combine(directory, "footage-" + SessionId + "-" + (++segment).ToString("D5", CultureInfo.InvariantCulture));
                        process = Start(executable, path + ".mp4", out sink);
                        Volatile.Write(ref processId, process.Id);
                        map = new StreamWriter(new FileStream(path + ".jsonl", FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                        string header = new CameraTraceRecord("video-header", item.At).Field("version", version)
                            .Field("videoSession", SessionId).Field("video", Path.GetFileName(path) + ".mp4")
                            .Field("width", Width).Field("height", Height).Field("fps", Fps)
                            .Field("audio", false).Field("rowOrder", "TopFirst").Field("precedingSampleGap", slot.SampleGap)
                            .Field("clock", "Unity clock resampled at 12fps; diagnostic repeats explicit; gaps over 0.25s split segments")
                            .ToString();
                        map.WriteLine(header); mapBytes = Encoding.UTF8.GetByteCount(header) + 1; frame = 0;
                    }
                    for (int i = 0; i < slot.Repeats; i++)
                    {
                        if (retained == null) throw new InvalidOperationException("Missing diagnostic repeat source.");
                        WriteFrame(retained, retainedMetadata, map!, frame++, true, ref mapBytes);
                    }
                    WriteFrame(item.Pixels, item.Metadata, map!, frame++, false, ref mapBytes);
                    if (frame != slot.Slot + 1) throw new InvalidOperationException("Diagnostic timeline diverged.");
                    if (retained != null) Return(retained);
                    retained = item.Pixels; retainedMetadata = item.Metadata; keep = true;
                }
                finally { if (!keep) Return(item.Pixels); }
            }
            CloseSegment(frame);
            map?.WriteLine(new CameraTraceRecord("video-end", 0).Field("complete", true).Field("frames", frame).ToString());
        }
        catch (Exception failure)
        {
            Interlocked.CompareExchange(ref error, "Diagnostic recorder stopped (" + failure.GetType().Name + "); broadcasting continues.", null);
            try { map?.WriteLine(new CameraTraceRecord("video-end", 0).Field("complete", false).Field("submittedFrames", frame).ToString()); } catch { }
            Kill();
        }
        finally
        {
            lock (gate) Interlocked.Exchange(ref stopped, 1);
            while (queue.TryDequeue(out Item item)) { Return(item.Pixels); Interlocked.Decrement(ref pending); Drop(); }
            if (retained != null) Return(retained);
            try { map?.Dispose(); } catch { }
            Kill(); process?.Dispose(); process = null; Volatile.Write(ref processId, 0); watchdog.Dispose();
        }
    }

    private void WriteFrame(byte[] pixels, string metadata, StreamWriter map, int frame, bool repeat, ref long bytes)
    {
        Interlocked.Exchange(ref lastWriteTicks, DateTime.UtcNow.Ticks);
        process!.StandardInput.BaseStream.Write(pixels, 0, pixels.Length);
        Interlocked.Exchange(ref lastWriteTicks, 0);
        string row = metadata.Substring(0, metadata.Length - 1) +
            ",\"segment\":" + segment.ToString(CultureInfo.InvariantCulture) +
            ",\"videoFrame\":" + frame.ToString(CultureInfo.InvariantCulture) +
            ",\"videoSeconds\":" + ((double)frame / Fps).ToString("R", CultureInfo.InvariantCulture) +
            ",\"diagnosticRepeat\":" + (repeat ? "true" : "false") +
            ",\"droppedTotal\":" + Dropped.ToString(CultureInfo.InvariantCulture) + "}";
        bytes += Encoding.UTF8.GetByteCount(row) + 1;
        if (bytes > 3 * 1024 * 1024 - 1024) throw new IOException("Diagnostic metadata byte allowance exhausted.");
        map.WriteLine(row); Interlocked.Increment(ref written);
        if (frame % Fps == 0) map.Flush();
    }

    internal static string Arguments(string output) =>
        "-hide_banner -loglevel error -nostats -progress pipe:2 -nostdin -f rawvideo -pixel_format rgba -video_size " + Width + "x" + Height +
        " -framerate " + Fps + " -i pipe:0 -an -c:v libx264 -preset ultrafast -crf 26 -threads 1 -pix_fmt yuv420p -bf 0 -g " +
        Fps * 2 + " -fs " + MaximumSegmentBytes + " -movflags frag_keyframe+empty_moov+default_base_moof -f mp4 " + Quote(output);

    private static string Quote(string path)
    {
        if (path.IndexOfAny(new[] { '\r', '\n', '\0', '"' }) >= 0) throw new ArgumentException("Invalid diagnostic path.");
        // Output ends in .mp4, so a Windows trailing-backslash quote cannot occur.
        return "\"" + path + "\"";
    }

    private static Process Start(string executable, string path, out SegmentState state)
    {
        state = new SegmentState();
        SegmentState current = state;
        var result = new Process { StartInfo = new ProcessStartInfo(executable, Arguments("pipe:1")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardError = true, RedirectStandardOutput = true } };
        try
        {
            result.ErrorDataReceived += (_, e) =>
            {
                string? line = e.Data;
                if (line != null && line.StartsWith("frame=", StringComparison.Ordinal) &&
                    int.TryParse(line.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                    Volatile.Write(ref current.EncodedFrames, count);
                if (line == "progress=end") Volatile.Write(ref current.Ended, 1);
            };
            if (!result.Start()) throw new IOException("Could not start diagnostic encoder.");
            result.BeginErrorReadLine();
            current.Worker = new Thread(() =>
            {
                try
                {
                    using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    byte[] data = new byte[16384]; long bytes = 0; int count;
                    while ((count = result.StandardOutput.BaseStream.Read(data, 0, data.Length)) != 0)
                    {
                        // Exact byte cap even if FFmpeg's -fs ends after a large packet.
                        if (bytes + count > MaximumSegmentBytes) throw new IOException("Diagnostic video byte allowance exhausted.");
                        file.Write(data, 0, count); bytes += count;
                    }
                }
                catch (Exception failure)
                {
                    Volatile.Write(ref current.Error, failure.GetType().Name);
                    try { if (!result.HasExited) result.Kill(); } catch { }
                }
            }) { IsBackground = true, Name = "Broadcaster diagnostic file" };
            current.Worker.Start(); return result;
        }
        catch
        {
            try { if (!result.HasExited) result.Kill(); } catch { }
            result.Dispose(); throw;
        }
    }

    private void CloseSegment(int expectedFrames)
    {
        Process? current = process;
        if (current == null) return;
        Interlocked.Exchange(ref lastWriteTicks, DateTime.UtcNow.Ticks);
        current.StandardInput.Close();
        if (!current.WaitForExit(3000)) { Kill(); throw new IOException("Diagnostic encoder did not finish."); }
        current.WaitForExit(); // Drain the finite progress callback after process exit.
        bool sinkFinished = sink?.Worker?.Join(3000) == true;
        int code = current.ExitCode;
        process = null; Volatile.Write(ref processId, 0); current.Dispose(); Interlocked.Exchange(ref lastWriteTicks, 0);
        if (code != 0 || !sinkFinished || sink!.Error != null || sink.Ended != 1 || sink.EncodedFrames != expectedFrames)
            throw new IOException("Diagnostic encoder did not confirm every mapped frame.");
        sink = null;
    }

    internal static void Prune(string directory, long? retentionBytes = null)
    {
        // Files outside this exact prefix/extensions are never touched.
        string[] files = Directory.GetFiles(directory, "footage-*")
            .Where(f => f.EndsWith(".mp4", StringComparison.Ordinal) || f.EndsWith(".jsonl", StringComparison.Ordinal)).ToArray();
        var groups = files.GroupBy(f => Path.Combine(directory, Path.GetFileNameWithoutExtension(f)))
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.ToArray()).ToList();
        long total = files.Sum(f => new FileInfo(f).Length);
        while ((!retentionBytes.HasValue && groups.Count >= MaximumSegments) || total > (retentionBytes ?? MaximumBytes) - (MaximumSegmentBytes + 3 * 1024 * 1024))
        {
            if (groups.Count == 0) break;
            foreach (string file in groups[0]) { total -= new FileInfo(file).Length; File.Delete(file); }
            groups.RemoveAt(0);
        }
    }

    private void Watch(object? _)
    {
        long started = Interlocked.Read(ref lastWriteTicks), stop = Interlocked.Read(ref stopTicks), now = DateTime.UtcNow.Ticks;
        if (started > 0 && now - started > TimeSpan.FromSeconds(5).Ticks || stop > 0 && now - stop > TimeSpan.FromSeconds(4).Ticks)
        {
            Interlocked.CompareExchange(ref error, "Diagnostic encoder stalled; capture stopped while broadcasting continues.", null);
            lock (gate) Interlocked.Exchange(ref stopped, 1);
            Kill(); wake.Set();
        }
    }

    private void Kill()
    {
        Process? current = process;
        if (current == null) return;
        try { if (!current.HasExited) current.Kill(); } catch { }
        // Worker owns disposal; closing stdin from a concurrent watchdog is unsafe.
    }

    public void Dispose()
    {
        lock (gate)
        {
        if (Interlocked.Exchange(ref stopped, 1) == 0)
        { Interlocked.Exchange(ref stopTicks, DateTime.UtcNow.Ticks); wake.Set(); }
        }
        // Draining/finalizing runs off the Unity thread. Watchdog bounds shutdown.
    }
}
