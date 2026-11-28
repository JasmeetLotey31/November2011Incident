using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the armory's small-arms locker prefabs (PS1 low-poly, the station's own materials):
/// a steel wall locker with a wire-grille door on a hinge, the gun hanging inside on two hooks
/// under a warm strip light, an amber terminal screen above it (locker number, gun, price) and
/// an amber caged lamp so you can find it in the dark mid-wave. Writes
/// Assets/Prefabs/Level/WeaponLocker_&lt;Gun&gt;.prefab. Menu: Tools / Armory / Rebuild Weapon Lockers.
/// Prefab origin: the floor at the middle of the locker's back; the front faces +Z.
/// </summary>
public static class WeaponLockerBuilder
{
    const string OutDir = "Assets/Prefabs/Level";
    const string Mats = "Assets/Materials/Station/";

    // (gun display name, gun prefab, locker label, cost, ammo cost)
    static readonly (string gun, string prefab, string label, int cost, int ammo)[] Lockers =
    {
        ("SHOTGUN", "Assets/Prefabs/Weapons/Shotgun.prefab", "SMALL ARMS 02", 750, 375),
        ("ION CARBINE", "Assets/Prefabs/Weapons/IonCarbine.prefab", "SMALL ARMS 03", 1500, 750),
        ("ARC REPEATER", "Assets/Prefabs/Weapons/ArcRepeater.prefab", "HEAVY ARMS 01", 3500, 1750),
    };

    [MenuItem("Tools/Armory/Rebuild Weapon Lockers")]
    static void BuildMenu() => Debug.Log(BuildAll());

    public static string BuildAll()
    {
        string log = "";
        foreach (var l in Lockers) log += Build(l.gun, l.prefab, l.label, l.cost, l.ammo) + "\n";
        return log;
    }

    public static string Build(string gunName, string gunPrefabPath, string label, int cost, int ammoCost)
    {
        Material metal = Mat("Station_Rack"), dark = Mat("Vent_Dark"), grate = Mat("Station_Rack"), hazard = Mat("Station_Hazard");
        Material gunMetal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Weapons/NailGun_Metal.mat");
        Material strip = Glow("Locker_Strip", new Color(.2f, .2f, .2f), new Color(1f, .85f, .62f) * 1.6f);
        Material bulb = Glow("Locker_Lamp", new Color(.3f, .2f, .1f), new Color(1f, .6f, .25f) * 2.2f);

        var root = new GameObject("WeaponLocker_" + Title(gunName));
        Transform r = root.transform;

        // Cabinet.
        Box("Back", r, new Vector3(0f, 1f, .02f), new Vector3(1f, 2f, .04f), metal);
        Box("Liner", r, new Vector3(0f, 1.02f, .05f), new Vector3(.86f, 1.74f, .02f), dark);
        Box("Side L", r, new Vector3(-.48f, 1f, .225f), new Vector3(.04f, 2f, .45f), metal);
        Box("Side R", r, new Vector3(.48f, 1f, .225f), new Vector3(.04f, 2f, .45f), metal);
        Box("Top", r, new Vector3(0f, 1.98f, .225f), new Vector3(1f, .04f, .45f), metal);
        Box("Base", r, new Vector3(0f, .06f, .225f), new Vector3(1f, .12f, .45f), metal);
        Box("Kick Plate", r, new Vector3(0f, .065f, .452f), new Vector3(.96f, .1f, .006f), hazard);
        Box("Hook Low", r, new Vector3(0f, .7f, .09f), new Vector3(.05f, .03f, .07f), gunMetal);
        Box("Hook High", r, new Vector3(0f, 1.36f, .09f), new Vector3(.05f, .03f, .07f), gunMetal);
        Box("Strip Light", r, new Vector3(0f, 1.8f, .3f), new Vector3(.7f, .025f, .035f), strip);
        var inner = new GameObject("Inner Light").AddComponent<Light>();
        inner.transform.SetParent(r, false);
        inner.transform.localPosition = new Vector3(0f, 1.25f, .36f);
        inner.type = LightType.Point; inner.range = 1.7f; inner.intensity = 2.4f; inner.color = new Color(1f, .85f, .62f); inner.shadows = LightShadows.None;

        // The gun on its hooks: a copy of the weapon's own model, hanging barrel-up, side on.
        var gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gunPrefabPath);
        var display = new GameObject("Display").transform;
        display.SetParent(r, false);
        if (gunPrefab != null && gunPrefab.transform.Find("Model") != null)
        {
            Transform model = Object.Instantiate(gunPrefab.transform.Find("Model"), display);
            model.name = "Model";
            foreach (var c in model.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)) Object.DestroyImmediate(c);
            foreach (var mr in model.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.name.Contains("Flash")) Object.DestroyImmediate(mr.gameObject);
            Bounds b = Bounds(model);
            model.localRotation = Quaternion.identity;
            model.localPosition = -b.center;          // centre the gun on the display pivot
            display.localRotation = Quaternion.Euler(0f, 0f, -6f) * Quaternion.LookRotation(Vector3.up, Vector3.right);
            float scale = Mathf.Clamp(1.05f / Mathf.Max(.2f, b.size.z), .9f, 1.6f);
            display.localScale = Vector3.one * scale;
            display.localPosition = new Vector3(.02f, 1.05f, .17f);
        }

        // Wire-grille door on its hinge (left edge).
        var door = new GameObject("Door").transform;
        door.SetParent(r, false);
        door.localPosition = new Vector3(-.46f, 1.02f, .46f);
        const float W = .92f, H = 1.78f;
        Box("Frame L", door, new Vector3(.02f, 0f, 0f), new Vector3(.04f, H, .03f), metal);
        Box("Frame R", door, new Vector3(W - .02f, 0f, 0f), new Vector3(.04f, H, .03f), metal);
        Box("Frame T", door, new Vector3(W * .5f, H * .5f - .02f, 0f), new Vector3(W, .04f, .03f), metal);
        Box("Frame B", door, new Vector3(W * .5f, -H * .5f + .02f, 0f), new Vector3(W, .04f, .03f), metal);
        for (int i = 1; i <= 6; i++) Box("Bar", door, new Vector3(W * i / 7f, 0f, 0f), new Vector3(.012f, H - .06f, .012f), grate);
        foreach (float y in new[] { -.45f, 0f, .45f }) Box("Rail", door, new Vector3(W * .5f, y, 0f), new Vector3(W - .06f, .014f, .014f), grate);
        Box("Handle", door, new Vector3(W - .08f, 0f, .035f), new Vector3(.03f, .16f, .03f), gunMetal);
        Box("Lock Plate", door, new Vector3(W - .08f, -.14f, .02f), new Vector3(.06f, .08f, .012f), dark);

        // Stenciled header plate: station property, not a shop (painted in the pixel font: crisp at PS1 resolution).
        Box("Header", r, new Vector3(0f, 1.91f, .455f), new Vector3(.96f, .13f, .012f), dark);
        string file = Title(gunName);
        Material header = Painted($"Locker_{file}_Header", HeaderTexture(label, file));
        Quad("Stencil", r, new Vector3(0f, 1.91f, .4622f), new Vector2(.94f, .11f), header);

        // Terminal above it: a dark CRT with the gun and the price in amber pixel letters. The text is
        // painted white; WeaponLocker tints it (amber, green when you buy, red when you can't).
        Box("Terminal", r, new Vector3(0f, 2.33f, .06f), new Vector3(.9f, .44f, .08f), dark);
        Material screen = Painted($"Locker_{file}_Screen", ScreenTexture(WeaponLocker.ScreenName(gunName), cost.ToString(), file), lit: false);
        Transform glow = Quad("Screen", r, new Vector3(0f, 2.33f, .1012f), new Vector2(.82f, .36f), screen).transform;

        // Caged amber lamp: a beacon in the dark hall.
        Box("Lamp Cage", r, new Vector3(0f, 2.68f, .1f), new Vector3(.16f, .1f, .12f), metal);
        Box("Lamp Bulb", r, new Vector3(0f, 2.66f, .14f), new Vector3(.1f, .06f, .08f), bulb);
        var lamp = new GameObject("Lamp").AddComponent<Light>();
        lamp.transform.SetParent(r, false);
        lamp.transform.localPosition = new Vector3(0f, 2.6f, .55f);
        lamp.type = LightType.Point; lamp.range = 4.5f; lamp.intensity = 2f; lamp.color = new Color(1f, .6f, .25f); lamp.shadows = LightShadows.None;

        // Solid to walk into, and what the player's look ray hits.
        var col = root.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 1.2f, .24f);
        col.size = new Vector3(1f, 2.4f, .48f);

        var audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false; audio.spatialBlend = 1f; audio.minDistance = 1f; audio.maxDistance = 16f;
        audio.rolloffMode = AudioRolloffMode.Linear;

        var locker = root.AddComponent<WeaponLocker>();
        var so = new SerializedObject(locker);
        so.FindProperty("weaponName").stringValue = gunName;
        so.FindProperty("lockerLabel").stringValue = label;
        so.FindProperty("cost").intValue = cost;
        so.FindProperty("ammoCost").intValue = ammoCost;
        so.FindProperty("door").objectReferenceValue = door;
        so.FindProperty("display").objectReferenceValue = display.gameObject;
        so.FindProperty("screenName").objectReferenceValue = null;
        so.FindProperty("screenPrice").objectReferenceValue = null;
        so.FindProperty("screenGlow").objectReferenceValue = glow.GetComponent<Renderer>();
        so.FindProperty("lamp").objectReferenceValue = lamp;
        so.FindProperty("audioSource").objectReferenceValue = audio;
        so.FindProperty("openClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/NailGun/NailGun_Reload_Open.wav");
        so.FindProperty("closeClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Impacts/Impact_Metal_1.wav");
        so.ApplyModifiedPropertiesWithoutUndo();

        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Level");
        string path = $"{OutDir}/{root.name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        int tris = 0;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) tris += mf.sharedMesh != null ? mf.sharedMesh.triangles.Length / 3 : 0;
        Object.DestroyImmediate(root);
        return $"{path}: {tris} triangles";
    }

    /// <summary>A quad facing the locker's front (+Z).</summary>
    static GameObject Quad(string name, Transform parent, Vector3 pos, Vector2 size, Material mat)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(parent, false);
        q.transform.localPosition = pos;
        q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        q.GetComponent<MeshRenderer>().sharedMaterial = mat;
        q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return q;
    }

    static Texture2D HeaderTexture(string text, string file)
    {
        int w = PixelFont.Width(text) + 8, h = 13;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = PixelFont.Stepped(new Color(.05f, .05f, .05f), new Color(.11f, .11f, .1f), PixelFont.Blotch(x * .2f, y * .2f, 3));
        PixelFont.Stamp(px, w, h, text, w / 2, h / 2, 1, new Color(.78f, .62f, .2f), .2f, file.Length);
        return PixelFont.Save(px, w, h, $"Assets/Textures/Station/Locker_{file}_Header.png");
    }

    static Texture2D ScreenTexture(string gun, string price, string file)
    {
        // Sized so one texel lands on about one game pixel from a couple of metres (crisp, not aliased).
        const int w = 56, h = 24;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = y % 2 == 0 ? new Color(.02f, .025f, .02f) : new Color(.045f, .05f, .04f);   // CRT scanlines
        PixelFont.Stamp(px, w, h, gun, w / 2, 18, 1, Color.white, 0f, 1);
        PixelFont.Stamp(px, w, h, price, w / 2, 7, 2, Color.white, 0f, 2);
        return PixelFont.Save(px, w, h, $"Assets/Textures/Station/Locker_{file}_Screen.png");
    }

    static Material Painted(string name, Texture2D tex, bool lit = true)
    {
        string path = Mats + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", lit ? Color.white : new Color(1f, .62f, .2f));
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.SetFloat("_Affine", .1f);
        m.SetFloat("_Lighting", lit ? 1f : 0f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static string Title(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1).ToLower().Replace(" ", "");

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Text ScreenText(string name, RectTransform canvas, int size, Vector2 pos, FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(canvas, false);
        rt.sizeDelta = new Vector2(canvas.sizeDelta.x - 20f, size * 1.3f);
        rt.anchoredPosition = pos;
        var t = go.GetComponent<Text>();
        t.font = HudKit.Font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.color = new Color(1f, .62f, .2f);
        t.raycastTarget = false;
        return t;
    }

    static Bounds Bounds(Transform model)
    {
        // In the model's own space (it sits at identity under the display pivot when measured).
        model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity;
        var b = new Bounds(Vector3.zero, Vector3.zero);
        bool init = false;
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            foreach (Vector3 corner in new[] { mb.min, mb.max, new Vector3(mb.min.x, mb.max.y, mb.min.z), new Vector3(mb.max.x, mb.min.y, mb.max.z) })
            {
                Vector3 p = model.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!init) { b = new Bounds(p, Vector3.zero); init = true; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>(Mats + name + ".mat");

    static Material Glow(string name, Color baseColor, Color emission)
    {
        string path = Mats + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", baseColor);
        m.SetColor("_EmissionColor", emission);
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.SetFloat("_Affine", .5f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }
}
