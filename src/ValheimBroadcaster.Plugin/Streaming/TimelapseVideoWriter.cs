using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin.Streaming;

/// <summary>One silent accelerated clip. Every accepted source photograph is
/// exactly one output frame; unlike diagnostic CFR capture, gaps never invent
/// repeats. Encoding, metadata, storage checks and finalization are off-thread.</summary>
internal sealed class TimelapseVideoWriter : IDisposable
{
    internal const int Width = 1280, Height = 720, MaximumQueued = 3;
    internal const long MaximumVideoBytes = 256L * 1024 * 1024, MaximumMetadataBytes = 32L * 1024 * 1024;
    internal const long MaximumDirectoryBytes = 2L * 1024 * 1024 * 1024;
    private readonly ConcurrentQueue<Item> queue = new ConcurrentQueue<Item>();
    private readonly ConcurrentQueue<byte[]> buffers = new ConcurrentQueue<byte[]>();
    private readonly AutoResetEvent wake = new AutoResetEvent(false);
    private readonly object gate = new object();
    private readonly Thread worker;
    private readonly System.Threading.Timer watchdog;
    private readonly string directory, executable, version;
    private readonly TimelapseClock settings;
    private readonly long videoLimit;
    private volatile Process? process;
    private int processId;
    internal int ProcessId => Volatile.Read(ref processId);
    private int stopped, pending, encodedFrames, progressEnded;
    private long written, dropped, ioTicks, stopTicks;
    private string? error, sinkError;
    private string stopReason = "Capture completed";
    private Thread? sinkWorker;
    internal string SessionId { get; } = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    internal string OutputPath => Path.Combine(directory, "timelapse-" + SessionId + ".mp4");
    internal string? Error => Volatile.Read(ref error);
    internal bool Closed => !worker.IsAlive;
    internal long Written => Interlocked.Read(ref written);
    internal long Dropped => Interlocked.Read(ref dropped);
    internal int Pending => Volatile.Read(ref pending);

    private readonly struct Item
    {
        internal readonly byte[] Pixels;
        internal readonly string Metadata;
        internal readonly double At;
        internal Item(byte[] pixels, string metadata, double at) { Pixels = pixels; Metadata = metadata; At = at; }
    }

    internal TimelapseVideoWriter(string directory, string executable, string version, TimelapseClock settings,
        long maximumVideoBytes = MaximumVideoBytes)
    {
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
        this.executable = executable ?? throw new ArgumentNullException(nameof(executable));
        this.version = version ?? "unknown"; this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (maximumVideoBytes < 1024 || maximumVideoBytes > MaximumVideoBytes) throw new ArgumentOutOfRangeException(nameof(maximumVideoBytes));
        videoLimit = maximumVideoBytes;
        for (int i = 0; i < MaximumQueued + 2; i++) buffers.Enqueue(new byte[Width * Height * 4]);
        worker = new Thread(Write) { IsBackground = true, Name = "Broadcaster timelapse encoder" };
        watchdog = new System.Threading.Timer(Watch, null, 1000, 1000); worker.Start();
    }

    internal byte[]? Rent() => Volatile.Read(ref stopped) == 0 && Error == null && buffers.TryDequeue(out byte[]? result) ? result : null;
    internal void Return(byte[] buffer) { if (buffer.Length == Width * Height * 4) buffers.Enqueue(buffer); }
    internal void Drop() => Interlocked.Increment(ref dropped);
    internal bool Submit(byte[] pixels, string metadata, double at)
    {
        lock (gate)
        {
            if (double.IsNaN(at) || double.IsInfinity(at) || at < 0 || pixels.Length != Width * Height * 4 ||
                metadata.Length > 16384 || !metadata.EndsWith("}", StringComparison.Ordinal) ||
                Volatile.Read(ref stopped) != 0 || Error != null)
            { Return(pixels); Drop(); return false; }
            if (Interlocked.Increment(ref pending) > MaximumQueued)
            { Interlocked.Decrement(ref pending); Return(pixels); Drop(); return false; }
            queue.Enqueue(new Item(pixels, metadata, at)); Signal(); return true;
        }
    }

    private void Write()
    {
        StreamWriter? map = null;
        long mapBytes = 0;
        int frame = 0;
        double firstAt = -1, previousAt = -1;
        try
        {
            while (Volatile.Read(ref stopped) == 0 || !queue.IsEmpty)
            {
                if (!queue.TryDequeue(out Item item)) { map?.Flush(); wake.WaitOne(100); continue; }
                Interlocked.Decrement(ref pending);
                try
                {
                    if (item.At <= previousAt) { Drop(); continue; }
                    if (frame >= TimelapseClock.MaximumFrames) throw new IOException("Timelapse frame allowance exhausted.");
                    if (map == null)
                    {
                        Directory.CreateDirectory(directory);
                        CheckStorage(directory, videoLimit + MaximumMetadataBytes);
                        Start();
                        map = new StreamWriter(new FileStream(Path.ChangeExtension(OutputPath, ".jsonl"), FileMode.CreateNew,
                            FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                        string header = new CameraTraceRecord("timelapse-header", item.At).Field("version", version)
                            .Field("session", SessionId).Field("video", Path.GetFileName(OutputPath)).Field("width", Width).Field("height", Height)
                            .Field("outputFps", settings.OutputFps).Field("intervalSeconds", settings.IntervalSeconds)
                            .Field("requestedCaptureSeconds", settings.CaptureSeconds).Field("plannedOutputSeconds", settings.PlannedOutputSeconds)
                            .Field("audio", false).Field("rowOrder", "TopFirst")
                            .Field("clock", "One captured live source image per output frame; unscaled source seconds, explicit CFR output time; omitted intervals never repeated")
                            .ToString();
                        map.WriteLine(header); mapBytes = Encoding.UTF8.GetByteCount(header) + 1; firstAt = item.At;
                    }
                    double gap = previousAt >= 0 ? item.At - previousAt : 0;
                    string row = item.Metadata.Substring(0, item.Metadata.Length - 1) +
                        ",\"outputFrame\":" + frame.ToString(CultureInfo.InvariantCulture) +
                        ",\"outputSeconds\":" + ((double)frame / settings.OutputFps).ToString("R", CultureInfo.InvariantCulture) +
                        ",\"sourceElapsedSeconds\":" + (item.At - firstAt).ToString("R", CultureInfo.InvariantCulture) +
                        ",\"sourceGapSeconds\":" + gap.ToString("R", CultureInfo.InvariantCulture) +
                        ",\"droppedTotal\":" + Dropped.ToString(CultureInfo.InvariantCulture) + "}";
                    mapBytes += Encoding.UTF8.GetByteCount(row) + 1;
                    if (mapBytes > MaximumMetadataBytes - 2048) throw new IOException("Timelapse metadata allowance exhausted.");
                    Interlocked.Exchange(ref ioTicks, DateTime.UtcNow.Ticks);
                    process!.StandardInput.BaseStream.Write(item.Pixels, 0, item.Pixels.Length);
                    Interlocked.Exchange(ref ioTicks, 0);
                    map.WriteLine(row); map.Flush();
                    previousAt = item.At; frame++; Interlocked.Increment(ref written);
                }
                finally { Return(item.Pixels); }
            }
            CloseEncoder(frame);
            map?.WriteLine(new CameraTraceRecord("timelapse-end", Math.Max(0, previousAt)).Field("complete", true)
                .Field("frames", frame).Field("outputSeconds", (double)frame / settings.OutputFps).Field("reason", stopReason)
                .Field("droppedTotal", Dropped).ToString());
        }
        catch (Exception failure)
        {
            Interlocked.CompareExchange(ref error, sinkError ?? (failure is IOException ? failure.Message :
                "Timelapse encoder failed (" + failure.GetType().Name + ")."), null);
            try { map?.WriteLine(new CameraTraceRecord("timelapse-end", Math.Max(0, previousAt)).Field("complete", false)
                .Field("submittedFrames", frame).Field("reason", Error ?? "Encoder failure").ToString()); } catch { }
            Kill();
        }
        finally
        {
            lock (gate) Interlocked.Exchange(ref stopped, 1);
            while (queue.TryDequeue(out Item item)) { Return(item.Pixels); Interlocked.Decrement(ref pending); Drop(); }
            try { map?.Dispose(); } catch { }
            Kill(); sinkWorker?.Join(3000); process?.Dispose(); process = null; Volatile.Write(ref processId, 0); watchdog.Dispose(); wake.Dispose();
            while (buffers.TryDequeue(out _)) { }
        }
    }

    internal static string Arguments(int fps, long byteLimit) =>
        "-hide_banner -loglevel error -nostats -progress pipe:2 -nostdin -f rawvideo -pixel_format rgba -video_size " + Width + "x" + Height +
        " -framerate " + fps.ToString(CultureInfo.InvariantCulture) + " -i pipe:0 -an -c:v libx264 -preset veryfast -crf 22 -threads 2 -pix_fmt yuv420p -bf 0 -g " +
        (fps * 2).ToString(CultureInfo.InvariantCulture) + " -fs " + byteLimit.ToString(CultureInfo.InvariantCulture) +
        " -movflags frag_keyframe+empty_moov+default_base_moof -f mp4 pipe:1";

    private void Start()
    {
        var child = new Process { StartInfo = new ProcessStartInfo(executable, Arguments(settings.OutputFps, videoLimit)) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardError = true, RedirectStandardOutput = true } };
        process = child;
        child.ErrorDataReceived += (_, e) =>
        {
            string? line = e.Data;
            if (line != null && line.StartsWith("frame=", StringComparison.Ordinal) &&
                int.TryParse(line.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)) Volatile.Write(ref encodedFrames, count);
            if (line == "progress=end") Volatile.Write(ref progressEnded, 1);
        };
        if (!child.Start()) throw new IOException("Could not start timelapse encoder.");
        Volatile.Write(ref processId, child.Id);
        child.BeginErrorReadLine();
        sinkWorker = new Thread(() =>
        {
            try
            {
                using var file = new FileStream(OutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                byte[] block = new byte[16384]; long bytes = 0; int count;
                while ((count = child.StandardOutput.BaseStream.Read(block, 0, block.Length)) != 0)
                {
                    if (bytes + count > videoLimit) throw new IOException("Timelapse video allowance exhausted.");
                    file.Write(block, 0, count); bytes += count;
                }
            }
            catch (Exception failure)
            { Volatile.Write(ref sinkError, failure is IOException ? failure.Message : "Timelapse file write failed (" + failure.GetType().Name + ")."); Kill(); }
        }) { IsBackground = true, Name = "Broadcaster timelapse file" };
        sinkWorker.Start();
    }

    private void CloseEncoder(int expectedFrames)
    {
        Process? child = process;
        if (child == null) return;
        Interlocked.Exchange(ref ioTicks, DateTime.UtcNow.Ticks);
        child.StandardInput.Close();
        if (!child.WaitForExit(5000)) { Kill(); throw new IOException("Timelapse encoder did not finish."); }
        child.WaitForExit();
        bool ended = sinkWorker?.Join(3000) == true;
        if (sinkError != null) throw new IOException(sinkError);
        if (child.ExitCode != 0 || !ended || progressEnded != 1 || encodedFrames != expectedFrames)
            throw new IOException("Timelapse encoder did not confirm every mapped frame; retain the partial clip and metadata for diagnosis.");
        Interlocked.Exchange(ref ioTicks, 0);
    }

    internal static void CheckStorage(string directory, long reserve)
    {
        long bytes = 0; int count = 0;
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (++count > 4096) throw new IOException("Timelapse output folder has too many files; archive recordings before starting another clip.");
            bytes += new FileInfo(file).Length;
            if (bytes > MaximumDirectoryBytes - reserve)
                throw new IOException("Timelapse output allowance reached; archive recordings before starting another clip. No recordings were deleted.");
        }
    }

    private void Watch(object? _)
    {
        long io = Interlocked.Read(ref ioTicks), stop = Interlocked.Read(ref stopTicks), now = DateTime.UtcNow.Ticks;
        if (io > 0 && now - io > TimeSpan.FromSeconds(10).Ticks || stop > 0 && now - stop > TimeSpan.FromSeconds(12).Ticks)
        {
            Interlocked.CompareExchange(ref error, "Timelapse encoder stalled; live broadcasting continues.", null);
            lock (gate) Interlocked.Exchange(ref stopped, 1);
            Kill(); Signal();
        }
    }
    private void Kill() { try { if (process != null && !process.HasExited) process.Kill(); } catch { } }
    private void Signal() { try { wake.Set(); } catch (ObjectDisposedException) { } }
    internal void Complete(string reason)
    {
        lock (gate)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            stopReason = reason.Length > 256 ? reason.Substring(0, 256) : reason;
            Interlocked.Exchange(ref stopTicks, DateTime.UtcNow.Ticks); Signal();
        }
    }
    public void Dispose() => Complete("Capture stopped");
}
