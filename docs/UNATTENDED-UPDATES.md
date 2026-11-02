# Unattended updates for the sandboxed Windows broadcaster

The updater targets the existing **`ValheimBroadcastTest` Sandboxie box on Windows**. It runs outside the sandbox, watches one configured GitHub branch, downloads and verifies a successful client build, stops only its broadcaster process, replaces the sandbox's broadcaster files, and starts that copied game again. The plugin selects the configured existing character and joins the configured dedicated Steam server through Valheim's normal authentication flow.

Sandboxed Steam must already be running and signed in as the broadcaster account. This does not automate Steam login or Steam Guard. It does not stop the dedicated server, terminate the entire sandbox, or update the main Windows Valheim installation. Observer admission remains protocol 3, so no server upgrade is required for this feature.

The Windows adapter uses the installed Sandboxie API and retains the launched game's process handle. Identity checks include sandbox name, Windows session, executable and creation time. It does not infer ownership from `valheim.exe` alone. **Source checks and automated tests are not an in-game pass:** actual Sandboxie launch, authentication, streaming and rollback must pass the acceptance cases below before relying on unattended operation.

## How an update is accepted

1. Query the configured repository, workflow and branch. Only completed successful `push` or `workflow_dispatch` runs from that repository qualify. Pull-request builds and other branches do not qualify.
2. Select the exact client artifact `valheim-broadcaster-<commit>`. Verify its GitHub SHA-256, source commit, supported release manifest and every payload file. Missing, expired or malformed artifacts leave the current broadcast running.
3. Download and validate before introducing downtime. The only permitted payload is the three broadcaster DLLs and declared Cinemachine notices. Unsafe paths, links, unexpected files and hash mismatches are rejected.
4. Request a session-bound graceful shutdown. The plugin stops its output and disconnects the observer without saving the temporary travelling carrier position. The supervisor waits for the owned process to exit before changing files.
5. Install through a journaled transaction with a checked prior inventory. Start a new game session and require fresh status from that process, matching installed plugin bytes/version, fresh observer authorization and active observation. By default, actual publishing is also required for a stable interval.
6. A failed candidate is quarantined. Restore the prior owned binary payload and try the previous build; a failed rollback stops the cycle. Settings, saves and shared dependencies are not rolled back automatically.

The configured branch is an explicit update channel. The example follows `feature-unattended-client-updates`; change it deliberately when subsequent work moves to another branch. The updater does not search across unrelated development branches. GitHub Actions artifacts currently expire after 14 days; an expired artifact requires another successful build.

## One-time bootstrap

Use **64-bit Python 3.10 or newer on the Windows host**, outside Sandboxie, under the same signed-in Windows user that runs the box. The supervisor has no additional Python package dependencies. The installed Sandboxie Plus must expose the verified API functions; unsupported layouts or missing functions fail preflight.

Before the first automated update:

1. Close only the sandboxed broadcaster client. Leave the intended sandboxed Steam session available.
2. Manually install the startup-capable **0.8.19 or later** client payload into the broadcaster's copied installation inside `ValheimBroadcastTest`: all three DLLs and included `ThirdParty` notices together. Preserve existing configuration. Do not put the artifact's root `release-manifest.json` inside the plugin folder.
3. Keep an offline copy of the prior plugin files, broadcaster configuration and chosen character. For a Steam Cloud character, use the game's Save Manager to make a deliberate backup; an arbitrary copied Cloud cache is not a verified backup.
4. Configure and successfully supervise this build once. The first run establishes a healthy baseline before polling for updates, records its version and installed plugin hash, and subsequently backs up the exact owned payload during an update.

**0.8.18 and earlier cannot be an automatically recoverable baseline:** they do not have the startup bridge or session heartbeat. Binary rollback to those builds would not restore unattended character selection and rejoin. The first manual installation above is required; later healthy supervised builds can serve as rollback baselines.

Extract the supervisor artifact into a host folder such as `C:\Tools\ValheimBroadcasterSupervisor`. Keep `supervisor.py` and `sandboxie.py` beside each other. Copy `example-config.json` to a private host configuration path and edit it. The examples below use `C:\Tools\ValheimBroadcasterSupervisor\supervisor.json`; choose another absolute path if preferred.

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

The adapter verifies that the logical game executable maps to the configured physical copy. This initial version supports a standard local drive-backed sandbox and its `drive\c\...` mapping; UNC, redirected/junction-backed and other unsupported layouts are rejected. It launches the copied `valheim.exe` directly through Sandboxie rather than invoking a detached Steam launcher. The already-running sandboxed Steam client still supplies ordinary game authentication. Preserve any game arguments needed by the current working installation in the `launch_command` array.

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

For GitHub, use a fine-grained token limited to the repository with **Actions: read**. Store it in the private host `token_file`, outside Sandboxie, using the same hidden-prompt method and a private parent directory. Repository write permission is unnecessary. Do not share either secret file or put credentials in a task definition, repository, launch command or screenshot. `GH_TOKEN`/`GITHUB_TOKEN` are supported when `token_file` is omitted; the adapter removes those variables from the launched game's environment.

## First supervised run

Make sure the sandboxed Steam account is signed in, the existing sandboxed game is closed, and its normal BepInEx observer/stream settings are already working. In an ordinary host PowerShell window, outside the sandbox:

```powershell
Set-Location 'C:\Tools\ValheimBroadcasterSupervisor'
py -3 -c "import struct; print('Python bits:', struct.calcsize('P') * 8)"
py -3 supervisor.py --config 'C:\Tools\ValheimBroadcasterSupervisor\supervisor.json' check
py -3 supervisor.py --config 'C:\Tools\ValheimBroadcasterSupervisor\supervisor.json' run
```

The Python check must report 64. `check` performs preflight and GitHub metadata inspection; it does not stop the game, download or install a candidate. `run` establishes a healthy installed baseline before polling. An independently launched client is not silently adopted or killed.

The example polls every 120 seconds, allows 240 seconds for initial health and requires 20 seconds of stable health. Keep `require_publishing=true` together with `Streaming.AutoStart=true` for automatic stream recovery. If deliberately running camera-only, set `require_publishing=false`; acceptance then does not establish RTMP publishing. The health deadline includes game startup, joining and output startup. Increase it within supported bounds if a measured working launch takes longer.

Acceptance binds status to a new launch identity, the actual game process and installed plugin bytes. Old status files, a splash screen or a startup log line cannot mark the update healthy. The retained process handle and sandbox identity checks prevent another ordinary `valheim.exe` from receiving the updater's shutdown.

For automatic startup after Windows login, create a Task Scheduler task for this same Windows user, **Run only when the user is logged on**, using the full path to the selected 64-bit Python interpreter, the script path, `--config` and `run`. Set its working directory to the supervisor folder. Start the intended sandboxed Steam session first; a task delay alone does not prove that Steam has signed in. Do not configure unconditional task retries that undo a deliberate pause or blocked startup. The first supervised run should remain visible until verified.

## Controls, failures and recovery

All commands take the same `--config` path:

| Command | Behavior |
| --- | --- |
| `check` | Preflight and GitHub candidate metadata; no installation |
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
| Main game/other sandbox active | Unrelated clients, sandboxed Steam and dedicated server remain running and unmodified |
| Sandbox storage/process conflict | Preflight refuses unsupported mappings or unowned live client rather than editing uncertain files |
| Windows login/restart | Intended desktop and sandboxed Steam session are available before supervision begins |

Record artifact/run/commit, installed versions, startup/shutdown timings and receiver observations. Follow with a multi-hour soak including a server outage and a normal build update. Python/C# tests and assembly inspection cannot certify these live results.
