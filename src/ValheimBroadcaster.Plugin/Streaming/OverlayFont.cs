using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace ValheimBroadcaster.Plugin.Streaming;

// Pre-hinted grayscale coverage at each supported pixel size. Loading a strike
// requires neither a game font atlas readback nor a font on the encoder host.
internal sealed class OverlayFont
{
    internal const int MinSize = 8, MaxSize = 20;
    internal readonly int Size, Ascent, LineHeight;
    private readonly Dictionary<char, Glyph> glyphs = new Dictionary<char, Glyph>();

    internal sealed class Glyph
    {
        internal readonly int Left, Top, Width, Height, Advance;
        internal readonly byte[] Ink;
        internal Glyph(BinaryReader reader)
        {
            Left = reader.ReadSByte(); Top = reader.ReadSByte();
            Width = reader.ReadByte(); Height = reader.ReadByte(); Advance = reader.ReadByte();
            Ink = reader.ReadBytes(Width * Height);
            if (Ink.Length != Width * Height) throw new EndOfStreamException();
        }
    }

    internal OverlayFont(int size)
    {
        Size = size;
        using var data = typeof(OverlayFont).Assembly.GetManifestResourceStream("StreamOverlay.coverage")
            ?? throw new InvalidDataException("Missing stream overlay font");
        using var table = new BinaryReader(data);
        int offset = sizeof(int) * (MaxSize - MinSize + 1);
        for (int i = MinSize; i < size; i++) offset += table.ReadInt32();
        data.Position = offset;
        using var reader = new BinaryReader(new GZipStream(data, CompressionMode.Decompress));
        Ascent = reader.ReadByte();
        LineHeight = Ascent + reader.ReadByte();
        int count = reader.ReadUInt16();
        for (int i = 0; i < count; i++) glyphs.Add((char)reader.ReadUInt16(), new Glyph(reader));
    }

    internal Glyph this[char c] => glyphs.TryGetValue(c, out var glyph) ? glyph : glyphs['\uFFFD'];
}
