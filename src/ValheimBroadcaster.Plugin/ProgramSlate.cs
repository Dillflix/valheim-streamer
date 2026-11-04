using System;
using UnityEngine;

namespace ValheimBroadcaster.Plugin;

// An owned, pre-rendered broadcast card. It is independent of IMGUI, fonts,
// game HUDs and the backbuffer, so diagnostic text never enters the program.
internal sealed class ProgramSlate : IDisposable
{
    private Texture2D? texture;
    internal Texture Texture => texture ? texture : Create();
    internal bool ScenicTransition { get; private set; }
    internal void SetScenicTransition(bool value)
    {
        if (ScenicTransition == value) return;
        ScenicTransition = value; Dispose();
    }

    private Texture2D Create()
    {
        const int width = 640, height = 360;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            float glow = Mathf.Clamp01(1f - Mathf.Abs(y - 190f) / 190f);
            var color = new Color32((byte)(7 + glow * 5), (byte)(13 + glow * 10), (byte)(17 + glow * 11), 255);
            for (int x = 0; x < width; x++) pixels[y * width + x] = color;
        }
        var gold = new Color32(198, 160, 95, 255);
        var white = new Color32(232, 231, 219, 255);
        var muted = new Color32(145, 166, 169, 255);
        // Coordinates are deliberately bottom-up, matching Unity texture UVs.
        Line(pixels, width, 250, 194, 140, 2, gold);
        Text(pixels, width, height, "VALHEIM", 218, 4, white);
        Text(pixels, width, height, "BROADCAST", 163, 2, gold);
        Text(pixels, width, height, ScenicTransition ? "EXPLORING THE WORLD" : "RETURNING TO THE WORLD", 111, 2, muted);
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        { name = "Broadcaster clean recovery card", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static void Line(Color32[] pixels, int width, int x, int y, int length, int thickness, Color32 color)
    {
        for (int dy = 0; dy < thickness; dy++)
            for (int dx = 0; dx < length; dx++) pixels[(y + dy) * width + x + dx] = color;
    }

    private static void Text(Color32[] pixels, int width, int height, string text, int bottom, int scale, Color32 color)
    {
        int left = (width - (text.Length * 6 - 1) * scale) / 2;
        for (int i = 0; i < text.Length; i++)
        {
            string rows = Glyph(text[i]);
            for (int row = 0; row < 7; row++)
                for (int column = 0; column < 5; column++)
                {
                    if (rows[row * 5 + column] != '1') continue;
                    int x = left + (i * 6 + column) * scale, y = bottom + (6 - row) * scale;
                    for (int dy = 0; dy < scale && y + dy < height; dy++)
                        for (int dx = 0; dx < scale && x + dx < width; dx++)
                            if (x + dx >= 0 && y + dy >= 0) pixels[(y + dy) * width + x + dx] = color;
                }
        }
    }

    // A small original bitmap alphabet for the fixed card, not a shipped font asset.
    private static string Glyph(char value) => value switch
    {
        'A' => "01110" + "10001" + "10001" + "11111" + "10001" + "10001" + "10001",
        'B' => "11110" + "10001" + "10001" + "11110" + "10001" + "10001" + "11110",
        'C' => "01111" + "10000" + "10000" + "10000" + "10000" + "10000" + "01111",
        'D' => "11110" + "10001" + "10001" + "10001" + "10001" + "10001" + "11110",
        'E' => "11111" + "10000" + "10000" + "11110" + "10000" + "10000" + "11111",
        'G' => "01110" + "10001" + "10000" + "10111" + "10001" + "10001" + "01110",
        'H' => "10001" + "10001" + "10001" + "11111" + "10001" + "10001" + "10001",
        'I' => "11111" + "00100" + "00100" + "00100" + "00100" + "00100" + "11111",
        'L' => "10000" + "10000" + "10000" + "10000" + "10000" + "10000" + "11111",
        'M' => "10001" + "11011" + "10101" + "10101" + "10001" + "10001" + "10001",
        'N' => "10001" + "11001" + "10101" + "10011" + "10001" + "10001" + "10001",
        'O' => "01110" + "10001" + "10001" + "10001" + "10001" + "10001" + "01110",
        'P' => "11110" + "10001" + "10001" + "11110" + "10000" + "10000" + "10000",
        'R' => "11110" + "10001" + "10001" + "11110" + "10100" + "10010" + "10001",
        'S' => "01111" + "10000" + "10000" + "01110" + "00001" + "00001" + "11110",
        'T' => "11111" + "00100" + "00100" + "00100" + "00100" + "00100" + "00100",
        'U' => "10001" + "10001" + "10001" + "10001" + "10001" + "10001" + "01110",
        'V' => "10001" + "10001" + "10001" + "10001" + "10001" + "01010" + "00100",
        'W' => "10001" + "10001" + "10001" + "10101" + "10101" + "10101" + "01010",
        'X' => "10001" + "10001" + "01010" + "00100" + "01010" + "10001" + "10001",
        _ => "00000" + "00000" + "00000" + "00000" + "00000" + "00000" + "00000"
    };

    public void Dispose()
    {
        if (texture) UnityEngine.Object.Destroy(texture);
        texture = null;
    }
}
