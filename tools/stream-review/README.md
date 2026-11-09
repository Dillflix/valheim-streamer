# Private stream review

The developer player lives at `/review/` on the Owncast origin. It plays the existing HLS feed and can also play recent diagnostic recordings. Clicking **Report this moment** selects a permanent reference to that session and capture time. Developers can bookmark or share the link with other authorized viewers and download the available evidence around it. A bookmark identifies a moment across restarts and playback delays. Evidence expires according to host retention.

## Stream host

From this deployment ZIP (extract first) or the matching repository checkout, enter `tools/stream-review`. Create a private `.env` beside `compose.yaml` with the following settings. Generate two independent secrets, for example with `openssl rand -hex 32` twice. Supply the upload token only to the Windows uploader.

```ini
REVIEW_USER=reviewer
REVIEW_PASSWORD=<long private viewer password>
REVIEW_UPLOAD_TOKEN=<separate random upload token>
REVIEW_RETENTION_HOURS=24
REVIEW_MAX_MIB=2048
```

Run `docker compose up -d --build`. The default listener is 127.0.0.1:8788. In the existing TLS server block for `valheim.dillflix.ca`, add the following location. Keep Owncast's normal root and HLS routes in place.

```nginx
location ^~ /review/ {
    client_max_body_size 12m;
    proxy_pass http://127.0.0.1:8788;
    proxy_set_header Host $host;
    proxy_read_timeout 100s;
}
```

If NGINX is in another container or on another host, set `REVIEW_BIND` to the review host's private interface, point `proxy_pass` at that address, and restrict that port to the proxy. Check the proxy config and reload it. Serve the page on the same HTTPS origin as `/hls/stream.m3u8` so the player can read its video pixels. Viewer authentication covers the page, assets, recordings and downloads. The public Owncast player stays available at its existing URL. The upload endpoint separately requires a bearer token. Use a trusted private network for any unencrypted upstream hop.

## Windows broadcaster

1. Install the matching 0.8.66 client in the broadcaster profile. In Valheim run `broadcast capture on` and `broadcast stream marker on`. The small lower-left reference is visible on the RTMP feed. The full debug panel has its own `broadcast stream overlay on|off` control. Both stream settings persist. The marker is off by default.
2. Run the uploader **on Windows outside the sandbox**, under the account that can read the sandbox's actual diagnostic files. It requires standard Python 3.10+ and makes outbound HTTPS requests. Set the upload token in an environment variable, e.g. save it privately in the user environment for the scheduled account. For a one-session test in PowerShell:
   
   ```powershell
   $env:REVIEW_UPLOAD_TOKEN = Read-Host 'Upload token'
   $diag = 'C:\Sandbox\jason\ValheimBroadcastClean\drive\C\ValheimBroadcastSteam\steamapps\common\Valheim\BepInEx\diagnostics\ValheimBroadcaster'
   py -3 .\upload.py "$diag" https://valheim.dillflix.ca/review
   ```

   Use the actual active BepInEx profile if it stores diagnostics elsewhere. The uploader can run while the game is closed and rejoins after a restart. For unattended operation create a Task Scheduler task at logon using the absolute path to `python.exe` with arguments `"C:\path\to\upload.py" "C:\path\to\ValheimBroadcaster" https://valheim.dillflix.ca/review`. Configure restart on failure and remove the default time limit. The host's review credential and the broadcaster's RTMP key are unnecessary on the uploader.
3. Open `https://valheim.dillflix.ca/review/` with the review credentials. Start playback. Pause, click **Report this moment**, then download evidence. Resume and retry the bookmarked download after the recording closes to include the following 15 seconds. The view shows the number of trace rows and completed video segments. Extract the ZIP and open `footage-review.html` when video was available.

## What the player matches

Live HLS uses a versioned checksummed reference burned into each admitted encoder frame. It contains the trace session and unscaled capture milliseconds. Repeated encoder frames retain their original reference. An unreadable marker, missing trace, or unsupported crop is reported explicitly. Normal Owncast full-frame rendition resizes are supported. With both layers off, reviewers can select a recent local recording and report its playback position using its 12 fps sidecar. Capture repeats refer to their recorded source sample. This recording is a sampled clean picture without game audio. The public Owncast interface does not contain review controls.

The uploader sends completed video/sidecar pairs and newline-complete chunks of owned Scenic trace files. Game saves, configuration and BepInEx logs are outside its inputs. Coordinates and subjects may be present in diagnostics. Files still open at shutdown can yield partial trace-only evidence. Requests cover ±15 seconds and include whole overlapping video segments. There can be capture gaps inside a window. A new report or retry uses the selected instant until the viewer selects another. SQLite retains up to the configured logical payload budget and expires reads by age. Writes prune expired and oldest evidence; SQLite reuses freed pages. Leave room for the database high-water mark, journals and temporary exports. Use `docker compose down` to stop the service and preserve the evidence volume. Stop the Windows task and run `broadcast stream marker off` to stop sharing the live reference.
