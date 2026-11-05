using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ValheimBroadcaster.Plugin.Streaming;

// Contains no Unity dependency; the same code is exercised by the FFmpeg loopback test.
internal sealed class EncoderSession
{
    private readonly StreamOptions options;
    private readonly AudioRing audio;
    private readonly VideoMailbox video;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly TcpListener listener;
    private readonly Process process;
    private readonly EncoderDiagnostics diagnostics;
    private readonly Thread videoThread, audioThread;
    private volatile bool stopping, closed;
    private string failure = "", failureTiming = "";
    private long lastVideo, lastAudio, lastProgress;
    private long mediaStarted = -1;
    private int audioChannels;
    private long audioCallbacks, capturedFrames, connectedFrames, consumedFrames, sentFrames, videoWrites;
    private long firstCallback = -1, failureAt = -1, audioWriteSince = -1, videoWriteSince = -1;
    private long lastAudioWrite = -1, lastVideoWrite = -1, queuedAtFailure;
    private volatile bool invalidAudioFormat;
    private volatile bool publishing;
    private volatile bool errorsComplete;
    private volatile bool cleanupBlocked;
    private int processId;
    internal int ProcessId => closed ? 0 : Volatile.Read(ref processId);
    internal bool IsClosed => closed;
    internal bool IsStopping => stopping;
    internal bool IsPublishing => publishing && !stopping;
    internal bool PermanentFailure => invalidAudioFormat || diagnostics.PermanentFailure;
    internal bool CleanupBlocked => cleanupBlocked;
    internal string Status => failure.Length != 0 ? failure + diagnostics.Summary : closed ? "Stopped" : stopping ? "Stopping" : publishing ? "Publishing H.264 + AAC (" + options.VideoEncoder + ")" : "Starting encoder (" + options.VideoEncoder + ")";
    internal string FailureDetails => failure.Length == 0 ? "" : failure + "\nEncoder: " + options.VideoEncoder + "; audio " + options.SampleRate + " Hz / " + Volatile.Read(ref audioChannels) + " channels; input connected " + (Interlocked.Read(ref mediaStarted) >= 0) + "\n" + failureTiming + "\n" + diagnostics.Details;
    internal string TimingDetails
    {
        get
        {
            long failed = Interlocked.Read(ref failureAt);
            long now = failed >= 0 ? failed : clock.ElapsedMilliseconds;
            long began = Interlocked.Read(ref mediaStarted);
            long first = Interlocked.Read(ref firstCallback);
            long frames = Interlocked.Read(ref capturedFrames);
            double rate = first >= 0 && now > first ? frames * 1000.0 / (now - first) : 0;
            long queued = failed >= 0 ? Interlocked.Read(ref queuedAtFailure) : audio.QueuedSamples;
            return "Timing at " + (failed >= 0 ? "failure" : "observation") + ": session " + now + "ms; audio connected " + (began >= 0 ? (now - began) + "ms ago" : "never") +
                "; mux progress " + publishing + "; mode " + options.Width + "x" + options.Height + "@" + options.Fps +
                "\nCapture: " + Interlocked.Read(ref audioCallbacks) + " callbacks; " + frames + " frames; average " + rate.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " frames/s (expected " + options.SampleRate + "); since connect " + Interlocked.Read(ref connectedFrames) +
                "\nAudio: queued " + (queued * 500.0 / options.SampleRate).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "ms; consumed " + Interlocked.Read(ref consumedFrames) + " real frames; sent " + Interlocked.Read(ref sentFrames) + " total frames; write blocked " + WriteAge(audioWriteSince, now) + "ms; last write " + WriteAge(lastAudioWrite, now) + "ms ago" +
                "\nVideo: " + Interlocked.Read(ref videoWrites) + " writes; write blocked " + WriteAge(videoWriteSince, now) + "ms; last write " + WriteAge(lastVideoWrite, now) + "ms ago";
        }
    }
    private static long WriteAge(long since, long now) => since < 0 ? -1 : Math.Max(0, now - since);
    internal int SampleRate => options.SampleRate;

    internal EncoderSession(StreamOptions options)
    {
        options.Validate();
        this.options = options;
        diagnostics = new EncoderDiagnostics(options.Url);
        audio = new AudioRing(options.SampleRate); // 0.5 seconds of stereo samples.
        video = new VideoMailbox(options.Width * options.Height * 4);
        listener = new TcpListener(IPAddress.Loopback, 0);
        process = new Process();
        try
        {
            listener.Start(1);
            process.StartInfo = new ProcessStartInfo
            {
                FileName = options.Executable,
                Arguments = options.Arguments(((IPEndPoint)listener.LocalEndpoint).Port),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true
            };
            process.ErrorDataReceived += (_, e) => { if (e.Data == null) errorsComplete = true; else diagnostics.AddLine(e.Data); };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null && e.Data.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                    long.TryParse(e.Data.Substring(12), out long value) && value > 0)
                { publishing = true; Interlocked.Exchange(ref lastProgress, clock.ElapsedMilliseconds); }
            };
            if (!process.Start()) throw new InvalidOperationException();
            Volatile.Write(ref processId, process.Id);
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            videoThread = Worker(WriteVideo, "Valheim video encoder");
            audioThread = Worker(WriteAudio, "Valheim audio encoder");
            Worker(Supervise, "Valheim encoder supervisor");
        }
        catch
        {
            stopping = true;
            listener.Stop();
            try { process.Kill(); } catch { }
            process.Dispose();
            throw new EncoderStartException("Could not start FFmpeg. Check FFmpegPath and executable permissions.");
        }
    }

    internal byte[] SubmitVideo(byte[] pixels)
    {
        Interlocked.Exchange(ref lastVideo, clock.ElapsedMilliseconds);
        return video.Publish(pixels);
    }

    // Called on Unity's audio thread. Does not touch Unity APIs or the network.
    internal void SubmitAudio(float[] data, int channels)
    {
        if (stopping) return;
        Interlocked.Increment(ref audioCallbacks);
        Interlocked.CompareExchange(ref firstCallback, clock.ElapsedMilliseconds, -1);
        Volatile.Write(ref audioChannels, channels);
        Interlocked.Exchange(ref lastAudio, clock.ElapsedMilliseconds);
        if ((channels != 1 && channels != 2) || data.Length % channels != 0)
        {
            invalidAudioFormat = true;
            Fail("Unsupported game audio callback format: " + channels + " channels; select mono/stereo output and restart streaming.");
            return;
        }
        Interlocked.Add(ref capturedFrames, data.Length / channels);
        // Pre-roll is not part of the output timeline. Never accumulate it while
        // FFmpeg is opening its inputs (which can take seconds on a cold start).
        if (Interlocked.Read(ref mediaStarted) < 0) return;
        Interlocked.Add(ref connectedFrames, data.Length / channels);
        if (!audio.Push(data, channels))
            Fail("Audio queue filled after input connected. See capture rate and writer timing below.");
    }

    internal void Stop() { stopping = true; }
    internal void Fail(string safeReason)
    {
        if (Interlocked.CompareExchange(ref failureAt, clock.ElapsedMilliseconds, -1) < 0)
            Interlocked.Exchange(ref queuedAtFailure, audio.QueuedSamples);
        if (Interlocked.CompareExchange(ref failure, safeReason, "").Length == 0) failureTiming = TimingDetails;
        stopping = true;
    }

    private static Thread Worker(ThreadStart action, string name)
    {
        var thread = new Thread(action) { IsBackground = true, Name = name };
        thread.Start();
        return thread;
    }

    private bool Pace(double dueMilliseconds, string writer)
    {
        while (!stopping)
        {
            double remaining = Interlocked.Read(ref mediaStarted) + dueMilliseconds - clock.Elapsed.TotalMilliseconds;
            if (remaining <= 0)
            {
                if (remaining < -1500) { Fail(writer + " writer fell behind its media schedule by " + (-remaining).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "ms. See capture rate and writer timing below."); return false; }
                return true;
            }
            Thread.Sleep((int)Math.Min(10, Math.Max(1, remaining)));
        }
        return false;
    }

    private void WriteVideo()
    {
        try
        {
            byte[] frame = new byte[options.Width * options.Height * 4];
            Stream pipe = process.StandardInput.BaseStream;
            // One timestamp-zero frame lets FFmpeg open raw video before it opens
            // audio. Both continuing writers then use the audio connection epoch.
            frame = video.Take(frame);
            Interlocked.Exchange(ref videoWriteSince, clock.ElapsedMilliseconds);
            pipe.Write(frame, 0, frame.Length);
            Interlocked.Increment(ref videoWrites);
            Interlocked.Exchange(ref lastVideoWrite, clock.ElapsedMilliseconds);
            Interlocked.Exchange(ref videoWriteSince, -1);
            while (!stopping && Interlocked.Read(ref mediaStarted) < 0) Thread.Sleep(5);
            for (long number = 1; Pace(number * 1000.0 / options.Fps, "Video"); number++)
            {
                frame = video.Take(frame);
                Interlocked.Exchange(ref videoWriteSince, clock.ElapsedMilliseconds);
                pipe.Write(frame, 0, frame.Length);
                Interlocked.Increment(ref videoWrites);
                Interlocked.Exchange(ref lastVideoWrite, clock.ElapsedMilliseconds);
                Interlocked.Exchange(ref videoWriteSince, -1);
            }
        }
        catch { if (!stopping) Fail("Video encoder pipe closed. See RTMP diagnostics in LogOutput.log."); }
    }

    private void WriteAudio()
    {
        try
        {
            using (TcpClient client = listener.AcceptTcpClient())
            {
                client.SendTimeout = 5000;
                Interlocked.Exchange(ref mediaStarted, clock.ElapsedMilliseconds);
                using (NetworkStream pipe = client.GetStream())
                {
                    float[] chunk = new float[options.SampleRate / 50 * 2]; // 20 ms stereo.
                    byte[] bytes = new byte[chunk.Length * sizeof(float)];
                    for (long number = 0; Pace(number * 20.0, "Audio"); number++)
                    {
                        Interlocked.Add(ref consumedFrames, audio.Pop(chunk) / 2); // Underflow remains silence on the shared timeline.
                        Buffer.BlockCopy(chunk, 0, bytes, 0, bytes.Length);
                        Interlocked.Exchange(ref audioWriteSince, clock.ElapsedMilliseconds);
                        pipe.Write(bytes, 0, bytes.Length);
                        Interlocked.Add(ref sentFrames, chunk.Length / 2);
                        Interlocked.Exchange(ref lastAudioWrite, clock.ElapsedMilliseconds);
                        Interlocked.Exchange(ref audioWriteSince, -1);
                    }
                }
            }
        }
        catch { if (!stopping) Fail("Audio encoder connection closed; check receiver and FFmpeg."); }
    }

    private void Supervise()
    {
        try
        {
            while (!stopping)
            {
                long now = clock.ElapsedMilliseconds;
                if (now > 10000 && Interlocked.Read(ref mediaStarted) < 0) Fail("FFmpeg did not open its audio input within ten seconds. Check the executable and encoder startup diagnostics.");
                if (process.HasExited) { Fail("FFmpeg exited. See RTMP diagnostics in LogOutput.log."); break; }
                if (now - Interlocked.Read(ref lastVideo) > 5000) Fail("No video frames for five seconds; restore the game window and restart.");
                if (now - Interlocked.Read(ref lastAudio) > 5000) Fail("No game audio callbacks for five seconds; check the audio device and restart.");
                if (now > 15000 && now - Interlocked.Read(ref lastProgress) > 10000) Fail("Encoder output stalled; check the receiver and restart.");
                Thread.Sleep(100);
            }
        }
        catch { Fail("Encoder monitor failed; restart streaming."); }
        finally
        {
            stopping = true;
            listener.Stop();
            // A pipe failure often races the process exit and stderr callbacks.
            // Allow a short drain on this background thread before forced cleanup.
            try { if (failure.Length != 0 && process.WaitForExit(500)) diagnostics.SetExitCode(process.ExitCode); } catch { }
            // Kill closes blocked pipes without blocking the Unity or audio threads.
            try { if (!process.HasExited) process.Kill(); } catch { }
            bool videoClosed = videoThread.Join(2000);
            bool audioClosed = audioThread.Join(6000);
            bool processClosed = false;
            try { if (process.WaitForExit(1000)) { processClosed = true; diagnostics.SetExitCode(process.ExitCode); } } catch { }
            SpinWait.SpinUntil(() => errorsComplete, 1000);
            process.Dispose();
            if (videoClosed && audioClosed && processClosed) closed = true;
            else
            {
                cleanupBlocked = true;
                // Never let unattended retries overlap a process or writer whose
                // termination could not be confirmed (for example a launcher shim).
                Interlocked.Exchange(ref failure, "Encoder cleanup did not finish. Restart Valheim and configure the actual FFmpeg executable, not a launcher shim.");
            }
        }
    }
}

// Startup errors cannot improve by retrying the same configured executable.
internal sealed class EncoderStartException : InvalidOperationException
{
    internal EncoderStartException(string message) : base(message) { }
}
