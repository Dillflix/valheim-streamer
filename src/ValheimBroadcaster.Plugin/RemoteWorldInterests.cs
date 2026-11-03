using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Plugin;

internal sealed partial class RemoteObserverClient
{
    private readonly WorldInterestCache interests = new WorldInterestCache();
    private bool interestsSupported, interestsUnavailable, interestsRejected;
    private float interestProbeAt = -1, nextInterests;
    private long feedbackSequence;
    internal IReadOnlyList<WorldInterestSite> WorldInterests => Isolated && Approved && !interestsUnavailable ?
        interests.Available(Time.realtimeSinceStartup) : Array.Empty<WorldInterestSite>();
    internal string InterestStatus => !Isolated || !Approved ? "World interests: awaiting observer authorization" :
        interestsUnavailable ? "World interests: optional companion capability unavailable; generated regions remain available" :
        !interestsSupported ? "World interests: probing companion" : !interests.Fresh(Time.realtimeSinceStartup) ?
        "World interests: disabled or stale; using ordinary discovery" :
        "World interests: " + interests.Count + " destinations; " + (interests.Complete ? "completed survey" : "partial/indexing survey");
    private void ResetInterests()
    {
        interests.Reset(); interestsSupported = interestsUnavailable = interestsRejected = false;
        interestProbeAt = -1; nextInterests = 0; feedbackSequence = 0;
    }
    internal string InterestDiagnostics()
    {
        IReadOnlyList<WorldInterestSite> sites = WorldInterests;
        var text = new StringBuilder(InterestStatus);
        text.Append("\nBases/areas: ").Append(sites.Count(s => s.Kind != WorldInterestKind.Landmark))
            .Append("; placed landmarks: ").Append(sites.Count(s => s.Kind == WorldInterestKind.Landmark))
            .Append(". Top 12 by preference; travel distance and revisit rules still apply.");
        foreach (WorldInterestSite s in sites.OrderByDescending(s => s.Priority).ThenBy(s => s.Id, StringComparer.Ordinal).Take(12))
            text.AppendFormat("\n{0} | {1} | {2}; pieces {3}; score {4:F0}; age {5:F0}s; retry {6:F0}s; approach ({7:F0}, {8:F0}, {9:F0})",
                s.Id, s.Kind, s.Name, s.Pieces, s.Priority, s.Age, s.RetryAfter, s.Arrival.X, s.Arrival.Y, s.Arrival.Z);
        return text.ToString();
    }
    private void InterestReply(ZRpc rpc, ZPackage package)
    {
        if (!Isolated || !Approved || failed || interestsRejected || server == null || !ReferenceEquals(rpc, server.m_rpc) || !server.IsReady()) return;
        try
        {
            if (package.Size() > WorldInterestProtocol.MaxPacketBytes) throw new ArgumentException("Interest packet exceeds limit.");
            WorldInterestPage page = WorldInterestProtocol.Decode(package.GetArray());
            if (page.World != world!.GetWorldUID()) throw new ArgumentException("Interest world mismatch.");
            interests.Accept(page, Time.realtimeSinceStartup); interestsSupported = true; interestsUnavailable = false;
        }
        catch (Exception error)
        {
            interests.Reset(); interestsUnavailable = interestsRejected = true;
            log("Optional world interests rejected after " + error.GetType().Name + "; ordinary camera discovery continues.");
        }
    }
    private void TickInterests(float now)
    {
        if (!Approved || interestsUnavailable || server == null) return;
        if (!interestsSupported && interestProbeAt >= 0)
        {
            if (now - interestProbeAt >= WorldInterestProtocol.ProbeSeconds)
            { interestsUnavailable = true; log("Optional world-interest companion unavailable; generated-region discovery remains active."); }
            return;
        }
        if (now < nextInterests) return; nextInterests = now + 2;
        if (interestProbeAt < 0) interestProbeAt = now;
        try { server.m_rpc.Invoke(WorldInterestProtocol.Request, WorldInterestProtocol.Version); }
        catch (Exception error) { interestsUnavailable = true; interests.Reset(); log("World-interest request unavailable after " + error.GetType().Name); }
    }
    internal void ReportDestination(string id, DestinationOutcome outcome, float footage)
    {
        if (!Isolated || !Approved || !interestsSupported || interestsUnavailable || server == null) return;
        try
        {
            var feedback = new DestinationFeedback(world!.GetWorldUID(), ++feedbackSequence, id, outcome, outcome == DestinationOutcome.UsableFootage ? Mathf.Min(footage, 3600) : 0);
            server.m_rpc.Invoke(WorldInterestProtocol.Feedback, new ZPackage(WorldInterestProtocol.Encode(feedback)));
        }
        catch (Exception error) { log("Destination feedback could not be sent after " + error.GetType().Name + "; camera operation continues."); }
    }
}
