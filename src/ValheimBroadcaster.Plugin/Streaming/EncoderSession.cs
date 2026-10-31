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
    private string failure = "";
    private long lastVideo, lastAudio, lastProgress;
    private long mediaStarted = -1;
    private int audioChannels;
    private volatile bool invalidAudioFormat;
    private volatile bool publishing;
    private volatile bool errorsComplete;
    private volatile bool cleanupBlocked;
    internal bool IsClosed => closed;
    internal bool IsStopping => stopping;
    internal bool IsPublishing => publishing && !stopping;
    internal bool PermanentFailure => invalidAudioFormat || diagnostics.PermanentFailure;
    internal bool CleanupBlocked => cleanupBlocked;
    internal string Status => failure.Length != 0 ? failure + diagnostics.Summary : closed ? "Stopped" : stopping ? "Stopping" : publishing ? "Publishing H.264 + AAC (" + options.VideoEncoder + ")" : "Starting encoder (" + options.VideoEncoder + ")";
    internal string FailureDetails => failure.Length == 0 ? "" : failure + "\nEncoder: " + options.VideoEncoder + "; audio " + options.SampleRate + " Hz / " + Volatile.Read(ref audioChannels) + " channels; input connected " + (Interlocked.Read(ref mediaStarted) >= 0) + "\n" + diagnostics.Details;
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
        Volatile.Write(ref audioChannels, channels);
        Interlocked.Exchange(ref lastAudio, clock.ElapsedMilliseconds);
        if ((channels != 1 && channels != 2) || data.Length % channels != 0)
        {
            invalidAudioFormat = true;
            Fail("Unsupported game audio callback format: " + channels + " channels; select mono/stereo output and restart streaming.");
            return;
        }
        // Pre-roll is not part of the output timeline. Never accumulate it while
        // FFmpeg is opening its inputs (which can take seconds on a cold start).
        if (Interlocked.Read(ref mediaStarted) < 0) return;
        if (!audio.Push(data, channels))
            Fail("Audio buffer overflow after input connected; encoder consumption fell behind game audio. Check encoder/receiver load.");
    }

    internal void Stop() { stopping = true; }
    internal void Fail(string safeReason)
    {
        Interlocked.CompareExchange(ref failure, safeReason, "");
        stopping = true;
    }

    private static Thread Worker(ThreadStart action, string name)
    {
        var thread = new Thread(action) { IsBackground = true, Name = name };
        thread.Start();
        return thread;
    }

    private bool Pace(double dueMilliseconds)
    {
        while (!stopping)
        {
            double remaining = Interlocked.Read(ref mediaStarted) + dueMilliseconds - clock.Elapsed.TotalMilliseconds;
            if (remaining <= 0)
            {
                if (remaining < -1500) { Fail("Encoder fell behind real time; reduce resolution/FPS or check receiver."); return false; }
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
            pipe.Write(frame, 0, frame.Length);
            while (!stopping && Interlocked.Read(ref mediaStarted) < 0) Thread.Sleep(5);
            for (long number = 1; Pace(number * 1000.0 / options.Fps); number++)
            {
                frame = video.Take(frame);
                pipe.Write(frame, 0, frame.Length);
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
                    for (long number = 0; Pace(number * 20.0); number++)
                    {
                        audio.Pop(chunk); // Short gaps use silence, keeping the nominal A/V clocks aligned.
                        Buffer.BlockCopy(chunk, 0, bytes, 0, bytes.Length);
                        pipe.Write(bytes, 0, bytes.Length);
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
