using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Structure and fixtures for the Armory Holdout, so it reads as a working part of a station instead of
/// a box: steel beams over the pillars (the pillars now hold something up) with lighter joists between,
/// flanges and capitals on the pillars, wall fixtures in the gaps between vents, lockers, ribs and
/// racks (junction boxes, fire cabinets, intercoms, breaker panels, each fed by a conduit from the
/// ceiling), sagging cable bundles overhead, painted walkway lines from POD 3 to the armory and drain
/// grates. Painted in the same pixel style as the walls (ArmorySurfaceBuilder). No colliders: nothing
/// here changes how the room plays. Everything lives under Level/Detail, rebuilt on each run.
/// Menu: Tools / Armory / Rebuild Detail (with ArmoryHoldout open). Saves the scene.
/// </summary>
public static class ArmoryDetailBuilder
{
    const string Tex = "Assets/Textures/Station/";
    const string Mats = "Assets/Materials/Station/";
    const int Steps = 3;
    const float RoomX = 11f, RoomZ = 9f, Height = 4f;

    static readonly Color32[] Steel =
        { C(34, 36, 40), C(54, 57, 62), C(76, 80, 85), C(100, 103, 107), C(126, 127, 126), C(156, 153, 145) };
    static readonly Color32[] Red =
        { C(60, 12, 9), C(96, 18, 12), C(134, 28, 18), C(166, 42, 26), C(192, 62, 38), C(214, 94, 62) };
    static readonly Color32[] Hazard =
        { C(60, 44, 10), C(104, 78, 18), C(146, 112, 28), C(182, 142, 38), C(208, 168, 52), C(226, 192, 82) };
    static readonly Color32[] Ink =
        { C(12, 12, 12), C(20, 20, 19), C(30, 30, 28), C(42, 41, 38), C(54, 53, 48), C(66, 64, 58) };
    static readonly Color32[] Glass =
        { C(16, 20, 26), C(26, 32, 40), C(40, 48, 58), C(58, 68, 80), C(84, 96, 108), C(120, 132, 140) };
    static readonly Color32[] Deck =
        { C(26, 26, 28), C(42, 42, 44), C(60, 59, 58), C(80, 78, 74), C(102, 98, 90), C(126, 119, 106) };
    static readonly Color Ivory = new Color(.82f, .8f, .72f);

    static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);
    static Color Shade(Color32[] ramp, float f)
    {
        f = Mathf.Clamp(f, 0f, ramp.Length - 1);
        int i = Mathf.Min(ramp.Length - 2, Mathf.FloorToInt(f));
        return Color.Lerp(ramp[i], ramp[i + 1], Mathf.Round((f - i) * Steps) / Steps);
    }
    static float B(float x, float y, float scale, int seed) => PixelFont.Blotch(x * scale, y * scale, seed) - .5f;

    // Wall fixtures: (kind, wall, position along the wall, height of its centre).
    // Walls: W = x -11 facing +x, E = x 11 facing -x, S = z -9 facing +z, N = z 9 facing -z.
    enum Kind { Junction, Fire, Intercom, Fuse }
    static readonly (Kind kind, char wall, float along, float y)[] Fixtures =
    {
        (Kind.Junction, 'W', -7f, 1.75f), (Kind.Intercom, 'W', -2.35f, 1.55f), (Kind.Fire, 'W', 5.8f, 1.3f),
        (Kind.Fuse, 'E', -7f, 1.6f), (Kind.Junction, 'E', 3.3f, 1.8f), (Kind.Fire, 'E', 7.9f, 1.3f),
        (Kind.Junction, 'S', -7.5f, 1.8f), (Kind.Intercom, 'S', -3.6f, 1.5f), (Kind.Fuse, 'S', 6.25f, 1.6f),
        (Kind.Fire, 'N', -9f, 1.3f), (Kind.Fuse, 'N', -4f, 1.6f), (Kind.Intercom, 'N', 0f, 1.5f), (Kind.Junction, 'N', 9f, 1.75f),
    };

    [MenuItem("Tools/Armory/Rebuild Detail")]
    static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "ArmoryHoldout") return "Open ArmoryHoldout first (active scene is " + scene.name + ")";
        var level = GameObject.Find("Level").transform;
        Transform old = level.Find("Detail");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("Detail").transform;
        root.SetParent(level, false);

        Material metal = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Station_Rack.mat");
        Material cable = Plain("Detail_Cable", new Color(.09f, .09f, .1f));
        Mesh pipe = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/Station/Pipe8.asset");
        int count = 0;

        // ---- ceiling structure ----
        var ceiling = Group(root, "Ceiling Structure");
        foreach (float z in new[] { -4f, 4f })
        {
            Box("Beam", ceiling, new Vector3(0f, Height - .19f, z), new Vector3(2f * RoomX - .44f, .38f, .3f), metal);
            Box("Beam Flange", ceiling, new Vector3(0f, Height - .39f, z), new Vector3(2f * RoomX - .44f, .04f, .4f), metal);
            count += 2;
        }
        foreach (float x in new[] { -8.5f, -2.75f, 2.75f, 8.5f })
        { Box("Joist", ceiling, new Vector3(x, Height - .11f, 0f), new Vector3(.18f, .22f, 2f * RoomZ - .44f), metal); count++; }
        foreach (Transform t in level)
        {
            if (t.name != "Pillar") continue;
            Vector3 p = t.position;
            Box("Pillar Capital", ceiling, new Vector3(p.x, Height - .48f, p.z), new Vector3(1.02f, .16f, 1.02f), metal);
            Box("Pillar Flange", ceiling, new Vector3(p.x, .03f, p.z), new Vector3(1.06f, .06f, 1.06f), metal);
            count += 2;
        }

        // ---- cables sagging between the main beams ----
        var cables = Group(root, "Cables");
        foreach (var (x, sag, thick) in new[] { (-7.1f, .55f, .07f), (-6.9f, .42f, .05f), (-1.6f, .5f, .08f), (3.9f, .62f, .06f), (4.15f, .45f, .05f) })
        { Sag(cables, new Vector3(x, Height - .36f, -3.85f), new Vector3(x + .1f, Height - .36f, 3.85f), sag, thick, cable); count += 10; }

        // ---- wall fixtures, each fed by a conduit from the ceiling ----
        var fixtures = Group(root, "Wall Fixtures");
        var faces = new Dictionary<Kind, Material>
        {
            { Kind.Junction, Painted("Fixture_Junction", PaintJunction()) },
            { Kind.Fire, Painted("Fixture_Fire", PaintFire()) },
            { Kind.Intercom, Painted("Fixture_Intercom", PaintIntercom()) },
            { Kind.Fuse, Painted("Fixture_Fuse", PaintFuse()) },
        };
        foreach (var f in Fixtures)
        {
            Vector3 size = f.kind switch
            {
                Kind.Junction => new Vector3(.45f, .55f, .14f),
                Kind.Fire => new Vector3(.5f, .75f, .16f),
                Kind.Intercom => new Vector3(.25f, .4f, .08f),
                _ => new Vector3(.6f, .8f, .12f),
            };
            Vector3 inward = f.wall switch { 'W' => Vector3.right, 'E' => Vector3.left, 'S' => Vector3.forward, _ => Vector3.back };
            Vector3 wallPoint = f.wall switch
            {
                'W' => new Vector3(-RoomX, f.y, f.along), 'E' => new Vector3(RoomX, f.y, f.along),
                'S' => new Vector3(f.along, f.y, -RoomZ), _ => new Vector3(f.along, f.y, RoomZ),
            };
            var holder = new GameObject(f.kind.ToString()).transform;
            holder.SetParent(fixtures, false);
            holder.position = wallPoint + inward * size.z * .5f;
            holder.rotation = Quaternion.LookRotation(inward);
            Box("Body", holder, Vector3.zero, size, metal, local: true);
            var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.transform.SetParent(holder, false);
            face.transform.localPosition = new Vector3(0f, 0f, size.z * .5f + .003f);
            face.transform.localRotation = Quaternion.LookRotation(Vector3.back);   // the quad's front looks down its -Z
            face.transform.localScale = new Vector3(size.x * .96f, size.y * .96f, 1f);
            face.GetComponent<MeshRenderer>().sharedMaterial = faces[f.kind];
            // Conduit from the top of the box up to the pipe run under the ceiling.
            float top = f.y + size.y * .5f, end = 3.5f;
            var c = new GameObject("Conduit", typeof(MeshFilter), typeof(MeshRenderer));
            c.transform.SetParent(holder, false);
            c.transform.position = wallPoint + inward * .06f + Vector3.up * ((top + end) * .5f - f.y);
            c.transform.localScale = new Vector3(.055f, end - top, .055f);
            c.GetComponent<MeshFilter>().sharedMesh = pipe;
            c.GetComponent<MeshRenderer>().sharedMaterial = metal;
            count += 3;
        }

        // ---- floor: painted walkway lines from POD 3 to the armory, drain grates ----
        var floor = Group(root, "Floor Markings");
        Material paint = WorldPainted("Floor_Paint", PaintFloorPaint(), new Vector4(1f, 1f, 1f, 0f));
        Material grate = Painted("Floor_Grate", PaintGrate());
        foreach (float x in new[] { -1.9f, 1.9f })
        { FloorQuad(floor, new Vector3(x, .012f, -.55f), new Vector2(.12f, 15.7f), paint); count++; }
        FloorQuad(floor, new Vector3(0f, .012f, -7.35f), new Vector2(3.92f, .12f), paint);
        foreach (var g in new[] { new Vector3(-9.6f, 0f, -8.2f), new Vector3(9.7f, 0f, 7.6f), new Vector3(-9.8f, 0f, 1.2f), new Vector3(3.2f, 0f, -8.3f) })
        { FloorQuad(floor, g + Vector3.up * .014f, new Vector2(1f, .5f), grate); count++; }
        count++;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return $"detail: {count} pieces (beams, joists, pillar caps, cables, {Fixtures.Length} wall fixtures, floor lines, grates); saved";
    }

    // ================= building helpers =================

    static Transform Group(Transform parent, string name)
    {
        var g = new GameObject(name).transform;
        g.SetParent(parent, false);
        return g;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool local = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        if (local) go.transform.localPosition = pos; else go.transform.position = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>A cable hanging between two points: a few straight lengths along a catenary-ish curve.</summary>
    static void Sag(Transform parent, Vector3 a, Vector3 b, float sag, float thick, Material mat)
    {
        const int Segments = 10;
        Vector3 Point(float t) => Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
        for (int i = 0; i < Segments; i++)
        {
            Vector3 p0 = Point((float)i / Segments), p1 = Point((float)(i + 1) / Segments);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Cable";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = (p0 + p1) * .5f;
            go.transform.rotation = Quaternion.LookRotation(p1 - p0);
            go.transform.localScale = new Vector3(thick, thick, Vector3.Distance(p0, p1) + thick * .5f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    static void FloorQuad(Transform parent, Vector3 centre, Vector2 size, Material mat)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "Floor Paint";
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(parent, false);
        q.transform.position = centre;
        q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // front (-Z) turned to face up
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        q.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static Material Painted(string name, Texture2D tex)
    {
        string path = Mats + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.SetFloat("_Affine", .2f);
        m.SetFloat("_Lighting", 1f);
        m.SetFloat("_WorldUV", 0f); m.DisableKeyword("_WORLD_UV");
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material WorldPainted(string name, Texture2D tex, Vector4 tiles)
    {
        var m = Painted(name, tex);
        m.SetFloat("_WorldUV", 1f); m.EnableKeyword("_WORLD_UV");
        m.SetVector("_WorldUVScale", tiles);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Plain(string name, Color c)
    {
        string path = Mats + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c);
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ================= painting (64 px per metre; light from the top-left) =================

    /// <summary>Steel plate with a raised rim lit from the top-left and screws in the corners.</summary>
    static Color[] Plate(int w, int h, Color32[] ramp, float baseF, int seed)
    {
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float f = baseF + .8f * B(x, y, .12f, seed) + .3f * (y / (float)h);
                if (x == 0 || y == h - 1) f += 1f;
                if (x == w - 1 || y == 0) f -= 1.2f;
                if (x == 2 || y == h - 3) f -= .7f;          // door seam just inside the rim
                if (x == 3 || y == h - 4) f += .4f;
                px[y * w + x] = Shade(ramp, f);
            }
        foreach (var s in new[] { new Vector2Int(1, 1), new Vector2Int(w - 2, 1), new Vector2Int(1, h - 2), new Vector2Int(w - 2, h - 2) })
            px[s.y * w + s.x] = Shade(Steel, 4.8f);
        return px;
    }

    static void Rect(Color[] px, int w, int x0, int y0, int x1, int y1, System.Func<int, int, Color> paint)
    {
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) px[y * w + x] = paint(x, y);
    }

    /// <summary>Junction box (0.45 x 0.55 m): grey door, a yellow high-voltage triangle, a cable gland.</summary>
    static Texture2D PaintJunction()
    {
        const int w = 28, h = 36;
        var px = Plate(w, h, Steel, 2.6f, 101);
        // Warning triangle, top centre: apex at row h-6, ten rows down, dark rim, yellow inside, a "!".
        for (int r = 0; r < 10; r++)
        {
            int hw = r / 2 + 1, ty = h - 6 - r;
            for (int x = -hw; x <= hw; x++)
            {
                bool edge = Mathf.Abs(x) == hw || r == 9;
                px[ty * w + w / 2 + x] = edge ? Shade(Ink, 1.5f) : Shade(Hazard, 3.8f + .3f * B(w / 2 + x, ty, .5f, 102));
            }
        }
        Rect(px, w, w / 2, h - 12, w / 2 + 1, h - 8, (x, y) => Shade(Ink, 1f));            // the "!"
        px[(h - 14) * w + w / 2] = Shade(Ink, 1f);
        // Label plate and gland.
        Rect(px, w, 7, 9, 21, 13, (x, y) => Shade(Steel, 4.2f));
        Rect(px, w, 8, 10, 20, 11, (x, y) => (x % 3 == 0) ? Shade(Ink, 2f) : Shade(Steel, 4.2f));
        Rect(px, w, 11, 2, 17, 5, (x, y) => Shade(Ink, 2.5f + (y == 4 ? 1f : 0f)));
        return PixelFont.Save(px, w, h, Tex + "Fixture_Junction.png");
    }

    /// <summary>Fire cabinet (0.5 x 0.75 m): red, a glass door with the extinguisher behind it, FIRE in white.</summary>
    static Texture2D PaintFire()
    {
        const int w = 32, h = 48;
        var px = Plate(w, h, Red, 2.8f, 111);
        // Glass window with the extinguisher showing through.
        Rect(px, w, 5, 12, w - 5, h - 5, (x, y) =>
        {
            bool frame = x == 5 || x == w - 6 || y == 12 || y == h - 6;
            if (frame) return Shade(Steel, x == 5 || y == h - 6 ? 4.2f : 1.4f);
            bool body = x >= 12 && x <= 19 && y >= 15 && y <= h - 13;
            bool head = x >= 14 && x <= 17 && y > h - 13 && y <= h - 10;
            bool hose = (x == 20 || x == 21) && y >= 20 && y <= h - 12;
            Color c = body ? Shade(Red, 2.4f + (x < 14 ? 1.2f : 0f) + .3f * B(x, y, .4f, 112))
                    : head ? Shade(Ink, 2.5f) : hose ? Shade(Ink, 1.5f)
                    : Shade(Glass, 1.6f + .3f * B(x, y, .3f, 113));
            if ((x - y + 40) % 17 == 0 || (x - y + 41) % 17 == 0) c = Color.Lerp(c, Shade(Glass, 5f), .45f);   // reflection streak
            return c;
        });
        PixelFont.Stamp(px, w, h, "FIRE", w / 2, 6, 1, Ivory, .1f, 114);
        return PixelFont.Save(px, w, h, Tex + "Fixture_Fire.png");
    }

    /// <summary>Intercom (0.25 x 0.4 m): speaker slots, a call button, a dead red lamp.</summary>
    static Texture2D PaintIntercom()
    {
        const int w = 16, h = 26;
        var px = Plate(w, h, Steel, 2.4f, 121);
        for (int y = 12; y < 22; y += 2) Rect(px, w, 4, y, 12, y + 1, (x, yy) => Shade(Ink, 1f));
        Rect(px, w, 6, 5, 10, 9, (x, y) => Shade(Steel, x < 8 && y > 6 ? 4.6f : 3.4f));       // button
        Rect(px, w, 12, 22, 14, 24, (x, y) => Shade(Red, x == 12 && y == 23 ? 3.5f : 2f));       // lamp
        return PixelFont.Save(px, w, h, Tex + "Fixture_Intercom.png");
    }

    /// <summary>Breaker panel (0.6 x 0.8 m): hazard band, a window on rows of breakers, two tripped.</summary>
    static Texture2D PaintFuse()
    {
        const int w = 40, h = 52;
        var px = Plate(w, h, Steel, 2.5f, 131);
        Rect(px, w, 3, h - 8, w - 3, h - 4, (x, y) => ((x + y) / 3) % 2 == 0 ? Shade(Hazard, 3.4f + .4f * B(x, y, .5f, 132)) : Shade(Ink, 1.5f));
        Rect(px, w, 6, 12, w - 6, h - 12, (x, y) => Shade(Ink, 1.2f));
        int k = 0;
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 6; col++, k++)
            {
                int bx = 8 + col * 4, by = 14 + row * 6;
                bool tripped = k == 7 || k == 16;
                Rect(px, w, bx, by, bx + 3, by + 5, (x, y) => Shade(Steel, x == bx ? 3.8f : 2.4f));
                int ty = tripped ? by + 1 : by + 3;
                Rect(px, w, bx + 1, ty, bx + 2, ty + 1, (x, y) => tripped ? Shade(Red, 4f) : Shade(Steel, 4.8f));
            }
        PixelFont.Stamp(px, w, h, "PWR", w / 2, 6, 1, Ivory * .9f, .15f, 133);
        return PixelFont.Save(px, w, h, Tex + "Fixture_Fuse.png");
    }

    /// <summary>Worn yellow floor paint (1 m tile): flaked through to the deck in places.</summary>
    static Texture2D PaintFloorPaint()
    {
        const int s = 32;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float wear = PixelFont.Blotch(x * .35f, y * .35f, 141);
                px[y * s + x] = wear > .7f ? Shade(Deck, 2.6f + B(x, y, .3f, 142))
                                           : Shade(Hazard, 2.8f + .8f * B(x, y, .2f, 143) - (wear > .6f ? .8f : 0f));
            }
        return PixelFont.Save(px, s, s, Tex + "Floor_Paint.png");
    }

    /// <summary>Drain grate (1 x 0.5 m): a steel frame round bars over black, lit edges toward the top-left.</summary>
    static Texture2D PaintGrate()
    {
        const int w = 32, h = 16;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool frame = x < 2 || x >= w - 2 || y < 2 || y >= h - 2;
                bool bar = x % 3 == 0;
                Color c = frame ? Shade(Steel, 2.6f + (x == 0 || y == h - 1 ? 1f : 0f) - (x == w - 1 || y == 0 ? 1f : 0f))
                        : bar ? Shade(Steel, 2.2f + .4f * B(x, y, .4f, 151)) : Shade(Ink, .6f);
                px[y * w + x] = c;
            }
        return PixelFont.Save(px, w, h, Tex + "Floor_Grate.png");
    }
}
