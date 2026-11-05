using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class WaterClearanceAudits
{
    // Verify only the native contracts on which our conservative envelope
    // relies. This inspects the supplied game; it does not reimplement its
    // waves, load Unity, or claim to execute the native depth delegate in-game.
    internal static void Run(AssemblyDefinition game, Action<string, bool> check)
    {
        TypeDefinition Type(string name) => game.MainModule.Types.Single(t => t.Name == name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        var water = Type("WaterVolume");
        var depth = Method("WaterVolume", "Depth");
        check("Stable water depth reader retains its open-instance Vector3-to-float delegate signature",
            !depth.IsStatic && !depth.HasGenericParameters && depth.ReturnType.MetadataType == MetadataType.Single &&
            depth.Parameters.Count == 1 && depth.Parameters[0].ParameterType.FullName == "UnityEngine.Vector3");

        var surface = Method("WaterVolume", "GetWaterSurface");
        check("Loaded water exposes its base offset global-wave flag and live surface contract", surface.IsPublic &&
            !surface.IsStatic && surface.ReturnType.MetadataType == MetadataType.Single &&
            surface.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "UnityEngine.Vector3", "System.Single" }) &&
            water.Fields.Any(f => f.IsPublic && f.Name == "m_surfaceOffset" && f.FieldType.MetadataType == MetadataType.Single) &&
            water.Fields.Any(f => f.IsPublic && f.Name == "m_useGlobalWind" && f.FieldType.MetadataType == MetadataType.Boolean) &&
            Calls(surface, "WaterVolume", "Depth") == 1);

        var wind = Method("EnvMan", "GetWindData");
        check("Water wind snapshot exposes both wind states and a clamped transition", wind.IsPublic && !wind.IsStatic &&
            wind.ReturnType.MetadataType == MetadataType.Void && wind.Parameters.Select(p => p.ParameterType.FullName)
                .SequenceEqual(new[] { "UnityEngine.Vector4&", "UnityEngine.Vector4&", "System.Single&" }) &&
            Calls(wind, "Mathf", "Clamp01") == 1 && Calls(Method("WaterVolume", "StaticUpdate"), "EnvMan", "GetWindData") == 1);

        var troch = Method("WaterVolume", "TrochSin").Body.Instructions;
        var tail = troch.Skip(troch.Count - 6).ToArray();
        check("Native wave phase stays within zero and one", tail.Length == 6 &&
            Call(tail[0], "Mathf", "Sin") && Scalar(tail[1], .5) && tail[2].OpCode == OpCodes.Mul &&
            Scalar(tail[3], .5) && tail[4].OpCode == OpCodes.Add && tail[5].OpCode == OpCodes.Ret);

        var create = Method("WaterVolume", "CreateWave");
        tail = create.Body.Instructions.Skip(create.Body.Instructions.Count - 7).ToArray();
        check("Each native wave contributes at most 0.8 times its positive amplitude", Calls(create, "WaterVolume", "TrochSin") == 2 &&
            tail.Length == 7 && Call(tail[0], "WaterVolume", "TrochSin") && tail[1].OpCode == OpCodes.Mul &&
            Scalar(tail[2], .2) && tail[3].OpCode == OpCodes.Sub && Argument(tail[4], "waveHeight") &&
            tail[5].OpCode == OpCodes.Mul && tail[6].OpCode == OpCodes.Ret);

        var calc = water.Methods.Single(m => m.Name == "CalcWave" && m.Parameters.Count == 6);
        var instructions = calc.Body.Instructions;
        var factors = instructions.Where(i => Argument(i, "waveFactor") || Argument(i, "waveFactorBig")).ToArray();
        bool boundedAmplitudes = factors.Length == 10 && factors.All(i => i.Previous.OpCode == OpCodes.Ldc_R4 &&
            Convert.ToDouble(i.Previous.Operand) > 0 && i.Next.OpCode == OpCodes.Mul);
        double total = boundedAmplitudes ? factors.Sum(i => Convert.ToDouble(i.Previous.Operand)) : double.NaN;
        check("Ten positive native wave amplitudes retain the 18.64 metre full-depth crest bound", boundedAmplitudes &&
            Calls(calc, "WaterVolume", "CreateWave") == 10 && Math.Abs(total * .8 - 18.64) < .00001 &&
            instructions.Skip(instructions.Count - 21).Count(i => i.OpCode == OpCodes.Add) == 9 &&
            instructions[instructions.Count - 2].OpCode == OpCodes.Mul);

        var lerp = instructions.Single(i => Call(i, "Mathf", "Lerp"));
        check("Native wave amplitude scales by clamped depth and wind strength", lerp.Previous.OpCode == OpCodes.Ldarg_2 &&
            lerp.Previous.Previous.OpCode == OpCodes.Ldloc_0 && Scalar(lerp.Previous.Previous.Previous, 0) &&
            instructions.Any(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f &&
                f.DeclaringType.FullName == "UnityEngine.Vector4" && f.Name == "w" && i.Next.OpCode == OpCodes.Stloc_0));

        var blend = water.Methods.Single(m => m.Name == "CalcWave" && m.Parameters.Count == 5);
        var fade = Method("WorldGenerator", "DeepNorthWaveFade");
        check("Wind blending and northern attenuation do not amplify the crest envelope", Calls(blend, "Mathf", "LerpUnclamped") == 1 &&
            blend.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f && f.Name == "s_globalWindAlpha") &&
            Calls(surface, "WorldGenerator", "DeepNorthWaveFade") == 1 &&
            Call(fade.Body.Instructions[fade.Body.Instructions.Count - 2], "DUtils", "Clamp01"));
    }

    private static bool Scalar(Instruction instruction, double value) => instruction.OpCode == OpCodes.Ldc_R4 &&
        Math.Abs(Convert.ToDouble(instruction.Operand) - value) < .000001;
    private static bool Argument(Instruction instruction, string name) =>
        (instruction.OpCode == OpCodes.Ldarg || instruction.OpCode == OpCodes.Ldarg_S) &&
        instruction.Operand is ParameterDefinition parameter && parameter.Name == name;
    private static bool Call(Instruction instruction, string type, string name) =>
        instruction.Operand is MethodReference method && method.DeclaringType.Name == type && method.Name == name;
    private static int Calls(MethodDefinition method, string type, string name) =>
        method.Body.Instructions.Count(i => Call(i, type, name));
}
