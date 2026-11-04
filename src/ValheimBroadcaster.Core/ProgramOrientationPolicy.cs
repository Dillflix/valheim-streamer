using System;

namespace ValheimBroadcaster.Core
{
    public enum ProgramRowOrder { Unknown, TopFirst, BottomFirst }

    /// <summary>Measures a known four-quadrant calibration image. FFmpeg raw
    /// RGBA expects the visible top row first; Unity/backend names alone do not
    /// establish the ordering of an actual render-texture readback.</summary>
    public static class ProgramOrientationPolicy
    {
        public const int ProbeWidth = 16, ProbeHeight = 16;
        public const double CalibrationTimeoutSeconds = 2;

        // Texture2D.SetPixels32 consumes rows from the bottom left. The known
        // image is red/green on the bottom and blue/yellow on the top.
        public static byte[] CreateBottomUpProbe()
        {
            var data = new byte[ProbeWidth * ProbeHeight * 4];
            for (int y = 0; y < ProbeHeight; y++)
                for (int x = 0; x < ProbeWidth; x++)
                {
                    bool top = y >= ProbeHeight / 2, right = x >= ProbeWidth / 2;
                    int p = (y * ProbeWidth + x) * 4;
                    data[p] = (byte)(top ? right ? 255 : 0 : right ? 0 : 255);
                    data[p + 1] = (byte)(right ? 255 : 0);
                    data[p + 2] = (byte)(top && !right ? 255 : 0);
                    data[p + 3] = 255;
                }
            return data;
        }

        public static ProgramRowOrder Detect(byte[] rgba, int width, int height)
        {
            if (rgba == null || width < 8 || height < 8 || width > 256 || height > 256 ||
                rgba.Length != width * height * 4) return ProgramRowOrder.Unknown;
            int left = width / 4, right = width * 3 / 4, first = height / 4, last = height * 3 / 4;
            bool topFirst = Matches(rgba, width, left, first, 0, 0, 255) && Matches(rgba, width, right, first, 255, 255, 0) &&
                Matches(rgba, width, left, last, 255, 0, 0) && Matches(rgba, width, right, last, 0, 255, 0);
            bool bottomFirst = Matches(rgba, width, left, first, 255, 0, 0) && Matches(rgba, width, right, first, 0, 255, 0) &&
                Matches(rgba, width, left, last, 0, 0, 255) && Matches(rgba, width, right, last, 255, 255, 0);
            return topFirst ? ProgramRowOrder.TopFirst : bottomFirst ? ProgramRowOrder.BottomFirst : ProgramRowOrder.Unknown;
        }

        private static bool Matches(byte[] data, int width, int x, int y, int r, int g, int b)
        {
            // Sample a central 2x2 block, excluding filtering boundaries. Broad
            // channel tolerances tolerate color conversion, not unknown geometry.
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int p = ((y + dy) * width + x + dx) * 4;
                    if (Math.Abs(data[p] - r) > 45 || Math.Abs(data[p + 1] - g) > 45 ||
                        Math.Abs(data[p + 2] - b) > 45 || data[p + 3] < 220) return false;
                }
            return true;
        }
    }
}
