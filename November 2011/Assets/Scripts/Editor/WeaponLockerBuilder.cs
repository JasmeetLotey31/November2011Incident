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
        Material screen = Glow("Locker_Screen", Color.black, new Color(1f, .55f, .15f) * .06f);
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

        // Stenciled header plate: station property, not a shop.
        Box("Header", r, new Vector3(0f, 1.91f, .455f), new Vector3(.96f, .13f, .012f), dark);
        var stencilGo = new GameObject("Stencil", typeof(RectTransform), typeof(Canvas));
        stencilGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var srt = (RectTransform)stencilGo.transform;
        srt.SetParent(r, false);
        srt.sizeDelta = new Vector2(940f, 120f);
        srt.localScale = Vector3.one * .001f;
        srt.localPosition = new Vector3(0f, 1.91f, .462f);
        srt.localRotation = Quaternion.Euler(0f, 180f, 0f);
        Text stencil = ScreenText("Label", srt, 42, Vector2.zero, FontStyle.Bold);
        stencil.rectTransform.sizeDelta = new Vector2(920f, 100f);
        stencil.text = $"JUPITER ARMORY  //  {label}";
        stencil.color = new Color(.78f, .62f, .2f, .85f);

        // Terminal above it: amber screen with the locker number, the gun and the price.
        Box("Terminal", r, new Vector3(0f, 2.27f, .06f), new Vector3(.66f, .32f, .08f), dark);
        Transform glow = Box("Screen", r, new Vector3(0f, 2.27f, .101f), new Vector3(.58f, .25f, .004f), screen).transform;
        var canvasGo = new GameObject("Screen Text", typeof(RectTransform), typeof(Canvas));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var crt = (RectTransform)canvasGo.transform;
        crt.SetParent(r, false);
        crt.sizeDelta = new Vector2(580f, 250f);
        crt.localScale = Vector3.one * .001f;
        crt.localPosition = new Vector3(0f, 2.27f, .106f);
        crt.localRotation = Quaternion.Euler(0f, 180f, 0f); // UI faces -Z; the locker faces +Z
        // Big enough to read at the game's quarter resolution from a couple of metres.
        Text name = ScreenText("Gun", crt, 104, new Vector2(0f, 42f), FontStyle.Bold);
        Text price = ScreenText("Price", crt, 84, new Vector2(0f, -66f), FontStyle.Bold);

        // Caged amber lamp: a beacon in the dark hall.
        Box("Lamp Cage", r, new Vector3(0f, 2.58f, .1f), new Vector3(.16f, .1f, .12f), metal);
        Box("Lamp Bulb", r, new Vector3(0f, 2.56f, .14f), new Vector3(.1f, .06f, .08f), bulb);
        var lamp = new GameObject("Lamp").AddComponent<Light>();
        lamp.transform.SetParent(r, false);
        lamp.transform.localPosition = new Vector3(0f, 2.5f, .55f);
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
        so.FindProperty("screenName").objectReferenceValue = name;
        so.FindProperty("screenPrice").objectReferenceValue = price;
        so.FindProperty("screenGlow").objectReferenceValue = glow.GetComponent<Renderer>();
        so.FindProperty("lamp").objectReferenceValue = lamp;
        so.FindProperty("audioSource").objectReferenceValue = audio;
        so.FindProperty("openClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/NailGun/NailGun_Reload_Open.wav");
        so.FindProperty("closeClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Impacts/Impact_Metal_1.wav");
        so.ApplyModifiedPropertiesWithoutUndo();
        name.text = gunName; price.text = cost.ToString();

        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Level");
        string path = $"{OutDir}/{root.name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        int tris = 0;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) tris += mf.sharedMesh != null ? mf.sharedMesh.triangles.Length / 3 : 0;
        Object.DestroyImmediate(root);
        return $"{path}: {tris} triangles";
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
        rt.sizeDelta = new Vector2(560f, size * 1.3f);
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
