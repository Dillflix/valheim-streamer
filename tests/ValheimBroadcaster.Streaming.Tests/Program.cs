using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using ValheimBroadcaster.Plugin.Streaming;

internal static class Program
{
    private static int checks;
    private static void Check(bool pass, string name)
    {
        if (!pass) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    private static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, name);
    }
    private static int Main(string[] args)
    {
        using var deadline = new Timer(_ =>
        {
            Console.Error.WriteLine("Streaming harness exceeded its two-minute deadline.");
            Environment.Exit(1);
        }, null, 120000, Timeout.Infinite);
        try
        {
            foreach (string url in new[] { "", "https://localhost/live/key", "file:///tmp/video", "rtmp://localhost/a\" -f x", "rtmp://localhost/a\nkey", "rtmp://localhost/a\\key" })
                Reject(() => new StreamOptions { Url = url }.Validate(), "reject unsafe/unsupported URL");
            new StreamOptions { Url = "rtmps://localhost/live/key?token=abc" }.Validate();
            Reject(() => new StreamOptions { Url = "rtmp://localhost/live/test", Width = 321 }.Validate(), "reject odd width");
            Reject(() => new StreamOptions { Url = "rtmp://localhost/live/test", SampleRate = 96000 }.Validate(), "reject unsupported audio rate");
            CheckStreamPanel();
            EncoderSettingsAndDiagnostics();
            RecoveryChecks.Run(Check);
            var ring = new AudioRing(8);
            Check(ring.Push(new float[] { 1, 2, 3 }, 1), "mono is accepted");
            Check(!ring.Push(new float[] { 4, 5 }, 1), "overflow is rejected without overwriting unread audio");
            float[] chunk = new float[8];
            Check(ring.Pop(chunk) == 6 && chunk[0] == 1 && chunk[1] == 1 && chunk[4] == 3 && chunk[5] == 3 && chunk[6] == 0, "mono upmix and underflow silence");
            Check(ring.Push(new float[] { 4, 5, 6, 7 }, 2) && ring.Pop(chunk) == 4 && chunk[0] == 4 && chunk[3] == 7, "stereo wrap preserves channel order");
            Check(!ring.Push(new float[6], 6), "surround rejected");
            var mailbox = new VideoMailbox(4);
            byte[] first = { 1, 1, 1, 1 }, second = { 2, 2, 2, 2 }, previous = new byte[4];
            mailbox.Publish(first); mailbox.Publish(second);
            previous = mailbox.Take(previous);
            Check(ReferenceEquals(previous, second) && ReferenceEquals(mailbox.Take(previous), previous), "newest video wins and repeats without a queue");
            bool missing = false;
            try { new EncoderSession(new StreamOptions { Url = "rtmp://127.0.0.1/live/test", Executable = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) }); }
            catch (InvalidOperationException e) { missing = !e.Message.Contains("rtmp"); }
            Check(missing, "missing encoder gives a redacted error");
            Loopback(VideoEncoderKind.libx264);
            Loopback(VideoEncoderKind.libx264, overload: true);
            if (!OperatingSystem.IsWindows())
            {
                DelayedWriterCleanup();
                string delayed = Path.Combine(Path.GetTempPath(), "valheim-delayed-ffmpeg-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(delayed, "#!/bin/sh\nsleep 1\nexec ffmpeg \"$@\"\n");
                File.SetUnixFileMode(delayed, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                try { Loopback(VideoEncoderKind.libx264, delayed); }
                finally { File.Delete(delayed); }
            }
            if (Array.IndexOf(args, "--nvenc") >= 0) Loopback(VideoEncoderKind.h264_nvenc);
            else Console.WriteLine("NOT RUN: NVENC hardware loopback (use --nvenc on an NVIDIA-equipped host).");
            Console.WriteLine($"{checks} streaming checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void CheckStreamPanel()
    {
        var panel = new StreamStatusPanel();
        byte[] Render(string text, int width, int height, byte background, bool flip = false, int size = 14)
        {
            var pixels = new byte[width * height * 4];
            Array.Fill(pixels, background);
            panel.Prepare(text, width, height, size);
            panel.Composite(pixels, width, height, flip);
            return pixels;
        }

        byte[] normal = Render("Following Braggi", 1280, 720, 80);
        byte[] flipped = Render("Following Braggi", 1280, 720, 80, true);
        for (int y = 0; y < 720; y++)
            if (!normal.AsSpan(y * 1280 * 4, 1280 * 4).SequenceEqual(
                    flipped.AsSpan((719 - y) * 1280 * 4, 1280 * 4)))
                throw new Exception("encoder vflip changed panel appearance");
        Check(true, "both encoder orientations produce the same inset panel");
        int corner = (8 * 1280 + 8) * 4;
        Check(normal[0] == 80 && normal[corner] == 40 && normal[corner + 3] == 80 &&
            Render("Following Braggi", 1280, 720, 160)[corner] == 80,
            "inset and translucent padding preserve scene variation and source alpha");

        const int w = 320, h = 180;
        byte[] narrow = Render("iiii", w, h, 40), wide = Render("WWWW", w, h, 40);
        int Changed(byte[] a)
        {
            int changed = 0;
            for (int i = 0; i < a.Length; i += 4) if (a[i] != 40) changed++;
            return changed;
        }
        Check(Changed(narrow) < Changed(wide) && !wide.AsSpan().SequenceEqual(Render("wwww", w, h, 40)),
            "proportional advances and distinct lowercase survive compositing");
        Check(Array.Exists(wide, pixel => pixel > 70 && pixel < 190), "glyph edges retain grayscale coverage");
        byte[] overflow = Render(new string('W', 4500) + new string('\n', 100), w, h, 40);
        Check(overflow[(75 * w + 2) * 4] == 40 && overflow[(2 * w + w - 1) * 4] == 40,
            "long labels stay within the picture budget on the smallest stream");
        Check(Changed(Render("i", w, h, 40)) < Changed(narrow), "shorter text removes the old panel");
        Check(Changed(Render("Ruin — Bräggi", 1920, 1080, 40, size: 20)) >
            Changed(Render("Ruin — Bräggi", 1920, 1080, 40)), "configured size changes the render footprint");
    }

    private static Process Start(string executable, string arguments)
    {
        var p = new Process { StartInfo = new ProcessStartInfo(executable, arguments) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true } };
        p.Start();
        return p;
    }

    private static void EncoderSettingsAndDiagnostics()
    {
        var options = new StreamOptions { Url = "rtmp://localhost/live/test" };
        string software = options.Arguments(12345);
        Check(software.Contains("-c:v libx264 -preset veryfast -tune zerolatency") && !software.Contains("-gpu"), "existing software encoding remains the default");
        options.VideoEncoder = VideoEncoderKind.h264_nvenc;
        options.NvencGpu = 1;
        options.FlipVertical = true;
        string hardware = options.Arguments(12345);
        Check(hardware.Contains("-c:v h264_nvenc -preset p4 -tune ll -rc cbr -gpu 1") && !hardware.Contains("libx264") && !hardware.Contains("zerolatency"), "NVENC uses its own low-latency settings and explicit device");
        Check(hardware.Contains("-c:a aac") && !hardware.Contains("-vf vflip") && hardware.Contains("-pix_fmt yuv420p -bf 0 -g 60"), "automatic orientation cannot be double-flipped by a legacy setting");
        options.AutomaticOrientation = false;
        Check(options.Arguments(12345).Contains("-vf vflip"), "explicit manual orientation retains the legacy FFmpeg flip");
        options.FlipVertical = false;
        Check(!options.Arguments(12345).Contains("-vf vflip"), "manual unflipped output remains available");
        options.VideoEncoder = (VideoEncoderKind)100;
        Reject(options.Validate, "unknown encoder is rejected");
        options.VideoEncoder = VideoEncoderKind.h264_nvenc;
        options.NvencGpu = -2;
        Reject(options.Validate, "device-list sentinel cannot be used as a publishing device");

        const string url = "rtmps://username:password@example.invalid/private-app/secret%2Bkey?token=auth%2Fvalue&other=long+value";
        var diagnostics = new EncoderDiagnostics(url);
        string safe = diagnostics.Sanitize("Error opening output " + url + " : Connection refused");
        Check(safe.Contains("Connection refused") && !safe.Contains("secret") && !safe.Contains("example.invalid"), "full ingest URL is removed while error survives");
        safe = diagnostics.Sanitize("Server rejected app private-app playpath secret+key token auth/value username password long value");
        Check(!safe.Contains("private-app") && !safe.Contains("secret+key") && !safe.Contains("auth/value") && !safe.Contains("password") && !safe.Contains("long value"), "separately printed decoded credentials, app, playpath and query values are removed");
        safe = diagnostics.Sanitize("Rejected secret%2bkey auth%2fvalue");
        Check(!safe.Contains("secret") && !safe.Contains("auth%"), "encoded credentials are redacted case-insensitively");
        safe = diagnostics.Sanitize("Error tcp://10.20.30.40:1935?timeout=5000000: Connection refused");
        Check(!safe.Contains("10.20.30.40") && safe.Contains("Connection refused"), "alternate protocol diagnostics redact network destinations");
        Check(diagnostics.Sanitize(new string('x', 17000)).Contains("omitted"), "oversized diagnostics are discarded without retaining fragments");
        for (int n = 0; n < 20; n++) diagnostics.AddLine("failure-" + n + new string('x', 600));
        diagnostics.SetExitCode(7);
        Check(diagnostics.Details.Length < 4300 && !diagnostics.Details.Contains("failure-0") && diagnostics.Details.Contains("failure-19"), "diagnostic history is bounded and retains recent errors");
        Check(diagnostics.Summary.Contains("Exit 7") && diagnostics.Summary.Length < 210, "status preserves exit code with bounded error detail");
        var capability = new EncoderDiagnostics(url);
        capability.AddLine("[h264_nvenc] Cannot load nvcuda.dll");
        Check(capability.PermanentFailure, "missing NVIDIA capability blocks futile automatic retries");
        var unavailable = new EncoderDiagnostics(url);
        unavailable.AddLine("Connection refused");
        Check(!unavailable.PermanentFailure, "receiver outage remains retryable");
        var denied = new EncoderDiagnostics(url);
        denied.AddLine("Server error: 403 Forbidden");
        Check(denied.PermanentFailure, "known authorization rejection requires operator correction");
    }

    // Recreate a process that exits while an inherited pipe is still open.
    // Its real OS pipe holds the video writer until we explicitly release it.
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void DelayedWriterCleanup()
    {
        string root = Path.Combine(Path.GetTempPath(), "encoder-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string launcher = Path.Combine(root, "encoder"), release = Path.Combine(root, "release");
        EncoderSession? encoder = null;
        try
        {
            File.WriteAllText(launcher, "#!/bin/sh\ncd \"$(dirname \"$0\")\" || exit 2\nexec 3<&0\n" +
                "(i=0; while [ ! -f release ] && [ $i -lt 200 ]; do sleep 0.1; i=$((i+1)); done) <&3 &\nexit 7\n");
            File.SetUnixFileMode(launcher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            encoder = new EncoderSession(new StreamOptions { Executable = launcher, Url = "rtmp://127.0.0.1/live/test" });
            Check(SpinWait.SpinUntil(() => encoder.CleanupStatus.Contains("video writer stopped=False"), 12000),
                "late cleanup identifies the blocked video writer");
            Check(!encoder.IsClosed && encoder.ProcessId == 0 && encoder.FailureDetails.StartsWith("FFmpeg exited."),
                "late cleanup retains the original failure and prevents overlap after process exit");
            File.WriteAllText(release, "");
            Check(SpinWait.SpinUntil(() => encoder.IsClosed, 10000) && !encoder.PermanentFailure &&
                encoder.FailureDetails.Contains("FFmpeg exit: 7"), "late writer completion releases the session for retry");
        }
        finally
        {
            File.WriteAllText(release, "");
            encoder?.Stop();
            Directory.Delete(root, true);
        }
    }

    private static void Loopback(VideoEncoderKind videoEncoder, string executable = "ffmpeg", bool overload = false)
    {
        var reserve = new TcpListener(IPAddress.Loopback, 0);
        reserve.Start(); int port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        string root = Path.Combine(Path.GetTempPath(), "valheim-stream-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string recording = Path.Combine(root, "received.flv");
        string url = $"rtmp://127.0.0.1:{port}/live/test";
        using Process receiver = Start("ffmpeg", $"-hide_banner -loglevel error -listen 1 -i {url} -c copy -y \"{recording}\"");
        var receiverErrors = receiver.StandardError.ReadToEndAsync();
        var receiverOutput = receiver.StandardOutput.ReadToEndAsync();
        EncoderSession? encoder = null;
        try
        {
            Thread.Sleep(500); // FFmpeg must bind before the publisher connects.
            var options = new StreamOptions { Url = url, Width = 320, Height = 180, Fps = 20, BitrateKbps = 500, VideoEncoder = videoEncoder, Executable = executable,
                AutomaticOrientation = true, FlipVertical = true }; // A persisted screenshot-era workaround must not invert calibrated rows.
            encoder = new EncoderSession(options);
            byte[] frame = new byte[320 * 180 * 4];
            float[] audio = new float[1920];
            var clock = Stopwatch.StartNew();
            // A simultaneous white flash and tone at t=2s test mux synchronization.
            for (int tick = 0; tick < 250; tick++)
            {
                while (clock.Elapsed.TotalMilliseconds < tick * 20) Thread.Sleep(1);
                for (int i = 0; i < frame.Length; i += 4)
                {
                    frame[i] = frame[i + 1] = frame[i + 2] = tick >= 100 ? (byte)255 : (byte)0;
                    frame[i + 3] = 255;
                }
                // Asymmetric markers verify the encoder's actual top-row-first
                // interpretation after the GPU stage supplies normalized RGBA.
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                    {
                        int top = (y * 320 + x) * 4, bottom = ((179 - y) * 320 + x) * 4;
                        frame[top] = 255; frame[top + 1] = frame[top + 2] = 0;
                        frame[bottom] = frame[bottom + 1] = 0; frame[bottom + 2] = 255;
                    }
                frame = encoder.SubmitVideo(frame);
                for (int i = 0; i < 960; i++)
                {
                    float value = tick >= 100 ? (float)(0.2 * Math.Sin(2 * Math.PI * 440 * (tick * 960 + i) / 48000)) : 0;
                    audio[i * 2] = value;
                    audio[i * 2 + 1] = -value; // Preserve distinct left/right channels.
                }
                encoder.SubmitAudio(audio, 2);
                if (overload) encoder.SubmitAudio(audio, 2);
                if (encoder.IsStopping)
                {
                    if (!overload) throw new Exception("Publisher stopped early: " + encoder.Status);
                    Check(SpinWait.SpinUntil(() => encoder.IsClosed, 10000), "overproduction closes encoder workers");
                    string details = encoder.FailureDetails;
                    var rate = System.Text.RegularExpressions.Regex.Match(details, @"average (\d+) frames/s");
                    Check(details.Contains("Audio queue filled") && rate.Success && int.Parse(rate.Groups[1].Value) > 80000,
                        "failure telemetry identifies doubled callback sample rate");
                    Check(details.Contains("expected 48000") && details.Contains("queued ") && details.Contains("write blocked ") && details.Contains("Timing at failure"),
                        "failure telemetry preserves queue and writer timing after cleanup");
                    return;
                }
            }
            if (overload) throw new Exception("Unbounded overproduction did not fail");
            Check(encoder.Status == "Publishing H.264 + AAC (" + videoEncoder + ")", "encoder reports actual mux progress and selected encoder");
            encoder.Stop(); encoder.Stop();
            Check(SpinWait.SpinUntil(() => encoder.IsClosed, 10000), "idempotent stop closes encoder and workers");
            Check(receiver.WaitForExit(10000), "RTMP receiver observes stream close");
            using Process probe = Start("ffprobe", $"-v error -show_streams -of json \"{recording}\"");
            string json = probe.StandardOutput.ReadToEnd();
            probe.WaitForExit();
            using JsonDocument metadata = JsonDocument.Parse(json);
            bool video = false, sound = false;
            double videoOrigin = 0, audioOrigin = 0;
            foreach (JsonElement stream in metadata.RootElement.GetProperty("streams").EnumerateArray())
            {
                if (stream.GetProperty("codec_type").GetString() == "video")
                {
                    videoOrigin = double.Parse(stream.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture);
                    video = stream.GetProperty("codec_name").GetString() == "h264" && stream.GetProperty("width").GetInt32() == 320 && stream.GetProperty("height").GetInt32() == 180;
                }
                if (stream.GetProperty("codec_type").GetString() == "audio")
                {
                    audioOrigin = double.Parse(stream.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture);
                    sound = stream.GetProperty("codec_name").GetString() == "aac" && stream.GetProperty("sample_rate").GetString() == "48000" && stream.GetProperty("channels").GetInt32() == 2;
                }
            }
            Check(video && sound, "remote RTMP receiver records H.264 plus 48kHz stereo AAC");
            byte[] oriented = Decode(recording, "-ss 3 -frames:v 1 -vf scale=32:18 -pix_fmt rgb24 -f rawvideo");
            int topMarker = (1 * 32 + 1) * 3, bottomMarker = (16 * 32 + 1) * 3;
            Check(oriented.Length == 32 * 18 * 3 && oriented[topMarker] > 150 && oriented[topMarker + 2] < 80 &&
                oriented[bottomMarker + 2] > 150 && oriented[bottomMarker] < 80,
                "decoded RTMP keeps top/bottom markers upright despite a legacy FlipVertical setting");
            byte[] rawVideo = Decode(recording, "-map 0:v:0 -vf scale=1:1 -pix_fmt gray -f rawvideo");
            byte[] rawAudio = Decode(recording, "-map 0:a:0 -f f32le -ac 2 -ar 48000");
            int videoStart = Array.FindIndex(rawVideo, x => x > 128);
            int audioStart = -1;
            double energy = 0, channelDifference = 0;
            for (int i = 0; i + 7 < rawAudio.Length; i += 8)
            {
                float left = BitConverter.ToSingle(rawAudio, i), right = BitConverter.ToSingle(rawAudio, i + 4);
                if (audioStart < 0 && Math.Abs(left) > 0.04) audioStart = i / 8;
                energy += left * left;
                channelDifference += Math.Abs(left - right);
            }
            Check(videoStart >= 0 && rawVideo.Length > 70, "decoded video contains the test flash and sustained footage");
            Check(audioStart >= 0 && energy > 100 && channelDifference > 100, "decoded AAC contains audible stereo tone");
            double skew = Math.Abs(videoOrigin + videoStart / 20.0 - audioOrigin - audioStart / 48000.0);
            Check(skew < 0.25, $"flash/tone synchronization within 250 ms ({skew:F3}s)");
            Console.WriteLine("Loopback recording: " + recording);
            // A second session must fail promptly when a receiver is absent.
            encoder = new EncoderSession(options);
            Check(SpinWait.SpinUntil(() => encoder.IsClosed, 10000), "unavailable receiver terminates encoder without blocking caller");
            Check(!encoder.Status.Contains(url), "failure status does not disclose destination/key");
            Check(encoder.FailureDetails.Contains("FFmpeg exit:") && !encoder.FailureDetails.Contains("No FFmpeg error output"), "early pipe/process failure retains real FFmpeg stderr and exit code");
            Check(!encoder.FailureDetails.Contains(url) && !encoder.FailureDetails.Contains("127.0.0.1"), "persisted failure diagnostics redact destination and key");
            Check(!encoder.PermanentFailure, "actual refused receiver is classified as retryable");

            // Reconnect the production session after a real receiver outage. The
            // policy's virtual clock avoids spending five seconds waiting in CI.
            var retry = new OutputRecoveryPolicy();
            retry.RequestStart(0);
            Check(retry.TryBegin(0, true, encoder.IsClosed), "recovery starts only after prior encoder cleanup");
            retry.Failed(0, encoder.PermanentFailure, true, encoder.FailureDetails);
            Check(!retry.TryBegin(4, true, true), "real receiver failure observes retry backoff");
            string recovered = Path.Combine(root, "recovered.flv");
            using Process replacement = Start("ffmpeg", $"-hide_banner -loglevel error -listen 1 -i {url} -c copy -y \"{recovered}\"");
            var replacementErrors = replacement.StandardError.ReadToEndAsync();
            var replacementOutput = replacement.StandardOutput.ReadToEndAsync();
            try
            {
                Thread.Sleep(500);
                Check(retry.TryBegin(5, true, encoder.IsClosed), "retry becomes eligible after receiver recovery");
                encoder = new EncoderSession(options);
                clock.Restart();
                for (int tick = 0; tick < 150; tick++)
                {
                    while (clock.Elapsed.TotalMilliseconds < tick * 20) Thread.Sleep(1);
                    frame = encoder.SubmitVideo(frame);
                    encoder.SubmitAudio(audio, 2);
                    if (encoder.IsStopping) throw new Exception("Recovered publisher stopped early: " + encoder.Status);
                }
                Check(encoder.IsPublishing, "production encoder resumes muxing after receiver returns");
                encoder.Stop();
                Check(SpinWait.SpinUntil(() => encoder.IsClosed, 10000) && replacement.WaitForExit(10000), "recovered session closes without orphan workers");
                Check(File.Exists(recovered) && new FileInfo(recovered).Length > 1000, "recovered receiver records a new stream");
            }
            finally
            {
                encoder?.Stop();
                if (!replacement.HasExited) replacement.Kill(entireProcessTree: true);
                replacement.WaitForExit(5000);
                Console.WriteLine("Recovery receiver diagnostics: " + replacementErrors.GetAwaiter().GetResult());
            }
        }
        finally
        {
            encoder?.Stop();
            if (!receiver.HasExited) receiver.Kill(entireProcessTree: true);
            receiver.WaitForExit(5000);
            Console.WriteLine("Receiver diagnostics: " + receiverErrors.GetAwaiter().GetResult());
        }
    }

    private static byte[] Decode(string input, string outputArgs)
    {
        using Process p = Start("ffmpeg", $"-v error -i \"{input}\" {outputArgs} pipe:1");
        var errors = p.StandardError.ReadToEndAsync();
        using var bytes = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(bytes);
        p.WaitForExit();
        Check(p.ExitCode == 0, "decode received media: " + errors.GetAwaiter().GetResult());
        return bytes.ToArray();
    }
}
