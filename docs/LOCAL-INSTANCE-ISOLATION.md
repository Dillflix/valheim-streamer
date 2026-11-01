# Local Windows two-client isolation test

Status: **first Steam isolation test FAILED on the user's Windows PC**.
The sandboxed Steam auto-logged into the host account. Logging out closed the host
Valheim client and logged out host Steam too. Separate process membership did not
establish independent Steam sessions. The cause is not yet established: inherited
login/session state and host IPC/file access must be distinguished.

**Do not repeat the original Stage 1 procedure below.** Terminate only this test
sandbox's processes, restore normal Steam, and inspect the installed Sandboxie
version, sandbox type, and effective global/box/template settings first. Keep games
closed during the next account-isolation experiment. Do not delete host Steam
credentials, broaden IPC permissions, or use logout as an isolation test.
A fresh independently initialized Steam environment is a candidate next experiment,
not a verified fix. The later client/streaming tests remain not run.

The original Stage 1 steps are retained below as a record of the failed procedure.
This is an experimental compatibility test, not a guarantee that current Steam,
Sandboxie, Doorstop and NVENC work together. No production-server change is required.
Use the already installed 0.8.2 client for this isolation test; the 0.8.3 candidate
failed streaming CI and is not an approved replacement. Known 0.8.2 RTMP stalls
must be recorded separately from isolation failures.

## 1. Establish two Steam sessions first

Prerequisites: Windows 11, the existing game installation, Sandboxie Plus from
https://sandboxie-plus.com/downloads/, and a second Steam account you control.
Do not purchase anything merely to test whether two Steam sessions coexist.
Running both Valheim clients concurrently later requires appropriate game access
for both accounts. Sandboxie does not create an entitlement or a second SteamID.

1. Keep normal Steam running under your playing account.
2. Create a new standard sandbox named **ValheimBroadcastTest** in Sandboxie Plus.
   Keep its contents between runs. Do not enable automatic content deletion,
   automatic file recovery into the host, or direct write access to host Steam
   credentials, Valheim saves, mod profiles or game settings.
3. In a normal PowerShell window, run the following, adjusting installation paths:

```powershell
$SbieStart = 'C:\Program Files\Sandboxie-Plus\Start.exe'
$SteamExe = 'C:\Program Files (x86)\Steam\steam.exe'
if (!(Test-Path -LiteralPath $SbieStart)) { throw 'Locate Sandboxie Start.exe first.' }
if (!(Test-Path -LiteralPath $SteamExe)) { throw 'Locate Steam.exe first.' }
& $SbieStart /box:ValheimBroadcastTest $SteamExe
```

4. In the **sandboxed** Steam window, sign in to the second account. Verify which
   Steam window belongs to the box in Sandboxie Plus before changing accounts.
   A new box can initially read host files; it is not automatically a blank account.
5. Confirm the ordinary Steam window still has your playing account logged in.
   Close and reopen only the sandboxed Steam once; confirm the identities remain
   distinct and the normal session stays signed in.

**Pass:** two independently usable Steam sessions with different account identities.
**Fail:** second launch only focuses normal Steam, logs the host account out, exits,
or shows a sandbox error. Capture the exact Sandboxie message and stop at this stage.
Do not fix it by granting blanket host IPC/filesystem access: that may defeat the
isolation being tested. A command returning successfully does not establish isolation.

## 2. Prepare separate mod/profile and save state

Create a separate Thunderstore profile named **Broadcaster-Isolation** from the
working broadcaster profile. Keep its same dependency and plugin versions.
Do this while the relevant game instances are closed. The playing profile must
not contain the broadcaster plugin or its observer configuration.

In the new broadcaster profile set:
```ini
[Multiplayer]
ObserverConnection = true

[Unattended]
Enabled = false

[Streaming]
AutoStart = false
Width = 1280
Height = 720
FPS = 30
VideoBitrateKbps = 4000
VideoEncoder = h264_nvenc
```

Retain the existing FFmpeg path and private RTMP URL. Edit existing sections rather
than adding duplicate sections. This does not fix the known RTMP stalls.

Obtain this profile's exact **launch outside the mod manager** arguments from the
manager's Help page, if available. Use those arguments as Valheim launch options
in the **sandboxed Steam** client only, appending `-console`.
They must point to **Broadcaster-Isolation**, not the original profile.
Doorstop argument names vary by loader version; do not guess or replace them.
If the manager does not expose these arguments, record that fact before proceeding.

Launch Valheim from that sandboxed Steam Library. Do not click Start modded in the
host manager: that can route the launch back to the host Steam session.
Confirm both Steam and valheim.exe appear inside ValheimBroadcastTest in Sandboxie
Plus. Confirm the BepInEx log loads the expected broadcaster version and profile.

In sandboxed Steam, disable Steam Cloud for Valheim for this test, before creating
test saves. Create a fresh disposable local character and world; do not select,
delete or migrate existing saves. Temporarily use `ObserverConnection=false`
while completing the opening intro in that local world, then exit the game and
set it back to true in the **effective sandboxed config** before joining the server.

Sandboxie virtualizes writes. After the first sandboxed run, a config/log can exist
inside the sandbox even when its visible logical path matches the host profile.
Use Sandboxie's Explore Contents and a sandboxed editor to find/edit the effective
copy. Editing the host copy alone may no longer change the sandbox's copy.
Do not recover sandbox settings or saves into the ordinary profile.

Check isolation explicitly: change resolution in the sandboxed game, exit it,
and confirm ordinary Valheim retained its own settings. Confirm the new local
character is absent in the host character list. Preserve logs from each profile
before relaunching overwrites them.

## 3. Connect both clients to the LOCAL test dedicated server

Use a disposable world and the existing compatible protocol-2 server companion.
Keep the server outside the sandbox. Do not change production for this experiment.

With the local server stopped, change its
`BepInEx/config/com.dillflix.valheimbroadcaster.server.cfg`:
```ini
[Observer]
Enabled = true
SteamIds = SECOND_ACCOUNT_STEAM64_ID
DiscoveryEnabled = true
SharePrivatePlayerPositions = true
```

Replace the placeholder with the actual second account's numeric Steam64 ID.
Remove the playing account from this test server's observer allowlist.
The previously configured ID `76561198128343189` is the playing account in this
setup: leaving it allowlisted prevents it from acting as an ordinary player.
Restart the test server after editing. The observer role applies to a whole
connection, regardless of character name. Do not alter admin/bans or authentication.

Join the dedicated Steam server with the ordinary client first, then the
sandboxed broadcaster. Use separate disposable characters. Keep RTMP off.

| Check | Required observation |
| --- | --- |
| Identity | Server authenticates distinct account identities; only second account gets observer role |
| Gameplay | Playing account moves/interacts normally, without observer handshake rejection |
| Acquisition | Broadcaster diagnostics discover one other player and follow that character |
| Visibility | Playing client sees no broadcaster body or gameplay roster entry |
| Input | Clicking/typing in one window does not move or operate the other character |
| Distant target | Move playing character to another generated region; observer reacquires |
| Disconnect | Exit playing client; broadcaster transitions to scenic coverage |
| Rejoin | Rejoin; broadcaster acquires the player again |
| Isolation | Quit broadcaster normally; playing client and test server remain connected |

Visibility and acquisition success alone do not establish full simulation isolation.
Use [MULTIPLAYER-OBSERVER.md](MULTIPLAYER-OBSERVER.md) for ownership/gameplay checks.

## 4. Add streaming, then test background operation

First render the broadcaster game itself in a 1280x720 window, with modest graphics
and a 30 FPS game cap. Setting output Width/Height/FPS alone does not reduce the
game's original render resolution. Keep game audio enabled and use stereo output.

Run `broadcast stream on` only in the broadcaster. Confirm FFmpeg is also listed
inside the same sandbox. Confirm the receiver plays both video and game audio.
Then test each condition separately:

| Condition | Duration | Evidence |
| --- | --- | --- |
| Broadcaster focused | 5 minutes | Receiver video, audio, diagnostics, no restart |
| Playing client focused; broadcaster visible | 10 minutes | Continued motion/audio while actively playing |
| Broadcaster covered by another window | 5 minutes | No frozen capture or audio timeout |
| Broadcaster minimized | 2 minutes | Record pass/fail separately; do not assume background setting covers this |
| Repeated alt-tab and normal player travel | 10 minutes | No camera/input cross-talk; acquisition and audio continue |
| Both clients running | 30 minutes | GPU 3D/encode utilization, dedicated/shared GPU memory, system RAM, per-client FPS, retry counts |

If minimization fails, restore the broadcaster and test recovery; keep it
unminimized for subsequent tests. A successful unfocused test is useful even when
minimized rendering is unsupported. Lock/sleep/reboot are separate unattended tests;
sleep stops this local broadcaster. Do not claim 24/7 reliability from this matrix.

Listen at the receiver while making sounds in each client separately. The stream
should contain the broadcaster game's mix, not the host game's direct audio output.
Do not mute the broadcaster in-game as a way to silence local playback; it can
silence the captured mix. Receiver playback on the same PC can make this comparison
confusing; use headphones or a separate playback device.

If NVENC or the audio TCP connection fails only in the sandbox, preserve Sandboxie
messages and FFmpeg diagnostics. Compare the identical broadcaster configuration
outside the sandbox with the playing client closed. Existing 0.8.2 buffer/lag failures
outside the sandbox are a streaming baseline failure, not proof isolation is broken.

## Evidence and cleanup

Record OS/GPU/driver, Sandboxie version, game/dependency/plugin versions and hashes,
account roles, effective profile/log paths, and each row's pass/fail/not-run result.
Do not share Steam credentials, full process command lines, config files with RTMP
keys, or passwords. Save redacted diagnostics and the receiver observations.

Read-only process inventory (no command lines or stream keys):
```powershell
Get-CimInstance Win32_Process |
    Where-Object { $_.Name -in @('steam.exe', 'valheim.exe', 'ffmpeg.exe') } |
    Select-Object Name, ProcessId, ParentProcessId, ExecutablePath |
    Format-Table -AutoSize
```
Use Sandboxie Plus's process tree to establish sandbox membership; the Windows
process inventory alone cannot prove membership or account identity.

Stop the broadcaster stream, quit its game normally, then exit sandboxed Steam.
If hung, terminate programs in **ValheimBroadcastTest only** using Sandboxie Plus.
Check that only its FFmpeg exits and ordinary gameplay remains available.
Keep sandbox contents/logs for diagnosis; no deletion or save rollback is needed.
Restore the local server's prior allowlist only with it stopped if returning to the
previous single-account test arrangement.

Sources checked 2026-10-31:
- [Sandboxie launch and process commands](https://sandboxie-plus.github.io/sandboxie-docs/Content/StartCommandLine/)
- [Sandboxie Steam compatibility limitations](https://sandboxie-plus.github.io/sandboxie-docs/Content/KnownConflicts/)
- [r2modman launch-argument troubleshooting](https://github.com/ebkr/r2modmanPlus/issues/1932)
- [Steam Families simultaneous copy requirements](https://help.steampowered.com/en/faqs/view/054C-3167-DD7F-49D4)

