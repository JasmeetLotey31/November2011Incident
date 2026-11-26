using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Blood and gore, built at runtime (no textures or prefabs needed):
/// - <see cref="Spray"/>: a burst of droplets and mist from a wound, and splats painted on whatever
///   surfaces the spray reaches (walls, floor);
/// - <see cref="Pool"/>: a spreading pool under a body or a feeding key.
/// Colour is per call, so keys bleed black-gold ichor and the crew bleeds red.
/// </summary>
public sealed class BloodVFX : MonoBehaviour
{
    public static readonly Color HumanBlood = new Color(.42f, .02f, .02f, 1f);
    public static readonly Color KeyIchor = new Color(.12f, .08f, .02f, 1f);

    const int MaxDecals = 120;
    const float DecalLifetime = 90f;

    static BloodVFX instance;

    ParticleSystem droplets, mist;
    Material dropletMaterial, decalMaterial;
    Texture2D splatTexture, softTexture;
    Mesh quad;
    readonly Queue<GameObject> decals = new Queue<GameObject>();
    static readonly RaycastHit[] hits = new RaycastHit[4];

    /// <summary>Wound spray at <paramref name="point"/> heading <paramref name="direction"/>. <paramref name="amount"/> ~1 for a normal hit.</summary>
    public static void Spray(Vector3 point, Vector3 direction, Color color, float amount = 1f)
    {
        Ensure();
        instance.DoSpray(point, direction, color, amount);
    }

    /// <summary>A pool of blood spreading on the floor under <paramref name="point"/>.</summary>
    public static void Pool(Vector3 point, Color color, float size = 1.2f)
    {
        Ensure();
        if (Physics.Raycast(point + Vector3.up * .5f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore))
            instance.Splat(hit.point, hit.normal, color, size, true);
    }

    static void Ensure()
    {
        if (instance != null) return;
        instance = FindAnyObjectByType<BloodVFX>();
        if (instance == null) instance = new GameObject("Blood VFX").AddComponent<BloodVFX>();
    }

    void Awake()
    {
        softTexture = MakeSoft(32);
        splatTexture = MakeSplat(128);
        quad = MakeQuad();
        dropletMaterial = MakeMaterial("Blood Droplets", softTexture);
        decalMaterial = MakeMaterial("Blood Splat", splatTexture);
        droplets = MakeParticles("Blood Droplets", .25f, .6f, 2f, 7f, .02f, .06f, 1.6f, true);
        mist = MakeParticles("Blood Mist", .25f, .5f, .4f, 1.5f, .15f, .35f, .2f, false);
    }

    void DoSpray(Vector3 point, Vector3 direction, Color color, float amount)
    {
        Vector3 dir = direction.sqrMagnitude > .001f ? direction.normalized : Vector3.up;
        var emit = new ParticleSystem.EmitParams { position = point, applyShapeToPosition = true };
        int count = Mathf.RoundToInt(Random.Range(14, 22) * amount);
        for (int i = 0; i < count; i++)
        {
            emit.velocity = (dir + Random.insideUnitSphere * .6f).normalized * Random.Range(2f, 7f) + Vector3.up * Random.Range(0f, 1.5f);
            emit.startColor = Color.Lerp(color, color * 1.6f, Random.value);
            droplets.Emit(emit, 1);
        }
        emit.velocity = dir * .6f;
        emit.startColor = new Color(color.r, color.g, color.b, .55f);
        mist.Emit(emit, Mathf.RoundToInt(3 * amount));

        // Paint splats where the spray lands: behind the wound and on the floor.
        int splats = Mathf.Clamp(Mathf.RoundToInt(2 * amount), 1, 5);
        for (int i = 0; i < splats; i++)
        {
            Vector3 ray = (dir + Random.insideUnitSphere * .45f + Vector3.down * Random.Range(0f, .8f)).normalized;
            int n = Physics.RaycastNonAlloc(point + ray * .05f, ray, hits, 4f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity; int bi = -1;
            for (int h = 0; h < n; h++)
            {
                if (hits[h].collider.GetComponentInParent<KeyTarget>() != null) continue; // not on the player capsule
                if (hits[h].distance < best) { best = hits[h].distance; bi = h; }
            }
            if (bi >= 0) Splat(hits[bi].point, hits[bi].normal, color, Random.Range(.35f, .8f) * Mathf.Sqrt(amount), false);
        }
    }

    void Splat(Vector3 point, Vector3 normal, Color color, float size, bool pool)
    {
        while (decals.Count > 0 && decals.Peek() == null) decals.Dequeue();
        while (decals.Count >= MaxDecals) { GameObject old = decals.Dequeue(); if (old != null) Destroy(old); }

        var go = new GameObject(pool ? "Blood Pool" : "Blood Splat");
        go.transform.SetPositionAndRotation(point + normal * (.004f + Random.value * .004f),
            Quaternion.LookRotation(-normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > .95f ? Vector3.forward : Vector3.up)
            * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = decalMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", new Color(color.r, color.g, color.b, .92f));
        r.SetPropertyBlock(block);
        if (pool) go.AddComponent<SpreadOut>().Begin(size, 2.5f);
        else go.transform.localScale = Vector3.one * size;
        decals.Enqueue(go);
        Destroy(go, DecalLifetime);
    }

    /// <summary>Grows a pool over time.</summary>
    sealed class SpreadOut : MonoBehaviour
    {
        float target, seconds, t;
        public void Begin(float size, float duration) { target = size; seconds = duration; transform.localScale = Vector3.one * size * .1f; }
        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            transform.localScale = Vector3.one * target * Mathf.Lerp(.1f, 1f, 1f - (1f - k) * (1f - k));
            if (k >= 1f) enabled = false;
        }
    }

    ParticleSystem MakeParticles(string name, float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, bool stretch)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.gravityModifier = gravity;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        var col = ps.collision;
        col.enabled = stretch; col.type = ParticleSystemCollisionType.World; col.dampen = .9f; col.bounce = 0f; col.lifetimeLoss = .6f; col.quality = ParticleSystemCollisionQuality.Low;
        var fade = ps.colorOverLifetime; fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.9f, .7f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        r.velocityScale = .04f; r.lengthScale = 1.5f;
        r.material = dropletMaterial;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    static Material MakeMaterial(string name, Texture texture)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var m = new Material(shader) { name = name, hideFlags = HideFlags.DontSave, renderQueue = (int)RenderQueue.Transparent };
        m.SetOverrideTag("RenderType", "Transparent");
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return m;
    }

    /// <summary>Irregular splat: a dense core, satellite drops and a few streaks.</summary>
    static Texture2D MakeSplat(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
        var a = new float[size * size];
        var blobs = new List<Vector3>();
        blobs.Add(new Vector3(.5f, .5f, .22f));
        for (int i = 0; i < 14; i++)
        {
            float ang = Random.value * Mathf.PI * 2f, d = Random.Range(.08f, .4f);
            blobs.Add(new Vector3(.5f + Mathf.Cos(ang) * d, .5f + Mathf.Sin(ang) * d, Random.Range(.02f, .09f) * (1f - d)));
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + .5f) / size, (y + .5f) / size);
                float v = 0f;
                foreach (Vector3 b in blobs) v = Mathf.Max(v, Mathf.Clamp01((b.z - Vector2.Distance(p, new Vector2(b.x, b.y))) * size * .25f));
                float edge = Mathf.Clamp01((.5f - Vector2.Distance(p, new Vector2(.5f, .5f))) * 8f);
                a[y * size + x] = v * edge;
            }
        for (int i = 0; i < a.Length; i++) tex.SetPixel(i % size, i / size, new Color(1f, 1f, 1f, a[i]));
        tex.Apply();
        return tex;
    }

    static Texture2D MakeSoft(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        float r = size * .5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(r, r)) / r;
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - d * d)));
            }
        tex.Apply();
        return tex;
    }

    static Mesh MakeQuad()
    {
        var m = new Mesh { name = "Blood Quad" };
        m.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
