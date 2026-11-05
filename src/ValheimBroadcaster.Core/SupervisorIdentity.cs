using System;

namespace ValheimBroadcaster.Core;

public static class SupervisorIdentity
{
    public const int Protocol = 1;

    // The SDK's CI informational version appends the tested Git commit. A local
    // build with no exact revision still runs, but cannot attest an update build.
    public static string SourceSha(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion)) return "";
        int split = informationalVersion!.LastIndexOf('+');
        if (split < 0 || informationalVersion.Length - split - 1 != 40) return "";
        string candidate = informationalVersion.Substring(split + 1);
        foreach (char c in candidate)
            if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return "";
        return candidate.ToLowerInvariant();
    }
}
