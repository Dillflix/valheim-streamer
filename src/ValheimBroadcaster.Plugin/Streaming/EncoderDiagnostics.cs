using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ValheimBroadcaster.Plugin.Streaming;

// FFmpeg may print credentials separately as app/playpath/auth errors. Redact both
// URLs and their encoded/decoded components before retaining any diagnostic text.
internal sealed class EncoderDiagnostics
{
    private static readonly Regex Urls = new Regex(@"[a-zA-Z][a-zA-Z0-9+.-]*://[^\s\""'<>]+", RegexOptions.CultureInvariant);
    private readonly string[] secrets;
    private readonly object gate = new object();
    private readonly Queue<string> lines = new Queue<string>();
    private string last = "";
    private int? exitCode;

    internal EncoderDiagnostics(string url)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { url };
        var uri = new Uri(url);
        Add(values, uri.Host);
        Add(values, uri.UserInfo);
        foreach (string value in uri.UserInfo.Split(':')) Add(values, value);
        Add(values, uri.PathAndQuery.TrimStart('/'));
        Add(values, uri.AbsolutePath.TrimStart('/'));
        foreach (string value in uri.AbsolutePath.Split('/')) Add(values, value);
        foreach (string pair in uri.Query.TrimStart('?').Split('&'))
        {
            int equal = pair.IndexOf('=');
            if (equal >= 0) Add(values, pair.Substring(equal + 1));
        }
        Add(values, uri.Fragment.TrimStart('#'));
        values.Remove("");
        secrets = new string[values.Count]; values.CopyTo(secrets);
        Array.Sort(secrets, (a, b) => b.Length.CompareTo(a.Length));
    }

    private static void Add(HashSet<string> values, string value)
    {
        if (value.Length == 0) return;
        values.Add(value);
        values.Add(Uri.UnescapeDataString(value));
        values.Add(Uri.UnescapeDataString(value.Replace("+", " ")));
    }

    internal string Sanitize(string line)
    {
        // Do not truncate raw text: doing so can leave half a secret unrecognized.
        if (line.Length > 16384) return "[Oversized FFmpeg diagnostic omitted]";
        line = Urls.Replace(line, "[destination]");
        foreach (string secret in secrets)
            line = Regex.Replace(line, Regex.Escape(secret), "[redacted]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        line = Regex.Replace(line, @"[\x00-\x1f\x7f]", " ").Trim();
        return line.Length > 512 ? line.Substring(0, 512) + "…" : line;
    }

    internal void AddLine(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        string safe = Sanitize(raw!);
        lock (gate)
        {
            if (lines.Count == 8) lines.Dequeue();
            lines.Enqueue(safe);
            last = safe.Length > 180 ? safe.Substring(0, 180) + "…" : safe;
        }
    }

    internal void SetExitCode(int code) { lock (gate) exitCode = code; }
    internal string Summary { get { lock (gate) return (exitCode.HasValue ? " Exit " + exitCode.Value + "." : "") + (last.Length == 0 ? "" : " " + last); } }
    internal string Details { get { lock (gate) return "FFmpeg exit: " + (exitCode.HasValue ? exitCode.Value.ToString() : "unavailable") + "\n" + (lines.Count == 0 ? "No FFmpeg error output received." : string.Join("\n", lines.ToArray())); } }
}
