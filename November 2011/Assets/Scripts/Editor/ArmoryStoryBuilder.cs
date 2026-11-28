using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// What happened in the Armory, told by the room (Station Jupiter, 13 Nov 2011):
/// - the crew's last stand behind the barricade: brass everywhere, dropped magazines and papers, a
///   helmet in a pool of blood, a flashlight still on where it fell, its beam along the floor;
/// - three drag trails across the deck into the vents, blood sprayed round the vent openings;
/// - "IT HAS HIS FACE" written in blood on the west wall.
/// Blood is shaped polygons (no transparency: the PS1 way), painted like the rest of the room. No
/// colliders, nothing changes how it plays. Lives under Level/Story, rebuilt on each run.
/// Menu: Tools / Armory / Rebuild Story (with ArmoryHoldout open). Saves the scene.
/// </summary>
public static class ArmoryStoryBuilder
{
    const string Tex = "Assets/Textures/Station/";
    const string Mats = "Assets/Materials/Station/";
    const string Meshes = "Assets/Models/Armory/";
    const int Steps = 3;

    static readonly Color32[] Blood =
        { C(44, 5, 4), C(78, 9, 7), C(112, 15, 10), C(144, 24, 14), C(170, 38, 22), C(192, 58, 34) };
    static readonly Color32[] Paper =
        { C(70, 68, 60), C(104, 100, 88), C(136, 132, 116), C(164, 160, 142), C(186, 182, 164), C(204, 200, 182) };
    static Color32 C(byte r, byte g, byte b) => new Color32(r, g, b, 255);
    static Color Shade(Color32[] ramp, float f)
    {
        f = Mathf.Clamp(f, 0f, ramp.Length - 1);
        int i = Mathf.Min(ramp.Length - 2, Mathf.FloorToInt(f));
        return Color.Lerp(ramp[i], ramp[i + 1], Mathf.Round((f - i) * Steps) / Steps);
    }

    /// <summary>A mesh built up in world space (the Story group sits at the origin).</summary>
    sealed class Builder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
        public System.Func<Vector3, Vector2> map;

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f) (b, c) = (c, b);
            int i = v.Count;
            foreach (Vector3 p in new[] { a, b, c }) { v.Add(p); n.Add(normal); uv.Add(map(p)); }
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal) { Tri(a, b, c, normal); Tri(a, c, d, normal); }

        public Mesh Save(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            string path = Meshes + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
            existing.Clear();
            existing.SetVertices(v); existing.SetNormals(n); existing.SetUVs(0, uv); existing.SetTriangles(t, 0);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }

    [MenuItem("Tools/Armory/Rebuild Story")]
    static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "ArmoryHoldout") return "Open ArmoryHoldout first (active scene is " + scene.name + ")";
        var level = GameObject.Find("Level").transform;
        Transform old = level.Find("Story");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("Story").transform;
        root.SetParent(level, false);
        var rng = new System.Random(1113);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        Material blood = Painted("Story_Blood", PaintBlood(), .15f);
        Material brass = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Ammo_Brass.mat");
        Material metal = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Station_Rack.mat");
        Material helmetMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Keys/Crew_Helmet.mat");
        Material visor = Plain("Story_Visor", new Color(.05f, .06f, .08f));
        Material paper = Painted("Story_Paper", PaintPaper(), .1f);

        // ---- blood on the floor: pools where they fell, trails where they were dragged into the vents ----
        var floor = new Builder { map = p => new Vector2(p.x, p.z) * 2f };
        const float FloorY = .018f;
        void Pool(Vector2 c, float r, int seed)
        {
            const int Sides = 14;
            var centre = new Vector3(c.x, FloorY, c.y);
            for (int i = 0; i < Sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / Sides, a1 = (i + 1) * Mathf.PI * 2f / Sides;
                float r0 = r * (.65f + .6f * PixelFont.Blotch(i * .9f, seed, seed)), r1 = r * (.65f + .6f * PixelFont.Blotch(((i + 1) % Sides) * .9f, seed, seed));
                floor.Tri(centre, centre + new Vector3(Mathf.Cos(a0) * r0, 0f, Mathf.Sin(a0) * r0), centre + new Vector3(Mathf.Cos(a1) * r1, 0f, Mathf.Sin(a1) * r1), Vector3.up);
            }
        }
        void Trail(Vector2[] path, float width, int seed)
        {
            // Resample the path finely, wobble it, and drop some stretches where the body lifted off.
            var pts = new List<Vector2>();
            for (int i = 0; i + 1 < path.Length; i++)
                for (int k = 0; k < 6; k++) pts.Add(Vector2.Lerp(path[i], path[i + 1], k / 6f));
            pts.Add(path[path.Length - 1]);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                float t0 = (float)i / pts.Count, t1 = (float)(i + 1) / pts.Count;
                if (PixelFont.Blotch(i * .45f, seed, seed) < .28f) continue;                 // gaps
                Vector2 dir = (pts[i + 1] - pts[i]).normalized, side = new Vector2(-dir.y, dir.x);
                float w0 = width * (.55f + .6f * PixelFont.Blotch(i * .7f, seed + 1, seed)) * (1f - .4f * t0);
                float w1 = width * (.55f + .6f * PixelFont.Blotch((i + 1) * .7f, seed + 1, seed)) * (1f - .4f * t1);
                Vector2 a = pts[i] + side * w0 * .5f, b = pts[i] - side * w0 * .5f, c = pts[i + 1] - side * w1 * .5f, d = pts[i + 1] + side * w1 * .5f;
                floor.Quad(new Vector3(a.x, FloorY, a.y), new Vector3(b.x, FloorY, b.y), new Vector3(c.x, FloorY, c.y), new Vector3(d.x, FloorY, d.y), Vector3.up);
            }
        }
        // 1: from the helmet behind the barricade, round the end of it, to the west vent.
        Pool(new Vector2(-1.25f, 6.05f), .55f, 11);
        Trail(new[] { new Vector2(-1.25f, 6.05f), new Vector2(-2.6f, 5.6f), new Vector2(-4.2f, 4.6f), new Vector2(-6.4f, 3.8f), new Vector2(-8.6f, 3.2f), new Vector2(-10.75f, 3.0f) }, .5f, 21);
        // 2: from beside the crates to the east vent.
        Pool(new Vector2(6.3f, -2.6f), .45f, 12);
        Trail(new[] { new Vector2(6.3f, -2.6f), new Vector2(7.6f, -3.3f), new Vector2(9.2f, -3.1f), new Vector2(10.75f, -3.0f) }, .46f, 22);
        // 3: from the door to the vent beside it.
        Pool(new Vector2(-1.9f, -7.1f), .4f, 13);
        Trail(new[] { new Vector2(-1.9f, -7.1f), new Vector2(-3.4f, -7.9f), new Vector2(-4.6f, -8.5f), new Vector2(-5f, -8.85f) }, .44f, 23);
        // Spatter round the stand.
        for (int i = 0; i < 9; i++) Pool(new Vector2(R(-2.4f, 2.2f), R(5.5f, 7.2f)), R(.05f, .13f), 30 + i);
        Spawn(root, "Floor Blood", floor.Save("Story_FloorBlood"), blood);

        // ---- blood sprayed round the vent openings (on the wall, around the 1.05 x 0.8 m frame) ----
        var wall = new Builder();
        void Spray(Vector3 centre, Vector3 normal, Vector3 along, int seed)
        {
            wall.map = p => new Vector2(Vector3.Dot(p, along), p.y) * 2f;
            for (int k = 0; k < 7; k++)
            {
                float u = (PixelFont.Blotch(k * 1.3f, seed, seed) - .5f) * 2.2f, y = .9f + PixelFont.Blotch(k * 1.7f, seed + 3, seed) * .8f;
                float r = .06f + .16f * PixelFont.Blotch(k * 2.1f, seed + 5, seed);
                Vector3 c = centre + along * u + Vector3.up * (y - centre.y) + normal * .012f;
                const int Sides = 9;
                for (int i = 0; i < Sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / Sides, a1 = (i + 1) * Mathf.PI * 2f / Sides;
                    float r0 = r * (.6f + .7f * PixelFont.Blotch(i, k + seed, seed)), r1 = r * (.6f + .7f * PixelFont.Blotch((i + 1) % Sides, k + seed, seed));
                    wall.Tri(c, c + (along * Mathf.Cos(a0) + Vector3.up * Mathf.Sin(a0)) * r0, c + (along * Mathf.Cos(a1) + Vector3.up * Mathf.Sin(a1)) * r1, normal);
                }
                // A run dripping down from the bigger ones.
                if (r > .12f)
                {
                    float len = .25f + .3f * PixelFont.Blotch(k, seed + 9, seed), dw = .025f;
                    wall.Quad(c - along * dw, c + along * dw, c + along * dw * .5f + Vector3.down * len, c - along * dw * .5f + Vector3.down * len, normal);
                }
            }
        }
        // The walls' inner faces are at x = +-11 and z = +-9.
        Spray(new Vector3(-11f, 1f, 3f), Vector3.right, Vector3.forward, 41);
        Spray(new Vector3(11f, 1f, -3f), Vector3.left, Vector3.back, 42);
        Spray(new Vector3(-5f, 1f, -9f), Vector3.forward, Vector3.right, 43);

        // ---- "IT HAS HIS FACE", in blood on the west wall, above the vent ----
        const string Message = "IT HAS HIS FACE";
        const float Pix = .04f;
        float textW = PixelFont.Width(Message) * Pix;
        Vector3 origin = new Vector3(-11f + .012f, 2.74f, -5.05f - textW * .5f);   // top-left of the text, reading along +z
        wall.map = p => new Vector2(p.z, p.y) * 2f;
        var dripRng = new System.Random(7);
        for (int gx = 0; gx < PixelFont.Width(Message); gx++)
            for (int gy = 0; gy < 7; gy++)
            {
                if (!PixelFont.On(Message, gx, gy)) continue;
                // Finger-painted: each pixel a touch ragged.
                float j = (PixelFont.Blotch(gx * .9f, gy * .9f, 51) - .5f) * Pix * .5f;
                Vector3 p = origin + new Vector3(0f, -gy * Pix + j, gx * Pix);
                wall.Quad(p, p + Vector3.forward * Pix * 1.1f, p + new Vector3(0f, -Pix * 1.1f, Pix * 1.1f), p + Vector3.down * Pix * 1.1f, Vector3.right);
                bool bottom = gy == 6 || !PixelFont.On(Message, gx, gy + 1);
                if (bottom && dripRng.NextDouble() < .35)
                {
                    float len = Pix * (1 + dripRng.Next(6));
                    Vector3 d = p + new Vector3(0f, -Pix, Pix * .35f);
                    wall.Quad(d, d + Vector3.forward * Pix * .4f, d + new Vector3(0f, -len, Pix * .4f), d + Vector3.down * len, Vector3.right);
                }
            }
        Spawn(root, "Wall Blood", wall.Save("Story_WallBlood"), blood);

        // ---- the last stand behind the barricade ----
        var stand = Group(root, "Last Stand");
        var casings = new Builder { map = p => Vector2.zero };
        for (int i = 0; i < 46; i++)
        {
            float a = R(0f, Mathf.PI * 2f), d = Mathf.Sqrt(R(0f, 1f));
            var c = new Vector3(Mathf.Cos(a) * d * 1.7f + R(-.2f, .2f), .008f, 6.15f + Mathf.Sin(a) * d * .8f);
            Quaternion rot = Quaternion.Euler(0f, R(0f, 360f), 0f);
            Vector3 L = rot * Vector3.forward * .028f, W = rot * Vector3.right * .009f, H = Vector3.up * .016f;
            Vector3 b0 = c - L - W, b1 = c + L - W, b2 = c + L + W, b3 = c - L + W;
            casings.Quad(b0 + H, b1 + H, b2 + H, b3 + H, Vector3.up);
            casings.Quad(b0, b1, b1 + H, b0 + H, -(rot * Vector3.right));
            casings.Quad(b3, b2, b2 + H, b3 + H, rot * Vector3.right);
        }
        Spawn(stand, "Spent Casings", casings.Save("Story_Casings"), brass);
        foreach (var (pos, yaw) in new[] { (new Vector3(-.85f, .02f, 6.45f), 20f), (new Vector3(.65f, .02f, 5.85f), 75f), (new Vector3(1.4f, .02f, 6.7f), 140f) })
        {
            var mag = Box("Empty Magazine", stand, pos, new Vector3(.05f, .03f, .17f), metal);
            mag.transform.rotation = Quaternion.Euler(0f, yaw, 85f);
        }
        // The helmet, rolled on its side in the pool, visor up.
        var helmet = new GameObject("Crew Helmet").transform;
        helmet.SetParent(stand, false);
        helmet.position = new Vector3(-1.05f, .1f, 6.2f);
        helmet.rotation = Quaternion.Euler(-24f, 35f, 62f);
        Mesh sphere = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/KeyFlyer/LowSphere.asset");
        Mesh(helmet, "Shell", sphere, helmetMat, Vector3.zero, new Vector3(.28f, .27f, .31f));
        Mesh(helmet, "Visor", sphere, visor, new Vector3(0f, -.01f, .06f), new Vector3(.22f, .17f, .22f));
        // Papers.
        foreach (var (pos, yaw) in new[] { (new Vector3(2.05f, .02f, 6.8f), 12f), (new Vector3(-2.3f, .021f, 6.55f), -30f), (new Vector3(-.25f, .022f, 7.05f), 80f), (new Vector3(3.4f, .02f, 5.65f), 47f) })
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Paper";
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(stand, false);
            q.transform.position = pos;
            q.transform.rotation = Quaternion.Euler(90f, yaw, 0f);
            q.transform.localScale = new Vector3(.21f, .297f, 1f);
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = paper; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        // A flashlight still on where it fell, its beam along the floor toward the west vent.
        var torch = new GameObject("Dropped Flashlight").transform;
        torch.SetParent(stand, false);
        torch.position = new Vector3(.95f, .05f, 6.3f);
        torch.rotation = Quaternion.LookRotation(new Vector3(-1f, -.06f, -.35f));
        var body = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
        body.transform.SetParent(torch, false);
        body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        body.transform.localScale = new Vector3(.05f, .22f, .05f);
        body.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/Station/Pipe8.asset");
        body.GetComponent<MeshRenderer>().sharedMaterial = metal;
        var lens = Box("Lens", torch, Vector3.zero, new Vector3(.055f, .055f, .01f), Glow("Story_TorchLens", new Color(1f, .95f, .8f) * 1.5f));
        lens.transform.localPosition = new Vector3(0f, 0f, .11f);
        var beam = new GameObject("Beam").AddComponent<Light>();
        beam.transform.SetParent(torch, false);
        beam.transform.localPosition = new Vector3(0f, 0f, .12f);
        beam.type = LightType.Spot; beam.range = 7f; beam.spotAngle = 38f; beam.innerSpotAngle = 14f;
        beam.intensity = 2.4f; beam.color = new Color(1f, .93f, .78f); beam.shadows = LightShadows.None;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return $"story: floor blood {floor.t.Count / 3} tris, wall blood + message {wall.t.Count / 3} tris, casings {casings.t.Count / 3} tris; saved";
    }

    // ================= helpers =================

    static Transform Group(Transform parent, string name)
    {
        var g = new GameObject(name).transform;
        g.SetParent(parent, false);
        return g;
    }

    static void Spawn(Transform parent, string name, Mesh mesh, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Mesh(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Material Painted(string name, Texture2D tex, float affine)
    {
        string path = Mats + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.SetFloat("_Affine", affine);
        m.SetFloat("_Lighting", 1f);
        m.enableInstancing = true;
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

    static Material Glow(string name, Color emission)
    {
        var m = Plain(name, new Color(.9f, .88f, .8f));
        m.SetColor("_EmissionColor", emission);
        m.SetFloat("_Lighting", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Blood: 32 px = half a metre. Dark and clotted, wet sheen in soft patches, older edges browner.</summary>
    static Texture2D PaintBlood()
    {
        const int s = 32;
        var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float f = 2.2f + 1.4f * (PixelFont.Blotch(x * .18f, y * .18f, 61) - .5f);
                if (PixelFont.Blotch(x * .3f, y * .3f, 62) > .74f) f += 1.3f;                 // wet sheen
                if (PixelFont.Blotch(x * .25f, y * .25f, 63) < .22f) f -= .9f;                // clots
                px[y * s + x] = Shade(Blood, f);
            }
        return PixelFont.Save(px, s, s, Tex + "Story_Blood.png");
    }

    /// <summary>A dropped page: typed lines, a ring stain, a bloody thumbprint in the corner.</summary>
    static Texture2D PaintPaper()
    {
        const int w = 16, h = 24;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float f = 4.2f + .6f * (PixelFont.Blotch(x * .3f, y * .3f, 71) - .5f) + .3f * (y / (float)h);
                bool line = y < h - 3 && y > 3 && y % 2 == 0 && x > 1 && x < w - 2 - (y * 7 % 5);
                if (line) f = 2.2f;
                float dx = x - 10.5f, dy = y - 7.5f, ring = Mathf.Sqrt(dx * dx + dy * dy);
                if (ring > 2.4f && ring < 3.4f) f -= .9f;
                px[y * w + x] = Shade(Paper, f);
            }
        for (int y = 1; y < 5; y++) for (int x = 1; x < 4; x++) if ((x + y) % 3 != 0) px[y * w + x] = Shade(Blood, 2.6f);
        return PixelFont.Save(px, w, h, Tex + "Story_Paper.png");
    }
}
