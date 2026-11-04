using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using ValheimBroadcaster.Core;
using ValheimBroadcaster.Replay;

internal static class ScenicTraceTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Replay distinguishes throttled recenter from a capacity-limited survey", SurveyReplay);
        yield return ("Replay keeps missing picture spans across regional recovery resets", PictureGap);
        yield return ("Replay reproduces competing candidate and unknown-query deadlines", Deadlines);
        yield return ("Replay reports divergence and missing evidence without inventing clear space", DamagedEvidence);
        yield return ("Trace encoding preserves finite numeric inputs across locales and escapes text", Encoding);
        yield return ("Flight recorder rotates bounded files and preserves unrelated files", Rotation);
        yield return ("Flight recorder reports inaccessible storage without throwing into the producer", StorageFailure);
        yield return ("Encounter source metadata survives held-frame reuse and current replay headers", EncounterCapture);
        yield return ("Incremental foliage lifecycle evidence is summarized without claiming replayed Unity work", IncrementalEvidence);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static string Header() => new CameraTraceRecord("header", 0).Field("version", "0.8.29").Field("utc", "test-session").ToString();
    private static void IncrementalEvidence()
    {
        var replay = new ReplayEngine();
        replay.Read(new CameraTraceRecord("header", 0).Field("version", "0.8.36").Field("utc", "incremental-test").ToString());
        replay.Read(Row("foliage-projection", 1, 1).Field("action", "start").Field("retained", 400).ToString());
        replay.Read(Row("foliage-projection", 1.2, 2).Field("action", "budget-deferred").ToString());
        replay.Read(Row("foliage-catalogue-progress", 1.3, 3).Field("steps", 1024).ToString());
        replay.Read(Row("foliage-projection", 1.4, 4).Field("action", "commit").Field("retained", 410).ToString());
        Check(replay.Invalid == 0 && replay.Missing == 0 && replay.Replayed == 0,
            "Observation telemetry became an invented replay decision or invalid input");
        string report = string.Join("\n", replay.Summary());
        Check(report.Contains("Foliage projection: budget-deferred") && report.Contains("Foliage projection: commit") &&
            report.Contains("Incremental foliage catalogue progress") && report.Contains("not re-executed scheduling"),
            "Replay summary hid the incremental survey evidence or its limits");
    }
    private static void EncounterCapture()
    {
        var source = new DiagnosticPictureContext("Player", 12, 4, true, "Readable fallback", intent: "Action",
            encounter: "Developing", coverage: "Exchange unavailable; player retained", hostiles: 9,
            participants: 12, framed: 2, visibleOpponents: 0, actorOcclusion: .2);
        var held = source;
        source = new DiagnosticPictureContext("Player", 13, 5, true, "Clear exchange", encounter: "Engaged",
            hostiles: 8, participants: 11, framed: 5, visibleOpponents: 3);
        string encoded = held.Append(Row("video-sample", 10, 1).Field("presentation", "Held")).ToString();
        using JsonDocument row = JsonDocument.Parse(encoded);
        Check(row.RootElement.GetProperty("shot").GetInt64() == 12 && row.RootElement.GetProperty("encounter").GetString() == "Developing" &&
            row.RootElement.GetProperty("hostiles").GetInt32() == 9 && row.RootElement.GetProperty("visibleOpponents").GetInt32() == 0 &&
            row.RootElement.GetProperty("actorOcclusion").GetDouble() == .2, "Held picture acquired later encounter metadata");
        var replay = new ReplayEngine();
        replay.Read(new CameraTraceRecord("header", 0).Field("version", "0.8.41").Field("utc", "encounter-test").ToString());
        replay.Read(encoded);
        replay.Read(Row("budget", 11.5, 2).Field("operation", "step").Field("started", 10).Field("waitingAt", 11.2)
            .Field("urgent", true).Field("expired", true).ToString());
        Check(replay.Invalid == 0 && replay.Divergences == 0 && replay.Replayed == 1,
            "New encounter footage metadata broke existing scenic policy replay");
    }
    private static CameraTraceRecord Row(string kind, double time, long seq) => new CameraTraceRecord(kind, time).Field("seq", seq);
    private static void SurveyReplay()
    {
        var replay = new ReplayEngine(); replay.Read(Header());
        replay.Read(Row("refresh", 10.1f, 1).Field("owner", "spatial").Field("refreshedAt", 10)
            .Field("distance", 18).Field("radius", 110).Field("action", "Throttled").ToString());
        replay.Read(Row("coverage", 10.1f, 2).Field("owner", "spatial").Field("distance", 18).Field("radius", 110)
            .Field("gap", (long)ScenicCoverageGap.DetailRadius).Field("reason", "detail gap").ToString());
        replay.Read(Row("refresh", 10.5f, 3).Field("owner", "spatial").Field("refreshedAt", 10)
            .Field("distance", 18).Field("radius", 110).Field("action", "Recentered").ToString());
        replay.Read(Row("coverage", 10.5f, 4).Field("owner", "spatial").Field("distance", 0).Field("radius", 110)
            .Field("gap", 0).Field("reason", "clear").ToString());
        replay.Read(Row("coverage", 11, 5).Field("owner", "spatial").Field("distance", 0).Field("radius", 3)
            .Field("gap", (long)ScenicCoverageGap.RetainedRadius).Field("reason", "capacity gap even at centre").ToString());
        Check(replay.Replayed == 5 && replay.Divergences == 0 && replay.Invalid == 0, "Recorded observation sequence cannot be replayed");
        Check(replay.Summary().Any(s => s.Contains("capacity gap even at centre")), "Capacity cause lost in generic unknown classification");
        Check(ScenicObservationPolicy.Coverage(10, 110) == ScenicCoverageGap.DetailRadius, "Exact detail boundary became clear");
        Check(ScenicObservationPolicy.Refresh(9, 10, 0, 110) == ScenicSurveyRefresh.ClockReset, "Clock rollback reuses future evidence");
    }
    private static void PictureGap()
    {
        var replay = new ReplayEngine(); replay.Read(Header());
        long sequence = 0;
        void Picture(double time, bool usable, double localMissing, bool held = false) => replay.Read(
            Row("picture", time, ++sequence).Field("onAir", true).Field("usable", usable).Field("held", held)
                .Field("missingSeconds", localMissing).ToString());
        Picture(60, false, 0); Picture(69, false, 9);
        replay.Read(Row("travel", 70, ++sequence).ToString());
        replay.Read(Row("picture", 70.02, ++sequence).Field("onAir", false).Field("held", false).ToString());
        Picture(78, false, 0); Picture(87, false, 9);
        replay.Read(Row("travel", 88, ++sequence).ToString());
        replay.Read(Row("picture", 88.02, ++sequence).Field("onAir", false).Field("held", false).ToString());
        Picture(96, false, 0); Picture(97, true, 0);
        Check(replay.Invalid == 0 && replay.MaximumMissingSeconds == 9 && replay.MaximumUnavailableSpan == 37,
            "Region changes hid the continuous outage.");
        Picture(100, false, 0); Picture(101, false, 1, true); Picture(200, false, 0);
        Check(replay.MaximumUnavailableSpan == 37, "A held picture did not end the unavailable interval.");
        replay.Read(new CameraTraceRecord("header", 0).Field("version", "0.8.31").Field("utc", "another-session").ToString());
        Picture(250, false, 0);
        Check(replay.Invalid == 0 && replay.MaximumUnavailableSpan == 37, "Separate recordings were joined into an invented gap.");
    }
    private static void Deadlines()
    {
        var replay = new ReplayEngine(); replay.Read(Header());
        // A probe starts late in a candidate's life. Unknown wait has not expired
        // when the candidate does: both clocks must appear in the capture.
        replay.Read(Row("budget", 11.5, 1).Field("operation", "step").Field("started", 10).Field("waitingAt", 11.2)
            .Field("urgent", true).Field("expired", true).ToString());
        replay.Read(Row("budget", 11.5, 2).Field("operation", "wait").Field("started", 10).Field("waitingAt", 11.2)
            .Field("urgent", true).Field("expired", false).ToString());
        replay.Read(Row("budget", 12.1, 3).Field("operation", "wait").Field("started", 10).Field("waitingAt", 11.2)
            .Field("urgent", false).Field("expired", true).ToString());
        Check(replay.Replayed == 3 && replay.Divergences == 0 && replay.SearchExpirations == 1 && replay.WaitExpirations == 1,
            "Replay conflated query wait and overall search deadlines");
    }
    private static void DamagedEvidence()
    {
        var replay = new ReplayEngine(); replay.Read(Header());
        replay.Read(Row("coverage", 1, 5).Field("owner", "live").Field("distance", 12).Field("radius", 110).Field("gap", 0).ToString());
        replay.Read(Row("coverage", 2, 8).Field("owner", "live").Field("distance", 0).Field("radius", 110).Field("gap", 0).ToString());
        replay.Read("{\"schema\":1");
        Check(replay.Divergences == 1 && replay.Missing == 2 && replay.Invalid > 0, "Corrupt evidence passed replay silently");
    }
    private static void Encoding()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string text = "quote\" slash\\ newline\n";
            using JsonDocument json = JsonDocument.Parse(Row("test", 10.125, 1).Field("text", text).Field("initial", double.NegativeInfinity).ToString());
            Check(json.RootElement.GetProperty("time").GetDouble() == 10.125 && json.RootElement.GetProperty("text").GetString() == text,
                "Trace lost exact clock values or broke JSON escaping");
            Check(json.RootElement.GetProperty("initial").ValueKind == JsonValueKind.Null, "Non-finite initial clock produced invalid JSON");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    private static void Rotation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "valheim-trace-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "unrelated.jsonl"), "keep");
        try
        {
            var writer = new CameraTraceWriter(directory, Header(), 1024);
            for (int i = 0; i < 200; i++) Check(writer.Record(Row("test", i, i + 1).Field("data", new string('x', 200)).ToString()), "Unexpected producer failure");
            writer.Dispose();
            Check(writer.Error == null && writer.Written == 200 && writer.Pending == 0, "Buffered records did not drain on shutdown");
            string[] files = Directory.GetFiles(directory, "scenic-*.jsonl");
            Check(files.Length == CameraTraceWriter.MaximumFiles, "Rotation did not bound retained files");
            foreach (string file in files)
            {
                Check(File.ReadLines(file).First() == Header(), "Rotated file lacks standalone metadata");
                Check(new FileInfo(file).Length <= 1024, "Normal records exceeded rotation size");
                foreach (string line in File.ReadLines(file)) { using JsonDocument parsed = JsonDocument.Parse(line); }
            }
            Check(File.Exists(Path.Combine(directory, "unrelated.jsonl")), "Rotation deleted an unrelated file");
            Check(!writer.Record("{}"), "Disposed recorder accepts more work");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void StorageFailure()
    {
        string file = Path.GetTempFileName();
        try
        {
            var writer = new CameraTraceWriter(file, Header());
            writer.Record(Row("test", 0, 1).ToString()); writer.Dispose();
            Check(writer.Error != null && !writer.Record("{}"), "Storage failure remained invisible or kept accepting work");
        }
        finally { File.Delete(file); }
    }
}
