using System;

namespace ValheimBroadcaster.Plugin.Streaming;

// Cache glyph coverage when status changes. Only the measured panel rectangle is
// blended into outgoing pixels. All coordinates are in the final, upright video.
internal sealed class StreamStatusPanel
{
    private OverlayFont? font;
    private byte[] coverage = Array.Empty<byte>();
    private int panelWidth, panelHeight, inset;

    internal void Prepare(string text, int width, int height, int fontAt1080 = 14)
    {
        int size = Math.Max(OverlayFont.MinSize, Math.Min(OverlayFont.MaxSize, (height * fontAt1080 + 540) / 1080));
        if (font == null || font.Size != size) font = new OverlayFont(size);
        inset = Math.Max(2, (height + 45) / 90);
        int padX = inset, padY = Math.Max(2, inset * 2 / 3);
        int maxWidth = width - 2 * (inset + padX);
        int maxLines = Math.Max(1, (height * 2 / 5 - 2 * padY) / font.LineHeight);
        string[] lines = text.Split('\n');
        int count = Math.Min(maxLines, lines.Length);
        int longest = 0;
        for (int i = 0; i < count; i++)
        {
            string line = lines[i].TrimEnd('\r');
            bool more = i == count - 1 && count < lines.Length;
            int length = 0, end = 0;
            for (; end < line.Length; end++)
            {
                int advance = font[line[end]].Advance;
                if (length + advance > maxWidth) break;
                length += advance;
            }
            if (end < line.Length || more)
            {
                int ellipsis = font['…'].Advance;
                while (end > 0 && length + ellipsis > maxWidth) length -= font[line[--end]].Advance;
                line = line.Substring(0, end) + "…";
                length += ellipsis;
            }
            lines[i] = line;
            longest = Math.Max(longest, length);
        }
        panelWidth = longest + 2 * padX;
        panelHeight = count * font.LineHeight + 2 * padY;
        int area = panelWidth * panelHeight;
        if (coverage.Length < area) coverage = new byte[area];
        else Array.Clear(coverage, 0, area);
        for (int i = 0; i < count; i++)
        {
            int pen = padX, baseline = padY + font.Ascent + i * font.LineHeight;
            foreach (char c in lines[i])
            {
                var glyph = font[c];
                for (int row = 0; row < glyph.Height; row++)
                {
                    int y = baseline + glyph.Top + row;
                    if (y < 0 || y >= panelHeight) continue;
                    for (int col = 0; col < glyph.Width; col++)
                    {
                        int x = pen + glyph.Left + col;
                        if (x < 0 || x >= panelWidth) continue;
                        int p = y * panelWidth + x;
                        coverage[p] = Math.Max(coverage[p], glyph.Ink[row * glyph.Width + col]);
                    }
                }
                pen += glyph.Advance;
            }
        }
    }

    internal void Composite(byte[] rgba, int width, int height, bool flipVertical)
    {
        for (int row = 0; row < panelHeight; row++)
        {
            int y = inset + row;
            int dest = ((flipVertical ? height - 1 - y : y) * width + inset) * 4;
            for (int x = 0; x < panelWidth; x++, dest += 4)
            {
                // Light gray text over half-transparent black, retaining scene detail
                // even in the padding. The source is already display-encoded RGBA.
                int ink = coverage[row * panelWidth + x];
                int picture = (256 - ink) / 2;
                int label = ink * 212 + 128;
                rgba[dest] = (byte)((rgba[dest] * picture + label) >> 8);
                rgba[dest + 1] = (byte)((rgba[dest + 1] * picture + label) >> 8);
                rgba[dest + 2] = (byte)((rgba[dest + 2] * picture + label) >> 8);
            }
        }
    }
}
