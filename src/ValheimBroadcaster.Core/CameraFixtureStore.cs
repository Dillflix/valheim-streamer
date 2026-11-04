using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ValheimBroadcaster.Core;

/// <summary>World-UID scoped persistence. An existing fixture is replaced only
/// after its complete successor has been encoded, written and flushed.</summary>
public sealed class CameraFixtureStore
{
    public const int MaximumFixturesPerWorld = 64;
    public const string Extension = ".vbcamera";
    private readonly string root;
    public CameraFixtureStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Fixture directory is required.", nameof(directory));
        root = Path.GetFullPath(directory);
    }

    public IReadOnlyList<string> List(long worldId)
    {
        string directory = WorldDirectory(worldId); var names = new List<string>();
        if (!Directory.Exists(directory)) return names.AsReadOnly();
        foreach (string path in Directory.EnumerateFiles(directory, "*" + Extension, SearchOption.TopDirectoryOnly))
        {
            if (names.Count >= MaximumFixturesPerWorld) throw new IOException("Fixture directory exceeds its 64-file limit.");
            string name = Path.GetFileNameWithoutExtension(path);
            if (CameraFixture.NormalizeName(name) != name) throw new InvalidDataException("Fixture directory contains a noncanonical filename.");
            names.Add(name);
        }
        names.Sort(StringComparer.Ordinal); return names.AsReadOnly();
    }

    public CameraFixture Load(long worldId, string name)
    {
        name = CameraFixture.NormalizeName(name);
        string path = FilePath(worldId, name);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length < 64 || file.Length > CameraFixtureCodec.MaximumBytes) throw new InvalidDataException("Fixture file has an invalid size.");
        var bytes = new byte[(int)file.Length]; int offset = 0;
        while (offset < bytes.Length)
        {
            int count = file.Read(bytes, offset, bytes.Length - offset);
            if (count == 0) throw new EndOfStreamException("Fixture file changed while reading.");
            offset += count;
        }
        CameraFixture result = CameraFixtureCodec.Decode(bytes, worldId);
        if (result.Name != name) throw new InvalidDataException("Fixture name does not match its filename.");
        return result;
    }

    public string Save(CameraFixture fixture, bool overwrite = false)
    {
        if (fixture == null) throw new ArgumentNullException(nameof(fixture));
        byte[] bytes = CameraFixtureCodec.Encode(fixture);
        string directory = WorldDirectory(fixture.WorldId), path = FilePath(fixture.WorldId, fixture.Name);
        bool exists = File.Exists(path);
        if (exists && !overwrite) throw new IOException("A fixture with this name already exists; use save <name> replace to replace it.");
        if (!exists && List(fixture.WorldId).Count >= MaximumFixturesPerWorld)
            throw new IOException("This world already has 64 camera fixtures; remove one before saving another.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".pending-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            // No delete-and-move fallback: if atomic replacement is unsupported,
            // leave the previous fixture intact and report the actual failure.
            if (exists) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public bool Delete(long worldId, string name)
    {
        string path = FilePath(worldId, name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        return true;
    }

    private string FilePath(long worldId, string name) => Path.Combine(WorldDirectory(worldId), CameraFixture.NormalizeName(name) + Extension);
    private string WorldDirectory(long worldId)
    {
        if (worldId == 0) throw new ArgumentOutOfRangeException(nameof(worldId), "Join a world before accessing its fixtures.");
        return Path.Combine(root, unchecked((ulong)worldId).ToString("x16", CultureInfo.InvariantCulture));
    }
}
