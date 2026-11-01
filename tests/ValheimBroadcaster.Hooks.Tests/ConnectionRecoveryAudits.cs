using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class ConnectionRecoveryAudits
{
    internal static void Run(AssemblyDefinition game, Action<string, bool> check)
    {
        MethodDefinition Method(string type, string name) => game.MainModule.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == name);
        int Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.Name == type && m.Name == name);
        var join = Method("FejdStartup", "JoinServer");
        check("Reconnect uses the native privilege check and scene transition", join.Parameters.Count == 0 &&
            Calls(join, "FejdStartup", "VerifyHasMultiplayerPrivilege") == 1 && Calls(join, "FejdStartup", "TransitionToMainScene") == 1);
        var profile = Method("Game", "SetProfile");
        check("Reconnect restores the native profile filename and file source", profile.IsStatic && profile.Parameters.Count == 2 &&
            profile.Body.Instructions.Count(i => i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference f &&
                (f.Name == "m_profileFilename" || f.Name == "m_profileFileSource")) == 2);
        var exit = Method("Game", "ContinueLogout");
        check("Observer recovery has a native shutdown and start-menu transition", exit.Parameters.Count == 3 &&
            exit.Parameters.All(p => p.ParameterType.MetadataType == MetadataType.Boolean) &&
            Calls(exit, "Game", "Shutdown") == 1 && Calls(exit, "SystemResourceManager", "FastLoadScene") == 1);
        var shutdown = Method("Game", "Shutdown");
        var body = shutdown.Body.Instructions;
        int save = Enumerable.Range(0, body.Count).Single(i => body[i].Operand is MethodReference m && m.Name == "SavePlayerProfile");
        check("Native shutdown can skip saving the temporary observer character", save >= 5 && body[save - 5].OpCode == OpCodes.Ldarg_1 &&
            (body[save - 4].OpCode == OpCodes.Brfalse_S || body[save - 4].OpCode == OpCodes.Brfalse));
        var respawn = Method("Game", "UpdateRespawn");
        var spawn = Method("Game", "SpawnPlayer");
        check("Observer admission gate covers the native respawn transaction", respawn.ReturnType.MetadataType == MetadataType.Void &&
            respawn.Parameters.Count == 1 && respawn.Parameters[0].ParameterType.MetadataType == MetadataType.Single &&
            Calls(respawn, "Game", "SpawnPlayer") == 1 &&
            game.MainModule.Types.Single(t => t.Name == "Game").Methods.Where(m => m.HasBody).Sum(m => Calls(m, "Game", "SpawnPlayer")) == 1);
        int allocate = Enumerable.Range(0, spawn.Body.Instructions.Count).Single(i => spawn.Body.Instructions[i].Operand is MethodReference m && m.Name == "Instantiate");
        int characterId = Enumerable.Range(0, spawn.Body.Instructions.Count).Single(i => spawn.Body.Instructions[i].Operand is MethodReference m && m.Name == "SetCharacterID");
        check("Respawn guard must run before character allocation and peer access", allocate < characterId &&
            Calls(Method("ZNet", "SetCharacterID"), "List`1", "get_Item") == 1);
        check("No-save logout argument ordering matches the cleanup adapter", exit.Parameters.Select(p => p.Name).SequenceEqual(new[] { "save", "shouldExit", "changeToStartScene" }));
        var password = Method("ZNet", "OnPasswordEntered");
        check("Reconnect password reuses the native authenticated peer-info flow", password.Parameters.Count == 1 &&
            password.Parameters[0].ParameterType.MetadataType == MetadataType.String && Calls(password, "ZNet", "SendPeerInfo") == 1);
        check("Transport loss is distinct from operator logout", Calls(Method("ZNet", "RPC_Disconnect"), "Game", "Logout") == 0 &&
            Calls(Method("ZNet", "UpdatePeers"), "Game", "Logout") == 0 && Calls(Method("Menu", "Logout"), "Game", "Logout") == 1);
    }
}
