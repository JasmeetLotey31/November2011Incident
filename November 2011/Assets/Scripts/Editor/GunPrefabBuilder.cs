using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the station's energy weapons from the Blender models (ArtSource/Guns/build_guns.py):
/// import settings (point-filtered atlas, no mipmaps, no compression), PS1 Lit materials (the
/// painted atlas plus an emissive Glow for cells, coils and emitters), and the gun prefabs
/// (Sidearm numbers, glow/bolt/zap via GunFX, cyan muzzle flash, spinning barrels).
/// Starts each prefab from the shotgun (muzzle, flash, laser sight, audio, HUD already wired).
/// Menu: Tools / Armory / Rebuild Energy Weapons.
/// </summary>
public static class GunPrefabBuilder
{
    const string Models = "Assets/Models/Guns/";
    const string Mats = "Assets/Materials/Weapons/";
    const string Prefabs = "Assets/Prefabs/Weapons/";
    static readonly Color Cyan = new Color(.35f, .95f, 1f);

    [MenuItem("Tools/Armory/Rebuild Energy Weapons")]
    static void BuildMenu() => Debug.Log(BuildAll());

    public static string BuildAll()
    {
        Material glow = Glow();
        Material flash = Tint("Muzzle_Flash", "Muzzle_Flash_Ion", new Color(.55f, .95f, 1f));
        Material boltMat = Tint("Laser_Red", "Bolt_Ion", new Color(.5f, .95f, 1f));
        string log = "";

        // ION CARBINE: full-auto station security carbine.
        log += Build("IonCarbine", "ION CARBINE", glow, flash, boltMat, new Vector3(0f, .005f, .39f), .12f, new Vector3(-.034f, .005f, .04f), so =>
        {
            Int(so, "magazineSize", 30); Int(so, "startingReserve", 90); Int(so, "maxReserve", 180);
            F(so, "tacticalReloadTime", 2f); F(so, "emptyReloadTime", 2.5f);
            F(so, "damage", 26f); F(so, "range", 90f); F(so, "fireInterval", .1f); B(so, "automatic", true);
            F(so, "noiseRadius", 40f);
            F(so, "baseSpread", .5f); F(so, "moveSpread", 1.4f); F(so, "sprintSpread", 4f); F(so, "spreadPerShot", .35f);
            F(so, "maxShotBloom", 3.2f); F(so, "bloomRecoveryPerSecond", 7f); F(so, "aimSpreadMultiplier", .35f);
            F(so, "aimFov", 50f); F(so, "aimMoveSpeedMultiplier", .7f);
            F(so, "recoilAmount", .6f); F(so, "recoilKickBack", .03f); F(so, "recoilLift", .006f);
            F(so, "recoilRiseDegrees", 3f); F(so, "recoilSettleSeconds", .1f);
            F(so, "cameraKickAmount", .45f); F(so, "cameraKickDegrees", .9f);
            F(so, "viewClimbDegrees", .55f); F(so, "viewClimbYaw", .25f);
            F(so, "carryMoveMultiplier", 1f); F(so, "spinUpSeconds", 0f);
            F(so, "firePitch", .8f); F(so, "pitchVariation", .06f);
        }, GunFX.Zap.Ion, false);

        // ARC REPEATER: rotary heavy gun, spins up, slows you down.
        log += Build("ArcRepeater", "ARC REPEATER", glow, flash, boltMat, new Vector3(0f, 0f, .61f), .16f, new Vector3(-.075f, -.02f, .05f), so =>
        {
            Int(so, "magazineSize", 100); Int(so, "startingReserve", 200); Int(so, "maxReserve", 300);
            F(so, "tacticalReloadTime", 4.2f); F(so, "emptyReloadTime", 4.8f);
            F(so, "damage", 30f); F(so, "range", 90f); F(so, "fireInterval", .086f); B(so, "automatic", true);
            F(so, "noiseRadius", 50f);
            F(so, "baseSpread", .9f); F(so, "moveSpread", 2.4f); F(so, "sprintSpread", 6f); F(so, "spreadPerShot", .3f);
            F(so, "maxShotBloom", 4.5f); F(so, "bloomRecoveryPerSecond", 6f); F(so, "aimSpreadMultiplier", .45f);
            F(so, "aimFov", 55f); F(so, "aimMoveSpeedMultiplier", .55f);
            F(so, "recoilAmount", .8f); F(so, "recoilKickBack", .04f); F(so, "recoilLift", .008f);
            F(so, "recoilRiseDegrees", 3.5f); F(so, "recoilSettleSeconds", .12f);
            F(so, "cameraKickAmount", .7f); F(so, "cameraKickDegrees", 1.2f);
            F(so, "viewClimbDegrees", .75f); F(so, "viewClimbYaw", .45f);
            F(so, "carryMoveMultiplier", .8f); F(so, "spinUpSeconds", .4f); F(so, "spinDegreesPerSecond", 1500f);
            F(so, "firePitch", .62f); F(so, "pitchVariation", .05f);
        }, GunFX.Zap.Arc, true);

        AssetDatabase.SaveAssets();
        return log;
    }

    static string Build(string file, string displayName, Material glow, Material flash, Material boltMat,
                        Vector3 muzzlePos, float flashScale, Vector3 cellPos, System.Action<SerializedObject> stats, GunFX.Zap zap, bool rotary)
    {
        // Import settings: the PS1 rules.
        string texPath = $"{Models}Textures/{file}_Atlas.png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
        ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 256; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
        string fbxPath = $"{Models}{file}.fbx";
        var mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.SaveAndReimport();

        Material body = Mat(file, AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        string prefabPath = $"{Prefabs}{file}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) AssetDatabase.CopyAsset(Prefabs + "Shotgun.prefab", prefabPath);
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            root.name = file;
            Transform model = root.transform.Find("Model");
            for (int i = model.childCount - 1; i >= 0; i--) Object.DestroyImmediate(model.GetChild(i).gameObject);
            var art = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath), model);
            art.name = "Art";
            var glowRenderers = new System.Collections.Generic.List<Renderer>();
            foreach (var mr in art.GetComponentsInChildren<MeshRenderer>(true))
            {
                int subs = mr.GetComponent<MeshFilter>().sharedMesh.subMeshCount;
                mr.sharedMaterials = subs > 1 ? new[] { body, glow } : new[] { body };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // a viewmodel
                if (subs > 1) glowRenderers.Add(mr);
            }
            Transform spinner = null;
            foreach (Transform t in art.GetComponentsInChildren<Transform>(true)) if (t.name == "Barrels") spinner = t;

            Transform muzzle = root.transform.Find("Muzzle");
            muzzle.localPosition = muzzlePos;
            var light = muzzle.GetComponentInChildren<Light>(true);
            light.color = new Color(.45f, .9f, 1f);
            var flashT = muzzle.Find("Muzzle Flash");
            flashT.localScale = Vector3.one * flashScale;
            flashT.GetComponent<Renderer>().sharedMaterial = flash;

            Transform boltT = root.transform.Find("Bolt");
            if (boltT == null) { boltT = new GameObject("Bolt").transform; boltT.SetParent(root.transform, false); }
            var bolt = boltT.GetComponent<LineRenderer>(); if (bolt == null) bolt = boltT.gameObject.AddComponent<LineRenderer>();
            bolt.positionCount = 2; bolt.useWorldSpace = true; bolt.sharedMaterial = boltMat;
            bolt.startWidth = .035f; bolt.endWidth = .014f; bolt.numCapVertices = 0;
            bolt.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; bolt.receiveShadows = false;
            bolt.enabled = false;

            // The energy cell lights its own gun: a small cyan glow on the body (GunFX drives it).
            Transform cellT = model.Find("Cell Light");
            if (cellT == null) { cellT = new GameObject("Cell Light").transform; cellT.SetParent(model, false); }
            cellT.localPosition = cellPos;
            var cell = cellT.GetComponent<Light>(); if (cell == null) cell = cellT.gameObject.AddComponent<Light>();
            cell.type = LightType.Point; cell.color = new Color(.4f, .95f, 1f); cell.range = .35f; cell.intensity = 0f; cell.shadows = LightShadows.None;

            var gun = root.GetComponent<Sidearm>();
            var so = new SerializedObject(gun);
            so.FindProperty("displayName").stringValue = displayName;
            Int(so, "pellets", 1); F(so, "pelletSpread", 0f); F(so, "knockback", 0f);
            so.FindProperty("spinner").objectReferenceValue = spinner;
            var clips = so.FindProperty("fireClips");
            string[] shots = { "Energy_Fire_A", "Energy_Fire_B", "Energy_Fire_C" };   // under the GunFX zap (SoundForge)
            clips.arraySize = shots.Length;
            for (int i = 0; i < shots.Length; i++)
                clips.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/Guns/{shots[i]}.wav");
            stats(so);
            so.ApplyModifiedPropertiesWithoutUndo();

            var fx = root.GetComponent<GunFX>(); if (fx == null) fx = root.AddComponent<GunFX>();
            var fso = new SerializedObject(fx);
            var gr = fso.FindProperty("glowRenderers");
            gr.arraySize = glowRenderers.Count;
            for (int i = 0; i < glowRenderers.Count; i++) gr.GetArrayElementAtIndex(i).objectReferenceValue = glowRenderers[i];
            fso.FindProperty("bolt").objectReferenceValue = bolt;
            fso.FindProperty("cellLight").objectReferenceValue = cell;
            fso.FindProperty("zap").enumValueIndex = (int)zap;
            fso.FindProperty("spinWhine").boolValue = rotary;
            fso.FindProperty("glowCharged").floatValue = rotary ? .75f : 1f;   // the big coils glow softer
            fso.ApplyModifiedPropertiesWithoutUndo();

            var hud = root.GetComponent<GunHUD>();
            if (hud != null)
            {
                var hso = new SerializedObject(hud);
                hso.FindProperty("ammoColor").colorValue = new Color(.4f, .92f, 1f, 1f);
                hso.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            int tris = 0;
            foreach (var mf in art.GetComponentsInChildren<MeshFilter>(true)) tris += mf.sharedMesh.triangles.Length / 3;
            return $"{prefabPath}: {tris} tris, {glowRenderers.Count} glowing renderer(s), spinner {(spinner != null ? spinner.name : "none")}\n";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ================= the crew's kinetic guns, in the same look =================

    [MenuItem("Tools/Armory/Rebuild Pistol and Shotgun")]
    static void BuildKineticMenu() => Debug.Log(BuildKinetic());

    /// <summary>Swaps the pistol's and shotgun's placeholder boxes for their Blender models (same
    /// station look as the energy guns: cream armour, dark steel, orange marks, small cyan lights).
    /// Only the art and the muzzle point change: stats, sounds, flash and handling stay as they are.</summary>
    public static string BuildKinetic()
    {
        Material indicator = Indicator();
        string log = Rearm("Pistol", "Sidearm", 128, new Vector3(0f, .022f, .137f), indicator)
             + Rearm("Shotgun", "Shotgun", 256, new Vector3(0f, .03f, .482f), indicator);
        AssetDatabase.SaveAssets();   // new materials only reach the disk on a save: without it they commit blank
        return log;
    }

    static string Rearm(string file, string prefabName, int atlasSize, Vector3 muzzlePos, Material indicator)
    {
        string texPath = $"{Models}Textures/{file}_Atlas.png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
        ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = atlasSize; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
        string fbxPath = $"{Models}{file}.fbx";
        var mi = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.SaveAndReimport();

        Material body = Mat(file, AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        string prefabPath = $"{Prefabs}{prefabName}.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform model = root.transform.Find("Model");
            for (int i = model.childCount - 1; i >= 0; i--) Object.DestroyImmediate(model.GetChild(i).gameObject);
            var art = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath), model);
            art.name = "Art";
            int tris = 0;
            foreach (var mr in art.GetComponentsInChildren<MeshRenderer>(true))
            {
                Mesh mesh = mr.GetComponent<MeshFilter>().sharedMesh;
                mr.sharedMaterials = mesh.subMeshCount > 1 ? new[] { body, indicator } : new[] { body };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // a viewmodel
                tris += mesh.triangles.Length / 3;
            }
            root.transform.Find("Muzzle").localPosition = muzzlePos;
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return $"{prefabPath}: {tris} tris\n";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>Sight dots and status lights on the kinetic guns: a steady soft cyan (nothing drives them).</summary>
    static Material Indicator()
    {
        string path = Mats + "Gun_Indicator.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", new Color(.2f, .55f, .6f));
        m.SetColor("_EmissionColor", Cyan * .7f);
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.SetFloat("_Lighting", 0f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Mat(string name, Texture2D atlas)
    {
        string path = Mats + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", atlas);
        m.SetColor("_BaseColor", Color.white);
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.SetFloat("_Affine", .25f);
        // Half-lit, like PS1 weapons: the gun stays readable in a pitch-black hall but still takes the light.
        m.SetFloat("_Lighting", .5f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Glow()
    {
        string path = Mats + "Gun_Glow.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", new Color(.25f, .7f, .8f));
        m.SetColor("_EmissionColor", Cyan * 1f);      // the game drives it with the charge (GunFX)
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.SetFloat("_Lighting", 0f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Tint(string from, string name, Color c)
    {
        string path = Mats + name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) == null) AssetDatabase.CopyAsset(Mats + from + ".mat", path);
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void F(SerializedObject so, string n, float v) { var p = so.FindProperty(n); if (p == null) Debug.LogWarning("no field " + n); else p.floatValue = v; }
    static void Int(SerializedObject so, string n, int v) { var p = so.FindProperty(n); if (p == null) Debug.LogWarning("no field " + n); else p.intValue = v; }
    static void B(SerializedObject so, string n, bool v) { var p = so.FindProperty(n); if (p == null) Debug.LogWarning("no field " + n); else p.boolValue = v; }
}
