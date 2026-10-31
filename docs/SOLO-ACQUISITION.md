# Distant scene acquisition with only the broadcaster

0.5.0 adds an explicit no-teleport scene probe. It tests real network-reference loading and camera rendering without inventing another player. Use a disposable world/character because the observer remains a normal simulated player.

1. Enable Valheim's console using `-console` in the launch arguments used by your Thunderstore profile.
2. Visit an identifiable location (terrain, structures and creatures you can compare later). Run `broadcast probe mark` in F5. This saves your position only for this world session; it creates no object.
3. Travel away normally. Several hundred metres is a useful starting point. You must be at least 200 metres away **and the destination must no longer be area-ready**. The start command refuses a destination that is still loaded, preventing a false acquisition pass.
4. Park somewhere suitable and run `broadcast probe start`. It starts normal broadcasting if needed, then explicitly overrides network interest to the marked position. You do not need to enable `RemoteTargets`; this command authorizes only the bounded test. It never teleports you. Exit F10 self-follow first.
5. Close F5. A labeled recovery view appears while acquiring; once destination and intended camera area are ready, a labeled shot of the marked location appears. No fake player is required. If desired, start F11 before the probe while normal coverage is active to record the entire transition on the same RTMP connection.
6. Run `broadcast probe status`: record state, elapsed time, first-ready time, destination distance, and observer displacement. Compare the rendered scene with what you saw when marking it. Terrain, buildings, creatures and effects need visual validation; readiness is not proof of complete delivery. Displacement is measured because physics may move the ordinary player even though the mod does not teleport it.
7. Run `broadcast probe stop` to restore normal interest and resume coverage without restarting RTMP. Acquisition times out after 30 seconds continuously unready, and every test ends after 120 seconds total. F8 stops the entire broadcast. Allow the observer's area to reload after restoration.

Repeat against the same unloaded marker to check reproducibility. A world change invalidates the marker. This test does not establish remote-player discovery/replication, private-position availability, teleport travel, or harmless observer simulation. Automatic teleport travel is still unimplemented.
