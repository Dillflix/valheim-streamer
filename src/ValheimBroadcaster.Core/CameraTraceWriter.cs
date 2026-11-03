using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace ValheimBroadcaster.Core;

/// <summary>Bounded asynchronous flight recorder. The producer never waits on
/// disk. Files rotate; queue loss and I/O failures remain visible to the caller.</summary>
public sealed class CameraTraceWriter : IDisposable
{
    public const int MaximumQueued = 2048, MaximumLine = 16384, MaximumFiles = 8;
    public const long FileBytes = 4 * 1024 * 1024;
    private readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
    private readonly AutoResetEvent wake = new AutoResetEvent(false);
    private readonly Thread worker;
    private readonly string directory, header, session;
    private readonly long limit;
    private int pending, stopped, index;
    private long dropped, written;
    private string? error;
    public long Dropped => Interlocked.Read(ref dropped);
    public long Written => Interlocked.Read(ref written);
    public int Pending => Volatile.Read(ref pending);
    public string? Error => Volatile.Read(ref error);
    public string DirectoryPath => directory;

    public CameraTraceWriter(string directory, string header, long fileBytes = FileBytes)
    {
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
        this.header = header ?? throw new ArgumentNullException(nameof(header));
        if (fileBytes < 1024 || fileBytes > FileBytes) throw new ArgumentOutOfRangeException(nameof(fileBytes));
        limit = fileBytes;
        session = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        worker = new Thread(Write) { IsBackground = true, Name = "Broadcaster scenic trace" };
        worker.Start();
    }

    public bool Record(string line)
    {
        if (Volatile.Read(ref stopped) != 0 || Error != null) return false;
        if (line == null || line.Length > MaximumLine || Interlocked.Increment(ref pending) > MaximumQueued)
        {
            if (line != null && line.Length <= MaximumLine) Interlocked.Decrement(ref pending);
            Interlocked.Increment(ref dropped); return false;
        }
        queue.Enqueue(line); wake.Set(); return true;
    }

    private void Write()
    {
        StreamWriter? output = null;
        long bytes = 0, reportedDrops = 0;
        DateTime flushed = DateTime.UtcNow;
        try
        {
            // No files until the first observation; menu-only sessions do not
            // rotate away useful evidence. Cleanup is limited to our filename.
            while (Volatile.Read(ref stopped) == 0 || !queue.IsEmpty)
            {
                if (!queue.TryDequeue(out string? line)) { output?.Flush(); wake.WaitOne(250); continue; }
                Interlocked.Decrement(ref pending);
                if (output == null || bytes + Encoding.UTF8.GetByteCount(line) + 1 > limit)
                {
                    output?.Dispose(); Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, "scenic-" + session + "-" + (++index).ToString("D5") + ".jsonl");
                    output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
                    output.WriteLine(header); bytes = Encoding.UTF8.GetByteCount(header) + 1;
                    string[] files = Directory.GetFiles(directory, "scenic-*.jsonl");
                    Array.Sort(files, StringComparer.Ordinal);
                    for (int i = 0; i < files.Length - MaximumFiles; i++)
                        if (!string.Equals(files[i], path, StringComparison.Ordinal)) File.Delete(files[i]);
                }
                long lost = Dropped;
                if (lost != reportedDrops)
                {
                    string gap = new CameraTraceRecord("lost", 0).Field("count", lost - reportedDrops).ToString();
                    output.WriteLine(gap); bytes += Encoding.UTF8.GetByteCount(gap) + 1; reportedDrops = lost;
                }
                output.WriteLine(line); bytes += Encoding.UTF8.GetByteCount(line) + 1;
                Interlocked.Increment(ref written);
                if ((DateTime.UtcNow - flushed).TotalSeconds >= 1) { output.Flush(); flushed = DateTime.UtcNow; }
            }
        }
        catch (Exception failure) when (failure is IOException || failure is UnauthorizedAccessException || failure is ArgumentException || failure is NotSupportedException)
        { Volatile.Write(ref error, failure.GetType().Name); }
        finally
        {
            try { output?.Dispose(); } catch (IOException) { Volatile.Write(ref error, "IOException"); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        wake.Set();
        // Never stall world exit indefinitely on an unavailable filesystem.
        if (worker.Join(1000)) wake.Dispose();
    }
}
