using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The Armory Holdout's surfaces: hand-painted pixel textures (the same rules as his key: limited ramps,
/// light from the top-left, a few soft steps, soft blotches like brushwork, a soft darker line on seams,
/// no dither, no black outlines), tiled at a fixed size per metre so nothing stretches:
/// - deck plates on the floor, bolted, scuffed along the walkways, oil stains;
/// - wall panels: a kick plate, the station's oxide-red band at waist height, seams, rivet lines, a
///   grille, grime gathering near the floor and rust streaks running down from the seams;
/// - ceiling panels on a T-bar grid, some perforated;
/// - pillars: bolted column plates with collars;
/// - cargo crates (one panel per metre, frames lined up with the crate edges);
/// - gunmetal for racks, ribs and fixtures; worn hazard stripes.
/// The room shell (floor, ceiling, walls) gets meshes split into ~1 m cells, so the PS1 texture warp
/// and vertex lighting stay small instead of smearing across a 22 m polygon.
/// Menu: Tools / Armory / Rebuild Surfaces (with ArmoryHoldout open). Saves the scene.
/// </summary>
public static class ArmorySurfaceBuilder
{
    const string Tex = "Assets/Textures/Station/";
    const string Mats = "Assets/Materials/Station/";
    const string Meshes = "Assets/Models/Armory/";
    const int Steps = 3;

    // Ramps, darkest to lightest.
    static readonly Color32[] Steel =
        { C(34, 36, 40), C(54, 57, 62), C(76, 80, 85), C(100, 103, 107), C(126, 127, 126), C(156, 153, 145) };
    static readonly Color32[] Deck =
        { C(26, 26, 28), C(42, 42, 44), C(60, 59, 58), C(80, 78, 74), C(102, 98, 90), C(126, 119, 106) };
    static readonly Color32[] Oxide =
        { C(46, 16, 12), C(76, 24, 16), C(106, 36, 22), C(134, 50, 28), C(160, 68, 36), C(184, 92, 50) };
    static readonly Color32[] Rust =
        { C(40, 22, 14), C(62, 34, 18), C(88, 48, 24), C(112, 64, 32), C(136, 82, 42), C(160, 102, 56) };
    static readonly Color32[] Olive =
        { C(32, 35, 22), C(48, 53, 32), C(66, 72, 42), C(86, 92, 54), C(108, 112, 68), C(132, 134, 86) };
    static readonly Color32[] Hazard =
        { C(60, 44, 10), C(104, 78, 18), C(146, 112, 28), C(182, 142, 38), C(208, 168, 52), C(226, 192, 82) };
    static readonly Color32[] Ink =
        { C(16, 16, 15), C(24, 24, 22), C(34, 33, 30), C(46, 44, 40), C(58, 56, 50), C(70, 67, 60) };
    static readonly Color32[] Stencil =
        { C(70, 68, 60), C(104, 100, 88), C(136, 132, 114), C(164, 158, 136), C(186, 180, 156), C(204, 198, 174) };

    static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);

    static Color Shade(Color32[] ramp, float f)
    {
        f = Mathf.Clamp(f, 0f, ramp.Length - 1);
        int i = Mathf.Min(ramp.Length - 2, Mathf.FloorToInt(f));
        return Color.Lerp(ramp[i], ramp[i + 1], Mathf.Round((f - i) * Steps) / Steps);
    }

    /// <summary>Soft blotch noise -0.5..0.5, tiling with period <paramref name="period"/> pixels.</summary>
    static float N(float x, float y, float scale, int seed, int periodX, int periodY)
    {
        // Tile by blending the noise across the period (keeps textures seamless).
        float fx = x / periodX, fy = y / periodY;
        float a = PixelFont.Blotch(x * scale, y * scale, seed), b = PixelFont.Blotch((x - periodX) * scale, y * scale, seed);
        float c = PixelFont.Blotch(x * scale, (y - periodY) * scale, seed), d = PixelFont.Blotch((x - periodX) * scale, (y - periodY) * scale, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy) - .5f;
    }

    [MenuItem("Tools/Armory/Rebuild Surfaces")]
    static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "ArmoryHoldout") return "Open ArmoryHoldout first (active scene is " + scene.name + ")";
        foreach (string dir in new[] { "Assets/Textures", "Assets/Textures/Station", "Assets/Models/Armory" })
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(dir).Replace('\\', '/'), System.IO.Path.GetFileName(dir));

        // ---- textures and materials (world tiles per metre: across walls, up walls, on floors) ----
        Material floor = Mat("Station_Floor", PaintDeck(), new Vector4(.5f, .5f, .5f, 0f), .35f);
        Material wall = Mat("Station_Wall", PaintWall(), new Vector4(.5f, .25f, .5f, 0f), .35f);
        Material ceiling = Mat("Station_Ceiling", PaintCeiling(), new Vector4(.5f, .25f, .5f, 0f), .35f);
        Material pillar = Mat("Station_Pillar", PaintPillar(), Vector4.zero, .3f);
        Material metal = Mat("Station_Rack", PaintGunmetal(), new Vector4(1f, 1f, 1f, 0f), .3f);
        Material hazard = Mat("Station_Hazard", PaintHazard(), new Vector4(2f, 2f, 2f, 0f), .3f);
        Material crate = Mat("Station_Crate", PaintCrate(), Vector4.zero, .3f);

        // ---- the room shell: ~1 m cells ----
        var level = GameObject.Find("Level").transform;
        string log = "";
        int meshes = 0;
        foreach (Transform t in level)
        {
            var mf = t.GetComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            if (mf == null || mr == null) continue;
            switch (t.name)
            {
                case "Floor": Shell(t, "Floor", floor); meshes++; break;
                case "Ceiling": Shell(t, "Ceiling", ceiling); meshes++; break;
                case "Wall N": case "Wall S": case "Wall E": case "Wall W": Shell(t, t.name.Replace(" ", ""), wall); meshes++; break;
                case "Pillar":
                    mf.sharedMesh = SaveMesh(GridBox(t.lossyScale, 1f, new Vector2(t.lossyScale.x, t.lossyScale.y)), $"Pillar_{Key(t.lossyScale)}");
                    mr.sharedMaterial = pillar; meshes++;
                    break;
                case "Crate": case "Barricade":
                    mf.sharedMesh = SaveMesh(GridBox(t.lossyScale, 1f, Vector2.one), $"Crate_{Key(t.lossyScale)}");
                    mr.sharedMaterial = crate; meshes++;
                    break;
            }
        }
        log += $"{meshes} meshes (shell + crates)\n";
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return log + "textures: deck, wall, ceiling, pillar, gunmetal, hazard, crate; saved";
    }

    static string Key(Vector3 s) => $"{s.x:F1}x{s.y:F1}x{s.z:F1}".Replace('.', '_');

    static void Shell(Transform t, string name, Material mat)
    {
        t.GetComponent<MeshFilter>().sharedMesh = SaveMesh(GridBox(t.lossyScale, 1f, Vector2.zero), "Shell_" + name);
        t.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // ================= meshes =================

    /// <summary>A unit cube (like the primitive, so colliders and transforms stay as they are) with each
    /// face split into cells about <paramref name="cell"/> metres across at <paramref name="size"/>.
    /// <paramref name="tile"/> (across, up) gives each face UVs that repeat once per that many metres (rounded, so a
    /// texture's frame lines up with the box's edges); otherwise 0..1 per face.</summary>
    static Mesh GridBox(Vector3 size, float cell, Vector2 tile)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        void Face(Vector3 nrm, Vector3 u, Vector3 w, float su, float sw)
        {
            int nu = Mathf.Max(1, Mathf.RoundToInt(su / cell)), nw = Mathf.Max(1, Mathf.RoundToInt(sw / cell));
            bool side = nrm.y == 0f;
            float tsu = tile.x, tsw = side ? tile.y : tile.x;
            float tu = tsu > 0f ? Mathf.Max(1, Mathf.RoundToInt(su / tsu)) : 1f, tw = tsw > 0f ? Mathf.Max(1, Mathf.RoundToInt(sw / tsw)) : 1f;
            int b = v.Count;
            for (int j = 0; j <= nw; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float fu = (float)i / nu, fw = (float)j / nw;
                    v.Add(nrm * .5f + u * (fu - .5f) + w * (fw - .5f));
                    n.Add(nrm);
                    uv.Add(new Vector2(fu * tu, fw * tw));
                }
            bool flip = Vector3.Dot(Vector3.Cross(w, u), nrm) < 0f;   // front faces: Cross(b - a, c - a) along the normal
            for (int j = 0; j < nw; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = b + j * (nu + 1) + i, c = a + nu + 1;
                    if (!flip) tris.AddRange(new[] { a, c, a + 1, a + 1, c, c + 1 });
                    else tris.AddRange(new[] { a, a + 1, c, a + 1, c + 1, c });
                }
        }
        Face(Vector3.right, Vector3.back, Vector3.up, size.z, size.y);
        Face(Vector3.left, Vector3.forward, Vector3.up, size.z, size.y);
        Face(Vector3.forward, Vector3.right, Vector3.up, size.x, size.y);
        Face(Vector3.back, Vector3.left, Vector3.up, size.x, size.y);
        Face(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
        Face(Vector3.down, Vector3.right, Vector3.back, size.x, size.z);
        var m = new Mesh();
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    static Mesh SaveMesh(Mesh m, string name)
    {
        m.name = name;
        string path = Meshes + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
        existing.Clear();
        existing.SetVertices(m.vertices); existing.SetNormals(m.normals); existing.SetUVs(0, m.uv); existing.SetTriangles(m.triangles, 0);
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static Material Mat(string name, Texture2D tex, Vector4 world, float affine)
    {
        string path = Mats + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.SetFloat("_Affine", affine);
        m.SetFloat("_Lighting", 1f);
        bool worldUV = world != Vector4.zero;
        m.SetFloat("_WorldUV", worldUV ? 1f : 0f);
        if (worldUV) { m.EnableKeyword("_WORLD_UV"); m.SetVector("_WorldUVScale", world); }
        else m.DisableKeyword("_WORLD_UV");
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ================= painting =================

    /// <summary>Deck: 64 px = 2 m. Four bolted 1 m plates, raised edges lit from the top-left, scuffed
    /// lighter along the walkways, oil stains, rust round a few bolts.</summary>
    static Texture2D PaintDeck()
    {
        const int s = 64, plate = 32;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                int lx = x % plate, ly = y % plate;
                // Broad brushwork only: fine noise turns to speckle under the screen dither.
                float f = 2.7f + 1.1f * N(x, y, .08f, 31, s, s) + .2f * N(x, y, .25f, 32, s, s);
                f += .3f * ((plate - ly) / (float)plate - .5f);                               // plates catch light toward the top
                if (N(x, y, .12f, 33, s, s) > .3f) f -= .7f;                                 // a few oil stains
                if (N(x, y, .1f, 34, s, s) < -.25f) f += .45f;                               // worn lighter where people walk
                if (ly == plate - 1 || lx == 0) f += 1.1f;                                   // raised edge, top-left lit
                if (ly == 0 || lx == plate - 1) f -= 1.3f;                                   // its shadow side
                Color c = Shade(Deck, f);
                px[y * s + x] = c;
            }
        // Bolts: a lit pixel and a shadow pixel, rust bleeding round some.
        for (int py = 0; py < s; py += plate)
            for (int pxx = 0; pxx < s; pxx += plate)
                foreach (var o in new[] { new Vector2Int(3, 3), new Vector2Int(plate - 4, 3), new Vector2Int(3, plate - 4), new Vector2Int(plate - 4, plate - 4) })
                {
                    int bx = pxx + o.x, by = py + o.y;
                    if (PixelFont.Blotch(bx * .7f, by * .7f, 36) > .62f)
                        for (int dy = -2; dy <= 1; dy++) for (int dx = -1; dx <= 2; dx++)
                        { int i = (by + dy + s) % s * s + (bx + dx + s) % s; px[i] = Color.Lerp(px[i], Shade(Rust, 2.2f), .6f); }
                    px[by * s + bx] = Shade(Deck, 4.6f);
                    px[(by - 1 + s) % s * s + (bx + 1) % s] = Shade(Deck, .8f);
                }
        return PixelFont.Save(px, s, s, Tex + "Deck.png");
    }

    /// <summary>Wall: 64 x 128 px = 2 m across, 4 m up (v = 0 at the floor). Kick plate, the oxide-red band,
    /// two 1 m panels with seams and rivet lines, a grille, a darker strip under the ceiling; grime near
    /// the floor, rust streaks running down from the seams.</summary>
    static Texture2D PaintWall()
    {
        const int w = 64, h = 128;   // 32 px per metre
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int lx = x % 32;
                float noise = .9f * N(x, y, .07f, 41, w, h) + .15f * N(x, y, .25f, 42, w, h);
                float f = 2.6f + noise + .5f * (y / (float)h);                                // lighter going up
                Color32[] ramp = Steel;
                if (y < 13) { ramp = Steel; f = 1.6f + noise + (N(x, y, .2f, 43, w, h) > .22f ? .6f : 0f); }   // kick plate, scuffed
                else if (y >= 36 && y < 41) { ramp = Oxide; f = 2.8f + noise; if (N(x, y, .3f, 44, w, h) > .32f) { ramp = Steel; f = 2.4f + noise; } }  // red band, chipped
                else if (y >= 120) f = 1.6f + noise;                                         // under the ceiling
                // Seams: a groove, shadow above, lit lip below.
                if (y == 13 || y == 41 || y == 97 || y == 120) { f = 1f; ramp = Steel; }
                if (y == 12 || y == 40 || y == 96 || y == 119) f += .8f;
                if (y > 41 && y < 120 && lx == 0) { f = 1.1f; ramp = Steel; }
                if (y > 41 && y < 120 && lx == 1) f += .6f;
                // Grime collects near the floor and under the band.
                if (y < 26) f -= (26 - y) / 26f * 1.1f;
                if (y > 41 && y < 50 && N(x, y, .15f, 45, w, h) > 0f) f -= .35f;
                px[y * w + x] = Shade(ramp, f);
            }
        // Rust streaks running down from a few rivets and seams: one clean run each, fading as it goes.
        var rng = new System.Random(41);
        foreach (int seam in new[] { 96, 119, 40 })
            for (int x = 2; x < w; x += 2)
            {
                if (rng.NextDouble() > .16) continue;
                int len = 8 + rng.Next(seam == 40 ? 10 : 22);
                for (int i = 0; i < len && seam - 1 - i > 13; i++)
                {
                    int idx = (seam - 1 - i) * w + x;
                    float a = .7f * (1f - (float)i / len);
                    px[idx] = Color.Lerp(px[idx], Shade(Rust, 2.2f), Mathf.Round(a * 3f) / 3f);
                }
            }
        // Rivet lines along the seams.
        foreach (int ry in new[] { 7, 44, 100, 116 })
            for (int rx = 4; rx < w; rx += 8)
            { px[ry * w + rx] = Shade(Steel, 4.8f); px[(ry - 1) * w + rx + 1] = Shade(Steel, .6f); }
        // A grille on one panel (horizontal slots, lit lip under each).
        for (int y = 102; y < 114; y += 3)
            for (int x = 40; x < 58; x++) { px[y * w + x] = Shade(Ink, 1f); px[(y - 1) * w + x] = Shade(Steel, 3.8f); }
        return PixelFont.Save(px, w, h, Tex + "Wall.png");
    }

    /// <summary>Ceiling: 64 px = 2 m. 1 m panels in a lit T-bar grid; every other one perforated.</summary>
    static Texture2D PaintCeiling()
    {
        const int s = 64;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                int lx = x % 32, ly = y % 32;
                bool perforated = ((x / 32) + (y / 32)) % 2 == 0;
                float f = 2f + .8f * N(x, y, .08f, 51, s, s);
                if (lx < 3 || ly < 3) f = 2.7f + .3f * N(x, y, .2f, 52, s, s);              // T-bar, wide and dull
                else if (lx == 3 || ly == 3) f -= .8f;                                      // shadow inside the bar
                else if (perforated && lx % 4 == 1 && ly % 4 == 1) f -= .8f;
                if (N(x, y, .1f, 53, s, s) > .32f) f -= .5f;                                 // water stains
                px[y * s + x] = Shade(Steel, f);
            }
        return PixelFont.Save(px, s, s, Tex + "Ceiling.png");
    }

    /// <summary>Pillar: 32 x 128 px = 0.8 m round, 4 m up. Bolted plates with collars every metre,
    /// bolt columns, grime at the foot.</summary>
    static Texture2D PaintPillar()
    {
        const int w = 32, h = 128;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float noise = N(x, y, .15f, 61, w, h) + .4f * N(x, y, .4f, 62, w, h);
                float f = 2.3f + noise + .4f * (y / (float)h);
                int ly = y % 32;
                if (ly < 4) f = 3.3f + noise * .5f;                                         // collar
                if (ly == 4) f = 1f;                                                        // its shadow
                if (ly == 3) f += .7f;
                if (x == 0 || x == w - 1) f -= .8f;                                         // edges round the corner
                if (y < 20) f -= (20 - y) / 20f * 1.2f;
                if (N(x, y * .1f, .8f, 63, w, h) > .32f && ly > 6) f -= .6f;                  // drip marks
                px[y * w + x] = Shade(Steel, f);
            }
        for (int y = 8; y < h; y += 6)
            foreach (int x in new[] { 4, w - 5 })
            { if (y % 32 < 6) continue; px[y * w + x] = Shade(Steel, 4.8f); px[(y - 1) * w + x + 1] = Shade(Steel, .6f); }
        return PixelFont.Save(px, w, h, Tex + "Pillar.png");
    }

    /// <summary>Gunmetal for racks, ribs and fixtures: 32 px = 1 m, brushed, scratched, paint chips.</summary>
    static Texture2D PaintGunmetal()
    {
        const int s = 32;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float f = 1.9f + .9f * N(x, y, .15f, 71, s, s) + .5f * N(x * .15f, y, .9f, 72, s, s);   // brushed sideways
                if (N(x, y, .5f, 73, s, s) > .32f) f += 1.1f;                                         // bare-metal chips
                px[y * s + x] = Shade(Steel, f);
            }
        // A few scratches.
        var rng = new System.Random(7);
        for (int k = 0; k < 6; k++)
        {
            int x0 = rng.Next(s), y0 = rng.Next(s), len = 3 + rng.Next(5), dir = rng.Next(2) == 0 ? 1 : -1;
            for (int i = 0; i < len; i++) px[(y0 + i * dir + s) % s * s + (x0 + i) % s] = Shade(Steel, 4.2f);
        }
        return PixelFont.Save(px, s, s, Tex + "Gunmetal.png");
    }

    /// <summary>Hazard stripes: 32 px = 0.5 m, 45 degrees, worn through to metal at the edges, grimy.</summary>
    static Texture2D PaintHazard()
    {
        const int s = 32;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float noise = N(x, y, .2f, 81, s, s);
                bool yellow = ((x + y) / 8) % 2 == 0;
                Color c = yellow ? Shade(Hazard, 3.2f + 1.2f * noise + .4f * (y / (float)s)) : Shade(Ink, 2.4f + noise);
                if (N(x, y, .45f, 82, s, s) > .3f) c = Shade(Steel, 2.4f + noise);            // worn through
                if (N(x, y, .2f, 83, s, s) < -.3f) c = Color.Lerp(c, Shade(Ink, 1f), .5f);    // grime
                px[y * s + x] = c;
            }
        return PixelFont.Save(px, s, s, Tex + "Hazard.png");
    }

    /// <summary>A cargo crate side: 64 px = one 1 m panel. Olive boards in a frame, steel corner brackets,
    /// a faded stencil block and a small yellow label, scuffed edges.</summary>
    static Texture2D PaintCrate()
    {
        const int s = 64;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float noise = N(x, y, .12f, 91, s, s) + .4f * N(x, y, .35f, 92, s, s);
                bool frame = x < 6 || x >= s - 6 || y < 6 || y >= s - 6;
                bool slat = !frame && (y == 24 || y == 40);
                float f = 2.4f + noise + .35f * (y / (float)s) - .2f * (x / (float)s);
                if (frame) f += .6f;
                if (slat) f -= 1.1f;
                if (!frame && (y == 23 || y == 39)) f += .5f;
                if (x == 6 || y == s - 7) f -= .8f;                                         // frame's inner shadow (light from top-left)
                if (x == s - 7 || y == 6) f += .4f;
                if (frame && (x == 0 || y == 0 || x == s - 1 || y == s - 1)) f -= .7f;       // worn outer edge
                if (N(x, y, .6f, 93, s, s) > .3f && frame) f += .9f;                          // scuffs to bare wood
                Color c = Shade(Olive, f);
                bool bracket = (x < 10 || x >= s - 10) && (y < 10 || y >= s - 10);
                if (bracket) c = Shade(Steel, 2.6f + noise + ((x < 10) == (y >= s - 10) ? .4f : -.3f));
                px[y * s + x] = c;
            }
        // Faded stencil block (illegible on purpose: reads as text without mirroring problems) and a label.
        for (int y = 44; y < 54; y++)
            for (int x = 14; x < 50; x++)
            {
                bool on = (y == 44 || y == 45 || y == 48 || y == 49 || y == 52 || y == 53) && ((x / 3) % 3 != 2) && x < 14 + 36 - (y % 3) * 4;
                if (on && PixelFont.Blotch(x * .5f, y * .5f, 94) < .7f) px[y * s + x] = Shade(Stencil, 2.6f + N(x, y, .4f, 95, s, s));
            }
        for (int y = 12; y < 21; y++)
            for (int x = 40; x < 52; x++)
                px[y * s + x] = (y == 12 || x == 51) ? Shade(Hazard, 1.2f) : Shade(Hazard, 3.6f + N(x, y, .5f, 96, s, s));
        for (int x = 42; x < 50; x += 2) px[17 * s + x] = Shade(Ink, 1.5f);
        return PixelFont.Save(px, s, s, Tex + "Crate.png");
    }
}
