using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Provide the real assembly_valheim.dll path."); return 2; }
        using var game = AssemblyDefinition.ReadAssembly(args[0]);
        int checks = 0;
        MethodDefinition Method(string type, string name) => game.MainModule.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == name);
        void Check(string name, bool valid)
        { if (!valid) throw new InvalidOperationException(name); checks++; Console.WriteLine("PASS " + name); }
        int Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Count(i => i.Operand is MethodReference m && m.DeclaringType.Name == type && m.Name == name);
        try
        {
            var reference = Method("ZNetPeer", "GetRefPos");
            Check("World delivery position is independent of character identity", reference.Body.Instructions.Count == 3 && reference.Body.Instructions[1].Operand is FieldReference field && field.Name == "m_refPos");
            Check("Player-list peer replacement has one audited site", Method("ZNet", "UpdatePlayerList").Body.Instructions.Count(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == "m_peers") == 1);
            Check("Capacity replacement has one audited site", Calls(Method("ZNet", "RPC_PeerInfo"), "ZNet", "GetNrOfPlayers") == 1);
            Check("Random events use the filtered peer query", Calls(Method("RandEventSystem", "RefreshPlayerEventData"), "ZNet", "GetPeers") == 1);
            Check("World generation uses the filtered peer query", Calls(Method("ZoneSystem", "Update"), "ZNet", "GetPeers") == 1);
            Check("Sleeping depends on character ZDOs", Calls(Method("Game", "EverybodyIsTryingToSleep"), "ZNet", "GetAllCharacterZDOS") == 1);
            Check("Absent character IDs are checked in character discovery", Calls(Method("ZNet", "GetAllCharacterZDOS"), "ZDOID", "IsNone") >= 1);
            Check("Network-view authority uses the guarded ZDO ownership predicate", Calls(Method("ZNetView", "IsOwner"), "ZDO", "IsOwner") == 1);
            Check("Sending objects goes through the filtered sync list", Calls(Method("ZDOMan", "SendZDOs"), "ZDOMan", "CreateSyncList") == 1);
            var writes = Method("ZDOMan", "RPC_ZDOData");
            Check("World-write entry retains its transport sender", writes.Parameters.Count == 2 && writes.Parameters[0].ParameterType.Name == "ZRpc" && writes.Parameters[1].ParameterType.Name == "ZPackage");
            var routed = Method("ZRoutedRpc", "RPC_RoutedRPC");
            Check("Routed entry retains its transport sender", routed.Parameters.Count == 2 && routed.Parameters[0].ParameterType.Name == "ZRpc" && routed.Parameters[1].ParameterType.Name == "ZPackage");
            Check("Peer metadata starts with the interest position", Method("ZNet", "SendServerSyncPlayerData").Body.Instructions.First(i => i.OpCode == OpCodes.Ldfld).Operand is FieldReference position && position.Name == "m_referencePosition");
            var handshake = Method("ZNet", "RPC_PeerInfo").Body.Instructions;
            int auth = Enumerable.Range(0, handshake.Count).First(i => handshake[i].Operand is MethodReference m && m.Name == "VerifySessionTicket");
            int session = Enumerable.Range(0, handshake.Count).First(i => handshake[i].OpCode == OpCodes.Stfld && handshake[i].Operand is FieldReference f && f.DeclaringType.Name == "ZNetPeer" && f.Name == "m_uid");
            Check("Steam ticket verification precedes session publication", auth < session);
            var envelope = game.MainModule.Types.Single(t => t.Name == "ZRoutedRpc").NestedTypes.Single(t => t.Name == "RoutedRPCData").Methods.Single(m => m.Name == "Deserialize");
            string readers = string.Join(",", envelope.Body.Instructions.Where(i => i.Operand is MethodReference m && m.DeclaringType.Name == "ZPackage").Select(i => ((MethodReference)i.Operand).Name));
            Check("Bounded request parser matches the routed envelope", readers == "ReadLong,ReadLong,ReadLong,ReadZDOID,ReadInt,ReadPackage");
            var package = game.MainModule.Types.Single(t => t.Name == "ZPackage").Methods.Single(m => m.Name == "ReadPackage" && m.Parameters.Count == 0);
            Check("Embedded package uses a 32-bit length prefix", package.Body.Instructions.First(i => i.Operand is MethodReference).Operand is MethodReference length && length.Name == "ReadInt32");
            ConnectionRecoveryAudits.Run(game, Check);
            Console.WriteLine($"{checks} real-assembly structural checks passed. This does not run Unity, Harmony patches, or multiplayer.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL hook baseline: " + error.Message); return 1; }
    }
}
