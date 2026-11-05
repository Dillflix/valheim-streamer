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
    private static void Wait(Func<bool> condition, string label, int timeout = 10000)
    { var time = Stopwatch.StartNew(); bool ready; while (!(ready = condition()) && time.ElapsedMilliseconds < timeout) Thread.Sleep(10); Check(ready, label); }

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "valheim-diagnostic-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Timeline();
            Directory.CreateDirectory(root);
            Encode(root);
            MissingEncoder(root);
            ByteCap(root);
            DiskRetention(root);
            Console.WriteLine(checks + " diagnostic capture checks passed; Unity readback and runtime frame cost NOT RUN.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void Timeline()
    {
        foreach (int renderFps in new[] { 30, 17, 60 })
        {
            var clock = new DiagnosticVideoTimeline();
            double next = 0; int final = 0, samples = 0; bool monotonic = true;
            for (int i = 0; i <= renderFps * 10; i++)
            {
                double at = i / (double)renderFps;
                if (at + 1e-9 < next) continue;
                next += 1d / 12;
                DiagnosticVideoSlot slot = clock.Offer(at, 1);
                if (!slot.Accepted) continue;
                monotonic &= slot.Slot >= final;
                final = slot.Slot; samples++;
            }
            Check(monotonic && final == 120 && samples > 115, "ten seconds remain ten seconds at " + renderFps + " render fps");
        }
        var gaps = new DiagnosticVideoTimeline();
        Check(gaps.Offer(1, 2).NewSegment, "first sample begins segment");
        var shortGap = gaps.Offer(1.25, 2);
        Check(shortGap.Repeats == 2 && shortGap.Slot == 3 && !shortGap.NewSegment, "short missing capture slots repeat explicitly without speeding playback");
        Check(gaps.Offer(1.6, 2).NewSegment, "large gaps start new segment instead of inventing long held footage");
        Check(gaps.Offer(1.7, 3).NewSegment, "hard compositor boundaries never repeat previous world footage");
        Check(!gaps.Offer(1.71, 3).Accepted, "same CFR slot cannot accelerate picture");
        Check(gaps.Offer(.2, 3).NewSegment, "clock reversal resets segment");
    }

    private static void Encode(string root)
    {
        string dir = Path.Combine(root, "video with spaces");
        var writer = new DiagnosticVideoWriter(dir, "ffmpeg", "test");
        for (int n = 0; n < 20; n++)
        {
            byte[]? buffer = null;
            Wait(() => (buffer = writer.Rent()) != null, "bounded capture buffer available");
            for (int i = 0; i < buffer!.Length; i += 4)
            {
                bool top = i / 4 < DiagnosticVideoWriter.Width * DiagnosticVideoWriter.Height / 2;
                buffer[i] = top ? (byte)230 : (byte)10;
                buffer[i + 1] = 20; buffer[i + 2] = top ? (byte)10 : (byte)230; buffer[i + 3] = 255;
            }
            // One skipped sample produces a single explicitly repeated diagnostic frame.
            double at = 20 + (n < 8 ? n : n + 1) / 12d;
            string map = new CameraTraceRecord("video-sample", at).Field("seq", n + 1).Field("sourceTime", at)
                .Field("sourceFrame", n * 3).Field("traceSession", "synthetic-test").Field("presentation", "Live").ToString();
            Check(writer.Submit(buffer, map, at, n < 16 ? 1 : 2), "frame accepted with its capture metadata");
            if (n == 0)
            {
                Wait(() => writer.ProcessId > 0, "diagnostic child identity is available to its owning supervisor");
                using Process child = Process.GetProcessById(writer.ProcessId);
                Check(!child.HasExited && child.Id != Environment.ProcessId, "diagnostic identity names a live separate encoder");
            }
            Thread.Sleep(10);
        }
        writer.Dispose(); Wait(() => writer.Closed, "diagnostic encoder drains and exits");
        Check(writer.ProcessId == 0, "diagnostic encoder identity is cleared after finalization");
        Check(writer.Error == null, "real FFmpeg encoder confirms mapped output: " + writer.Error);
        string[] maps = Directory.GetFiles(dir, "*.jsonl");
        Check(maps.Length == 2, "compositor generation splits video segments");
        int mapped = 0, repeats = 0;
        foreach (string file in maps)
        {
            using JsonDocument end = JsonDocument.Parse(File.ReadLines(file).Last());
            Check(end.RootElement.GetProperty("complete").GetBoolean(), "encoder progress confirms completed segment");
            int rows = 0;
            foreach (string line in File.ReadLines(file))
            {
                using JsonDocument row = JsonDocument.Parse(line);
                if (row.RootElement.GetProperty("kind").GetString() != "video-sample") continue;
                Check(row.RootElement.GetProperty("videoFrame").GetInt32() == rows++, "encoded frame mapping contiguous");
                if (row.RootElement.GetProperty("diagnosticRepeat").GetBoolean()) repeats++;
                mapped++;
            }
            int decoded = int.Parse(Run("ffprobe", "-v error -count_frames -select_streams v:0 -show_entries stream=nb_read_frames -of default=nw=1:nk=1 \"" + Path.ChangeExtension(file, ".mp4") + "\""));
            Check(decoded == rows, "decoded video frame count equals diagnostic mapping");
            Check(new FileInfo(Path.ChangeExtension(file, ".mp4")).Length <= DiagnosticVideoWriter.MaximumSegmentBytes, "video obeys exact byte allowance");
        }
        Check(mapped == 21 && repeats == 1, "one missing capture slot adds one labelled repeat at real-time speed");
        string first = Directory.GetFiles(dir, "*.mp4").OrderBy(p => p, StringComparer.Ordinal).First();
        byte[] rgb = Decode(first);
        Check(rgb[0] > rgb[2] + 120 && rgb[rgb.Length - 1] > rgb[rgb.Length - 3] + 120, "synthetic upright rows survive actual encoder/decoder (not a GPU calibration test)");
    }

    private static string Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        string value = p.StandardOutput.ReadToEnd(), error = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(10000) || p.ExitCode != 0) throw new Exception("Tool failed: " + error);
        return value.Trim();
    }
    private static byte[] Decode(string path)
    {
        using var p = Process.Start(new ProcessStartInfo("ffmpeg", "-v error -i \"" + path + "\" -frames:v 1 -f rawvideo -pix_fmt rgb24 pipe:1")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        using var bytes = new MemoryStream(); p.StandardOutput.BaseStream.CopyTo(bytes); string error = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(10000) || p.ExitCode != 0) throw new Exception(error);
        return bytes.ToArray();
    }
    private static void MissingEncoder(string root)
    {
        var writer = new DiagnosticVideoWriter(Path.Combine(root, "missing"), Path.Combine(root, "missing-exe"), "test");
        byte[] data = writer.Rent()!;
        Check(writer.Submit(data, "{\"kind\":\"video-sample\"}", 0, 1), "missing encoder failure occurs on worker");
        Wait(() => writer.Closed, "missing encoder closes worker");
        Check(writer.Error != null && writer.Pending == 0, "missing encoder failure isolated and queue drained");
        writer.Dispose();
    }
    private static void ByteCap(string root)
    {
        string dir = Path.Combine(root, "hard-cap");
        var writer = new DiagnosticVideoWriter(dir, "ffmpeg", "test");
        var random = new Random(81334);
        for (int i = 0; i < 200 && writer.Error == null; i++)
        {
            byte[]? data = null; var wait = Stopwatch.StartNew();
            while ((data = writer.Rent()) == null && writer.Error == null && wait.ElapsedMilliseconds < 10000) Thread.Sleep(5);
            if (data == null) break;
            random.NextBytes(data);
            writer.Submit(data, new CameraTraceRecord("video-sample", i / 12d).ToString(), i / 12d, 1);
            Thread.Sleep(10);
        }
        writer.Dispose(); Wait(() => writer.Closed, "high-entropy encoder closes within bounded time");
        Check(writer.Error != null, "storage-limited encode cannot report complete footage");
        string video = Directory.GetFiles(dir, "*.mp4").Single();
        Check(new FileInfo(video).Length <= DiagnosticVideoWriter.MaximumSegmentBytes, "binary output cap is exact under high-entropy frames");
        using JsonDocument end = JsonDocument.Parse(File.ReadLines(Path.ChangeExtension(video, ".jsonl")).Last());
        Check(!end.RootElement.GetProperty("complete").GetBoolean(), "truncated encoder exit cannot certify mapped frames");
    }

    private static void DiskRetention(string root)
    {
        string dir = Path.Combine(root, "retention"); Directory.CreateDirectory(dir);
        string unrelated = Path.Combine(dir, "keep-me.txt"); File.WriteAllText(unrelated, "untouched");
        for (int i = 0; i < 11; i++)
        {
            string path = Path.Combine(dir, "footage-20200101-" + i.ToString("D5"));
            using (var file = File.Create(path + ".mp4")) file.SetLength(12 * 1024 * 1024);
            File.WriteAllText(path + ".jsonl", "{}");
        }
        DiagnosticVideoWriter.Prune(dir);
        Check(File.ReadAllText(unrelated) == "untouched", "retention preserves unrelated files");
        Check(Directory.GetFiles(dir, "*.mp4").Length < DiagnosticVideoWriter.MaximumSegments, "retention leaves a slot for current segment");
        Check(Directory.GetFiles(dir, "footage-*").Sum(f => new FileInfo(f).Length) <= DiagnosticVideoWriter.MaximumBytes - DiagnosticVideoWriter.MaximumSegmentBytes - 3 * 1024 * 1024,
            "retention reserves hard byte budget for next pair");
    }
}
