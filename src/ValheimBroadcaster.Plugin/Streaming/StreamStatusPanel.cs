using System;

namespace ValheimBroadcaster.Plugin.Streaming;

// Rasterize at status refresh time. Each RTMP sample then copies only occupied rows.
// Coordinates are expressed in final encoder space, including manual FFmpeg vflip.
internal sealed class StreamStatusPanel
{
    private byte[] bitmap = Array.Empty<byte>();
    private int panelWidth, rows, margin;

    internal void Prepare(string text, int width, int height)
    {
        int scale = Math.Max(1, Math.Min(width / 620, height / 330));
        margin = 2 * scale;
        int cell = 6 * scale, line = 9 * scale;
        int columns = (width - 2 * margin) / cell;
        int maxLines = Math.Max(1, (height * 2 / 3 - 2 * margin) / line);
        int x = 0, y = 0, longest = 0;
        void Layout(bool draw)
        {
            x = y = longest = 0;
            foreach (char c in text)
            {
                if (c == '\r') continue;
                if (c == '\n' || x == columns)
                {
                    if (++y == maxLines) { y--; break; }
                    x = 0;
                    if (c == '\n') continue;
                }
                if (draw && c != ' ')
                {
                    string bits = BroadcastBitmapFont.Glyph(c);
                    for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                    {
                        if (bits[row * 5 + col] != '1') continue;
                        for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                        {
                            int index = 4 * ((margin + y * line + row * scale + dy) * panelWidth +
                                margin + x * cell + col * scale + dx);
                            bitmap[index] = bitmap[index + 1] = bitmap[index + 2] = 244;
                        }
                    }
                }
                longest = Math.Max(longest, ++x);
            }
        }
        Layout(false);
        panelWidth = 2 * margin + longest * cell;
        rows = 2 * margin + (y + 1) * line;
        int bytes = rows * panelWidth * 4;
        if (bitmap.Length < bytes) bitmap = new byte[bytes];
        for (int i = 0; i < bytes; i += 4)
        {
            bitmap[i] = 15; bitmap[i + 1] = 20; bitmap[i + 2] = 24; bitmap[i + 3] = 255;
        }
        Layout(true);
    }

    internal void Composite(byte[] rgba, int width, int height, bool flipVertical)
    {
        int stride = panelWidth * 4;
        for (int row = 0; row < rows; row++)
        {
            int dest = flipVertical ? height - 1 - row : row;
            Buffer.BlockCopy(bitmap, row * stride, rgba, dest * width * 4, stride);
        }
    }
}
