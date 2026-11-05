using System;

namespace ValheimBroadcaster.Core;

/// <summary>Operator programme intent, separate from the picture actually on air.</summary>
public enum CoveragePreference { Auto, Scenic, Player, Creature }

public static class CoveragePreferences
{
    public static bool AllowsPlayers(CoveragePreference preference) =>
        preference == CoveragePreference.Auto || preference == CoveragePreference.Player;
    public static bool AllowsCreatures(CoveragePreference preference) =>
        preference == CoveragePreference.Auto || preference == CoveragePreference.Creature;

    public static bool TryParse(string? value, out CoveragePreference preference)
    {
        preference = CoveragePreference.Auto;
        switch (value?.ToLowerInvariant())
        {
            case "auto": return true;
            case "scenic": preference = CoveragePreference.Scenic; return true;
            case "player": preference = CoveragePreference.Player; return true;
            case "creature": preference = CoveragePreference.Creature; return true;
            default: return false;
        }
    }

    public static string Describe(CoveragePreference preference, CoverageMode actual)
    {
        switch (preference)
        {
            case CoveragePreference.Auto: return "Auto: player priority, with scenic and wildlife coverage";
            case CoveragePreference.Scenic: return "Scenic only: players and creatures excluded";
            case CoveragePreference.Player: return actual == CoverageMode.Player
                ? "Player: wildlife cutaways disabled" : "Player requested: awaiting a ready player; Scenic fallback";
            case CoveragePreference.Creature: return actual == CoverageMode.Creature
                ? "Creature: following eligible local wildlife" : "Creature requested: awaiting eligible nearby wildlife; Scenic fallback";
            default: throw new ArgumentOutOfRangeException(nameof(preference));
        }
    }
}
