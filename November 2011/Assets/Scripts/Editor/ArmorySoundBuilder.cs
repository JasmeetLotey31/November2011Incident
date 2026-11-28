using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts the SoundForge sounds into the game: the guns' fire and reload sounds, and (with ArmoryHoldout
/// open) the station ambience, the wave / radio / power cues, the steel room's reverb, the player's
/// footsteps, and silences the old western bell on power cuts. The ceiling fixtures' hum is set up by
/// ArmoryLightingBuilder. Menu: Tools / Audio / Wire Up Sounds (run Tools / Audio / Rebuild Sounds first).
/// </summary>
public static class ArmorySoundBuilder
{
    const string Station = "Assets/Audio/Station/";
    const string Guns = "Assets/Audio/Guns/";
    const string Weapons = "Assets/Prefabs/Weapons/";

    [MenuItem("Tools/Audio/Wire Up Sounds")]
    static void BuildMenu() => Debug.Log(Build());

    static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    static AudioClip[] Clips(string prefix, int count)
    {
        var a = new AudioClip[count];
        for (int i = 0; i < count; i++) a[i] = Clip($"{prefix}_{(char)('A' + i)}.wav");
        return a;
    }

    public static string Build()
    {
        if (EditorApplication.isPlaying) return "Stop play mode first";
        string log = "";
        // ---- guns: each its own voice ----
        log += Gun("Sidearm", Clips(Guns + "Pistol_Fire", 3), Clip(Guns + "Mag_Out.wav"), Clip(Guns + "Mag_In.wav"), Clip(Guns + "Slide_Release.wav"));
        log += Gun("Shotgun", Clips(Guns + "Shotgun_Fire", 3), Clip("Assets/Audio/NailGun/NailGun_Reload_Seat_A.wav"),
                   Clip("Assets/Audio/NailGun/NailGun_Reload_Seat_C.wav"), Clip(Guns + "Shotgun_Pump.wav"));
        foreach (string energy in new[] { "IonCarbine", "ArcRepeater" })
            log += Gun(energy, Clips(Guns + "Energy_Fire", 3), Clip(Guns + "Mag_Out.wav"), Clip(Guns + "Cell_In.wav"), null);

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "ArmoryHoldout") return log + "(open ArmoryHoldout to wire the room)";

        // ---- the room: ambience, wave cues, reverb ----
        var root = GameObject.Find("Station Audio");
        if (root != null) Object.DestroyImmediate(root);
        root = new GameObject("Station Audio");
        var amb = root.AddComponent<StationAmbience>();
        var aso = new SerializedObject(amb);
        aso.FindProperty("roomTone").objectReferenceValue = Clip(Station + "Room_Tone.wav");
        SetArray(aso.FindProperty("groans"), Clips(Station + "Hull_Groan", 3));
        SetArray(aso.FindProperty("ticks"), Clips(Station + "Metal_Tick", 4));
        aso.ApplyModifiedPropertiesWithoutUndo();
        var wave = root.AddComponent<WaveAudio>();
        var wso = new SerializedObject(wave);
        wso.FindProperty("klaxon").objectReferenceValue = Clip(Station + "Wave_Klaxon.wav");
        wso.FindProperty("allClear").objectReferenceValue = Clip(Station + "Wave_AllClear.wav");
        wso.FindProperty("radioIn").objectReferenceValue = Clip(Station + "Radio_In.wav");
        wso.FindProperty("radioOut").objectReferenceValue = Clip(Station + "Radio_Out.wav");
        wso.FindProperty("powerDown").objectReferenceValue = Clip(Station + "Power_Down.wav");
        wso.ApplyModifiedPropertiesWithoutUndo();
        // A steel room ~22 x 18 x 4 m: about a second and a half of hard, bright reverb.
        var verb = new GameObject("Steel Room Reverb").AddComponent<AudioReverbZone>();
        verb.transform.SetParent(root.transform, false);
        verb.transform.position = new Vector3(0f, 2f, 0f);
        verb.minDistance = 14f; verb.maxDistance = 22f;
        verb.reverbPreset = AudioReverbPreset.Hallway;
        log += "room: ambience, wave cues, reverb\n";

        // The old western bell on power cuts goes (WaveAudio plays the power dying instead).
        var director = Object.FindAnyObjectByType<KeyHordeDirector>();
        if (director != null)
        {
            var dso = new SerializedObject(director);
            dso.FindProperty("blackoutClip").objectReferenceValue = null;
            dso.ApplyModifiedPropertiesWithoutUndo();
        }

        // A limiter on the final mix, next to the listener.
        var listener = Object.FindAnyObjectByType<AudioListener>();
        if (listener != null && listener.GetComponent<MasterLimiter>() == null) listener.gameObject.AddComponent<MasterLimiter>();

        // Footsteps on the player.
        var fpc = Object.FindAnyObjectByType<FirstPersonController>();
        if (fpc != null)
        {
            var steps = fpc.GetComponent<PlayerFootsteps>();
            if (steps == null) steps = fpc.gameObject.AddComponent<PlayerFootsteps>();
            var sso = new SerializedObject(steps);
            SetArray(sso.FindProperty("steps"), Clips(Station + "Step_Deck", 6));
            sso.ApplyModifiedPropertiesWithoutUndo();
            log += "player: footsteps\n";
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return log + "saved";
    }

    static string Gun(string prefab, AudioClip[] fire, AudioClip reloadStart, AudioClip reloadInsert, AudioClip reloadEnd)
    {
        string path = Weapons + prefab + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var so = new SerializedObject(root.GetComponent<Sidearm>());
            SetArray(so.FindProperty("fireClips"), fire);
            so.FindProperty("reloadStartClip").objectReferenceValue = reloadStart;
            so.FindProperty("reloadInsertClip").objectReferenceValue = reloadInsert;
            so.FindProperty("reloadEndClip").objectReferenceValue = reloadEnd;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return $"{prefab}: {fire[0]?.name}.., reload {reloadStart?.name} / {reloadInsert?.name} / {(reloadEnd != null ? reloadEnd.name : "-")}\n";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void SetArray(SerializedProperty p, Object[] items)
    {
        p.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }
}
