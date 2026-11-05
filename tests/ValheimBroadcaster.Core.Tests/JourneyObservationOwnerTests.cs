using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ValheimBroadcaster.Core;

internal static class JourneyObservationOwnerTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Pending Journey observations retain independent projection owners across interleaved work", Interleaved);
        yield return ("Journey projection capacity waits rather than evicting unfinished geometry", Capacity);
        yield return ("Journey endpoint and midpoint cannot silently recenter one unfinished query", PhaseChange);
        yield return ("Completed Journey observations reuse nearby cached projections across query identities", CompletedReuse);
        yield return ("Journey observation epochs and graph resets invalidate old lease identities", ResetIdentity);
        yield return ("Long Journey observation episodes reuse a bounded set while keeping pending owners pinned", BoundedReuse);
        yield return ("Journey observation ownership rejects invalid request identities and capacities", Invalid);
        yield return ("Recorded 0.8.51 interleaved requests and wrong-centre commits cannot retarget pending projection owners", RecordedRequests);
    }

    private static AutonomyPosition P(float x, float y = 0, float z = 0) => new AutonomyPosition(x, y, z);
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static JourneyObservationLease Acquire(JourneyObservationOwners owners, long token, AutonomyPosition camera, long epoch = 3)
    {
        Check(owners.TryAcquire(token, epoch, camera, out var lease, out _), owners.Reason);
        return lease;
    }

    private static void Interleaved()
    {
        var owners = new JourneyObservationOwners(9); var leases = new JourneyObservationLease[8];
        for (int i = 0; i < leases.Length; i++) leases[i] = Acquire(owners, i + 1, P(i * 8, i % 3 * 4));
        for (int round = 0; round < 100; round++)
        for (int i = leases.Length - 1; i >= 0; i--)
        {
            Check(owners.TryAcquire(i + 1, 3, leases[i].Camera, out var again, out bool reset), owners.Reason);
            Check(!reset && again.Generation == leases[i].Generation && again.Slot == leases[i].Slot && owners.IsCurrent(leases[i]),
                "Another pending query displaced or restarted a projection");
        }
        var slots = new HashSet<int>(); foreach (var lease in leases) slots.Add(lease.Slot);
        Check(slots.Count == 8 && owners.ActiveCount == 8, "Pending graph probes shared a projection slot");
    }

    private static void Capacity()
    {
        var owners = new JourneyObservationOwners(2);
        var first = Acquire(owners, 1, P(0)); var second = Acquire(owners, 2, P(20));
        Check(!owners.TryAcquire(3, 3, P(40), out _, out _) && owners.ActiveCount == 2 &&
            owners.IsCurrent(first) && owners.IsCurrent(second), "Capacity pressure evicted unfinished evidence");
        Check(owners.Release(first), "Known result did not release its owner");
        var third = Acquire(owners, 3, P(40));
        Check(third.Slot == first.Slot && third.Generation != first.Generation && owners.IsCurrent(second),
            "A released slot could not be reused without disturbing the other pending observation");
    }

    private static void PhaseChange()
    {
        var owners = new JourneyObservationOwners(2);
        var endpoint = Acquire(owners, 146, P(16)); var other = Acquire(owners, 147, P(-16));
        Check(!owners.TryAcquire(146, 3, P(8), out _, out _) && owners.IsCurrent(endpoint),
            "An unresolved endpoint was recentered onto the midpoint");
        owners.Release(endpoint);
        Check(owners.TryAcquire(146, 3, P(8), out var midpoint, out bool reset) && !reset &&
            midpoint.Generation != endpoint.Generation && owners.IsCurrent(other), "The known endpoint did not release safely for midpoint work");
        Check(!owners.Release(endpoint) && owners.IsCurrent(midpoint), "A late endpoint release discarded its newer midpoint owner");
    }

    private static void CompletedReuse()
    {
        var owners = new JourneyObservationOwners(2);
        var first = Acquire(owners, 1, P(0)); var second = Acquire(owners, 2, P(10));
        owners.Release(first); owners.Release(second);
        Check(owners.TryAcquire(1, 3, P(0), out var same, out bool reset) && !reset && same.Slot == first.Slot &&
            same.Generation != first.Generation, "A matching completed query lost its reusable projection");
        owners.Release(same);
        Check(owners.TryAcquire(3, 3, P(9), out var third, out reset) && !reset && third.Slot == second.Slot,
            "A nearby new query discarded or bypassed a reusable completed spatial cache");
        Check(!owners.Release(second) && owners.IsCurrent(third), "An old completed query released its replacement");
        owners.Release(third);
        // Equidistant idle observations prefer the older use; neither result
        // means the cache has proved the new camera's actual geometry.
        Check(owners.TryAcquire(4, 3, P(4.5f), out var tied, out reset) && !reset && tied.Slot == first.Slot,
            "An equal-distance cache selection did not use bounded LRU order");
    }

    private static void ResetIdentity()
    {
        var owners = new JourneyObservationOwners(2);
        var old = Acquire(owners, 1, P(0)); Acquire(owners, 2, P(10));
        var changed = Acquire(owners, 1, P(0), 4);
        Check(owners.ActiveCount == 1 && changed.Generation != old.Generation && !owners.IsCurrent(old),
            "Scene replacement retained a previous scene's pending proof");
        owners.Reset(); var nextGraph = Acquire(owners, 1, P(0), 4);
        Check(nextGraph.Generation != changed.Generation && !owners.Release(changed),
            "Reset reused a stale lease generation in the new graph");
    }

    private static void BoundedReuse()
    {
        var owners = new JourneyObservationOwners(9); var held = new JourneyObservationLease[8];
        for (int i = 0; i < held.Length; i++) held[i] = Acquire(owners, i + 1, P(i));
        for (int i = 10; i < 1010; i++)
        {
            var transient = Acquire(owners, i, P(i % 64));
            Check(transient.Slot >= 0 && transient.Slot < 9 && owners.ActiveCount == 9, "Projection capacity grew during repeated edges");
            owners.Release(transient);
            foreach (var pending in held) Check(owners.IsCurrent(pending), "A pending observation was evicted by completed edge churn");
        }
        Check(owners.Capacity == 9 && owners.ActiveCount == 8, "Projection owner storage escaped its configured bound");
    }

    private static void Invalid()
    {
        foreach (int capacity in new[] { 0, 10 })
        {
            bool rejected = false; try { _ = new JourneyObservationOwners(capacity); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Unbounded projection capacity accepted");
        }
        var owners = new JourneyObservationOwners(2);
        foreach (long token in new[] { 0L, -1 })
        {
            bool rejected = false; try { owners.TryAcquire(token, 3, P(0), out _, out _); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid observation owner identity accepted");
        }
        Check(!owners.Release(default(JourneyObservationLease)) && !owners.Release(99), "Unknown lease released pending work");
    }

    private static void RecordedRequests()
    {
        using JsonDocument fixture = Fixture();
        Check(fixture.RootElement.GetProperty("sourceVersion").GetString() == "0.8.51", "Wrong captured ownership regression version");
        var owners = new JourneyObservationOwners(9);
        var pending = new Dictionary<long, JourneyObservationLease>();
        int retries = 0, commits = 0, outsideCoverage = 0, protectedMismatches = 0, peak = 0;
        long previousSequence = 0;
        foreach (JsonElement row in fixture.RootElement.GetProperty("observationSequence").EnumerateArray())
        {
            long sequence = row.GetProperty("sourceSequence").GetInt64();
            Check(sequence > previousSequence, "Captured observation order was changed"); previousSequence = sequence;
            if (row.GetProperty("kind").GetString() == "journey-view")
            {
                long token = row.GetProperty("query").GetInt64() * 1000000 + row.GetProperty("probe").GetInt64();
                long epoch = row.GetProperty("epoch").GetInt64();
                AutonomyPosition camera = Point(row.GetProperty("camera"));
                Check(owners.TryAcquire(token, epoch, camera, out var lease, out _), "Recorded request lost its observation owner: " + owners.Reason);
                if (pending.TryGetValue(token, out var old))
                {
                    Check(lease.Generation == old.Generation && lease.Slot == old.Slot && owners.IsCurrent(old),
                        "Interleaved captured work restarted an unfinished probe's projection");
                    retries++;
                }
                peak = Math.Max(peak, owners.ActiveCount);
                if (row.GetProperty("status").GetString() == "Unknown") pending[token] = lease;
                else { owners.Release(lease); pending.Remove(token); }
            }
            else if (row.GetProperty("action").GetString() == "commit")
            {
                commits++;
                AutonomyPosition requested = Point(row.GetProperty("requested"));
                AutonomyPosition retained = Point(row.GetProperty("retainedOrigin"));
                double coverage = Math.Min(16, row.GetProperty("coveredRadius").GetDouble());
                if (requested.Distance(retained) + 6 > coverage)
                {
                    outsideCoverage++;
                    foreach (var lease in pending.Values)
                        if (lease.Camera.Distance(requested) < .0001)
                        {
                            Check(owners.IsCurrent(lease) && lease.Camera.Distance(retained) > .0001,
                                "A captured commit for another centre retargeted this pending observation");
                            protectedMismatches++;
                        }
                }
                // This records the historical shared-projection failure; it
                // is not a commit against the new independently owned views.
                // No renderer/triangle evidence exists in this fixture, so no
                // captured Unknown is converted into a successful viewpoint.
            }
            foreach (var lease in pending.Values) Check(owners.IsCurrent(lease), "A different captured request displaced pending work");
        }
        Check(commits == 8 && outsideCoverage == 5 && protectedMismatches >= 2,
            "The selected capture no longer reproduces the observed wrong-centre commit sequence");
        Check(retries >= 4 && peak == 9 && owners.ActiveCount == pending.Count && pending.Count == 8,
            "The real interleaving did not exercise bounded pending ownership or silently resolved unknown geometry");
    }

    private static AutonomyPosition Point(JsonElement point) => P(point[0].GetSingle(), point[1].GetSingle(), point[2].GetSingle());
    private static JsonDocument Fixture()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
        {
            string path = Path.Combine(folder.FullName, "tests", "fixtures", "journey-mission", "0.8.51-observation-restart.json");
            if (File.Exists(path)) return JsonDocument.Parse(File.ReadAllText(path));
        }
        throw new FileNotFoundException("Sanitized 0.8.51 ownership fixture was not found");
    }
}
