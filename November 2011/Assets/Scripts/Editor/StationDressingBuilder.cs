using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dresses the Armory Holdout hall so it reads as a real place on Station Jupiter:
/// - stenciled signage in the pixel font ("ARMORY B-2" over the racks, "JUPITER STATION" on the
///   south wall), worn paint on metal plates;
/// - a sealed blast door to POD 3 behind the spawn (hazard stripes, red warning lamp): the
///   escape pod the radio promises at the escape wave;
/// - steel ribs down the walls and pipe runs under the ceiling (grey conduits, a yellow gas line,
///   a red fire line), clear of the vents, lockers and racks.
/// Textures are painted here (Assets/Textures/Station), point-filtered, no mipmaps.
/// Menu: Tools / Armory / Rebuild Dressing (with ArmoryHoldout open). Saves the scene.
/// </summary>
public static class StationDressingBuilder
{
    const string Tex = "Assets/Textures/Station/";
    const string Mats = "Assets/Materials/Station/";
    static readonly Color Yellow = new Color(.84f, .66f, .2f), Ivory = new Color(.82f, .8f, .72f);

    [MenuItem("Tools/Armory/Rebuild Dressing")]
    static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "ArmoryHoldout") return "Open ArmoryHoldout first";
        var level = GameObject.Find("Level").transform;
        Transform old = level.Find("Dressing");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject("Dressing").transform;
        root.SetParent(level, false);
        var log = new System.Text.StringBuilder();

        Material steel = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Station_Rack.mat");
        Material dark = AssetDatabase.LoadAssetAtPath<Material>(Mats + "Vent_Dark.mat");
        Mesh pipe = PipeMesh();

        // ---- signs ----
        Sign(root, "Sign ARMORY", new[] { "ARMORY B-2" }, new[] { 2 }, 168, 34, new Vector3(0f, 3.05f, 8.975f), 0f, new Vector2(4.4f, .9f),
             new Color(.15f, .16f, .14f), Yellow, true);
        Sign(root, "Sign JUPITER", new[] { "JUPITER STATION", "DECK 4 // SECURITY" }, new[] { 2, 1 }, 200, 40, new Vector3(-6.6f, 3.0f, -8.975f), 180f,
             new Vector2(3.6f, .72f), new Color(.17f, .19f, .22f), Ivory, false);
        log.Append("2 signs; ");

        // ---- POD 3 blast door behind the spawn ----
        var door = new GameObject("Blast Door POD 3").transform;
        door.SetParent(root, false);
        door.position = new Vector3(0f, 0f, -9f);
        door.rotation = Quaternion.Euler(0f, 180f, 0f);                 // its front (-Z local) faces the hall
        Material doorMat = Painted("Dressing_BlastDoor", DoorTexture());
        Box("Post", door, new Vector3(-1.75f, 1.55f, -.15f), new Vector3(.36f, 3.1f, .3f), steel);
        Box("Post", door, new Vector3(1.75f, 1.55f, -.15f), new Vector3(.36f, 3.1f, .3f), steel);
        Box("Header", door, new Vector3(0f, 3.0f, -.15f), new Vector3(3.86f, .46f, .3f), steel);
        var doorBody = Box("Door", door, new Vector3(0f, 1.39f, -.06f), new Vector3(3.14f, 2.78f, .1f), Plain("Dressing_DoorBody", new Color(.16f, .17f, .19f)));
        Face(doorBody.transform, new Vector2(1f, 1f), .5f + .001f / .1f, doorMat);
        Box("Sill", door, new Vector3(0f, .03f, -.2f), new Vector3(3.5f, .06f, .4f), dark);
        Sign(door, "Sign EVAC", new[] { "EVAC ACCESS" }, new[] { 2 }, 76, 22, new Vector3(0f, 3.0f, -.31f), 0f, new Vector2(1.9f, .42f),
             new Color(.42f, .07f, .05f), Ivory, false, local: true);
        // red warning lamp over the door: pulses slowly (an EmergencyLight), dies in power cuts
        Material red = Glow("Dressing_RedLamp", new Color(.3f, .03f, .02f), new Color(1f, .15f, .08f) * 2f);
        var lampMesh = Box("Warning Lamp", door, new Vector3(1.4f, 3.0f, -.33f), new Vector3(.18f, .12f, .1f), red);
        var warn = new GameObject("Warning Light").AddComponent<Light>();
        warn.transform.SetParent(door, false);
        warn.transform.localPosition = new Vector3(1.4f, 2.9f, -.7f);
        warn.type = LightType.Point; warn.color = new Color(1f, .15f, .08f); warn.range = 4.5f; warn.intensity = 1.8f; warn.shadows = LightShadows.None;
        var em = warn.gameObject.AddComponent<EmergencyLight>();
        var eso = new SerializedObject(em);
        eso.FindProperty("pulseSpeed").floatValue = .7f;
        eso.FindProperty("pulseDepth").floatValue = .8f;
        eso.FindProperty("brokenness").floatValue = .05f;
        var gl = eso.FindProperty("glows"); gl.arraySize = 1; gl.GetArrayElementAtIndex(0).objectReferenceValue = lampMesh.GetComponent<Renderer>();
        eso.FindProperty("glowColor").colorValue = new Color(1f, .15f, .08f) * 2f;
        eso.ApplyModifiedPropertiesWithoutUndo();
        // solid: you can't walk through a blast door
        var col = door.gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 1.55f, -.15f); col.size = new Vector3(3.86f, 3.1f, .3f);
        log.Append("blast door; ");

        // ---- wall ribs, clear of everything on the walls ----
        // (wall, along-axis obstacles as (centre, half width))
        var obstacles = new Dictionary<string, (float c, float h)[]>
        {
            ["W"] = new[] { (-5f, .5f), (3f, .5f), (-1f, .5f) },
            ["E"] = new[] { (-3f, .5f), (5.5f, .5f), (1f, .5f) },
            ["N"] = new[] { (-6f, 1.1f), (-2f, 1.1f), (2f, 1.1f), (6f, 1.1f), (0f, 2.2f) },
            ["S"] = new[] { (-5f, .5f), (4f, .5f), (8.5f, .5f), (0f, 1.95f), (-6.6f, 1.8f) },
        };
        int ribs = 0;
        foreach (var wall in obstacles)
        {
            bool ns = wall.Key == "N" || wall.Key == "S";
            float half = ns ? 10.5f : 8.5f;
            for (float a = -half + .5f; a <= half - .5f; a += 2.5f)
            {
                bool blocked = false;
                foreach (var o in wall.Value) if (Mathf.Abs(a - o.c) < o.h + .4f) blocked = true;
                if (blocked) continue;
                Vector3 p = wall.Key switch
                {
                    "W" => new Vector3(-10.93f, 2f, a), "E" => new Vector3(10.93f, 2f, a),
                    "N" => new Vector3(a, 2f, 8.93f), _ => new Vector3(a, 2f, -8.93f),
                };
                Vector3 size = ns ? new Vector3(.22f, 4f, .14f) : new Vector3(.14f, 4f, .22f);
                Box("Rib", root, p, size, steel);
                ribs++;
            }
        }
        log.Append($"{ribs} ribs; ");

        // ---- pipe runs under the ceiling ----
        Material yellowPipe = Plain("Dressing_PipeYellow", new Color(.55f, .44f, .12f));
        Material redPipe = Plain("Dressing_PipeRed", new Color(.46f, .1f, .07f));
        int pipes = 0;
        void Run(string name, Vector3 a, Vector3 b, float r, Material m)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.transform.position = (a + b) * .5f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            go.transform.localScale = new Vector3(r * 2f, (b - a).magnitude, r * 2f);
            go.GetComponent<MeshFilter>().sharedMesh = pipe;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            pipes++;
        }
        foreach (float s in new[] { -1f, 1f })
        {
            float x = s * 10.86f;
            Run("Conduit", new Vector3(x, 3.56f, -9f), new Vector3(x, 3.56f, 9f), .05f, steel);
            Run("Conduit", new Vector3(x, 3.7f, -9f), new Vector3(x, 3.7f, 9f), .034f, dark);
            Run(s < 0 ? "Gas Line" : "Fire Line", new Vector3(x, 3.38f, -9f), new Vector3(x, 3.38f, 9f), .042f, s < 0 ? yellowPipe : redPipe);
            float z = s * 8.86f;
            Run("Conduit", new Vector3(-11f, 3.56f, z), new Vector3(11f, 3.56f, z), .05f, steel);
            Run("Conduit", new Vector3(-11f, 3.7f, z), new Vector3(11f, 3.7f, z), .034f, dark);
        }
        for (float a = -7.5f; a <= 7.5f; a += 3f)                       // brackets on the long walls
            foreach (float s in new[] { -1f, 1f })
                Box("Bracket", root, new Vector3(s * 10.9f, 3.5f, a), new Vector3(.12f, .42f, .05f), dark);
        log.Append($"{pipes} pipe runs");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ---------------- pieces ----------------

    static void Sign(Transform parent, string name, string[] lines, int[] scales, int w, int h, Vector3 pos, float yaw, Vector2 size,
                     Color plate, Color ink, bool hazardEdge, bool local = false)
    {
        var px = new Color[w * h];
        int seed = name.Length * 31 + lines[0].Length;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // worn painted metal: soft blotches in a few steps, a darker rim, lighter top edge
                float t = .45f + .35f * (PixelFont.Blotch(x * .18f, y * .18f, seed) - .5f) + .12f * (y / (float)h);
                Color c = PixelFont.Stepped(plate * .7f, plate * 1.35f, t, 4);
                if (x < 2 || y < 2 || x >= w - 2 || y >= h - 2) c *= .65f;
                if (y == h - 3 && x > 2 && x < w - 3) c *= 1.25f;
                if (hazardEdge && (y < 5 || y >= h - 5) && x >= 2 && x < w - 2)
                    c = ((x + y) / 4) % 2 == 0 ? Color.Lerp(Yellow * .85f, c, .2f) : new Color(.08f, .08f, .07f);
                px[y * w + x] = c;
            }
        // rivets in the corners
        foreach (var (rx, ry) in new[] { (4, 4), (w - 5, 4), (4, h - 5), (w - 5, h - 5) })
            if (!hazardEdge) { px[ry * w + rx] = plate * .4f; px[(ry + 1) * w + rx] = plate * 1.6f; }
        int total = 0;
        foreach (int s in scales) total += 7 * s;
        int gap = 3, cy = h / 2 + (total + gap * (lines.Length - 1)) / 2;
        for (int i = 0; i < lines.Length; i++)
        {
            int lh = 7 * scales[i];
            PixelFont.Stamp(px, w, h, lines[i], w / 2, cy - lh / 2, scales[i], ink, .22f, seed + i);
            cy -= lh + gap;
        }
        Material mat = Painted("Dressing_" + name.Replace(" ", ""), PixelFont.Save(px, w, h, $"{Tex}{name.Replace(" ", "_")}.png"));
        // The plate is a thin box in the plate colour; the painting sits on a quad just in front of it
        // (a quad is always upright; cube faces map their texture rotated).
        var body = Box(name, parent, pos, new Vector3(size.x, size.y, .04f), Plain("Dressing_Plate", plate * .8f));
        if (local) body.transform.localRotation = Quaternion.identity;
        else body.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Face(body.transform, new Vector2(1f, 1f), .5f + .001f / .04f, mat);
    }

    static Texture2D DoorTexture()
    {
        const int w = 128, h = 112;
        var px = new Color[w * h];
        Color metal = new Color(.2f, .22f, .25f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float t = .5f + .3f * (PixelFont.Blotch(x * .12f, y * .12f, 11) - .5f) + .15f * (y / (float)h);
                Color c = PixelFont.Stepped(metal * .7f, metal * 1.4f, t, 4);
                if (Mathf.Abs(x - w / 2) < 1) c = metal * .35f;                            // centre seam
                if (y % 28 == 0) c *= .7f;                                                 // panel lines
                if (y < 18) c = ((x + y) / 6) % 2 == 0 ? Yellow * (.75f + .2f * PixelFont.Blotch(x * .3f, y * .3f, 4))
                                                       : new Color(.07f, .07f, .06f);       // hazard band
                if (x < 3 || x >= w - 3 || y >= h - 3) c *= .6f;
                px[y * w + x] = c;
            }
        for (int ry = 24; ry < h; ry += 28)
            foreach (int rx in new[] { 8, 56, 70, 118 })
            { px[ry * w + rx] = metal * .4f; px[(ry + 1) * w + rx] = metal * 1.7f; }
        PixelFont.Stamp(px, w, h, "POD 3", w / 2, 66, 3, Ivory * .9f, .3f, 5);
        PixelFont.Stamp(px, w, h, "SEALED", w / 2, 38, 2, new Color(.75f, .16f, .1f), .25f, 6);
        return PixelFont.Save(px, w, h, Tex + "Blast_Door_POD3.png");
    }

    static Mesh PipeMesh()
    {
        const string path = "Assets/Models/Station/Pipe8.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        const int seg = 8;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * .5f, p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * .5f;
            Vector3 nn = ((p0 + p1) * .5f).normalized;
            int b = v.Count;
            v.Add(p0 + Vector3.down * .5f); v.Add(p1 + Vector3.down * .5f); v.Add(p1 + Vector3.up * .5f); v.Add(p0 + Vector3.up * .5f);
            n.Add(nn); n.Add(nn); n.Add(nn); n.Add(nn);
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        var m = new Mesh { name = "Pipe8" };
        m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
        if (!AssetDatabase.IsValidFolder("Assets/Models/Station")) AssetDatabase.CreateFolder("Assets/Models", "Station");
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>A painted quad on the -Z face of a (scaled) box, in the box's local units.</summary>
    static void Face(Transform box, Vector2 size, float front, Material mat)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "Painted Face";
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(box, false);
        q.transform.localPosition = new Vector3(0f, 0f, -front);
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        q.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;     // the Dressing group sits at the level's origin
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
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

    static Material Glow(string name, Color baseColor, Color emission)
    {
        var m = Plain(name, baseColor);
        m.SetColor("_EmissionColor", emission);
        m.SetFloat("_Lighting", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }
}
