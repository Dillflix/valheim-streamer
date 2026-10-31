# Direct RTMP with game audio

Version 0.3.0 adds H.264 video and stereo AAC game audio from the broadcaster client. The plugin manages capture and an FFmpeg child process. OBS is not used. Other players and the Valheim server do not need this mod.

The audio source is the game's existing AudioListener mix: game sounds/music and any other mod audio routed through that listener. The listener follows the broadcast camera while publishing. No microphone, Discord, or desktop audio capture is opened. Local playback is not muted by our filter. Actual volume/mixer behavior and spatial alignment must be checked in Valheim.

## Thunderstore setup

1. Close Valheim. In Thunderstore, select the profile you launch and use **Settings → Browse profile folder** (wording may vary by manager version).
2. Install both DLLs from the same 0.3.0 test build into `BepInEx/plugins/ValheimBroadcaster/`. Remove duplicate older copies of this plugin elsewhere in that profile. Keep BepInEx and Jötunn installed.
3. Install a trusted FFmpeg build with `libx264`, `aac`, TCP, RTMP, and optionally RTMPS support. The [FFmpeg download page](https://ffmpeg.org/download.html) links Windows builds. The mod does not download or bundle FFmpeg. Run `ffmpeg -encoders` to check those encoders.
4. Launch modded once to generate the new config entries, then close Valheim. Edit `BepInEx/config/com.dillflix.valheimbroadcaster.cfg` in this same profile:

```ini
[Streaming]
FFmpegPath = C:\Tools\ffmpeg\bin\ffmpeg.exe
RtmpUrl = rtmp://YOUR_RECEIVER:1935/live/YOUR_STREAM_KEY
Width = 1280
Height = 720
FPS = 30
VideoBitrateKbps = 4000
FlipVertical = false
```

Use the complete ingest URL supplied by your receiver. RTMPS is accepted if your FFmpeg build supports it. The mod is the publisher, not the remote RTMP server. The URL stays empty by default and streaming is always manually started. Do not share this config with a real stream key; FFmpeg's local process arguments also contain that key.

5. Select stereo output on a working 44.1 or 48 kHz audio device. A VM needs a functioning virtual audio device and GPU acceleration. The output feed is always 48 kHz stereo AAC at 128 kbps. Mono input is duplicated to both channels; surround output is rejected.
6. Start your RTMP receiver, then use Thunderstore's **Start modded**. Join a disposable world alone. Press **F10** to follow your own character, then **F11** to start video **and audio**. The overlay should progress from `Starting encoder` to `Publishing H.264 + AAC`.
7. View/record the feed on the receiver. Walk, jump, swing a tool, and stand near a fire/water source. Verify audible sound, left/right positioning, motion/sound alignment, image orientation, and local playback. If upside down, stop, set `FlipVertical = true`, and restart the game. Match output/game aspect ratios to avoid stretching.
8. **F11** stops publishing while preserving the follow camera. **F8** stops both. Switching camera modes with F10 or `broadcast on/self`, dying, leaving the world, or disabling the plugin also stops output. After switching modes, press F11 again. Wait for cleanup if status says `Stopping`.

Console equivalents (requires Valheim `-console`): `broadcast self`, `broadcast stream on`, `broadcast stream status`, `broadcast stream off`. No console flag is needed for hotkeys.

## A small receiver test

On the receiver machine, an FFmpeg build with RTMP listen support can record a single publishing connection:

```sh
ffmpeg -listen 1 -i rtmp://0.0.0.0:1935/live/test -c copy received.flv
```

Set the client's URL to `rtmp://RECEIVER_IP:1935/live/test`, and allow TCP 1935 only from the broadcaster for this test. The command is a one-connection test listener, not an authenticated production media service. Stop/restart the receiver for another connection. Inspect the recording:

```sh
ffprobe -v error -show_streams received.flv
ffplay received.flv
```

Expect H.264 video and AAC audio, two channels, 48000 Hz. Stopping the mod terminates FFmpeg to release blocked pipes, so the receiver may report end-of-stream/I/O errors and the final packet can be truncated. Most live receivers handle this as a publisher disconnect. For a durable streaming service or browser HLS/DASH playback, use a remote media server that accepts RTMP and packages those outputs; no embedded HTTP server is included here.

## Performance and troubleshooting

- Defaults are 720p30, 4 Mbps H.264 + 128 kbps AAC. Software `libx264` uses CPU. This version has no hardware encoder selector. Lower resolution/FPS if the encoder falls behind; capture/readback also costs GPU and memory bandwidth.
- Capture includes the actual game framebuffer: chat, menus, console, overlays, and the waiting slate can be visible. It is not a desktop capture. Self-follow always displays its test label.
- Keep the game rendering and its audio device active. Minimizing, suspending, or loss of focus can stop rendering/audio depending on game/OS settings. Five seconds without capture callbacks stops output rather than broadcasting a frozen frame indefinitely. Return to the game and restart explicitly.
- `Could not start FFmpeg`: check the absolute executable path and permissions. `FFmpeg exited`/pipe errors: check the ingest URL, receiver availability, and encoder support. The plugin deliberately hides raw FFmpeg stderr to keep keys out of logs. Test FFmpeg separately with a disposable key if deeper diagnostics are needed.
- `Audio buffer overflow`: check stereo layout and encoder CPU capacity. Audio device changes stop the stream; restart after the device stabilizes. Audio callbacks are required even during silence; a valid silent mix is allowed.
- Short input gaps insert silence/repeat video. Prolonged gaps, overflow, or encoder lag stop the session. Automatic reconnection is not implemented.
- RTMP progress confirms the local encoder is producing output; it does not prove viewers can play the remote feed. Check the receiver separately.

## Automated validation

With .NET 8, FFmpeg and ffprobe on PATH:

```sh
dotnet run --project tests/ValheimBroadcaster.Streaming.Tests -c Release
```

This runs the production encoder/session code against an actual local RTMP listener. It validates input rejection, bounded buffers, mono/stereo transfer, process cleanup, codec metadata, decoded non-silent stereo audio, a synchronized flash/tone, and an unavailable receiver. It does not instantiate Unity, prove game audio capture, or measure a Windows VM's performance. Follow the game-specific matrix in [RUNTIME-TESTS.md](RUNTIME-TESTS.md).
