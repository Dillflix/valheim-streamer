using System;
using System.Globalization;

namespace ValheimBroadcaster.Plugin.Streaming;

internal enum VideoEncoderKind { libx264, h264_nvenc }

internal sealed class StreamOptions
{
    internal string Executable = "ffmpeg";
    internal string Url = "";
    internal int Width = 1280, Height = 720, Fps = 30, BitrateKbps = 4000, SampleRate = 48000;
    internal bool FlipVertical = false;
    internal VideoEncoderKind VideoEncoder = VideoEncoderKind.libx264;
    internal int NvencGpu = -1;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Executable)) throw new ArgumentException("Set Streaming.FFmpegPath to an FFmpeg executable.");
        // This is one quoted argv value, never shell text. Reject quoting/control characters
        // rather than relying on different Mono/Windows argument parsing conventions.
        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != "rtmp" && uri.Scheme != "rtmps") || string.IsNullOrEmpty(uri.Host) ||
            Url.IndexOfAny(new[] { '"', '\\', '\r', '\n', '\t', '\0' }) >= 0 ||
            Array.Exists(Url.ToCharArray(), char.IsControl))
            throw new ArgumentException("Set Streaming.RtmpUrl to a valid rtmp:// or rtmps:// receiver URL without quotes or control characters.");
        if (Width < 320 || Width > 1920 || Height < 180 || Height > 1080 || Width % 2 != 0 || Height % 2 != 0)
            throw new ArgumentException("Stream size must be even, between 320x180 and 1920x1080.");
        if (Fps < 10 || Fps > 60 || BitrateKbps < 250 || BitrateKbps > 20000)
            throw new ArgumentException("Stream FPS must be 10–60 and bitrate 250–20000 kbps.");
        if (SampleRate != 44100 && SampleRate != 48000)
            throw new ArgumentException("Use a 44100 or 48000 Hz game audio device.");
        if (!Enum.IsDefined(typeof(VideoEncoderKind), VideoEncoder))
            throw new ArgumentException("Streaming.VideoEncoder must be libx264 or h264_nvenc.");
        if (NvencGpu < -1 || NvencGpu > 31)
            throw new ArgumentException("Streaming.NvencGpu must be -1 (automatic) or a GPU index from 0 to 31.");
        if (!BitConverter.IsLittleEndian) throw new ArgumentException("Streaming requires a little-endian platform.");
    }

    internal string Arguments(int audioPort)
    {
        Validate();
        return string.Format(CultureInfo.InvariantCulture,
            "-hide_banner -loglevel error -nostdin -nostats " +
            "-thread_queue_size 8 -probesize 32 -analyzeduration 0 -fpsprobesize 0 -f rawvideo -pixel_format rgba -video_size {0}x{1} -framerate {2} -i pipe:0 " +
            "-thread_queue_size 8 -probesize 32 -analyzeduration 0 -f f32le -ar {3} -ac 2 -i tcp://127.0.0.1:{4} " +
            "-map 0:v:0 -map 1:a:0 {5}{10} " +
            "-pix_fmt yuv420p -bf 0 -g {6} -keyint_min {6} -b:v {7}k -maxrate {7}k -bufsize {8}k " +
            "-c:a aac -b:a 128k -ar 48000 -progress pipe:1 -rw_timeout 5000000 " +
            "-f flv -flvflags no_duration_filesize \"{9}\"",
            Width, Height, Fps, SampleRate, audioPort, FlipVertical ? "-vf vflip " : "", Fps * 2, BitrateKbps, BitrateKbps * 2, Url,
            VideoEncoder == VideoEncoderKind.h264_nvenc
                ? "-c:v h264_nvenc -preset p4 -tune ll -rc cbr -gpu " + NvencGpu.ToString(CultureInfo.InvariantCulture)
                : "-c:v libx264 -preset veryfast -tune zerolatency -threads 2");
    }
}
