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
    private volatile bool publishing;
    private volatile bool errorsComplete;
    internal bool IsClosed => closed;
    internal bool IsStopping => stopping;
    internal string Status => failure.Length != 0 ? failure + diagnostics.Summary : closed ? "Stopped" : stopping ? "Stopping" : publishing ? "Publishing H.264 + AAC (" + options.VideoEncoder + ")" : "Starting encoder (" + options.VideoEncoder + ")";
    internal string FailureDetails => failure.Length == 0 ? "" : failure + "\nEncoder: " + options.VideoEncoder + "\n" + diagnostics.Details;
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
            throw new InvalidOperationException("Could not start FFmpeg. Check FFmpegPath and executable permissions.");
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
        if (!audio.Push(data, channels))
        { Fail("Audio buffer overflow or unsupported channels; use stereo audio and restart."); return; }
        Interlocked.Exchange(ref lastAudio, clock.ElapsedMilliseconds);
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
            double remaining = dueMilliseconds - clock.Elapsed.TotalMilliseconds;
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
            for (long number = 0; Pace(number * 1000.0 / options.Fps); number++)
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
            videoThread.Join(2000);
            audioThread.Join(6000);
            try { if (process.WaitForExit(1000)) diagnostics.SetExitCode(process.ExitCode); } catch { }
            SpinWait.SpinUntil(() => errorsComplete, 1000);
            process.Dispose();
            closed = true;
        }
    }
}
