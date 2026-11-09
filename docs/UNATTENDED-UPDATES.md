# Unattended updates for the sandboxed Windows broadcaster

**0.8.48 is the first main-line integration of this capability.** The earlier updater remained on `feature-unattended-client-updates` and its 0.8.19 label did not mean that later main-line clients contained it. Startup and supervision are optional: `Startup.Enabled` defaults to `false`, and installing the client does not start a host supervisor or schedule updates. See the [audit findings and evidence](UPDATER-0.8.48-AUDIT.md).

The updater targets the Windows box named in your supervisor JSON. `ValheimBroadcastTest` is the example name. Keep your existing configuration when using a box such as `ValheimBroadcastClean`. It runs outside the sandbox, watches one configured GitHub branch, downloads and verifies a successful client build, stops only its broadcaster process, replaces the sandbox's broadcaster files, and starts that copied game again. The plugin selects the configured existing character and joins the configured dedicated Steam server through Valheim's normal authentication flow.

Sandboxed Steam must already be running and signed in as the broadcaster account. This does not automate Steam login or Steam Guard. It does not stop the dedicated server, terminate the entire sandbox, or update the main Windows Valheim installation. Observer admission remains protocol 3, so no server upgrade is required for this feature.

The Windows adapter uses the installed Sandboxie API and retains the launched game's process handle. Identity checks include sandbox name, Windows session, executable and creation time. It does not infer ownership from `valheim.exe` alone. **Source checks and automated tests are not an in-game pass:** actual Sandboxie launch, authentication, streaming and rollback must pass the acceptance cases below before relying on unattended operation.

## Existing broadcaster quick start (0.8.67)

The host ZIP contains only a template named `example-config.json`. Supply your saved `supervisor.json` separately. From the host supervisor folder, `Test-Path -LiteralPath .\supervisor.json -PathType Leaf` should return `True`. If you keep the config elsewhere, use its full path with `--config` in every command.

Before starting, have the Valheim server password ready. If your configured GitHub token file is missing, you also need a fine-grained token to download builds and compare commits. [Open the prefilled GitHub token form for Dillflix](https://github.com/settings/personal-access-tokens/new?name=Valheim+Broadcaster+Updater&target_name=Dillflix&actions=read&contents=read), choose **Only select repositories → valheim-streamer** and verify **Actions: Read-only** and **Contents: Read-only**. The wizard prompts for this token separately.

1. Keep the broadcaster's sandboxed Steam account signed in. Close the broadcaster game and stop its supervisor if already running. Extract the client ZIP to a staging folder such as `C:\Tools\ValheimBroadcasterSupervisor\client`. Copy its complete `ValheimBroadcaster` directory into the physical sandbox's `BepInEx\plugins`. Leave the root `release-manifest.json` in staging. Extract the supervisor ZIP into the host supervisor folder alongside your existing `supervisor.json`.
2. From the host run the commands below. `configure-startup` edits the sandboxed BepInEx config, retaining the other mod settings. It prompts once for the exact existing filename stem, Local or Cloud, dedicated Steam address and port, and the server password. Press Enter to reuse configured values. When choosing **set**, enter the password at the hidden prompt. When the host token file is configured but missing, a separate hidden prompt creates it outside the sandbox. Permissions are checked using the existing private-directory policy. For the first setup, the mod must have generated its config once.
3. The first `run` can use the supplied manifest to hand off a manually updated installation, including one with an older saved baseline. It checks all installed DLLs and notices, then launches with the pinned version and source and accepts only after stable observer/picture/publisher evidence. It respects saved pause and quarantine. Once accepted, omit `--installed-manifest` on subsequent launches and in scheduled tasks.

```powershell
Set-Location C:\Tools\ValheimBroadcasterSupervisor
py -3 supervisor.py --config .\supervisor.json configure-startup
py -3 supervisor.py --config .\supervisor.json run --installed-manifest .\client\release-manifest.json
```

To exercise shutdown and automatic rejoin without waiting for an update, leave `run` open. In a second host PowerShell in the same folder, use `py -3 supervisor.py --config .\supervisor.json pause`. Wait for `status` to show `paused` and the owned client to exit, then issue `resume`. Watch for a new launch, character selection, password submission (on a password-protected server), observer authorization and stable broadcasting. The host persists the last GitHub poll result in `status`. A qualified newer build exercises installation and rollback through the normal transaction. Use a **push-build artifact** for a pre-merge manual handoff. A PR test-merge SHA may never enter the configured branch's ancestry. If that happens after a squash or rebase, install the merged branch's CI build and hand it off with its matching manifest.

## How an update is accepted

1. Query the configured repository, workflow and branch. Only completed successful `push` or `workflow_dispatch` runs from that repository qualify. Pull-request builds and other branches do not qualify.
2. Require the candidate source to be a descendant of the source reported by the healthy installed client. A newer workflow number or version label alone is insufficient. Identical sources, older sources, divergent history and quarantined sources do not qualify.
3. Select the exact client artifact `valheim-broadcaster-<commit>`. Verify its GitHub SHA-256, source commit, supported release manifest and every payload file. The manifest requires observer protocol **3**, supervisor protocol **1**, all three broadcaster DLLs and nine Cinemachine/Splines notice files. Missing, expired, malformed or incomplete artifacts leave the current broadcast running. Downloading is bounded and checks cancellation between reads; a pending network read can take up to its thirty-second timeout to return.
4. Request a session-bound graceful shutdown. The plugin stops its output and disconnects the observer without saving the temporary travelling carrier position. The supervisor waits for the owned process to exit before changing files.
5. Install through a journaled transaction with a checked prior inventory. On Windows the game exits before the supervisor replaces changed files individually inside the existing plugin directory. The supervisor verifies each copy and the complete installed inventory before launch. Start a new game session and require fresh status from that process, matching installed plugin bytes/version/source, supervisor protocol, fresh observer authorization, active observation and live picture. By default, publishing is also required for a stable interval. If normal Scenic recovery begins during staging, postpone the update without interrupting that session.
6. A candidate that fails after installation is quarantined. A failure before activation restores the verified previous files and leaves the candidate eligible for another poll. An interrupted Windows file update can reconcile a mixture of the two recorded inventories from its backup. If rollback itself fails, retain the journal and enter `recovery_required`: no new ordinary launch or update may bypass that unresolved transaction. Settings, saves and shared dependencies are not rolled back automatically.

The configured branch is an explicit update channel. The example follows **`main`**. The updater does not search across unrelated development branches. GitHub Actions client artifacts currently expire after 14 days; an expired artifact requires another successful build. Existing on-disk rollback backups remain usable independently of GitHub retention. This implementation does not create permanent GitHub Release assets.

## One-time bootstrap

Use **64-bit Python 3.10 or newer on the Windows host**, outside Sandboxie, under the same signed-in Windows user that runs the box. The supervisor has no additional Python package dependencies. The installed Sandboxie Plus must expose the verified API functions; unsupported layouts or missing functions fail preflight.

Before the first automated update:

1. Close only the sandboxed broadcaster client. Leave the intended sandboxed Steam session available.
2. Manually install the **0.8.48 client built by repository CI**, or a later compatible main-line client, into the broadcaster's copied installation inside `ValheimBroadcastTest`: all three DLLs and included `ThirdParty` notices together. Preserve existing configuration. Do not put the artifact's root `release-manifest.json` inside the plugin folder.
3. Keep an offline copy of the prior plugin files, broadcaster configuration and chosen character. For a Steam Cloud character, use the game's Save Manager to make a deliberate backup; an arbitrary copied Cloud cache is not a verified backup.
4. Configure and successfully supervise this build once. The first run establishes a healthy baseline before polling for updates, records its version, exact CI source SHA and installed plugin hash, and subsequently backs up the exact owned payload during an update.

**Main-line 0.8.47 and earlier cannot be a supervised bootstrap baseline:** they do not contain this bridge/startup integration. The old feature-branch implementation also lacks the current provenance/capability contract. A local build without an exact CI source revision cannot establish an update baseline. Version numbering alone does not establish support. The first manual installation above is required; later healthy supervised builds can serve as rollback baselines.

Extract the supervisor artifact into a host folder such as `C:\Tools\ValheimBroadcasterSupervisor`. Keep `supervisor.py`, `sandboxie.py` and `startup_setup.py` beside each other. Copy `example-config.json` to a private host configuration path and edit it. The examples below use `C:\Tools\ValheimBroadcasterSupervisor\supervisor.json`; choose another absolute path if preferred.

## Two different kinds of game path

The host updater must edit the **physical sandbox copy**. The game must be launched using its **logical path inside the sandbox**. Do not point both settings at the main game installation.

Use Sandboxie Plus's file-root view to locate the actual `ValheimBroadcastTest` root, then locate the copied `valheim.exe` and the BepInEx directory used by that copy. Paths below are illustrative, not detected machine settings:

| Setting | Example | Used by |
| --- | --- | --- |
| `sandbox_root` | `C:\Sandbox\REPLACE_USER\ValheimBroadcastTest` | Host updater; checked against Sandboxie's reported root |
| `game_dir` | `<sandbox_root>\drive\c\REPLACE_GAME_FOLDER\Valheim` | Host inventory and installation |
| `game_executable` | `<game_dir>\valheim.exe` | Exact copied executable identity |
| `plugin_dir` | `<game_dir>\BepInEx\plugins\ValheimBroadcaster` | Owned DLLs and notices only |
| `launch_command[0]` | `C:\REPLACE_GAME_FOLDER\Valheim\valheim.exe` | Game path as seen inside the sandbox |
| `control_guest_root` | `C:\ValheimBroadcasterControl` | Private per-launch status/shutdown exchange inside the box |
| `state_dir` | `C:\Users\REPLACE_USER\AppData\Local\ValheimBroadcasterSupervisor` | Host state, outside the game and sandbox |
| `token_file` | `<state_dir>\github-token` | Host-only GitHub credential |

Replace all placeholders in [example-config.json](../tools/broadcaster-supervisor/example-config.json). JSON backslashes must be doubled, as shown in that file. Paths must be absolute; `<sandbox_root>`, environment variables and shell expansion are not processed as templates.

The adapter verifies that the logical game executable maps to the configured physical copy. This version supports a standard local drive-backed sandbox and its `drive\c\...` mapping; UNC, redirected/junction-backed and other unsupported layouts are rejected. It launches the copied `valheim.exe` directly through Sandboxie rather than invoking a detached Steam launcher. The already-running sandboxed Steam client still supplies ordinary game authentication. Preserve any game arguments needed by the current working installation in the `launch_command` array. An instance using the same copied files in another Windows session blocks replacement; it is never adopted or stopped. An API inspection failure is not treated as evidence that a process exited.

`control_guest_root` maps to `<sandbox_root>\drive\c\ValheimBroadcasterControl`. Each launch gets a new private subdirectory. The updater reads/writes the physical copy and the plugin reads/writes the logical copy. **Do not add an `OpenFilePath` exception:** this exchange uses sandbox storage, and the broadcaster executable itself is copied into the sandbox. No host-wide file-access exception is required.

## Character and dedicated-server settings

Launch the new client to the menu once with `Startup.Enabled=false` and `-console`. Run:

```text
broadcast startup profiles
```

Record the exact **filename stem and source** reported for the intended existing broadcaster character. Close that client, then edit its sandboxed `BepInEx\config\com.dillflix.valheimbroadcaster.cfg`. Merge these settings into the existing sections; retain the current camera, FFmpeg and RTMP configuration:

```ini
[Multiplayer]
ObserverConnection = true

[Unattended]
Enabled = true

[Startup]
Enabled = true
CharacterFile = REPLACE_WITH_EXACT_EXISTING_FILENAME_STEM
CharacterSource = Local
ServerAddress = REPLACE_WITH_DEDICATED_HOST:2456
PasswordFile = C:\ValheimBroadcasterSecrets\server-password

[Streaming]
AutoStart = true
```

`CharacterFile` excludes `.fch` and is not merely the display name. `CharacterSource` must be `Local` or `Cloud`, matching the enumeration; Steam must have made a Cloud profile available. A missing, ambiguous or incompatible profile blocks startup. There is no first-character fallback, character creation or local-world fallback.

`ServerAddress` accepts a dedicated Steam server's DNS name or IPv4 address with an explicit port. Use the endpoint that already works in a manual join. Crossplay join codes, IPv6 and account switching are outside this initial adapter. Normal server-password, Steam, capacity, version and observer-allowlist checks remain in effect.

The password file path above is the **logical path inside Sandboxie**. Create its physical backing file at `<sandbox_root>\drive\c\ValheimBroadcasterSecrets\server-password`. Store the password on one line with no added text. For a passwordless server leave `PasswordFile` empty. Do not put the password itself in configuration or launch arguments.

Keep the credential parent directory private to the current Windows user. From host PowerShell, with the real physical path substituted:

```powershell
$secretDir = 'C:\Sandbox\REPLACE_USER\ValheimBroadcastTest\drive\c\ValheimBroadcasterSecrets'
New-Item -ItemType Directory -Force -Path $secretDir | Out-Null
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
icacls $secretDir /inheritance:r /grant:r "$($identity):(OI)(CI)F"
$secretFile = Join-Path $secretDir 'server-password'
$secure = Read-Host 'Dedicated server password' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    [IO.File]::WriteAllText($secretFile, [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer), [Text.UTF8Encoding]::new($false))
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    $secure.Dispose()
}
```

Run this only on the dedicated credential directory. A hidden prompt avoids placing the password in PowerShell history; the file necessarily contains the credential for unattended joining. The updater does not back up or publish that file.

For GitHub, use a fine-grained token limited to the repository with **Actions: read** for workflow artifacts and **Contents: read** for [source ancestry comparison](https://docs.github.com/en/rest/commits/commits#compare-two-commits). Store it in the private host `token_file`, outside Sandboxie, using the same hidden-prompt method and a private parent directory. Repository write permission is unnecessary. Do not share either secret file or put credentials in a task definition, repository, launch command or screenshot. `GH_TOKEN`/`GITHUB_TOKEN` are supported when `token_file` is omitted; the adapter removes those variables from the launched game's environment.

## First supervised run

Make sure the sandboxed Steam account is signed in, the existing sandboxed game is closed, and its normal BepInEx observer/stream settings are already working. In an ordinary host PowerShell window, outside the sandbox:

```powershell
Set-Location 'C:\Tools\ValheimBroadcasterSupervisor'
py -3 -c "import struct; print('Python bits:', struct.calcsize('P') * 8)"
py -3 supervisor.py --config 'C:\Tools\ValheimBroadcasterSupervisor\supervisor.json' check
py -3 supervisor.py --config 'C:\Tools\ValheimBroadcasterSupervisor\supervisor.json' run
```

The Python check must report 64. `check` performs local preflight; it does not stop the game, download or install a candidate. Before the first healthy supervised run it reports `update_qualification: requires_first_healthy_supervised_baseline` and no candidate. That is an expected bootstrap result, not an installation failure or a claim that GitHub authentication has already succeeded. Once an accepted source SHA is recorded, `check` also qualifies GitHub candidates against it. `run` establishes the healthy installed baseline before polling. An independently launched client is not silently adopted or killed.

The example polls every 120 seconds and requires 20 seconds of authorized service plus a current live picture to confirm startup. The client measures the last completed render during its next game update. The supervisor prints the authorization, observer, publisher and picture flags while verifying. An existing authorized, publishing client stays up past the normal 240-second deadline while the supervisor waits for a live picture. An automatically installed update must still verify by the deadline or roll back. Keep `require_publishing=true` together with `Streaming.AutoStart=true` for automatic stream recovery. If deliberately running camera-only, set `require_publishing=false`; acceptance then does not establish RTMP publishing. The health deadline includes game startup, joining and output startup. Increase it within supported bounds if a measured working launch takes longer.

Acceptance binds status to a new launch identity, the actual game process, installed plugin bytes, source revision and supervisor protocol. Old status files, a splash screen or a startup log line cannot mark the update healthy. The retained process handle and sandbox identity checks prevent another ordinary `valheim.exe` from receiving the updater's shutdown. The bridge reports the actual owned RTMP, diagnostic-capture and timelapse encoder PIDs. The host retains their proven identities for cleanup, including during writer rotation and shutdown; it never terminates every process named `ffmpeg.exe`.

For automatic startup after Windows login, create a Task Scheduler task for this same Windows user, **Run only when the user is logged on**, using the full path to the selected 64-bit Python interpreter, the script path, `--config` and `run`. Set its working directory to the supervisor folder. Start the intended sandboxed Steam session first; a task delay alone does not prove that Steam has signed in. Do not configure unconditional task retries that undo a deliberate pause or blocked startup. The first supervised run should remain visible until verified.

## Controls, failures and recovery

The live JSON reader opens status and operator snapshots with read, write and delete sharing on Windows. It retries short access conflicts, including plain errno 13. A missed status sample cannot accept an installation. Only a fresh authorized live picture and publisher can complete startup. When operator.json cannot be read, an existing broadcast can continue and new launches and updates are deferred. Extended loss of status remains subject to the configured health deadline. Both filenames are reported on read denial.

For a retryable startup failure, the host prints the failure and last observed startup phase before cleanup. It records whether the held game process was still running or had already exited, including the exit code when available. Inspect `last_startup_failure` using `status`. While a managed launch is recorded, the host also attempts to save the final 2 MiB of its current BepInEx log to `startup-failure-1.log`, `-2.log` or `-3.log` in the configured private state directory before sending shutdown. These are local diagnostic logs.


All commands take the same `--config` path:

| Command | Behavior |
| --- | --- |
| `check` | Local preflight; candidate qualification only after a healthy source baseline; no installation |
| `run` | Supervise the configured copied client and apply qualified updates |
| `status` | Read local supervisor state |
| `pause` | Persist a pause; the running supervisor stops its client and suspends updates/restarts |
| `resume` | Clear pause so a running supervisor can launch again |

For example, use a second host PowerShell window to pause while leaving the supervisor running:

```powershell
py -3 'C:\Tools\ValheimBroadcasterSupervisor\supervisor.py' --config 'C:\Tools\ValheimBroadcasterSupervisor\supervisor.json' pause
```

Manual quit/logout or unattended-stop intent must not become a crash-restart loop. Correct the reason and explicitly resume. The supervisor's state resides in the private `state_dir`; per-launch plugin status resides in the sandbox's mapped control directory. Use `broadcast startup status`, `broadcast unattended status`, `broadcast diagnostics` and supervisor `status` together. Retain redacted failure evidence before another attempt.

Quarantine records identify failed source commits; a later good build can qualify. There is no general-purpose command to clear quarantine or force arbitrary archives into the installation. Do not hand-edit a pending transaction while a process is running. If journal recovery or identity checks stop operation, retain the state and inspect it before making a manual repair.

`recovery_required` means an installation transaction could not be safely reconciled, for example because its backup failed verification or a restore rename failed. The redacted `last_update_failure` in supervisor `status` identifies the update step and any separate rollback failure. Preserve the transaction directory and any remaining `transaction.json`. Resolve the reported storage/permission problem, then start supervision again so recovery runs before any game launch. A `resume` request does not bypass a pending journal. If backup contents are corrupt, restore from a separately verified copy rather than deleting the journal and trying the candidate again.

Startup acceptance and readiness to replace a working build require live footage. Ongoing session health retains grace for legitimate travel, recovery and operator-held output; it is not a watchdog that proves every rendered frame is useful. Use the broadcaster's footage/diagnostics to evaluate persistent picture problems.

The updater does not update itself. Replace its Python files deliberately while supervision is stopped. It does not update BepInEx, Jötunn, FFmpeg, Steam, Valheim or the server companion. A game update can break both the new and previous broadcaster; DLL rollback cannot downgrade the game. Binary rollback never claims to undo save changes.

## Host acceptance — not yet run

Use a disposable observer character/profile and a test dedicated server before relying on production automation.

| Case | Required observation |
| --- | --- |
| Cold startup | Correct copied game starts in `ValheimBroadcastTest`; intended character/source/server selected; fresh authentication and observer authorization complete |
| Publishing | Expected plugin version, live picture and game audio; receiver accepts the new publisher; no duplicate FFmpeg process |
| New good build | Download finishes before shutdown; only physical sandbox plugin payload changes; new process rejoins and meets the stability gate |
| Bad candidate | Candidate quarantined; exact healthy prior payload restored; previous client rejoins; no per-poll retry loop |
| GitHub/download failure | Existing healthy broadcast continues; no credential or signed download URL appears in logs |
| Missing character/wrong password | Startup blocks; no different character, source or server is selected |
| Manual pause/logout/quit | No unwanted rejoin/relaunch; explicit resume required |
| Supervisor interrupted during install | Journal reconciles before launch; no mixed DLL set is loaded |
| Rollback storage failure | `recovery_required` and retained journal; no candidate relaunch or second update; recovery succeeds after the test fault is removed |
| Three active encoders | RTMP, diagnostic capture and timelapse writers close during shutdown; no surviving owned encoder; unrelated FFmpeg continues |
| Main game/other sandbox active | Unrelated clients, sandboxed Steam and dedicated server remain running and unmodified |
| Same copied game in another Windows session | Installation refused without terminating that other session |
| Sandbox storage/process conflict | Preflight refuses unsupported mappings or unowned live client rather than editing uncertain files |
| Windows login/restart | Intended desktop and sandboxed Steam session are available before supervision begins |

Record artifact/run/commit, installed versions, startup/shutdown timings and receiver observations. Follow with a multi-hour soak including a server outage and a normal build update. Python/C# tests and assembly inspection cannot certify these live results.
