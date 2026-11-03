using System;
using System.Globalization;
using System.Text;

namespace ValheimBroadcaster.Core;

/// <summary>Small dependency-free JSONL records. Only explicitly selected fields
/// are recorded: never configuration dumps, network addresses or credentials.</summary>
public sealed class CameraTraceRecord
{
    public const int Schema = 1;
    private readonly StringBuilder text = new StringBuilder(512);
    public CameraTraceRecord(string kind, double time)
    { text.Append('{'); Field("schema", Schema); Field("kind", kind); Field("time", time); }
    public CameraTraceRecord Field(string name, string? value)
    {
        Name(name);
        if (value == null) text.Append("null"); else Quote(value, 512);
        return this;
    }
    public CameraTraceRecord Field(string name, bool value)
    { Name(name); text.Append(value ? "true" : "false"); return this; }
    public CameraTraceRecord Field(string name, long value)
    { Name(name); text.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }
    public CameraTraceRecord Field(string name, double value)
    {
        Name(name);
        text.Append(double.IsNaN(value) || double.IsInfinity(value) ? "null" : value.ToString("R", CultureInfo.InvariantCulture));
        return this;
    }
    public override string ToString() => text.ToString() + "}";
    private void Name(string name)
    { if (text.Length > 1) text.Append(','); Quote(name, 64); text.Append(':'); }
    private void Quote(string value, int maximum)
    {
        text.Append('"');
        for (int i = 0; i < Math.Min(value.Length, maximum); i++)
        {
            char c = value[i];
            if (c == '"' || c == '\\') text.Append('\\').Append(c);
            else if (c < 32 || char.IsSurrogate(c)) text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else text.Append(c);
        }
        text.Append('"');
    }
}
