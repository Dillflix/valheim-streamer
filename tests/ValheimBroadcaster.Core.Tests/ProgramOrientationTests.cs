using System;
using System.Collections.Generic;
using ValheimBroadcaster.Core;

internal static class ProgramOrientationTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("Orientation detects native bottom-first calibration rows", BottomFirst);
        yield return ("Orientation detects encoder-ready top-first calibration rows", TopFirst);
        yield return ("Orientation rejects horizontal mirroring and rotations", Mirrored);
        yield return ("Orientation rejects blank or incomplete GPU data", Unavailable);
        yield return ("Orientation rejects invalid dimensions without indexing data", Dimensions);
        yield return ("Orientation tolerates bounded color conversion", Conversion);
        yield return ("Orientation rejects inconsistent quadrants", Inconsistent);
        yield return ("Orientation checks more than one pixel per corner", Corruption);
    }
    private const int Width = ProgramOrientationPolicy.ProbeWidth, Height = ProgramOrientationPolicy.ProbeHeight;
    private static ProgramRowOrder Detect(byte[] data) => ProgramOrientationPolicy.Detect(data, Width, Height);
    private static void Check(bool condition) { if (!condition) throw new Exception("Orientation calibration assertion failed."); }
    private static void BottomFirst() => Check(Detect(ProgramOrientationPolicy.CreateBottomUpProbe()) == ProgramRowOrder.BottomFirst);
    private static void TopFirst() => Check(Detect(Transform(true, false)) == ProgramRowOrder.TopFirst);
    private static void Mirrored()
    {
        Check(Detect(Transform(false, true)) == ProgramRowOrder.Unknown);
        Check(Detect(Transform(true, true)) == ProgramRowOrder.Unknown);
    }
    private static void Unavailable()
    {
        Check(Detect(new byte[Width * Height * 4]) == ProgramRowOrder.Unknown);
        Check(Detect(new byte[Width * Height * 4 - 1]) == ProgramRowOrder.Unknown);
        byte[] transparent = ProgramOrientationPolicy.CreateBottomUpProbe();
        for (int i = 3; i < transparent.Length; i += 4) transparent[i] = 0;
        Check(Detect(transparent) == ProgramRowOrder.Unknown);
    }
    private static void Dimensions()
    {
        byte[] data = ProgramOrientationPolicy.CreateBottomUpProbe();
        Check(ProgramOrientationPolicy.Detect(data, int.MaxValue, int.MaxValue) == ProgramRowOrder.Unknown);
        Check(ProgramOrientationPolicy.Detect(data, -1, 16) == ProgramRowOrder.Unknown);
        Check(ProgramOrientationPolicy.Detect(new byte[64], 4, 4) == ProgramRowOrder.Unknown);
    }
    private static void Conversion()
    {
        byte[] data = Transform(true, false);
        for (int i = 0; i < data.Length; i++) data[i] = data[i] == 0 ? (byte)20 : (byte)235;
        Check(Detect(data) == ProgramRowOrder.TopFirst);
    }
    private static void Inconsistent()
    {
        byte[] data = Transform(true, false);
        for (int y = 0; y < Height / 2; y++)
            for (int x = 0; x < Width / 2; x++) data[(y * Width + x) * 4 + 2] = 0;
        Check(Detect(data) == ProgramRowOrder.Unknown);
    }
    private static void Corruption()
    {
        byte[] data = Transform(true, false);
        data[((Height / 4 + 1) * Width + Width / 4 + 1) * 4 + 2] = 0;
        Check(Detect(data) == ProgramRowOrder.Unknown);
    }
    private static byte[] Transform(bool vertical, bool horizontal)
    {
        byte[] source = ProgramOrientationPolicy.CreateBottomUpProbe(), result = new byte[source.Length];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                Array.Copy(source, ((vertical ? Height - y - 1 : y) * Width + (horizontal ? Width - x - 1 : x)) * 4,
                    result, (y * Width + x) * 4, 4);
        return result;
    }
}
