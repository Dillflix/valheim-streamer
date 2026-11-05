using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using ValheimBroadcaster.Core;

namespace ValheimBroadcaster.Replay;

/// <summary>Replays the measured coverage and scheduler inputs through the same
/// policy called by the game. Geometry/Unity rendering are recorded evidence,
/// not simulated physics. Missing records never become clear-space evidence.</summary>
public sealed class ReplayEngine
{
    private readonly Dictionary<string, int> reasons = new Dictionary<string, int>();
    private readonly Dictionary<string, int> coverage = new Dictionary<string, int>();
    private readonly Dictionary<string, int> refresh = new Dictionary<string, int>();
    private string? session;
    private long previousSequence;
    private bool compatible;
    public int Records { get; private set; }
    public int Replayed { get; private set; }
    public int Divergences { get; private set; }
    public int Invalid { get; private set; }
    public long Missing { get; private set; }
    public long Dropped { get; private set; }
    public int SearchExpirations { get; private set; }
    public int WaitExpirations { get; private set; }
    public int MissingPictures { get; private set; }
    public int LivePictures { get; private set; }
    public double MaximumMissingSeconds { get; private set; }
    public double MaximumUnavailableSpan { get; private set; }
    private double? unavailableSince;
    private bool scenicTravel;
    public List<string> Problems { get; } = new List<string>();

    public void Read(string line)
    {
        Records++;
        if (line.Length > CameraTraceWriter.MaximumLine) { Bad("Oversize record"); return; }
        try
        {
            using JsonDocument document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement row = document.RootElement;
            if (row.GetProperty("schema").GetInt32() != CameraTraceRecord.Schema)
            { compatible = false; Bad("Unsupported schema"); return; }
            string kind = row.GetProperty("kind").GetString()!;
            if (kind == "header")
            {
                string? version = row.GetProperty("version").GetString();
                compatible = version == "0.8.29" || version == "0.8.30" || version == "0.8.31" || version == "0.8.32" || version == "0.8.33" || version == "0.8.34" || version == "0.8.35" || version == "0.8.36" || version == "0.8.37" || version == "0.8.38" || version == "0.8.39" || version == "0.8.40" || version == "0.8.41" || version == "0.8.42" || version == "0.8.43" || version == "0.8.44" || version == "0.8.45" || version == "0.8.46" || version == "0.8.47" || version == "0.8.48" || version == "0.8.49" || version == "0.8.50";
                if (!compatible) Bad("Replay reads captures from 0.8.29 through 0.8.50 and checks their recorded coverage/deadline decisions; unsupported build");
                string current = row.GetProperty("utc").GetString()!;
                if (session != current) { previousSequence = 0; session = current; unavailableSince = null; scenicTravel = false; }
                return;
            }
            if (!compatible) { Bad("Observation without a compatible file header"); return; }
            if (kind == "lost") { Dropped += row.GetProperty("count").GetInt64(); return; }
            long seq = row.GetProperty("seq").GetInt64();
            if (previousSequence > 0 && seq > previousSequence + 1) Missing += seq - previousSequence - 1;
            if (previousSequence > 0 && seq <= previousSequence) Bad("Duplicate or out-of-order observation");
            previousSequence = seq;
            float now = Number(row, "time");
            if (kind == "refresh")
            {
                JsonElement at = row.GetProperty("refreshedAt");
                float refreshed = at.ValueKind == JsonValueKind.Null ? float.NegativeInfinity : at.GetSingle();
                var result = row.TryGetProperty("policy", out JsonElement policy) && policy.GetString() == "local-v2"
                    ? ScenicObservationPolicy.LocalRefresh(now, refreshed, Number(row, "distance"), Number(row, "radius"), row.GetProperty("catalogueChanged").GetBoolean())
                    : ScenicObservationPolicy.Refresh(now, refreshed, Number(row, "distance"), Number(row, "radius"));
                Compare(result.ToString() == row.GetProperty("action").GetString(), seq, "refresh");
                Count(refresh, row.GetProperty("owner").GetString() + ": " + result);
            }
            else if (kind == "coverage")
            {
                var result = ScenicObservationPolicy.Coverage(Number(row, "distance"), Number(row, "radius"));
                Compare((int)result == row.GetProperty("gap").GetInt32(), seq, "coverage");
                Count(coverage, row.GetProperty("owner").GetString() + ": " + result);
                if (result != ScenicCoverageGap.None) Count(reasons, row.GetProperty("reason").GetString()!);
            }
            else if (kind == "budget")
            {
                bool wait = row.GetProperty("operation").GetString() == "wait";
                bool result = wait ? ScenicObservationPolicy.WaitExpired(now, Number(row, "waitingAt")) :
                    ScenicObservationPolicy.SearchExpired(now, Number(row, "started"), row.GetProperty("urgent").GetBoolean());
                Compare(result == row.GetProperty("expired").GetBoolean(), seq, "deadline");
                if (result) { if (wait) WaitExpirations++; else SearchExpirations++; }
            }
            else if (kind == "event") Count(reasons, row.GetProperty("owner").GetString() + ": " + row.GetProperty("reason").GetString());
            else if (kind == "foliage-projection")
                Count(reasons, "Foliage projection: " + row.GetProperty("action").GetString());
            else if (kind == "foliage-catalogue-progress") Count(reasons, "Incremental foliage catalogue progress");
            else if (kind == "travel") scenicTravel = true;
            else if (kind == "picture")
            {
                if (!row.GetProperty("onAir").GetBoolean())
                { if (!scenicTravel || row.GetProperty("held").GetBoolean()) unavailableSince = null; return; }
                scenicTravel = false;
                // The local recovery clock resets on travel. Keep the observed
                // output gap across those resets so repeated black trips are not
                // misleadingly summarized as one short local search.
                if (unavailableSince.HasValue)
                    MaximumUnavailableSpan = Math.Max(MaximumUnavailableSpan, now - unavailableSince.Value);
                if (row.GetProperty("usable").GetBoolean() || row.GetProperty("held").GetBoolean()) unavailableSince = null;
                else if (!unavailableSince.HasValue) unavailableSince = now;
                if (row.GetProperty("usable").GetBoolean()) LivePictures++; else MissingPictures++;
                MaximumMissingSeconds = Math.Max(MaximumMissingSeconds, Number(row, "missingSeconds"));
            }
        }
        catch (Exception error) when (error is JsonException || error is KeyNotFoundException || error is InvalidOperationException || error is FormatException || error is OverflowException)
        { Bad("Malformed/incomplete record: " + error.GetType().Name); }
    }
    private static float Number(JsonElement row, string name)
    {
        float result = row.GetProperty(name).GetSingle();
        if (!float.IsFinite(result)) throw new FormatException("Non-finite numeric input");
        return result;
    }
    private void Compare(bool matches, long sequence, string kind)
    {
        Replayed++;
        if (matches) return;
        Divergences++; if (Problems.Count < 20) Problems.Add($"Decision mismatch at sequence {sequence}: {kind}");
    }
    private void Bad(string reason) { Invalid++; if (Problems.Count < 20) Problems.Add(reason); }
    private static void Count(Dictionary<string, int> values, string key) => values[key] = values.TryGetValue(key, out int count) ? count + 1 : 1;
    public IEnumerable<string> Summary()
    {
        yield return $"Records {Records}; decisions replayed {Replayed}; divergences {Divergences}; invalid {Invalid}.";
        yield return $"Sequence gaps {Missing}; writer-reported drops {Dropped} (may overlap). Rotation can omit earlier history.";
        yield return $"Deadline outcomes: search expired {SearchExpirations}; unknown-query expired {WaitExpirations}.";
        yield return $"Picture samples: live {LivePictures}; unavailable {MissingPictures}; longest recorded missing interval {MaximumMissingSeconds.ToString("F2", CultureInfo.InvariantCulture)}s.";
        yield return $"Longest observed Scenic recovery span including requested travel: {MaximumUnavailableSpan.ToString("F2", CultureInfo.InvariantCulture)}s (sampled evidence, not frame-exact video).";
        foreach (var pair in refresh.OrderByDescending(p => p.Value)) yield return $"Refresh {pair.Key}: {pair.Value}";
        foreach (var pair in coverage.OrderByDescending(p => p.Value)) yield return $"Coverage {pair.Key}: {pair.Value}";
        foreach (var pair in reasons.OrderByDescending(p => p.Value).Take(15)) yield return $"Evidence {pair.Value}: {pair.Key}";
        foreach (string problem in Problems) yield return "Problem: " + problem;
        yield return "Replay checks recorded coverage/deadline decisions only. Incremental projection/catalogue lifecycle rows are summarized observations, not re-executed scheduling. It does not reproduce Unity traversal, physics, loading, foliage pixels, full route selection, or video.";
    }
}
