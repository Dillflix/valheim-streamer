using System;
using System.Globalization;

namespace ValheimBroadcaster.Plugin.Streaming;

// 24 columns x 7 rows. Normalized coordinates survive adaptive HLS resizing.
// Header, UTC session milliseconds (48 bits), random session suffix (32 bits),
// capture milliseconds (48 bits), then CRC16-CCITT over the preceding 19 bytes.
internal sealed class ReviewMarker
{
    private readonly byte[] data = new byte[21];
    private string session = "";
    private bool valid;

    internal bool Prepare(string id, double seconds)
    {
        if (id != session)
        {
            session = id;
            valid = false;
            if (id.Length != 27 || id[18] != '-' ||
                !DateTimeOffset.TryParseExact(id.Substring(0, 18), "yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc) ||
                !uint.TryParse(id.Substring(19), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint suffix))
                return false;
            data[0] = 0x56; data[1] = 0x42; data[2] = 1;
            Put(3, 6, utc.ToUnixTimeMilliseconds());
            Put(9, 4, suffix);
            valid = true;
        }
        if (!valid || !(seconds >= 0 && seconds < 281474976710)) return false;
        Put(13, 6, (long)Math.Round(seconds * 1000));
        int crc = 0xffff;
        for (int i = 0; i < 19; i++)
        {
            crc ^= data[i] << 8;
            for (int bit = 0; bit < 8; bit++)
                crc = ((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0)) & 0xffff;
        }
        Put(19, 2, crc);
        return true;
    }

    private void Put(int offset, int length, long value)
    {
        while (length > 0) { data[offset + --length] = (byte)value; value >>= 8; }
    }

    internal void Composite(byte[] rgba, int width, int height, bool flip)
    {
        for (int row = 0; row < 7; row++)
        {
            int top = (82 + row) * height / 90, bottom = (83 + row) * height / 90;
            for (int col = 0; col < 24; col++)
            {
                int bit = row * 24 + col;
                byte value = (data[bit / 8] & (128 >> (bit % 8))) == 0 ? (byte)16 : (byte)240;
                int left = (1 + col) * width / 160, right = (2 + col) * width / 160;
                for (int y = top; y < bottom; y++)
                {
                    int index = 4 * ((flip ? height - 1 - y : y) * width + left);
                    for (int x = left; x < right; x++, index += 4)
                    { rgba[index] = rgba[index + 1] = rgba[index + 2] = value; rgba[index + 3] = 255; }
                }
            }
        }
    }
}
