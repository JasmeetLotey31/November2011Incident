using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The Armory Holdout's light: the station on emergency power, but readable.
/// - Six caged fluorescent fixtures on the ceiling throw cold pools on the floor with dark gaps
///   between; two of them are failing (buzz and stutter). All of them die in a power cut
///   (they're EmergencyLights), so blackouts still mean "flashlight, now".
/// - Dim cool ambient and blue-grey fog instead of a black void (the PS1 fog-to-colour look).
/// - Gentler grading: less contrast, neutral tonemapping, lighter vignette and grain.
/// - A stronger, wider flashlight.
/// Menu: Tools / Armory / Rebuild Lighting (with ArmoryHoldout open). Saves the scene.
/// </summary>
public static class ArmoryLightingBuilder
{
    const string FixturePath = "Assets/Prefabs/Level/CeilingLight.prefab";
    static readonly Color Tube = new Color(.85f, .93f, 1f);

    // (x, z, failing)
    static readonly (float x, float z, bool failing)[] Fixtures =
    {
        (-5.5f, -4.5f, true), (0f, -4.5f, false), (5.5f, -4.5f, false),
        (-5.5f, 3.5f, false), (0f, 3.5f, false), (5.5f, 3.5f, true),
    };

    [MenuItem("Tools/Armory/Rebuild Lighting")]
    static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "ArmoryHoldout") return "Open ArmoryHoldout first (active scene is " + scene.name + ")";
        string log = BuildFixture() + "\n";

        // Fixtures on the ceiling.
        var level = GameObject.Find("Level").transform;
        Transform group = level.Find("Ceiling Lights");
        if (group != null) Object.DestroyImmediate(group.gameObject);
        group = new GameObject("Ceiling Lights").transform;
        group.SetParent(level, false);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FixturePath);
        foreach (var f in Fixtures)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            go.transform.position = new Vector3(f.x, 3.85f, f.z);
            var so = new SerializedObject(go.GetComponentInChildren<EmergencyLight>());
            so.FindProperty("brokenness").floatValue = f.failing ? .6f : .04f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        log += $"{Fixtures.Length} ceiling fixtures\n";

        // Ambient and fog: murky blue-grey, never pure black.
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.062f, .066f, .08f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(.03f, .034f, .044f);
        RenderSettings.fogDensity = .04f;
        log += "ambient .10 cool, fog blue-grey .035\n";

        // Grading.
        var vol = Object.FindAnyObjectByType<Volume>();
        var profile = vol.sharedProfile;
        if (profile.TryGet(out ColorAdjustments ca)) { ca.contrast.value = 6f; ca.postExposure.value = .5f; ca.saturation.value = -20f; }
        if (profile.TryGet(out Tonemapping tm)) tm.mode.value = TonemappingMode.Neutral;
        if (profile.TryGet(out Vignette v)) v.intensity.value = .3f;
        if (profile.TryGet(out FilmGrain fg)) fg.intensity.value = .3f;
        EditorUtility.SetDirty(profile);
        log += "grading: contrast 6, exposure .5, neutral tonemap, vignette .3, grain .3\n";

        // Flashlight.
        var torch = Object.FindAnyObjectByType<Flashlight>(FindObjectsInactive.Include).GetComponent<Light>();
        torch.intensity = 15f; torch.range = 30f; torch.spotAngle = 66f; torch.innerSpotAngle = 34f;
        EditorUtility.SetDirty(torch);
        log += "flashlight 15 / 30 m / 66 deg\n";

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return log + "saved";
    }

    static string BuildFixture()
    {
        Material metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Station/Station_Rack.mat");
        Material dark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Station/Vent_Dark.mat");
        Material tube = TubeMaterial();
        var root = new GameObject("CeilingLight");
        Transform r = root.transform;
        Box("Hanger", r, new Vector3(-.5f, .075f, 0f), new Vector3(.015f, .15f, .015f), metal);
        Box("Hanger", r, new Vector3(.5f, .075f, 0f), new Vector3(.015f, .15f, .015f), metal);
        Box("Housing", r, new Vector3(0f, -.04f, 0f), new Vector3(1.3f, .08f, .34f), metal);
        Box("Reflector", r, new Vector3(0f, -.09f, 0f), new Vector3(1.24f, .02f, .28f), dark);
        var tubes = new[]
        {
            Box("Tube", r, new Vector3(0f, -.115f, .07f), new Vector3(1.18f, .035f, .035f), tube).GetComponent<Renderer>(),
            Box("Tube", r, new Vector3(0f, -.115f, -.07f), new Vector3(1.18f, .035f, .035f), tube).GetComponent<Renderer>(),
        };
        foreach (float x in new[] { -.42f, 0f, .42f }) Box("Cage", r, new Vector3(x, -.145f, 0f), new Vector3(.012f, .012f, .34f), metal);
        foreach (float z in new[] { -.15f, .15f }) Box("Cage", r, new Vector3(0f, -.145f, z), new Vector3(1.26f, .012f, .012f), metal);
        Box("End Cap", r, new Vector3(-.64f, -.1f, 0f), new Vector3(.03f, .06f, .3f), dark);
        Box("End Cap", r, new Vector3(.64f, -.1f, 0f), new Vector3(.03f, .06f, .3f), dark);

        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(r, false);
        light.transform.localPosition = new Vector3(0f, -.35f, 0f);
        light.type = LightType.Point; light.range = 7.5f; light.intensity = 2.6f; light.color = new Color(.82f, .9f, 1f);
        light.shadows = LightShadows.None;
        var em = light.gameObject.AddComponent<EmergencyLight>();
        var so = new SerializedObject(em);
        so.FindProperty("pulseDepth").floatValue = 0f;
        so.FindProperty("brokenness").floatValue = .04f;
        var g = so.FindProperty("glows");
        g.arraySize = tubes.Length;
        for (int i = 0; i < tubes.Length; i++) g.GetArrayElementAtIndex(i).objectReferenceValue = tubes[i];
        so.FindProperty("glowColor").colorValue = Tube * 1.8f;
        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, FixturePath);
        int tris = 0;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) tris += mf.sharedMesh.triangles.Length / 3;
        Object.DestroyImmediate(root);
        return $"{FixturePath}: {tris} triangles";
    }

    static Material TubeMaterial()
    {
        const string path = "Assets/Materials/Station/Ceiling_Tube.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", new Color(.75f, .8f, .85f));
        m.SetColor("_EmissionColor", Tube * 1.8f);
        m.SetFloat("_Lighting", 0f);
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

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
}
