using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The dead crew on the Armory floor, from the Blender bodies (ArtSource/Crew/build_crew.py): import
/// settings (point-filtered atlas, no mipmaps, no compression), PS1 Lit materials, and two corpse
/// prefabs, face down and dragged (CrewCorpse) and on its back, eaten open (CrewCorpse_Torn). The
/// keys still feed on them as before: the Corpse component and the "Body" it shrinks are kept, only
/// the art under Body changes. With ArmoryHoldout open it also sets which pose each body in the
/// level uses. Menu: Tools / Armory / Rebuild Crew Bodies.
/// </summary>
public static class CrewBodyBuilder
{
    const string Models = "Assets/Models/Crew/";
    const string Mats = "Assets/Materials/Keys/";
    const string Prone = "Assets/Prefabs/Level/CrewCorpse.prefab";
    const string Torn = "Assets/Prefabs/Level/CrewCorpse_Torn.prefab";

    // Where the bodies lie in the Armory, which pose, and (for some) which way the head points.
    static readonly (Vector2 at, bool torn, float yaw)[] Placement =
    {
        (new Vector2(-7f, 1f), false, 116.6f),    // dragged feet-first toward the west vent, reaching away
        (new Vector2(6f, -2.5f), false, -1f),
        (new Vector2(-2.5f, 6.8f), true, -1f),    // at the last stand
        (new Vector2(8.5f, 6f), true, -1f),
        (new Vector2(-8.5f, -7.5f), false, -1f),
    };

    [MenuItem("Tools/Armory/Rebuild Crew Bodies")]
    static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        if (EditorApplication.isPlaying) return "Stop play mode first";
        string log = Prefab(Prone, null, "CrewBody_Prone") + Prefab(Torn, Prone, "CrewBody_Torn");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name == "ArmoryHoldout") log += Place(scene);
        AssetDatabase.SaveAssets();
        return log;
    }

    static string Prefab(string path, string copyFrom, string art)
    {
        string tex = $"{Models}Textures/{art}_Atlas.png", fbx = $"{Models}{art}.fbx";
        var ti = (TextureImporter)AssetImporter.GetAtPath(tex);
        ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 256; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
        var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
        mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.SaveAndReimport();

        string matPath = Mats + "Crew_" + art.Replace("CrewBody_", "Body_") + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (m == null) { m = new Material(Shader.Find("Retro/PS1 Lit")); AssetDatabase.CreateAsset(m, matPath); }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
        m.SetColor("_BaseColor", Color.white);
        m.SetVector("_SnapResolution", new Vector4(320f, 240f, 0f, 0f));
        m.SetFloat("_Affine", .25f);
        m.SetFloat("_Lighting", 1f);            // lit only by the room: the dark corners keep them hidden
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);

        if (copyFrom != null && AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) AssetDatabase.CopyAsset(copyFrom, path);
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform body = root.transform.Find("Body");
            for (int i = body.childCount - 1; i >= 0; i--) Object.DestroyImmediate(body.GetChild(i).gameObject);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), body);
            go.name = "Art";
            int tris = 0;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.sharedMaterial = m;
                tris += mr.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return $"{path}: {tris} tris\n";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static string Place(UnityEngine.SceneManagement.Scene scene)
    {
        var group = GameObject.Find("Crew Bodies");
        if (group == null) return "no Crew Bodies group in the scene\n";
        var prone = AssetDatabase.LoadAssetAtPath<GameObject>(Prone);
        var torn = AssetDatabase.LoadAssetAtPath<GameObject>(Torn);
        int swapped = 0;
        var bodies = new System.Collections.Generic.List<Transform>();
        foreach (Transform t in group.transform) bodies.Add(t);
        foreach (Transform t in bodies)
        {
            var spot = new Vector2(t.position.x, t.position.z);
            foreach (var p in Placement)
            {
                if (Vector2.Distance(spot, p.at) > .5f) continue;
                GameObject want = p.torn ? torn : prone;
                Transform body = t;
                if (PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) != want)
                {
                    var fresh = (GameObject)PrefabUtility.InstantiatePrefab(want, group.transform);
                    fresh.transform.SetPositionAndRotation(t.position, t.rotation);
                    fresh.transform.SetSiblingIndex(t.GetSiblingIndex());
                    Object.DestroyImmediate(t.gameObject);
                    body = fresh.transform;
                    swapped++;
                }
                if (p.yaw >= 0f) body.rotation = Quaternion.Euler(0f, p.yaw, 0f);
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"scene: {bodies.Count} bodies, {swapped} swapped to their pose; saved\n";
    }
}
