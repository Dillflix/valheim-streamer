using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ValheimBroadcaster.Replay;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet ValheimBroadcaster.Replay.dll <trace-directory|file.jsonl> [...]");
    return 2;
}
var replay = new ReplayEngine();
try
{
    var paths = new SortedSet<string>(StringComparer.Ordinal);
    foreach (string arg in args)
    {
        if (Directory.Exists(arg)) foreach (string file in Directory.GetFiles(arg, "scenic-*.jsonl")) paths.Add(Path.GetFullPath(file));
        else paths.Add(Path.GetFullPath(arg));
    }
    if (paths.Count == 0) { Console.Error.WriteLine("No scenic trace files found."); return 2; }
    if (paths.Count > 128 || paths.Sum(path => new FileInfo(path).Length) > 256L * 1024 * 1024)
    { Console.Error.WriteLine("Input exceeds 128 files / 256 MiB; replay one capture session at a time."); return 2; }
    foreach (string path in paths)
        foreach (string line in File.ReadLines(path)) replay.Read(line);
    foreach (string line in replay.Summary()) Console.WriteLine(line);
    return replay.Divergences == 0 && replay.Invalid == 0 && replay.Replayed > 0 ? 0 : 1;
}
catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
{ Console.Error.WriteLine("Could not read capture: " + error.Message); return 2; }
