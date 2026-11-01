using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class CinematicShotDirectorTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Cinematic idle coverage develops motion and chooses fresh clear views", OpenIdle);
        yield return ("Cinematic novelty never abandons the only readable view", OnlyReadable);
        yield return ("Cinematic novelty cannot override physical eligibility", PhysicalPriority);
        yield return ("Cinematic forest corrections settle before discretionary movement", CorrectionSettling);
        yield return ("Cinematic confined scenes retain readable coverage with limited motion", Confined);
        yield return ("Cinematic spacious interiors allow slower deliberate variety", SpaciousInterior);
        yield return ("Cinematic pacing counts usable frames and clears stale comparisons", FootageGap);
        yield return ("Cinematic motion is bounded and previews do not change state", MotionBounds);
        yield return ("Cinematic configured motion strength reaches its bounded upper endpoint", MotionStrength);
        yield return ("Cinematic sustained travel uses less decorative motion than idle", SustainedTravel);
        yield return ("Cinematic combat suppresses decoration without forced editorial cuts", Combat);
        yield return ("Cinematic history avoids immediate return to the preceding view", History);
        yield return ("Cinematic reset starts a new independent subject and clock", Reset);
    }

    private static readonly float[] Clear = { 0.95f, 0.95f, 0.95f, 0.95f };
    private static readonly bool[] All = { true, true, true, true };
    private static int Tick(CinematicShotDirector policy, double time, float[]? scores = null,
        bool[]? eligible = null, CinematicActivity activity = CinematicActivity.Idle,
        float speed = 0, bool confined = false, float space = 1, bool usable = true) =>
        policy.Select(time, 0.1, scores ?? Clear, eligible ?? All, activity, speed, confined, space, usable);
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static float Movement(CinematicShotMotion motion) => Math.Abs(motion.AzimuthOffset) +
        Math.Abs(motion.RadiusScale - 1) * 20 + Math.Abs(motion.HeightOffset);

    private static void OpenIdle()
    {
        var director = new CinematicShotDirector();
        var shown = new HashSet<int>();
        float movement = 0;
        for (int i = 0; i <= 900; i++)
        {
            shown.Add(Tick(director, i * 0.1));
            movement = Math.Max(movement, Movement(director.Motion()));
        }
        Check(shown.Count >= 3, "An unobstructed stationary player remained on one composition.");
        Check(movement > 3, "Clear idle shots never developed deliberate movement.");
        Check(shown.Count <= Clear.Length, "Selected a nonexistent candidate.");
    }

    private static void OnlyReadable()
    {
        var director = new CinematicShotDirector();
        for (int i = 0; i <= 1500; i++)
            Check(Tick(director, i * 0.1, new[] { 0.9f, 0.25f, 0.2f, 0.1f }) == 0,
                "A novelty timer chose obscured footage instead of the sole readable shot.");
    }

    private static void PhysicalPriority()
    {
        var director = new CinematicShotDirector();
        Check(Tick(director, 0) == 0, "Initial selection changed.");
        Check(Tick(director, 0.01, eligible: new[] { false, true, true, true }) == 1,
            "Shot dwell retained physically invalid geometry.");
        Check(Tick(director, 0.02, eligible: new[] { false, false, false, false }) == -1,
            "Novelty invented a physically eligible view.");
        Check(Tick(director, 0.03, new[] { 0.03f, 0.02f, 0.01f, 0f }) == 0,
            "Poor foliage scores declared a physically valid scene unavailable.");
    }

    private static void CorrectionSettling()
    {
        var director = new CinematicShotDirector();
        for (int i = 0; i <= 80; i++) Tick(director, i * 0.1);
        director.NotifyCorrection(8);
        CinematicShotMotion frozen = director.Motion();
        for (int i = 81; i < 130; i++)
        {
            Check(Tick(director, i * 0.1) == 0, "Director added an editorial cut while settling.");
            Check(Math.Abs(director.Motion().AzimuthOffset - frozen.AzimuthOffset) < 0.0001,
                "Deliberate motion continued immediately after a blocked path.");
        }
        director.NotifyCorrection(13);
        director.NotifyCorrection(13.1);
        director.NotifyCorrection(13.6);
        Check(director.RecentCorrections == 3, "Repeated render-frame reports inflated correction count.");
        Check(director.ShotAge == 0, "Corrections did not count as fresh camera activity.");
        for (int i = 137; i < 180; i++) Tick(director, i * 0.1);
        Check(director.Reason.Contains("Settling"), "Repeated corrections did not quiet the director.");
    }

    private static void Confined()
    {
        var director = new CinematicShotDirector();
        float maximum = 0;
        for (int i = 0; i <= 1200; i++)
        {
            Check(Tick(director, i * 0.1, confined: true, space: 0.3f) == 0,
                "Confined space triggered an unnecessary timed cut.");
            maximum = Math.Max(maximum, Movement(director.Motion()));
        }
        Check(maximum < 0.3, "Confined camera attempted a substantial arc.");
    }

    private static void FootageGap()
    {
        var director = new CinematicShotDirector();
        for (int i = 0; i <= 60; i++) Tick(director, i * 0.1);
        double age = director.ShotAge;
        for (int i = 61; i <= 500; i++) Tick(director, i * 0.1, usable: false);
        Check(director.ShotAge == age && director.SelectedIndex == 0,
            "Unavailable footage exhausted the shot timer.");
        Tick(director, 70);
        Check(director.ShotAge == age, "A large frame gap was counted as usable footage.");
        Tick(director, 70.1);
        Check(director.ShotAge > age && director.ShotAge < age + 0.2,
            "Pacing failed to resume from actual footage.");
    }

    private static void SpaciousInterior()
    {
        var director = new CinematicShotDirector();
        var shown = new HashSet<int>();
        int firstChange = -1;
        for (int i = 0; i <= 1200; i++)
        {
            int selected = Tick(director, i * 0.1, confined: true, space: 0.9f);
            shown.Add(selected);
            if (selected != 0 && firstChange < 0) firstChange = i;
        }
        Check(shown.Count >= 3, "A spacious interior suppressed camera variety indefinitely.");
        Check(firstChange >= 270, "An interior used the same quick pacing as open ground.");
    }

    private static void MotionBounds()
    {
        var director = new CinematicShotDirector();
        for (int i = 0; i <= 2400; i++)
        {
            Tick(director, i * 0.1);
            CinematicShotMotion before = director.Motion();
            CinematicShotMotion preview = director.Motion(1, 1000);
            CinematicShotMotion after = director.Motion();
            foreach (CinematicShotMotion value in new[] { before, preview })
                Check(Math.Abs(value.AzimuthOffset) <= 14.001 && value.RadiusScale >= 0.879 &&
                    value.RadiusScale <= 1.141 && Math.Abs(value.HeightOffset) <= 1.101,
                    "Shot motion exceeded its bounded envelope.");
            Check(before.AzimuthOffset == after.AzimuthOffset && before.RadiusScale == after.RadiusScale,
                "Preview advanced live camera state.");
            Check(Movement(director.Motion(0)) == 0, "Motion disable did not produce a neutral intent.");
        }
    }

    private static void SustainedTravel()
    {
        var idle = new CinematicShotDirector();
        var travel = new CinematicShotDirector();
        for (int i = 0; i <= 100; i++)
        {
            Tick(idle, i * 0.1);
            Tick(travel, i * 0.1, activity: CinematicActivity.Travel, speed: 8);
        }
        Check(Movement(travel.Motion()) < Movement(idle.Motion()) * 0.4,
            "Sustained sprinting retained idle camera decoration.");
        Check(travel.SelectedIndex == 0, "Movement classification forced an early viewpoint change.");
    }

    private static void MotionStrength()
    {
        var director = new CinematicShotDirector();
        for (int i = 0; i <= 140; i++) Tick(director, i * 0.1);
        float normal = Movement(director.Motion(1));
        CinematicShotMotion maximum = director.Motion(1.5f);
        Check(normal > 1 && Math.Abs(Movement(maximum) - normal * 1.5f) < 0.001,
            "The configured1.5 motion endpoint was silently clamped to1.");
        Check(Math.Abs(maximum.AzimuthOffset) <= 21.001 && maximum.RadiusScale >= 0.819 &&
            maximum.RadiusScale <= 1.211 && Math.Abs(maximum.HeightOffset) <= 1.651,
            "The maximum strength escaped its finite movement envelope.");
        Check(Math.Abs(Movement(director.Motion(100)) - Movement(maximum)) < 0.001,
            "External strength values bypassed the maximum motion limit.");
    }

    private static void Combat()
    {
        var director = new CinematicShotDirector();
        for (int i = 0; i <= 100; i++) Tick(director, i * 0.1);
        for (int i = 101; i <= 700; i++)
            Check(Tick(director, i * 0.1, activity: CinematicActivity.Combat) == 0,
                "A duration timer interrupted readable combat coverage.");
        Check(Movement(director.Motion()) < 0.01, "Combat failed to settle decorative movement.");
        Tick(director, 70.1, eligible: new[] { false, true, true, true }, activity: CinematicActivity.Combat);
        Check(director.SelectedIndex != 0, "Combat preference prevented necessary physical correction.");
    }

    private static void History()
    {
        var director = new CinematicShotDirector();
        var sequence = new List<int>();
        for (int i = 0; i <= 1000; i++)
        {
            int index = Tick(director, i * 0.1);
            if (sequence.Count == 0 || sequence[sequence.Count - 1] != index) sequence.Add(index);
        }
        Check(sequence.Count >= 4, "No meaningful shot history developed.");
        for (int i = 2; i < sequence.Count; i++)
            Check(sequence[i] != sequence[i - 2], "Director immediately returned to a recently shown view.");
    }

    private static void Reset()
    {
        var director = new CinematicShotDirector();
        for (int i = 0; i <= 400; i++) Tick(director, i * 0.1);
        director.NotifyCorrection(40);
        director.Reset();
        Check(director.SelectedIndex == -1 && director.RecentCorrections == 0 && director.ShotAge == 0,
            "Reset retained state belonging to a previous player/world.");
        Check(Tick(director, 0) == 0 && Movement(director.Motion()) == 0,
            "New target did not establish an independent camera state.");
    }
}
