using System;
using System.Linq;
using Mono.Cecil;

// Read metadata/IL only. No game code is loaded or executed. Run against the exact
// client assembly too; CI's dedicated-server baseline cannot certify client behavior.
internal static class Program
{
    private static int checks;
    private static AssemblyDefinition game = null!;
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Pass the path to assembly_valheim.dll");
            using var assembly = AssemblyDefinition.ReadAssembly(args[0]); game = assembly;
            string[] methods = {
                "Game|SpawnPlayer|UnityEngine.Vector3,System.Boolean", "Game|UpdateRespawn|System.Single",
                "Game|RequestRespawn|System.Single,System.Boolean", "Game|SavePlayerProfile|System.Boolean,System.Boolean",
                "Game|Start|", "Game|WaitingForRespawn|", "Game|Logout|System.Boolean,System.Boolean",
                "ZNet|SaveWorld|System.Boolean", "ZNet|SetReferencePosition|UnityEngine.Vector3",
                "ZNet|SendServerSyncPlayerData|ZNetPeer", "ZNet|RPC_PeerInfo|ZRpc,ZPackage", "ZNet|UpdatePlayerList|",
                "ZoneSystem|Update|", "ZoneSystem|CreateGhostZones|UnityEngine.Vector3", "ZoneSystem|IsZoneGenerated|Vector2s",
                "ZNetScene|CreateDestroyObjects|", "ZNetView|Awake|", "ZNetView|ClaimOwnership|",
                "ZDO|IsOwner|", "ZDO|get_Owner|", "ZDO|SetOwner|System.Int64", "ZDO|SetOwnerInternal|System.Int64",
                "ZRoutedRpc|InvokeRoutedRPC|System.Int64,ZDOID,System.String,System.Object[]",
                "ZRpc|Invoke|System.String,System.Object[]", "ZRpc|HandlePackage|ZPackage",
                "ZDOMan|RPC_ZDOData|ZRpc,ZPackage", "ZDOMan|IsInPeerActiveArea|UnityEngine.Vector3,System.Int64",
                "ZDOMan|CreateSyncList|ZDOMan/ZDOPeer,System.Collections.Generic.List`1<ZDO>",
                "ZDOMan|GetPeer|System.Int64", "ZDOMan|ForceSendZDO|System.Int64,ZDOID",
                "ZDOMan|FindSectorObjects|Vector2s,SimulationDistance,System.Collections.Generic.List`1<ZDO>,System.Collections.Generic.List`1<ZDO>",
                "GameCamera|UpdateCamera|System.Single", "GameCamera|UpdateListner|", "GameCamera|UpdateMouseCapture|",
                "Hud|UpdateBlackScreen|Player,System.Single"
            };
            foreach (string contract in methods)
            {
                string[] pieces = contract.Split('|');
                Method(pieces[0], pieces[1], pieces[2]); checks++;
            }
            string[] fields = { "Game:m_requestRespawn", "Game:m_queuedIntro", "Game:m_inIntro", "ZNetScene:m_instances",
                "ZNetView:m_initZDO", "ZDOMan/ZDOPeer:m_peer", "ZDOMan/ZDOPeer:m_zdos", "GameCamera:m_listner", "GameCamera:m_skyCamera", "Hud:m_loadingScreen" };
            foreach (string contract in fields)
            {
                string[] pieces = contract.Split(':');
                Require(Type(pieces[0]).Fields.Any(f => f.Name == pieces[1]), "missing field " + contract);
            }
            var ready = Method("ZNetPeer", "IsReady", "");
            Require(ready.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_uid"), "peer readiness identity");
            Require(!ready.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_characterID"), "peer readiness became character dependent");
            var sync = Method("ZDOMan", "CreateSyncList", "ZDOMan/ZDOPeer,System.Collections.Generic.List`1<ZDO>");
            Require(Calls(sync, "ZNetPeer", "GetRefPos"), "sync selection no longer reads peer reference");
            Require(!sync.Body.Instructions.Any(i => i.Operand is MethodReference m && (m.Name.Contains("SetOwner") || m.DeclaringType.Name == "SpawnSystem")), "sync selection gained simulation side effects");
            Require(Calls(Method("ZNetScene", "CreateDestroyObjects", ""), "ZNet", "GetReferencePosition"), "scene streaming reference changed");
            var dispatch = Method("ZRpc", "HandlePackage", "ZPackage").Body.Instructions;
            var firstCall = dispatch.First(i => i.Operand is MethodReference).Operand as MethodReference;
            Require(firstCall?.DeclaringType.Name == "ZPackage" && firstCall.Name == "ReadInt", "RPC dispatch no longer starts with method hash");
            Require(Calls(Method("ZDO", "GetOwner", ""), "ZDOExtraData", "GetOwner"), "raw owner audit changed");
            Require(Method("ZNet", "GetPlayerList", "").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_players"), "player list no longer returns stored list");
            Require(Type("ZNetScene").Fields.Single(f => f.Name == "m_instances").FieldType.FullName == "System.Collections.Generic.Dictionary`2<ZDO,ZNetView>", "scene audit dictionary changed");
            Console.WriteLine($"{checks} real-assembly contract checks passed. This is not a Unity or multiplayer runtime test.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("CONTRACT FAILURE: " + e.Message); return 1; }
    }
    private static TypeDefinition Type(string name)
    {
        string[] parts = name.Split('/');
        var type = game.MainModule.Types.Single(t => t.Name == parts[0]);
        foreach (string part in parts.Skip(1)) type = type.NestedTypes.Single(t => t.Name == part);
        return type;
    }
    private static MethodDefinition Method(string type, string name, string args) => Type(type).Methods.Single(m =>
        m.Name == name && string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) == args);
    private static bool Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Any(i =>
        i.Operand is MethodReference m && m.DeclaringType.Name == type && m.Name == name);
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
}
