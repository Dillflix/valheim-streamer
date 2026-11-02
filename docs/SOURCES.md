# Implementation evidence

These references explain the design and API investigation. They are not a claim that a third-party mod supports the installed Valheim version, and this project does not copy their implementations. Verify signatures against the target game's actual assemblies.

| Reference | Relevant evidence |
| --- | --- |
| [Jötunn Mod Example](https://github.com/Valheim-Modding/JotunnModExample) | Framework examples and development setup |
| [Jötunn Mod Stub](https://github.com/Valheim-Modding/JotunnModStub) | Template build conventions; inspect build hooks rather than inherit automatic deployment |
| [Jötunn console commands](https://valheim-modding.github.io/Jotunn/tutorials/console-commands.html) | `CommandManager` and `ConsoleCommand` integration |
| [Jötunn network compatibility](https://valheim-modding.github.io/Jotunn/tutorials/networkcompatibility.html) | `NotEnforced` does not require ordinary clients/server to install this camera mod |
| [Jötunn headless predicate](https://valheim-modding.github.io/Jotunn/api/Jotunn.Managers.GUIManager.IsHeadless.html) | Guard graphical initialization |
| [Official Jötunn ListPlayersCommand](https://github.com/Valheim-Modding/Jotunn/blob/f4bb47121f1685712b2bcf1279b61fe6de996074/TestMod/ConsoleCommands/ListPlayersCommand.cs) | Instantiated players and names |
| [BuildCamera camera patch](https://github.com/gittywithexcitement/ValheimBuildCamera/blob/07bba3fc039610802d8b91e5a35683f8b8f89402/BuildCamera.cs) | `GameCamera.UpdateCamera(float)` patch target |
| [EpicLoot camera patch](https://github.com/RandyKnapp/ValheimMods/blob/79498d09d56e6d14be3c20ac8d2083edf34bd76d/EpicLoot/src/GamePatches/GameCamera_Patch.cs) | Independent use of the camera update patch point |
| [CameraMan](https://github.com/Oppodelldog/CameraMan) | Jötunn-based detached-camera precedent and distant-travel limitation |
| [ServerDevcommands player adapter](https://github.com/JereKuusela/valheim-dev/blob/73945fb05372913eefbaa7063a2f14a65fefaab0/ServerDevcommands/Service/PlayerInfo.cs) | Network roster, character ZDO IDs, and instantiated-player matching |
| [ServerDevcommands position delivery](https://github.com/JereKuusela/valheim-dev/blob/73945fb05372913eefbaa7063a2f14a65fefaab0/ServerDevcommands/Features/MinimapPrivatePlayers.cs) | Why private distant positions can require a server companion; no such relay is implemented here |
| [MistWorms reference-position patch](https://github.com/clementplancon/MistWorms/blob/fb552cc413b0c165f75567729be1bd5077eb4cd3/src/MistWorms/Patches/OwnerPatches.cs) | Observed `ZNet.SetReferencePosition(Vector3)` interception |
| [OttoBifrost destination synchronization](https://github.com/potto007/OttoBifrost/blob/e771be28a820994534e469ae42b3c2b04819a263/Patches/DestinationSync.cs) | Interest-position delivery and the distinction between local readiness and complete server object delivery |
| [Official Valheim dedicated-server guide](https://www.valheimgame.com/support/a-guide-to-dedicated-servers) | Supported server setup and Steam/crossplay distinction |
| [Unity OnRenderImage](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnRenderImage.html), [Graphics.Blit](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.Blit.html) and [AsyncGPUReadback](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.html) | Clean post-effects camera composition, regular-texture copies and bounded asynchronous readback; signatures checked against matching Unity assemblies |
| [Unity audio filter callback](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) | Interleaved samples and audio-thread constraints |
| [Unity WebRTC audio filter](https://github.com/Unity-Technologies/com.unity.webrtc/blob/main/Runtime/Scripts/AudioCustomFilter.cs) and [audio track](https://github.com/Unity-Technologies/com.unity.webrtc/blob/main/Runtime/Scripts/AudioStreamTrack.cs) | Official listener/source tap pattern; our filter preserves local playback |
| [FFmpeg protocols](https://ffmpeg.org/ffmpeg-protocols.html) and [formats](https://ffmpeg.org/ffmpeg-formats.html) | Pipe/TCP inputs, RTMP/RTMPS, network timeouts, rawvideo/f32le and FLV output |

The previously prepared Jötunn documentation toolkit contains the full Markdown snapshot. Set `JOTUNN_DOCS_ROOT` to its `docs` directory for local agent retrieval. Its published-site commit is `18a0b624ceaf757abcfeb5cb1386d111a6dec9b2`; that identifier is not a Jötunn package version. API/runtime evidence from target assemblies takes precedence when the snapshot differs.

The companion skills are `jotunn-reference`, `jotunn-create-mod`, `jotunn-maintain-mod`, and `jotunn-deploy-mod`. They remain separate reusable tools; this repository does not duplicate the entire corpus or embed installation-specific skill paths.

## 0.7.0 observer and continuity evidence

The server/client isolation hooks were inspected against the pinned real Valheim 1.0.21 assembly. `tests/ValheimBroadcaster.Hooks.Tests` checks critical metadata/IL assumptions in CI; it does not load or execute the game. The checked-in code contains our adapters/audits, not game implementation dumps or game binaries.

The Jötunn snapshot used for compatibility/RPC/config guidance is site commit `18a0b624ceaf757abcfeb5cb1386d111a6dec9b2`, captured 2026-10-30. Both owned plugins use NotEnforced compatibility to preserve ordinary clients; protocol 1 explicitly authorizes only broadcaster/server pairs. Raw per-connection ZRpc messages carry small integers and bind replies to the actual server RPC.

Earlier output used end-of-frame screenshot capture. As of 0.8.19, the clean program is copied from the existing `OnRenderImage` compositor before overlay UI, and the unused screenshot helper has been removed. Live and held pictures follow the same regular-texture blit path; program/image-analysis readbacks use their own bounded buffers. No second world render is added. Actual backend orientation and third-party rendering compatibility remain runtime checks.
