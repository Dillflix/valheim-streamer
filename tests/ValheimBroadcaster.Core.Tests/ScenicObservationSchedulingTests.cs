using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Replay;

internal static class ScenicObservationSchedulingTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Captured canopy and viewpoint changes no longer wait for a discovery throttle", CapturedRequests);
        yield return ("Local projections reuse coverage while discovery is independently bounded", IndependentDiscovery);
        yield return ("Scene revision changes revoke local coverage without renewing search deadlines", RevisionAndDeadlines);
        yield return ("New replay reads local projection decisions and still accepts legacy captures", LocalReplay);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void CapturedRequests()
    {
        // Exact finite inputs from the three uploaded 0.8.29 capture sessions. Only timing
        // and distances are retained; no world coordinates or identifying data.
        // These reproduce evidence starvation, not Unity visibility or footage.
        var requests = new (float Now, float At, float Distance, float Radius)[] {
            (786.3047485351562f,785.8927612304688f,14.17514705657959f,97.02739715576172f),
            (802.1368408203125f,802.112548828125f,18.80620765686035f,67.84607696533203f),
            (803.6627197265625f,803.6423950195312f,17.308204650878906f,67.72338104248047f),
            (805.1878051757812f,805.1677856445312f,19.548171997070312f,67.3575210571289f),
            (806.714599609375f,806.6936645507812f,21.86966323852539f,65.21717834472656f),
            (810.36376953125f,810.3444213867188f,22.858173370361328f,63.515777587890625f),
            (52.75764083862305f,52.70750427246094f,25.845325469970703f,60.82927322387695f),
            (54.24851608276367f,54.22756576538086f,11.313767433166504f,61.793087005615234f),
            (55.77390670776367f,55.75374984741211f,12.574885368347168f,66.92632293701172f),
            (57.30059814453125f,57.280784606933594f,10.763916015625f,64.34382629394531f),
            (58.82855987548828f,58.807369232177734f,14.0128812789917f,55.81312561035156f),
            (60.37879180908203f,60.339969635009766f,21.826704025268555f,64.17680358886719f),
            (71.86651611328125f,71.84789276123047f,76.21707916259766f,65.1216049194336f),
            (125.94636535644531f,125.92969512939453f,88.82624816894531f,57.63802719116211f),
            (196.93568420410156f,196.91041564941406f,63.647621154785156f,60.00975799560547f),
            (219.5790252685547f,219.55348205566406f,79.68904113769531f,62.3409423828125f)
        };
        foreach (var r in requests)
        {
            Check(ScenicObservationPolicy.Refresh(r.Now, r.At, r.Distance, r.Radius) == ScenicSurveyRefresh.Throttled,
                "Captured legacy refresh decision was not reproduced");
            Check((ScenicObservationPolicy.Coverage(r.Distance, r.Radius) & ScenicCoverageGap.DetailRadius) != 0,
                "Fixture no longer reproduces the recorded incomplete neighborhood");
            Check(ScenicObservationPolicy.LocalRefresh(r.Now, r.At, r.Distance, r.Radius, false) == ScenicSurveyRefresh.Recentered,
                "Local camera query still waits on the unrelated scene-enumeration clock");
            Check(!ScenicObservationPolicy.CatalogueDue(r.Now, r.At), "A new local query forces an expensive whole-scene scan");
        }
    }
    private static void IndependentDiscovery()
    {
        float scanAt = float.NegativeInfinity; int scans = 0, projections = 0;
        for (int frame = 0; frame < 120; frame++)
        {
            float now = frame / 60f;
            // Multiple owners can inspect distant viewpoints in the same frame.
            for (int owner = 0; owner < 3; owner++)
            {
                if (ScenicObservationPolicy.CatalogueDue(now, scanAt)) { scans++; scanAt = now; }
                if (ScenicObservationPolicy.NeedsRefresh(ScenicObservationPolicy.LocalRefresh(now, now, 18, 16, false))) projections++;
            }
        }
        Check(scans == 4 && projections == 360, "Independent local queries multiplied scene enumerations or were starved");
        Check(ScenicObservationPolicy.LocalRefresh(2, 1.9f, 5, 16, false) == ScenicSurveyRefresh.Reused, "A covered query rebuilt unnecessarily");
        Check(ScenicObservationPolicy.Coverage(0, 4) == ScenicCoverageGap.RetainedRadius, "Truncated geometry was declared complete");
        Check(ScenicObservationPolicy.LocalRefresh(2, 1.9f, 0, 4, false) != ScenicSurveyRefresh.Reused, "Incomplete local coverage became reusable evidence");
    }
    private static void RevisionAndDeadlines()
    {
        Check(ScenicObservationPolicy.LocalRefresh(2, 1.9f, 0, 16, true) == ScenicSurveyRefresh.CatalogueChanged,
            "A changed scene retained the preceding projection");
        Check(ScenicObservationPolicy.LocalRefresh(1, 2, 0, 16, false) == ScenicSurveyRefresh.ClockReset,
            "A clock rollback reused future observations");
        Check(ScenicObservationPolicy.SearchExpired(803.625732421875f, 802.112548828125f, true),
            "The real missing-picture deadline was enlarged to hide the starvation");
        Check(ScenicObservationPolicy.WaitExpired(2, 1), "Unknown evidence is now allowed to wait indefinitely");
    }
    private static void LocalReplay()
    {
        var replay = new ReplayEngine();
        replay.Read(new CameraTraceRecord("header",0).Field("version","0.8.30").Field("utc","test").ToString());
        replay.Read(new CameraTraceRecord("refresh",2.1f).Field("seq",1).Field("owner","preflight")
            .Field("refreshedAt",2).Field("distance",18).Field("radius",16).Field("policy","local-v2")
            .Field("catalogueChanged",false).Field("action","Recentered").ToString());
        Check(replay.Replayed == 1 && replay.Invalid == 0 && replay.Divergences == 0, "New capture was replayed with the old throttle");
    }
}
