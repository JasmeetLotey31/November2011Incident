using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A 5x7 pixel font for station lettering (signs, locker terminals), painted straight into
/// textures so it stays crisp at the game's quarter resolution (the built-in font smears).
/// Also the painting helpers the signs share: soft stepped blotches like brushwork, no dither.
/// </summary>
public static class PixelFont
{
    static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
    {
        ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
        ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
        ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
        ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" },
        ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['I'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" },
        ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
        ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
        ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
        ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
        ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
        ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
        ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
        ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
        ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
        ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
        ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" },
        ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
        ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
        ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
        ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
        ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
        ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
        ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
        ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
        ['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
        ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
        ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
        ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
        ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
        ['/'] = new[] { "....#", "....#", "...#.", "..#..", ".#...", "#....", "#...." },
        ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." },
        [':'] = new[] { ".....", ".##..", ".##..", ".....", ".##..", ".##..", "....." },
        ['>'] = new[] { "#....", ".#...", "..#..", "...#.", "..#..", ".#...", "#...." },
        ['<'] = new[] { "....#", "...#.", "..#..", ".#...", "..#..", "...#.", "....#" },
        [' '] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." },
    };

    /// <summary>Width in glyph pixels (5 per letter + 1 gap).</summary>
    public static int Width(string text) => Mathf.Max(0, text.Length * 6 - 1);

    /// <summary>Is the glyph pixel at (gx, gy) of the text lit? gy = 0 is the top row.</summary>
    public static bool On(string text, int gx, int gy)
    {
        if (gy < 0 || gy > 6 || gx < 0) return false;
        int c = gx / 6, col = gx % 6;
        if (c >= text.Length || col == 5) return false;
        if (!Glyphs.TryGetValue(char.ToUpperInvariant(text[c]), out string[] g)) g = Glyphs[' '];
        return g[gy][col] == '#';
    }

    /// <summary>Stamp text into a pixel array (y up), centred on (cx, cy), each glyph pixel scale x scale.
    /// <paramref name="wear"/> chips the paint in soft blotches (0 = clean).</summary>
    public static void Stamp(Color[] px, int w, int h, string text, int cx, int cy, int scale, Color ink, float wear, int seed)
    {
        int tw = Width(text) * scale, th = 7 * scale;
        int x0 = cx - tw / 2, yTop = cy + th / 2;
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                if (!On(text, x / scale, y / scale)) continue;
                int px0 = x0 + x, py0 = yTop - 1 - y;
                if (px0 < 0 || py0 < 0 || px0 >= w || py0 >= h) continue;
                float chip = Blotch(px0 * .35f, py0 * .35f, seed);
                if (chip > 1f - wear) continue;                        // paint chipped off here
                Color under = px[py0 * w + px0];
                px[py0 * w + px0] = Color.Lerp(under, ink, chip > .6f - wear ? .7f : 1f);
            }
    }

    /// <summary>Soft value noise 0..1 (brushwork blotches).</summary>
    public static float Blotch(float x, float y, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float sx = x - xi, sy = y - yi;
        sx = sx * sx * (3f - 2f * sx); sy = sy * sy * (3f - 2f * sy);
        float H(int a, int b) { uint n = (uint)(a * 374761393 + b * 668265263 + seed * 1274126177); n = (n ^ (n >> 13)) * 1274126177u; return (n & 0xFFFF) / 65535f; }
        return Mathf.Lerp(Mathf.Lerp(H(xi, yi), H(xi + 1, yi), sx), Mathf.Lerp(H(xi, yi + 1), H(xi + 1, yi + 1), sx), sy);
    }

    /// <summary>A colour in a few soft steps between dark and light (t = 0..1).</summary>
    public static Color Stepped(Color dark, Color light, float t, int steps = 4) =>
        Color.Lerp(dark, light, Mathf.Round(Mathf.Clamp01(t) * steps) / steps);

    /// <summary>Save as a point-filtered, uncompressed, mipmap-free PNG (the project's PS1 rules).</summary>
    public static Texture2D Save(Color[] px, int w, int h, string path)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.SetPixels(px);
        tex.Apply();
        string dir = System.IO.Path.GetDirectoryName(path);
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.filterMode = FilterMode.Point;
        imp.mipmapEnabled = false;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(w, h));
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
