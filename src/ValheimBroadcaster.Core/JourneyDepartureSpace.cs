using System;
using System.Collections.Generic;

namespace ValheimBroadcaster.Core;

/// <summary>A local opening proposal, never a claim of loaded or collision-free
/// space. The adapter measures its surface, body, lens and forward sightline.</summary>
public readonly struct JourneyOpeningProposal
{
    public readonly int Id;
    public readonly AutonomyPosition Position, Heading;
    public readonly double FocusRange;
    public JourneyOpeningProposal(int id, AutonomyPosition position, AutonomyPosition heading, double range)
    { Id = id; Position = position; Heading = heading; FocusRange = range; }
    public AutonomyPosition AimAt(AutonomyPosition measuredPosition) => new AutonomyPosition(
        measuredPosition.X + Heading.X * (float)FocusRange, measuredPosition.Y - 2,
        measuredPosition.Z + Heading.Z * (float)FocusRange);
}

/// <summary>Searches independent departure origins before spending whole-route
/// work. Distinct observed openings survive a rejected corridor; a known bad
/// opening is never repeated under another route alternative. Unknown geometry
/// is deferred, not labelled blocked. This state belongs to one scene/search.</summary>
public sealed class JourneyDepartureSpace
{
    private static readonly (float Ahead, float Side)[] offsets = {
        (0, 0), (0, 8), (0, -8), (8, 0), (-8, 0), (0, 16), (0, -16),
        (12, 12), (12, -12), (-8, 12), (-8, -12), (20, 0)
    };
    public const int CandidateCount = 108, RetainedOpenings = 8, RoutesPerOpening = 3;
    public const double MaximumOriginRelocation = 32;
    private readonly bool[] finished = new bool[CandidateCount];
    private readonly double[] retryAt = new double[CandidateCount];
    private readonly HashSet<string> observed = new HashSet<string>();
    private int cursor;
    private long episodeJourney, episodeEpoch;
    private AutonomyPosition episodeSeed, episodeGoal, episodeCarrier;
    private double episodeStarted, episodeLast;
    private bool episodeBound;
    public int Screened { get; private set; }
    public int Rejected { get; private set; }
    public int Deferred { get; private set; }

    public static JourneyOpeningProposal Propose(int id, AutonomyPosition seed, AutonomyPosition destination, double speed)
    {
        if (id < 0 || id >= CandidateCount) throw new ArgumentOutOfRangeException(nameof(id));
        double range = JourneyRunwayPolicy.GazeReach(speed);
        double x = destination.X - seed.X, z = destination.Z - seed.Z, norm = Math.Sqrt(x * x + z * z);
        if (norm < .001) throw new ArgumentException("A departure needs an outward destination bearing.", nameof(destination));
        x /= norm; z /= norm;
        var offset = offsets[id % offsets.Length];
        // The first comparison batch already spans lateral positions and
        // elevations. Searching every ground-level view first can spend the
        // entire budget beneath one continuous canopy or behind a ridge.
        int block = id / offsets.Length;
        int bearing = (id + block) % 3, level = (id + block / 3) % 3;
        double angle = (bearing == 1 ? 24 : bearing == 2 ? -24 : 0) * Math.PI / 180;
        var position = new AutonomyPosition((float)(seed.X + x * offset.Ahead + z * offset.Side),
            seed.Y + level * 8, (float)(seed.Z + z * offset.Ahead - x * offset.Side));
        var heading = new AutonomyPosition((float)(x * Math.Cos(angle) + z * Math.Sin(angle)), 0,
            (float)(z * Math.Cos(angle) - x * Math.Sin(angle)));
        return new JourneyOpeningProposal(id, position, heading, range);
    }

    public bool TryNext(double now, out int id)
    {
        if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
        for (int n = 0; n < CandidateCount; n++)
        {
            int next = cursor++ % CandidateCount;
            if (finished[next] || now < retryAt[next]) continue;
            id = next; return true;
        }
        id = -1; return false;
    }

    /// <summary>Continue bounded proposal work across preparation batches.
    /// This remembers search history, never loaded space or a safety proof.</summary>
    public bool Bind(long journey, long epoch, AutonomyPosition seed, AutonomyPosition goal,
        AutonomyPosition carrier, double now, bool compatibleLoadingContext = false)
    {
        if (journey <= 0 || epoch < 0 || !Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
        bool same = episodeBound && episodeJourney == journey && episodeEpoch == epoch &&
            episodeSeed.Distance(seed) <= 1 && episodeGoal.Distance(goal) <= 16 &&
            (compatibleLoadingContext || episodeCarrier.Distance(carrier) <= 8) && now >= episodeLast && now - episodeStarted < 120;
        if (!same)
        {
            Reset(); episodeBound = true; episodeJourney = journey; episodeEpoch = epoch;
            episodeSeed = seed; episodeGoal = goal; episodeCarrier = carrier; episodeStarted = now;
        }
        episodeLast = now; return same;
    }

    public void Defer(int id, double now)
    {
        Index(id);
        if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
        retryAt[id] = now + .5; Deferred++;
    }

    public void Complete(int id, bool readable)
    {
        Index(id); if (finished[id]) return;
        finished[id] = true; Screened++; if (!readable) Rejected++;
    }

    /// <summary>Terrain fitting may map distinct probes onto the same opening.
    /// Quantized identity is only a negative-work cache, never a safety proof.</summary>
    public bool RememberMeasured(AutonomyPosition body, AutonomyPosition aim)
        => observed.Add(Key(body) + "/" + Key(aim));

    public bool WasMeasured(AutonomyPosition body, AutonomyPosition aim) => observed.Contains(Key(body) + "/" + Key(aim));

    private static string Key(AutonomyPosition p) => Math.Round(p.X * 4).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
        Math.Round(p.Y * 4).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
        Math.Round(p.Z * 4).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public void Reset()
    {
        Array.Clear(finished, 0, finished.Length); Array.Clear(retryAt, 0, retryAt.Length);
        cursor = Screened = Rejected = Deferred = 0; observed.Clear();
        episodeBound = false; episodeJourney = episodeEpoch = 0; episodeStarted = episodeLast = 0;
    }
    private static void Index(int id) { if (id < 0 || id >= CandidateCount) throw new ArgumentOutOfRangeException(nameof(id)); }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
