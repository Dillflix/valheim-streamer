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
    private int interestVersion = WorldInterestProtocol.Version2;
    internal IReadOnlyList<WorldInterestSite> WorldInterests => Isolated && Approved && !interestsUnavailable ?
        interests.Available(Time.realtimeSinceStartup) : Array.Empty<WorldInterestSite>();
    internal string InterestStatus => !Isolated || !Approved ? "World interests: awaiting observer authorization" :
        interestsUnavailable ? "World interests: optional companion capability unavailable; generated regions remain available" :
        !interestsSupported ? "World interests: probing companion" : !interests.Fresh(Time.realtimeSinceStartup) ?
        "World interests: disabled or stale; using ordinary discovery" :
        "World interests v" + interestVersion + ": " + interests.Count + " destinations; " + (interests.Complete ? "completed survey" : "partial/indexing survey");
    private void ResetInterests()
    {
        interests.Reset(); interestsSupported = interestsUnavailable = interestsRejected = false;
        interestProbeAt = -1; nextInterests = 0; feedbackSequence = 0; interestVersion = WorldInterestProtocol.Version2;
    }
    internal string InterestDiagnostics()
    {
        IReadOnlyList<WorldInterestSite> sites = WorldInterests;
        var text = new StringBuilder(InterestStatus);
        text.Append("\nBases/areas: ").Append(sites.Count(s => s.Kind == WorldInterestKind.Base || s.Kind == WorldInterestKind.BaseArea))
            .Append("; placed landmarks: ").Append(sites.Count(s => s.Kind == WorldInterestKind.Landmark))
            .Append("; natural landscapes: ").Append(sites.Count(s => s.Kind == WorldInterestKind.Natural))
            .Append("; coasts: ").Append(sites.Count(s => s.Landscape == ScenicLandscape.Coast))
            .Append(". Top 12 by preference; travel distance and revisit rules still apply.");
        foreach (WorldInterestSite s in sites.OrderByDescending(s => s.Priority).ThenBy(s => s.Id, StringComparer.Ordinal).Take(12))
            text.AppendFormat("\n{0} | {1} | {2}; pieces {3}; score {4:F0}; age {5:F0}s; retry {6:F0}s; approach ({7:F0}, {8:F0}, {9:F0}); {10} / {11}",
                s.Id, s.Kind, s.Name, s.Pieces, s.Priority, s.Age, s.RetryAfter, s.Arrival.X, s.Arrival.Y, s.Arrival.Z, s.Biome.Length == 0 ? "unknown biome" : s.Biome, s.Landscape);
        return text.ToString();
    }
    private void InterestReply(ZRpc rpc, ZPackage package) => ReadInterests(rpc, package, WorldInterestProtocol.Version);
    private void InterestReply2(ZRpc rpc, ZPackage package) => ReadInterests(rpc, package, WorldInterestProtocol.Version2);
    private void ReadInterests(ZRpc rpc, ZPackage package, int version)
    {
        if (version != interestVersion) return;
        if (!Isolated || !Approved || failed || interestsRejected || server == null || !ReferenceEquals(rpc, server.m_rpc) || !server.IsReady()) return;
        try
        {
            if (package.Size() > WorldInterestProtocol.MaxPacketBytes) throw new ArgumentException("Interest packet exceeds limit.");
            WorldInterestPage page = version == WorldInterestProtocol.Version2 ? WorldInterestProtocol.DecodeV2(package.GetArray()) : WorldInterestProtocol.Decode(package.GetArray());
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
            {
                if (interestVersion == WorldInterestProtocol.Version2)
                {
                    interestVersion = WorldInterestProtocol.Version; interestProbeAt = -1; nextInterests = 0;
                    log("Landscape metadata unavailable; trying legacy world interests with client-side source balancing.");
                }
                else { interestsUnavailable = true; log("Optional world-interest companion unavailable; generated-region discovery remains active."); }
            }
            return;
        }
        if (now < nextInterests) return; nextInterests = now + 2;
        if (interestProbeAt < 0) interestProbeAt = now;
        try { server.m_rpc.Invoke(interestVersion == WorldInterestProtocol.Version2 ? WorldInterestProtocol.Request2 : WorldInterestProtocol.Request, interestVersion); }
        catch (Exception error) { interestsUnavailable = true; interests.Reset(); log("World-interest request unavailable after " + error.GetType().Name); }
    }
    internal void ReportDestination(string id, DestinationOutcome outcome, float footage)
    {
        if (!Isolated || !Approved || !interestsSupported || interestsUnavailable || server == null) return;
        try
        {
            if (interestVersion == WorldInterestProtocol.Version && outcome == DestinationOutcome.SubjectUnavailable) outcome = DestinationOutcome.PictureUnavailable;
            var feedback = new DestinationFeedback(world!.GetWorldUID(), ++feedbackSequence, id, outcome, outcome == DestinationOutcome.UsableFootage ? Mathf.Min(footage, 3600) : 0);
            server.m_rpc.Invoke(interestVersion == WorldInterestProtocol.Version2 ? WorldInterestProtocol.Feedback2 : WorldInterestProtocol.Feedback, new ZPackage(WorldInterestProtocol.Encode(feedback)));
        }
        catch (Exception error) { log("Destination feedback could not be sent after " + error.GetType().Name + "; camera operation continues."); }
    }
}
