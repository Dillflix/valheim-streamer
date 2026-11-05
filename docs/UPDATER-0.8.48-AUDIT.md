# Windows updater audit — 0.8.48

The audit starts from the complete 0.8.47 main-line source. The existing updater was on `feature-unattended-client-updates` at `5d60844ef616534a184c0a29fe8d522b6f0cbe0b`, based on an older feature lineage. Main-line 0.8.47 did not contain its host supervisor, startup bridge or automatic character/server selection. The old branch already had automated tests; it had no recorded successful Windows/Sandboxie/game acceptance run. Its version label was not evidence that later main-line clients included it.

The 0.8.48 integration brings those capabilities forward onto current camera, observer and streaming code. Installing the client leaves startup disabled by default. The host Python supervisor runs only when the operator starts or schedules it. Observer admission remains protocol 3; no companion update is required for the updater.

## Findings and corrections

| Finding | Consequence | Correction and regression evidence |
| --- | --- | --- |
| Updater existed only on its old branch | Current client could not provide its startup/heartbeat contract; switching back would discard later work | Forward-port the integration onto current main; declare supervisor protocol 1 and exact CI source identity in both manifest and heartbeat |
| Old exact inventory omitted Splines notices present in current client packages | Valid current payloads would be rejected | Manifest generator and archive/installed inventory agree on twelve owned files: three runtime DLLs and nine Cinemachine/Splines notices; test the actual current layout |
| Selection trusted workflow order and version more than ancestry | A later workflow could advertise older or divergent source | Qualify only successful matching-channel runs whose source descends from the proven installed source; reject identical/behind/divergent/quarantined sources and mismatched comparison evidence |
| Failed rollback flowed into ordinary `update_deferred` handling | An unresolved transaction could be followed by another launch or another update | Persist `recovery_required`; gate normal launch, staging and update on journal reconciliation; permit only the matching installed transaction's health check; fault-inject corrupt backups and failed restore renames |
| Failed backup/flush before journal creation left staging history behind | Repeated polling could retain another unused copy each time | Remove never-installed staging while retaining journal-pinned, retired and rejected payload evidence; test an injected backup flush failure without stopping the current game |
| A recovery picture passed the immediate pre-update recheck | Replacement could begin after the staging baseline had ceased showing live footage | Defer that update until a live broadcasting picture is present; preserve the existing session and its normal recovery behavior |
| Sandbox process-query failures could resemble absence, and inventory was session-local | Replacement could proceed without establishing that the copied files were unused | Treat native inspection errors as errors; only documented absence is absence; inventory users of the same copied executable across Windows sessions while retaining exact current-session control authority |
| Descendant inventory checks did not uniformly reject Windows reparse points | A junction could escape the declared owned-directory mapping | Apply the same no-link/no-reparse checks throughout installed inventory and duplicate scanning; include a real Windows junction test |
| Forced cleanup tracked only the RTMP encoder | Diagnostic-capture or timelapse encoders could survive a stopped game | Expose actual live PIDs for all three owned writers; retain verified child identities through empty shutdown reports; terminate only those identities; graceful shutdown waits for all writers |
| Manual startup takeover and supervisor shutdown needed distinct persistent intent | A later callback could restart joining, or a supervisor-directed shutdown could look like a fresh user pause | Preserve manual cancellation in startup policy; canonicalize requested-shutdown status; test cancellation/authorization ordering and exact source identity |
| Download had only per-read and byte bounds | A slow sequence of successful reads could keep staging alive indefinitely | Add an overall deadline and cancellation checks between reads; retain the socket timeout for a blocked read; test timeout/cancellation without changing the running installation |

## Accepted update contract

1. Start with an explicitly selected existing character/source and dedicated Steam endpoint. Keep the intended sandboxed Steam login available. Local configuration and separate private password storage remain operator-owned.
2. Establish the installed baseline through a new supervised launch. Require matching session, PID, plugin bytes, version, source SHA and supervisor protocol, plus fresh observer authorization, live picture and configured output health for the stability interval.
3. Select an eligible descendant from the configured repository/workflow/branch. A version string and a high run number cannot substitute for source ancestry. An unknown baseline source defers qualification.
4. Download and verify the complete archive before downtime. The declared mapping contains only owned DLLs and notices; configurations, saves, shared dependencies and other mods are outside it.
5. Recheck live baseline health, create and verify the backup, persist the transaction, then stop the owned process. No replacement occurs while an instance using the same copied files is present.
6. Swap the payload, verify installed bytes and launch the exact candidate for its health check. Commit acceptance only after stable authorized output. Preserve the local backup independently of remote artifact retention.
7. Restore exact prior bytes on failure and quarantine that candidate source. A failed restore leaves the journal authoritative and blocks further work until recovery succeeds.

The supervisor does not self-update, update the game/dependencies or install server packages. Its example channel is `main`. Client Actions artifacts retain the repository's fourteen-day expiry; this audit does not introduce durable Release assets.

## Verification evidence and its limits

| Layer | What is exercised | What it establishes |
| --- | --- | --- |
| Release fixtures | Current inventory, protocol/source fields, archive paths and links, duplicate entries, payload hashes, size limits, ancestry responses, redirects and cancellation | Artifact rejection/selection behavior on controlled inputs |
| Filesystem/lifecycle fixtures | Actual temporary directory swaps/backups, crash points, corrupt backup, failed rename, rollback, operator pause, stale/replayed heartbeat, exact source, multiple encoder identities | Deterministic transaction and ownership policy; no live game involved |
| Sandbox adapter fixtures | Native ABI contracts, query errors, same executable in another Windows session, process identity/ancestry and path mapping | Adapter logic against controlled native responses; Windows CI additionally exercises actual ACL and junction behavior |
| Real subprocess tests | Temporary executable emits actual heartbeat files; production supervisor launches/stops it, installs real test archives and restores a failed candidate while preserving an unrelated same-named executable | OS process/file lifecycle on a compatible Linux runner; no Steam, Sandboxie or Unity execution |
| Client/Core tests and real-assembly audits | Exact profile selection, bounded startup attempts, persistent manual takeover, source identity and inspected native join/no-save APIs; real FFmpeg writer PID lifecycle | Policy/API and writer ownership evidence, separate from game runtime |
| Valheim on the user's Windows/Sandboxie setup | Cold join, read-only authorization, actual live output/audio, update, rollback, manual intervention and soak | **Not run here** |

The local Python suites pass their executable cases. The real Windows junction case is skipped on this Linux host. Three real-process cases are explicitly skipped in this particular work environment because its reported PIDs and `/proc` refer to different PID namespaces; pretending those cases passed would be misleading. The normal Linux CI job must execute those cases. Windows/Linux supervisor jobs and the client package manifest are now part of repository CI. Use the completed workflow for the delivered source revision as final build evidence; test totals are recorded in its logs.

Source review and a green CI run do not demonstrate that Sandboxie injects the intended client successfully, that Steam is logged into the intended account, that a native character/server join succeeds, or that an NVENC stream survives the entire cycle.

## Windows acceptance sequence

Follow [the setup guide](UNATTENDED-UPDATES.md) using a disposable observer character/profile and a test server first. Keep the ordinary playing client or another unrelated test process running when exercising isolation.

1. Install the CI-built 0.8.48 client and its matching supervisor bundle. Leave `Startup.Enabled=false` while enumerating the exact existing character with `broadcast startup profiles`. Then configure the intended profile/source/server, observer connection, unattended intent and streaming auto-start.
2. Run `check` from the Windows host. Verify the physical sandbox mapping and owned inventory. Before a healthy baseline exists, `requires_first_healthy_supervised_baseline` with no candidate is expected. This result does not claim successful GitHub authentication.
3. Run supervision visibly. Confirm the copied game starts in `ValheimBroadcastTest`, selects the intended character/source, authenticates to the intended test server, receives the read-only role, and produces live picture and audible game audio at the receiver. Record the source/version, launch time and stability time. Confirm the ordinary game, sandboxed Steam and server remain running.
4. Use `status` while supervision is running. To inspect candidate metadata with `check`, stop supervision with Ctrl+C first; this stops its owned client and releases the instance lock. Then run `check` and restart with `run`. The recorded baseline SHA persists, so candidate qualification now uses GitHub's exact branch/run/artifact/ancestry evidence. No eligible descendant is a normal result. During normal `run` polling, rejection or absence must leave the broadcast intact.
5. Exercise one succeeding update on a dedicated test channel with a valid descendant build. Record installed payload hashes before/after, old/new process identities, graceful shutdown duration, rejoin duration and receiver recovery. Verify configuration, saves and other plugins did not change.
6. On that disposable channel, exercise a deliberately failing candidate, then restore/relaunch the healthy prior payload. Verify candidate quarantine, no repeated poll-by-poll install, and no second transaction over a pending journal. Exercise interrupted installation and a removable restore-permission fault separately; verify `recovery_required` prevents launching until reconciliation succeeds. Do not create a bad candidate on the production channel.
7. While RTMP, diagnostic capture and timelapse recording are active, pause and stop supervision. Check that the owned writers exit and an unrelated FFmpeg process continues. Test explicit game logout/quit and manual menu takeover: neither should become an unwanted automatic join/relaunch.
8. Run a multi-hour supervised session with one server outage and one normal update. Inspect receiver video/audio as well as local status and BepInEx logs. Record actual timings and failures; successful process launch alone is not acceptance.

An installation fault that cannot be safely created on the user's active broadcaster should be exercised only on the disposable copy. Preserve the supervisor's state/journal, transaction directory and matching game diagnostics when a case fails; exclude both credential files and unredacted stream URLs from anything shared.
