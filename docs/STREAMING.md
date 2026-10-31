# Direct RTMP with game audio

Version 0.8.0 integrates selectable NVIDIA NVENC encoding and redacted FFmpeg failure diagnostics with output intent, optional automatic start, and bounded retries. It retains H.264 video and stereo AAC game audio from the broadcaster client. The plugin manages capture and an FFmpeg child process. OBS is not used. RTMP capture alone does not require the server companion; the read-only multiplayer observer and autonomous discovery do.

The audio source is the game's existing AudioListener mix: game sounds/music and any other mod audio routed through that listener. The listener follows the broadcast camera while publishing. No microphone, Discord, or desktop audio capture is opened. Local playback is not muted by our filter. Actual volume/mixer behavior and spatial alignment must be checked in Valheim.

## Thunderstore setup

1. Close Valheim. In Thunderstore, select the profile you launch and use **Settings → Browse profile folder** (wording may vary by manager version).
2. Install both client DLLs from the same 0.8.0 test build into `BepInEx/plugins/ValheimBroadcaster/`. Remove duplicate older copies of this plugin elsewhere in that profile. Keep BepInEx and Jötunn installed.
3. Install a trusted FFmpeg build with your selected video encoder (`libx264` or `h264_nvenc`), `aac`, TCP, RTMP, and optionally RTMPS support. The [FFmpeg download page](https://ffmpeg.org/download.html) links Windows builds. The mod does not download or bundle FFmpeg. Point `FFmpegPath` at the actual binary, not a package-manager launcher/shim or a shell script; the mod must own the encoder process to stop it and close its pipes. For Chocolatey installations the actual binary is normally under `C:\ProgramData\chocolatey\lib\ffmpeg\tools\ffmpeg\bin\ffmpeg.exe`, not `C:\ProgramData\chocolatey\bin\ffmpeg.exe`. Run `ffmpeg -encoders` to check those encoders.
4. Launch modded once to generate the new config entries, then close Valheim. Edit `BepInEx/config/com.dillflix.valheimbroadcaster.cfg` in this same profile:

```ini
[Streaming]
FFmpegPath = C:\Tools\ffmpeg\bin\ffmpeg.exe
RtmpUrl = rtmp://YOUR_RECEIVER:1935/live/YOUR_STREAM_KEY
Width = 1280
Height = 720
FPS = 30
VideoBitrateKbps = 4000
VideoEncoder = h264_nvenc
NvencGpu = -1
FlipVertical = false
AutoStart = false
AutoRetry = true
```

The example selects NVIDIA encoding. The default remains `VideoEncoder = libx264` to preserve existing configurations. Use `NvencGpu = -1` to select an available NVIDIA GPU, or `0`, `1`, etc. to select a particular device. NVENC runs on the **broadcaster client**, not the RTMP receiver. It requires an NVENC-capable NVIDIA GPU exposed to that client, a compatible installed driver, and an FFmpeg build containing `h264_nvenc`. Unsupported hardware, driver/API mismatches, or unavailable encoder sessions fail explicitly; the mod does not silently fall back to software.

Use the complete ingest URL supplied by your receiver. RTMPS is accepted if your FFmpeg build supports it. The mod is the publisher, not the remote RTMP server. The URL stays empty and automatic start is disabled by default. Do not share this config with a real stream key; FFmpeg's local process arguments also contain that key.

5. Select stereo output on a working 44.1 or 48 kHz audio device. A VM needs a functioning virtual audio device and GPU acceleration. The output feed is always 48 kHz stereo AAC at 128 kbps. Mono input is duplicated to both channels; surround output is rejected.
6. Start your RTMP receiver, then use Thunderstore's **Start modded**. Join a disposable world alone. Press **F10** to follow your own character, then **F11** to start video **and audio**. The overlay should progress from `Starting encoder` to `Publishing H.264 + AAC (h264_nvenc)` (or `libx264`).
7. View/record the feed on the receiver. Walk, jump, swing a tool, and stand near a fire/water source. Verify audible sound, left/right positioning, motion/sound alignment, image orientation, and local playback. If upside down, stop, set `FlipVertical = true`, and restart the game. Match output/game aspect ratios to avoid stretching.
8. **F11** stops publishing while preserving the follow camera. **F8**/`broadcast off` stops publishing; in active observer mode it also requests protected return/park. Explicit stream off suppresses automatic start until an explicit stream on. Wait for cleanup if status says `Stopping`; a new encoder cannot overlap the old workers or pending GPU readback. See the recovery behavior below before enabling unattended output.

Console equivalents (requires Valheim `-console`): `broadcast self`, `broadcast stream on`, `broadcast stream status`, `broadcast stream diagnostics`, `broadcast stream off`. No console flag is needed for hotkeys.

## Output intent and recovery

`AutoStart=true` requests output automatically when the configured broadcast session is eligible. `AutoRetry=true` retries a requested stream after recoverable receiver, capture or audio-device failures. Starting the plugin in a menu is not permission to publish menu footage. See [AUTONOMOUS-BROADCAST.md](AUTONOMOUS-BROADCAST.md) for the authorized-session and reconnect setup.

Manual `broadcast stream on`/F11 establishes output intent; explicit `broadcast stream off`/F11 or broadcast off cancels it. `broadcast unattended off` and native logout/quit also cancel requested output; logout does so even when unattended recovery was disabled. Automatic start does not undo a manual stop. Use `broadcast stream on` to request output again. An unexpected connection loss suspends the encoder while retaining requested output, so an authorized recovered session can resume it. Camera target cuts, scenic changes and ordinary controlled travel do not themselves restart a healthy encoder.

Recoverable failures use delays of 5, 10, 20, 40, 60, 60 and 60 seconds, then a 300-second cooldown after the eighth consecutive failure. The bounded cycle repeats while output remains requested. A stable 30 seconds of actual publishing resets the failure burst. No retry starts until the previous encoder and readback have completed cleanup.

Invalid configuration, an unavailable executable, unsupported GPU readback, recognized unavailable encoders/NVENC hardware or driver capability, and explicit receiver authentication rejection block automatic retries. Correct the cause and explicitly start again. Receiver/network, capture and audio-device failures can retry; diagnostics retain the redacted reason, attempt count and retry delay. A retry schedule is not evidence of successful output—verify the remote receiver.

## Test NVIDIA encoding independently

On the **broadcaster Windows machine**, use the same executable specified in `FFmpegPath`. This one-second synthetic test does not contact your RTMP server or include a key:

```powershell
& 'C:\Tools\ffmpeg\bin\ffmpeg.exe' -hide_banner -f lavfi -i color=c=black:s=1280x720:r=30 -frames:v 30 -c:v h264_nvenc -preset p4 -tune ll -rc cbr -b:v 4M -pix_fmt yuv420p -gpu 0 -f null -
```

A successful encode verifies usable NVENC on that device; `ffmpeg -encoders` alone only verifies that the encoder was compiled in. If it fails, use its exact error to distinguish missing support, a driver mismatch, device selection or capacity. Keep audio enabled for the subsequent in-game RTMP test.

Implementation references: [NVIDIA FFmpeg guide](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/ffmpeg-with-nvidia-gpu/index.html), and the installed executable's `ffmpeg -h encoder=h264_nvenc` options.

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

- Defaults are 720p30, 4 Mbps H.264 + 128 kbps AAC. `libx264` uses the CPU; `h264_nvenc` uses NVIDIA hardware for H.264 encoding (p4 preset, low-latency tuning, CBR, no B frames, two-second keyframe interval). Capture/readback, pixel conversion, copying and AAC still have CPU/GPU costs. This is not a zero-copy GPU capture path. Lower resolution/FPS if capture or encoding cannot keep up.
- Capture includes the actual game framebuffer: chat, menus, console, overlays, and the waiting slate can be visible. It is not a desktop capture. Self-follow always displays its test label.
- Keep the game rendering and its audio device active. Minimizing, suspending, or loss of focus can stop rendering/audio depending on game/OS settings. Five seconds without capture callbacks stops output rather than broadcasting a frozen frame indefinitely. Automatic retries cannot supply frames while the game or OS prevents rendering.
- `Could not start FFmpeg`: check the absolute executable path and permissions. `FFmpeg exited`/pipe errors are symptoms; inspect `broadcast stream diagnostics` or the automatic **RTMP failure (redacted)** block in `BepInEx/LogOutput.log`. The block records the selected encoder, exit code and up to eight bounded stderr lines. URLs and configured destination/credential components are removed before retention; the config and local process arguments still contain the stream key. Unknown encoder, missing NVENC driver/library, unsupported driver API, connection refusal and receiver rejection now retain their underlying error. A pipe failure gets a short background drain period so its follow-on error does not replace the useful FFmpeg message. Review logs before sharing; do not post your config or a full publishing command.
- `Audio buffer overflow`: check stereo layout and encoder CPU capacity. Audio device changes stop the current encoder; requested output can retry after the device stabilizes. Audio callbacks are required even during silence; a valid silent mix is allowed.
- Short input gaps insert silence/repeat video. Prolonged gaps, overflow, or encoder lag stop the session and enter the configured retry policy. In-game server reconnection is separate from RTMP recovery.
- RTMP progress confirms the local encoder is producing output; it does not prove viewers can play the remote feed. Check the receiver separately.

## Automated validation

With .NET 8, FFmpeg and ffprobe on PATH:

```sh
dotnet run --project tests/ValheimBroadcaster.Streaming.Tests -c Release
```

CI runs this harness on Linux and Windows. It runs the production encoder/session code against an actual local RTMP listener using software encoding. On an NVIDIA-equipped host, append `-- --nvenc` to also run the same received-media and synchronization checks using NVENC. Hardware encoding is explicitly reported as not run unless requested. It validates encoder selection, bounded/redacted diagnostics, early-exit error preservation, input rejection, bounded buffers, mono/stereo transfer, process cleanup, codec metadata, decoded non-silent stereo audio, a synchronized flash/tone, an unavailable receiver, and publishing after the receiver restarts. Recovery policy checks exercise explicit-stop precedence, backoff, stable-output reset and a 48-hour simulated outage. The 0.8.0 local run passed all 66 checks. These checks do not instantiate Unity, prove game audio capture, establish actual multi-day operation, or measure a Windows VM's performance. Physical NVIDIA validation was not run. Follow the game-specific matrix in [RUNTIME-TESTS.md](RUNTIME-TESTS.md).

## 0.8.1 audio startup repair

The previous capture path could fill its half-second audio ring while FFmpeg was still opening video, before the audio socket connected. It then terminated the encoder with an ambiguous overflow/channel-format message. The screenshot from 0.8.0 establishes that ambiguous failure, not a proven NVIDIA driver or speaker-mode fault.

The encoder now receives one initial video frame with raw-input probing minimized. Audio before the input connection is discarded as pre-roll; continuing video and audio share the connection epoch. Startup remains bounded to ten seconds. Buffer overflow after connection and unsupported callback channels have distinct messages, including observed channels, sample rate and connection state. Unsupported formats block automatic retries until an explicit restart.

Client 0.8.1 remains protocol-compatible with server 0.8.0. Replace the client plugin and its matching Core DLL together. Live Valheim/NVENC validation remains pending.
