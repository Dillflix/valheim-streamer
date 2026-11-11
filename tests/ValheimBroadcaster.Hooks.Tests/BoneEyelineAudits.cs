using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BoneEyelineAudits
{
    // The bone-eyeline framing (SimpleFollowCamera) measures EVERY vertical span
    // from Character.GetEyePoint(): the eye bone is the single measured quantity
    // and the capsule is fallback-only. Pin the native contract this relies on -
    // a public instance Vector3 reader over the public Transform field m_eye with
    // NO internal null guard (we guard m_eye at every call site) - so a game
    // update that changes the eye source fails CI loudly. This inspects the
    // supplied game assembly; it does not load Unity or run animation.
    internal static void Run(AssemblyDefinition game, Action<string, bool> check)
    {
        var character = game.MainModule.Types.Single(t => t.Name == "Character");
        var eyePoint = character.Methods.Single(m => m.Name == "GetEyePoint");
        check("Eye point is a public instance parameterless Vector3 reader",
            eyePoint.IsPublic && !eyePoint.IsStatic && !eyePoint.HasGenericParameters &&
            eyePoint.Parameters.Count == 0 && eyePoint.ReturnType.FullName == "UnityEngine.Vector3");

        // IL shape: ldarg.0; ldfld m_eye; callvirt get_position; ret - exactly one
        // field read (m_eye) and one Transform.position read, and NO null guard
        // (a guard would add branches; our call sites own the m_eye check).
        var instructions = eyePoint.Body.Instructions;
        check("Eye point reads exactly the m_eye transform position with no internal guard",
            instructions.Count(i => i.OpCode == OpCodes.Ldfld) == 1 &&
            instructions.Single(i => i.OpCode == OpCodes.Ldfld).Operand is FieldReference field &&
            field.Name == "m_eye" &&
            instructions.Count(i => i.Operand is MethodReference m && m.Name == "get_position" &&
                m.DeclaringType.Name == "Transform") == 1 &&
            !instructions.Any(i => i.OpCode.FlowControl == FlowControl.Cond_Branch));

        var eyeField = character.Fields.Single(f => f.Name == "m_eye");
        check("Eye bone field is a public instance Transform (remote-safe, null-guardable)",
            eyeField.IsPublic && !eyeField.IsStatic &&
            eyeField.FieldType.FullName == "UnityEngine.Transform");
    }
}
