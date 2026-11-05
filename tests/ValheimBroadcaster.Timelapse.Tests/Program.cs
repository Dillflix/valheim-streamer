using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Plugin.Streaming;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label)
    { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    private static void Wait(Func<bool> condition, string label, int milliseconds = 15000)
    { var time = Stopwatch.StartNew(); bool ready; while (!(ready = condition()) && time.ElapsedMilliseconds < milliseconds) Thread.Sleep(10); Check(ready, label); }
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "valheim-timelapse-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            Encode(root); SingleFrame(root); SparseInput(root); MissingEncoder(root); ByteCap(root); PreserveStorage(root);
            Console.WriteLine(checks + " timelapse checks passed; Unity readback, source appearance and runtime frame cost NOT RUN.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
    private static byte[] Pixels(TimelapseVideoWriter writer)
    {
        byte[]? buffer = null;
        Wait(() => (buffer = writer.Rent()) != null || writer.Error != null, "capture buffer available");
        if (buffer == null) throw new Exception(writer.Error);
        for (int i = 0; i < buffer.Length; i += 4)
        {
            bool top = i / 4 < TimelapseVideoWriter.Width * TimelapseVideoWriter.Height / 2;
            buffer[i] = top ? (byte)230 : (byte)10; buffer[i + 1] = 20;
            buffer[i + 2] = top ? (byte)10 : (byte)230; buffer[i + 3] = 255;
        }
        return buffer;
    }
    private static string Metadata(double at, int frame) => new CameraTraceRecord("timelapse-sample", at)
        .Field("sourceFrame", frame).Field("generation", 1).Field("mode", "Scenic").ToString();
    private static void Encode(string root)
    {
        string directory = Path.Combine(root, "clip with spaces");
        var writer = new TimelapseVideoWriter(directory, "ffmpeg", "test", new TimelapseClock(2, 60, 30));
        for (int i = 0; i < 12; i++)
        {
            double at = i < 6 ? i * 2 : 10 + i * 2;
            Check(writer.Submit(Pixels(writer), Metadata(at, i * 120), at), "interval image submitted");
            Wait(() => writer.Written == i + 1 || writer.Error != null, "encoder consumes source photograph");
            if (i == 0)
            {
                Check(writer.ProcessId > 0, "timelapse child identity is available to its owning supervisor");
                using Process child = Process.GetProcessById(writer.ProcessId);
                Check(!child.HasExited && child.Id != Environment.ProcessId, "timelapse identity names a live separate encoder");
            }
        }
        writer.Complete("Test complete"); Wait(() => writer.Closed, "timelapse finalizes asynchronously");
        Check(writer.ProcessId == 0, "timelapse encoder identity is cleared after finalization");
        Check(writer.Error == null, "real encoder confirms clip: " + writer.Error);
        using JsonDocument probe = JsonDocument.Parse(Run("ffprobe", "-v error -count_frames -show_entries stream=codec_type,nb_read_frames,avg_frame_rate -show_entries format=duration -of json \"" + writer.OutputPath + "\""));
        var streams = probe.RootElement.GetProperty("streams");
        Check(streams.GetArrayLength() == 1 && streams[0].GetProperty("codec_type").GetString() == "video", "timelapse clip is intentionally silent");
        Check(streams[0].GetProperty("nb_read_frames").GetString() == "12", "missing source intervals do not invent repeated video frames");
        Check(streams[0].GetProperty("avg_frame_rate").GetString() == "30/1", "explicit output clock stays30fps");
        Check(Math.Abs(double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture) - .4) < .01,
            "source duration accelerates to twelve frames / thirty fps");
        int rows = 0; bool gap = false;
        foreach (string line in File.ReadLines(Path.ChangeExtension(writer.OutputPath, ".jsonl")))
        {
            using JsonDocument record = JsonDocument.Parse(line);
            if (record.RootElement.GetProperty("kind").GetString() != "timelapse-sample") continue;
            Check(record.RootElement.GetProperty("outputFrame").GetInt32() == rows++, "output mapping contiguous");
            gap |= record.RootElement.GetProperty("sourceGapSeconds").GetDouble() > 2.1;
        }
        Check(rows == 12 && gap, "metadata preserves real source gap separately from output time");
        using JsonDocument end = JsonDocument.Parse(File.ReadLines(Path.ChangeExtension(writer.OutputPath, ".jsonl")).Last());
        Check(end.RootElement.GetProperty("complete").GetBoolean(), "finished clip explicitly confirmed");
        byte[] rgb = Decode(writer.OutputPath);
        Check(rgb[0] > rgb[2] + 120 && rgb[rgb.Length - 1] > rgb[rgb.Length - 3] + 120,
            "upright rows survive real encoding and decoding (GPU calibration not exercised)");
    }
    private static void SingleFrame(string root)
    {
        var writer = new TimelapseVideoWriter(Path.Combine(root, "one"), "ffmpeg", "test", new TimelapseClock(2, 60, 30));
        writer.Submit(Pixels(writer), Metadata(1, 1), 1); writer.Complete("Immediate manual stop");
        Wait(() => writer.Closed, "one photograph finalizes without awaiting more input");
        Check(writer.Error == null && writer.Written == 1, "one-frame clip confirmed");
    }
    private static void SparseInput(string root)
    {
        var writer = new TimelapseVideoWriter(Path.Combine(root, "sparse"), "ffmpeg", "test", new TimelapseClock(30, 300, 30));
        writer.Submit(Pixels(writer), Metadata(1, 1), 1);
        Wait(() => writer.Written == 1 || writer.Error != null, "first sparse photograph consumed");
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed.TotalSeconds < 11.5 && writer.Error == null) Thread.Sleep(100);
        Check(writer.Error == null && !writer.Closed, "intentional sparse interval exceeds I/O watchdog without false stall");
        writer.Submit(Pixels(writer), Metadata(31, 1801), 31); writer.Complete("Sparse test complete");
        Wait(() => writer.Closed, "sparse input finalizes"); Check(writer.Error == null && writer.Written == 2, "sparse clip retains both photographs");
    }
    private static void MissingEncoder(string root)
    {
        var writer = new TimelapseVideoWriter(Path.Combine(root, "missing"), Path.Combine(root, "missing-exe"), "test", new TimelapseClock(2, 60, 30));
        writer.Submit(Pixels(writer), Metadata(0, 0), 0);
        Wait(() => writer.Closed, "missing encoder exits off-thread");
        Check(writer.Error != null && writer.Pending == 0, "encoder failure preserves failure state and drains queue"); writer.Dispose();
    }
    private static void ByteCap(string root)
    {
        var writer = new TimelapseVideoWriter(Path.Combine(root, "cap"), "ffmpeg", "test", new TimelapseClock(2, 60, 30), 4096);
        var random = new Random(81337);
        for (int i = 0; i < 5 && writer.Error == null; i++)
        {
            byte[]? pixels = writer.Rent(); if (pixels == null) break;
            random.NextBytes(pixels); writer.Submit(pixels, Metadata(i, i), i); Thread.Sleep(50);
        }
        writer.Complete("Limit test"); Wait(() => writer.Closed, "storage-limited encoder exits");
        Check(writer.Error != null, "storage-limited clip cannot falsely claim completion");
        Check(File.Exists(writer.OutputPath) && new FileInfo(writer.OutputPath).Length <= 4096, "video byte allowance exact");
    }
    private static void PreserveStorage(string root)
    {
        string directory = Path.Combine(root, "full"); Directory.CreateDirectory(directory);
        string existing = Path.Combine(directory, "keep.mp4");
        using (var file = File.Create(existing)) file.SetLength(TimelapseVideoWriter.MaximumDirectoryBytes);
        bool denied = false;
        try { TimelapseVideoWriter.CheckStorage(directory, TimelapseVideoWriter.MaximumVideoBytes); }
        catch (IOException) { denied = true; }
        Check(denied && File.Exists(existing) && new FileInfo(existing).Length == TimelapseVideoWriter.MaximumDirectoryBytes,
            "quota refuses new capture without deleting existing recordings");
    }
    private static string Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        string result = p.StandardOutput.ReadToEnd(), error = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(10000) || p.ExitCode != 0) throw new Exception(error); return result;
    }
    private static byte[] Decode(string path)
    {
        using var p = Process.Start(new ProcessStartInfo("ffmpeg", "-v error -i \"" + path + "\" -frames:v 1 -f rawvideo -pix_fmt rgb24 pipe:1")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        using var data = new MemoryStream(); p.StandardOutput.BaseStream.CopyTo(data); string error = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(10000) || p.ExitCode != 0) throw new Exception(error); return data.ToArray();
    }
}
