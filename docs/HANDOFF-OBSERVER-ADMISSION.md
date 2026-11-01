# Server-agent handoff: observer admission, 0.8.6

The operator has verified the broadcaster account is already allowlisted. Preserve that configuration. The observed failure is a missing observer reservation after successful native admission; its original timing cause is not yet proved.

## Install under the server's existing maintenance authority

1. Use the **server** artifact from the successful Actions run for the 0.8.6 commit on `fix-player-reacquisition`. Confirm the run's commit and version; do not use a stale 0.8.4/0.8.5 artifact.
2. Record current installed plugin/Core hashes and back up those two DLLs outside the plugin scan path. Preserve all configuration, world and character files.
3. Stop the dedicated server normally within the authorized maintenance window. Replace its `ValheimBroadcaster.Server.dll` and `ValheimBroadcaster.Core.dll` together. Do not install the rendering/client `ValheimBroadcaster.dll` on the dedicated server.
4. Restart and verify the server reports observer server **0.8.6**, with no initialization or Harmony-patching errors. Preserve the existing `[Observer]` settings and allowlist.
5. The user must fully exit the sandboxed broadcaster client and replace its client plugin plus Core with the matching **client** 0.8.6 artifact. This includes the no-save return-to-menu/respawn-storm fix. Ordinary players need no mod update.
6. Perform a fresh broadcaster join. Do not attempt to convert an already-connected ordinary player into an observer.

## Capture this evidence

Keep server and broadcaster logs from the new join, including:

- `Observer admission new-connection`
- `Observer admission before-peer-info`
- `Observer admission after-peer-info`
- `Read-only observer connection reserved; awaiting matching client protocol.`
- Client authorization and subsequent watching state, or the exact new denial.
- Any `Observer admission state cleared`, reservation removal, or `Observer hello denied` lines.

Successful sequence: the role becomes `reserved=True` while `ready=False` at socket registration or before-peer-info; after-peer-info is `ready=True; reserved=True`; a lease is created and client approval follows. Native Steam authentication must still succeed. If a ready peer has no reservation, denial is intentional: report the preceding stage lines rather than bypassing the role lock.

## Acceptance

- Repeated fresh broadcaster joins succeed with the same verified allowlist.
- Normal playing clients continue joining as ordinary peers and do not acquire observer restrictions.
- On a disposable test server, an ordinary admitted connection cannot be promoted by editing its allowlist entry; a fresh join is required.
- Rejected broadcaster admission returns its client to the menu without repeated character spawning/SetCharacterID exceptions, with unattended reconnect both enabled and disabled.
- Removing authorization retains isolation until disconnect; it does not release a live observer into ordinary gameplay.
- Following a walking/sprinting player remains continuous; a distant portal can trigger bounded reacquisition.

The automated tests and compilation do not establish these runtime outcomes. Report actual observed results. Do not delete player/world data or loosen authentication to make a failed test pass. If rollback is necessary, restore the saved server plugin/Core pair together after a normal stop and keep the existing configuration; an older server retains the admission timing limitation.
