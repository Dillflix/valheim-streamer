using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal enum CinematicShotKind { Player, Creature, Scenic }

// Production shot identity and observed/planned inputs. Camera mechanics never
// manufacture a Player or a network entity for a creature or scenic subject.
internal readonly struct CinematicShotInput
{
    internal readonly CinematicShotKind Kind;
    internal readonly TargetRoster.Target? PlayerTarget;
    internal readonly Character? Creature;
    internal readonly ScenicShotPlan? ScenicPlan;
    internal readonly ScenicVisibility? ScenicVisibility;
    internal readonly float Progress;
    internal readonly long Revision;
    internal Character? Actor => Kind == CinematicShotKind.Player ? PlayerTarget?.LoadedPlayer : Creature;
    internal string StableId => Kind == CinematicShotKind.Player ? PlayerTarget?.Identity.Id ?? "" :
        Kind == CinematicShotKind.Creature ? Creature ? Creature.GetZDOID().ToString() : "" : "scenic:" + ScenicPlan?.Id;

    private CinematicShotInput(CinematicShotKind kind, TargetRoster.Target? player, Character? creature,
        ScenicShotPlan? plan, float progress, long revision, ScenicVisibility? visibility = null)
    { Kind = kind; PlayerTarget = player; Creature = creature; ScenicPlan = plan; Progress = progress; Revision = revision; ScenicVisibility = visibility; }

    internal static CinematicShotInput ForPlayer(TargetRoster.Target player, long revision) =>
        new CinematicShotInput(CinematicShotKind.Player, player, null, null, 0, revision);
    internal static CinematicShotInput ForCreature(Character creature, long revision) =>
        new CinematicShotInput(CinematicShotKind.Creature, null, creature, null, 0, revision);
    internal static CinematicShotInput ForScenic(ScenicShotPlan plan, float progress, long revision, ScenicVisibility? visibility = null) =>
        new CinematicShotInput(CinematicShotKind.Scenic, null, null, plan, progress, revision, visibility);
}
