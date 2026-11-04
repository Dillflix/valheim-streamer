using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ActorOcclusionTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("A foreground body blocks a sample on the player behind it", Foreground);
        yield return ("Bodies behind the target or camera cannot obscure the target", Behind);
        yield return ("Nearby off-axis bodies do not become opaque bounding boxes", OffAxis);
        yield return ("Subject identity prevents self occlusion at every sampled point", Self);
        yield return ("Unidentified scene points do not acquire invented actor occlusion", Identityless);
        yield return ("A moving body immediately clears its previous sightline", Movement);
        yield return ("A large measured body obscures more samples without using wing bounds", LargeBody);
        yield return ("Capsule end caps and spherical bodies are recognized", EndCaps);
        yield return ("A camera inside another visible body is visually obstructed", Inside);
        yield return ("Rotated and parallel body capsules retain finite segment behavior", Rotated);
        yield return ("Invalid actor body dimensions do not manufacture visibility evidence", Invalid);
        yield return ("Finite body visibility remains translation invariant in dungeon coordinates", Translation);
        yield return ("Dense bodies combine by blocked samples rather than double-counting opacity", DenseBodySamples);
    }
    private static AutonomyPosition P(float x, float y, float z) => new AutonomyPosition(x, y, z);
    private static ActorBodyCapsule Body(float x = 0, float z = 5, double radius = .5, int id = 2) =>
        new ActorBodyCapsule(id, P(x, .5f, z), P(x, 1.5f, z), radius);
    private static bool Blocks(ActorBodyCapsule body, float y = 1, float x = 0, int subject = 1) =>
        ActorOcclusionGeometry.Occludes(P(0, y, 0), P(x, y, 10), subject, body);
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }

    private static void Foreground() => Check(Blocks(Body()), "Foreground actor was ignored.");
    private static void Behind() => Check(!Blocks(Body(z: 12)) && !Blocks(Body(z: -2)), "Infinite ray treated a rear actor as foreground.");
    private static void OffAxis() => Check(!Blocks(Body(x: 1)) && Blocks(Body(x: .2f)), "A body AABB or wrong axis blocked the neighboring open sightline.");
    private static void Self()
    {
        for (int y = 0; y <= 20; y++) Check(!Blocks(Body(id: 1), y * .1f), "Actor blocked its own body sample.");
        Check(Blocks(Body(id: 2)), "Self exclusion hid unrelated actors.");
    }
    private static void Identityless() => Check(!Blocks(Body(), subject: 0) && !Blocks(Body(id: 0)), "Unidentified object inherited actor visibility rules.");
    private static void Movement() => Check(Blocks(Body(x: 0)) && !Blocks(Body(x: 2)) && Blocks(Body(x: 0)), "Fresh position did not replace prior occlusion.");
    private static void LargeBody()
    {
        var troll = new ActorBodyCapsule(2, P(0, 1, 5), P(0, 4, 5), 1);
        Check(Blocks(troll, 3) && !Blocks(troll, 6), "Large body silhouette was ignored or inflated above its actual body.");
        Check(!Blocks(Body(radius: .4), 1, 2), "A notional wide wing envelope blocked a ray outside the body.");
    }
    private static void EndCaps()
    {
        Check(Blocks(Body(), 1.9f) && !Blocks(Body(), 2.1f), "Capsule's rounded top was lost or expanded.");
        var sphere = new ActorBodyCapsule(2, P(0, 1, 5), P(0, 1, 5), .5);
        Check(Blocks(sphere) && !Blocks(sphere, 2), "Degenerate capsule failed spherical visibility.");
    }
    private static void Inside() => Check(ActorOcclusionGeometry.Occludes(P(0, 1, 5), P(0, 1, 10), 1, Body()), "Camera inside actor was incorrectly reported unobscured.");
    private static void Rotated()
    {
        var horizontal = new ActorBodyCapsule(2, P(-2, 1, 5), P(2, 1, 5), .5);
        var parallel = new ActorBodyCapsule(2, P(.2f, 1, 2), P(.2f, 1, 7), .5);
        Check(Blocks(horizontal) && Blocks(parallel), "Rotated/parallel capsule did not intersect its crossing sample.");
        Check(!Blocks(new ActorBodyCapsule(2, P(1, 1, 2), P(1, 1, 7), .5)), "Parallel open sightline incorrectly blocked.");
    }
    private static void Invalid()
    {
        foreach (double radius in new[] { 0d, -1, double.NaN, double.PositiveInfinity, 33 })
            Check(!Blocks(Body(radius: radius)), "Invalid radius was treated as actual opacity.");
        Check(!ActorOcclusionGeometry.Occludes(P(0, 1, 0), P(0, 1, 0), 1, Body()), "Zero-length sample blocked.");
    }
    private static void Translation()
    {
        var translated = new ActorBodyCapsule(2, P(8000, 5000.5f, -7995), P(8000, 5001.5f, -7995), .5);
        Check(ActorOcclusionGeometry.Occludes(P(8000, 5001, -8000), P(8000, 5001, -7990), 1, translated), "Dungeon coordinates changed relative visibility.");
    }
    private static void DenseBodySamples()
    {
        var a = Body(); var b = Body(z: 6, id: 3);
        int seen = 0;
        for (int i = -2; i <= 2; i++)
            if (!Blocks(a, 1, i * 2) && !Blocks(b, 1, i * 2)) seen++;
        Check(seen == 4, "Overlapping foreground bodies erased independent clear samples.");
    }
}
