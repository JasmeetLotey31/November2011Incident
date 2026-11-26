using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Bullet impact: dust puff, chips, sparks, a tiny flash and a small hole mark.
/// Ported from the Western project's RevolverImpactVFX (runtime-built, pooled particles, no
/// prefab needed).
/// Call <see cref="Play"/>; the effect object creates itself on first use.
/// </summary>
[DisallowMultipleComponent]
public sealed class ImpactVFX : MonoBehaviour
{
    private const string BulletHoleTexturePath =
        "NailGunVFX/nail_puncture";

    private const string DustTexturePath =
        "NailGunVFX/impact_dust";

    private const int MaximumBulletHoles = 48;
    private const float BulletHoleLifetime = 60f;

    private static ImpactVFX instance;

    /// <summary>Plays the impact. <paramref name="hitTarget"/>: hit a key (sparks only, no mark on the creature).</summary>
    public static void Play(RaycastHit hit, bool hitTarget)
    {
        Ensure();
        instance.PlayImpact(hit, hitTarget);
    }

    /// <summary>A burst of sparks and chips at any point (e.g. a key's eye bursting). <paramref name="count"/> scales it.</summary>
    public static void PlaySparks(Vector3 point, Vector3 normal, float count = 1f)
    {
        Ensure();
        if (!instance.initialized) instance.BuildEffect();
        if (!instance.initialized) return;
        instance.impactRoot.SetPositionAndRotation(point + normal * 0.012f, CreateSurfaceRotation(normal));
        instance.sparkParticles.Emit(Mathf.RoundToInt(Random.Range(10, 16) * count));
        instance.debrisParticles.Emit(Mathf.RoundToInt(Random.Range(3, 6) * count));
        if (instance.lightRoutine != null) instance.StopCoroutine(instance.lightRoutine);
        instance.lightRoutine = instance.StartCoroutine(instance.PlayImpactLight());
    }

    private static void Ensure()
    {
        if (instance != null) return;
        instance = FindAnyObjectByType<ImpactVFX>();
        if (instance == null)
            instance = new GameObject("Impact VFX").AddComponent<ImpactVFX>();
    }

    private readonly Queue<GameObject> bulletHoles =
        new Queue<GameObject>();

    private Transform impactRoot;
    private ParticleSystem dustParticles;
    private ParticleSystem debrisParticles;
    private ParticleSystem sparkParticles;
    private Light impactLight;

    private Material dustMaterial;
    private Material debrisMaterial;
    private Material sparkMaterial;
    private Material bulletHoleMaterial;

    private Mesh chipMesh;
    private Mesh quadMesh;
    private Coroutine lightRoutine;
    private bool initialized;

    private void Awake()
    {
        BuildEffect();
    }

    public void PlayImpact(RaycastHit hit, bool hitTarget)
    {
        if (!initialized)
        {
            BuildEffect();
        }

        if (!initialized || hit.collider == null)
        {
            return;
        }

        Vector3 effectPosition =
            hit.point + hit.normal * 0.012f;

        Quaternion surfaceRotation =
            CreateSurfaceRotation(hit.normal);

        impactRoot.SetPositionAndRotation(
            effectPosition,
            surfaceRotation
        );

        // Keys are metal: hitting one throws sparks, no dust or hole mark.
        if (hitTarget)
        {
            sparkParticles.Emit(Random.Range(5, 9));
            debrisParticles.Emit(Random.Range(2, 4));
        }
        else
        {
            dustParticles.Emit(Random.Range(1, 3));
            debrisParticles.Emit(Random.Range(4, 8));
            sparkParticles.Emit(Random.Range(2, 5));
            CreateBulletHole(hit);
        }

        if (lightRoutine != null)
        {
            StopCoroutine(lightRoutine);
        }

        lightRoutine = StartCoroutine(PlayImpactLight());
    }

    private void BuildEffect()
    {
        if (initialized)
        {
            return;
        }

        Texture2D bulletHoleTexture =
            Resources.Load<Texture2D>(BulletHoleTexturePath);

        Texture2D dustTexture =
            Resources.Load<Texture2D>(DustTexturePath);

        if (bulletHoleTexture == null || dustTexture == null)
        {
            Debug.LogWarning(
                "ImpactVFX: Impact textures could not be loaded from Resources/NailGunVFX.",
                this
            );

            return;
        }

        GameObject rootObject =
            new GameObject("Runtime Impact VFX");

        impactRoot = rootObject.transform;
        impactRoot.SetPositionAndRotation(
            Vector3.zero,
            Quaternion.identity
        );
        impactRoot.localScale = Vector3.one;

        dustMaterial = CreateParticleMaterial(
            "Runtime Concrete Dust",
            dustTexture,
            false
        );

        debrisMaterial = CreateParticleMaterial(
            "Runtime Concrete Chips",
            Texture2D.whiteTexture,
            false
        );

        sparkMaterial = CreateParticleMaterial(
            "Runtime Impact Sparks",
            Texture2D.whiteTexture,
            true
        );

        bulletHoleMaterial = CreateParticleMaterial(
            "Runtime Concrete Bullet Hole",
            bulletHoleTexture,
            false
        );

        chipMesh = CreateChipMesh();
        quadMesh = CreateQuadMesh();

        dustParticles = CreateDustParticles();
        debrisParticles = CreateDebrisParticles();
        sparkParticles = CreateSparkParticles();
        impactLight = CreateImpactLight();

        initialized = true;
    }

    private ParticleSystem CreateDustParticles()
    {
        ParticleSystem particles =
            CreateParticleObject("Impact Dust");

        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 24;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            0.20f,
            0.38f
        );
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.18f, 0.55f);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
        main.startSizeZ = 1f;
        main.startRotation = new ParticleSystem.MinMaxCurve(
            0f,
            Mathf.PI * 2f
        );
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.72f, 0.67f, 0.58f, 0.45f),
            new Color(0.94f, 0.91f, 0.84f, 0.68f)
        );

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 26f;
        shape.radius = 0.012f;
        shape.length = 0.025f;

        ParticleSystem.ColorOverLifetimeModule color =
            particles.colorOverLifetime;

        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(
            CreateDustGradient()
        );

        ParticleSystem.SizeOverLifetimeModule size =
            particles.sizeOverLifetime;

        size.enabled = true;
        size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 0.42f),
                new Keyframe(0.14f, 1f),
                new Keyframe(1f, 1.38f)
            )
        );

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.quality = ParticleSystemNoiseQuality.High;
        noise.strength = new ParticleSystem.MinMaxCurve(0.01f, 0.028f);
        noise.frequency = 0.7f;
        noise.scrollSpeed = 0.25f;
        noise.damping = true;

        ParticleSystemRenderer renderer =
            particles.GetComponent<ParticleSystemRenderer>();

        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.material = dustMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 12;

        return particles;
    }

    private ParticleSystem CreateDebrisParticles()
    {
        ParticleSystem particles =
            CreateParticleObject("Concrete Chips");

        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 96;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.52f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.85f, 2.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.012f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(
            0f,
            Mathf.PI * 2f
        );
        main.startRotationY = new ParticleSystem.MinMaxCurve(
            0f,
            Mathf.PI * 2f
        );
        main.startRotationZ = new ParticleSystem.MinMaxCurve(
            0f,
            Mathf.PI * 2f
        );
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.38f, 0.36f, 0.32f, 1f),
            new Color(0.82f, 0.78f, 0.68f, 1f)
        );
        main.gravityModifier = 1.15f;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 42f;
        shape.radius = 0.008f;
        shape.length = 0.018f;

        ParticleSystem.ColorOverLifetimeModule color =
            particles.colorOverLifetime;

        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(
            CreateDebrisGradient()
        );

        ParticleSystemRenderer renderer =
            particles.GetComponent<ParticleSystemRenderer>();

        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = chipMesh;
        renderer.material = debrisMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 14;

        return particles;
    }

    private ParticleSystem CreateSparkParticles()
    {
        ParticleSystem particles =
            CreateParticleObject("Impact Sparks");

        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 48;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 4.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.002f, 0.0055f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.36f, 0.03f, 0.75f),
            new Color(1f, 0.92f, 0.48f, 1f)
        );
        main.gravityModifier = 0.28f;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 34f;
        shape.radius = 0.006f;
        shape.length = 0.012f;

        ParticleSystem.ColorOverLifetimeModule color =
            particles.colorOverLifetime;

        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(
            CreateSparkGradient()
        );

        ParticleSystemRenderer renderer =
            particles.GetComponent<ParticleSystemRenderer>();

        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.06f;
        renderer.lengthScale = 0.28f;
        renderer.material = sparkMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 18;

        return particles;
    }

    private Light CreateImpactLight()
    {
        GameObject lightObject = new GameObject("Impact Light");
        lightObject.transform.SetParent(impactRoot, false);

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.54f, 0.20f);
        light.range = 0.55f;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForcePixel;

        return light;
    }

    private IEnumerator PlayImpactLight()
    {
        const float lifetime = 0.032f;

        float peak = Random.Range(0.65f, 1.15f);
        float elapsed = 0f;

        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / lifetime);
            impactLight.intensity = Mathf.Lerp(peak, 0f, t * t);

            yield return null;
        }

        impactLight.intensity = 0f;
        lightRoutine = null;
    }

    private void CreateBulletHole(RaycastHit hit)
    {
        RemoveExpiredQueueEntries();

        while (bulletHoles.Count >= MaximumBulletHoles)
        {
            GameObject oldest = bulletHoles.Dequeue();

            if (oldest != null)
            {
                Destroy(oldest);
            }
        }

        GameObject decal = new GameObject("Bullet Hole");
        decal.transform.SetPositionAndRotation(
            hit.point + hit.normal * 0.0025f,
            CreateSurfaceRotation(hit.normal) *
            Quaternion.Euler(0f, 0f, Random.Range(0f, 360f))
        );

        // Static level geometry often has a large hierarchy scale. Keeping
        // those decals in world space prevents a 4 cm mark from becoming a
        // giant patch on floors or modular structures. Moving rigidbodies
        // still own their decals so marks travel with the hit object.
        if (hit.rigidbody != null)
        {
            decal.transform.SetParent(hit.rigidbody.transform, true);
        }

        float size = Random.Range(0.06f, 0.08f); // pistol round
        SetUniformWorldScale(decal.transform, size);

        MeshFilter meshFilter = decal.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = quadMesh;

        MeshRenderer meshRenderer = decal.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = bulletHoleMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.sortingOrder = 8;

        bulletHoles.Enqueue(decal);
        Destroy(decal, BulletHoleLifetime);
    }

    private static Quaternion CreateSurfaceRotation(Vector3 normal)
    {
        Vector3 surfaceNormal = normal.normalized;

        // A ground normal is parallel to Vector3.up, so using the default
        // LookRotation up vector makes its roll ambiguous. Forward provides
        // a stable tangent for floors and ceilings; walls continue using up.
        Vector3 tangentUp =
            Mathf.Abs(Vector3.Dot(surfaceNormal, Vector3.up)) > 0.95f
                ? Vector3.forward
                : Vector3.up;

        return Quaternion.LookRotation(surfaceNormal, tangentUp);
    }

    private static void SetUniformWorldScale(
        Transform target,
        float worldSize
    )
    {
        if (target.parent == null)
        {
            target.localScale = Vector3.one * worldSize;
            return;
        }

        Vector3 parentScale = target.parent.lossyScale;

        target.localScale = new Vector3(
            worldSize / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
            worldSize / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f),
            worldSize / Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f)
        );
    }

    private void RemoveExpiredQueueEntries()
    {
        while (bulletHoles.Count > 0 && bulletHoles.Peek() == null)
        {
            bulletHoles.Dequeue();
        }
    }

    private ParticleSystem CreateParticleObject(string objectName)
    {
        GameObject particleObject = new GameObject(objectName);
        particleObject.transform.SetParent(impactRoot, false);

        return particleObject.AddComponent<ParticleSystem>();
    }

    private static Material CreateParticleMaterial(
        string materialName,
        Texture texture,
        bool additive
    )
    {
        Shader shader = Shader.Find(
            "Universal Render Pipeline/Particles/Unlit"
        );

        if (shader == null)
        {
            shader = Shader.Find("Particles/Standard Unlit");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSave,
            renderQueue = (int)RenderQueue.Transparent
        };

        material.SetOverrideTag("RenderType", "Transparent");

        SetTextureIfPresent(material, "_BaseMap", texture);
        SetTextureIfPresent(material, "_MainTex", texture);
        SetColorIfPresent(material, "_BaseColor", Color.white);
        SetColorIfPresent(material, "_Color", Color.white);
        SetFloatIfPresent(material, "_Surface", 1f);
        SetFloatIfPresent(material, "_Blend", additive ? 1f : 0f);
        SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
        SetFloatIfPresent(
            material,
            "_DstBlend",
            additive
                ? (float)BlendMode.One
                : (float)BlendMode.OneMinusSrcAlpha
        );
        SetFloatIfPresent(material, "_ZWrite", 0f);
        SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetShaderPassEnabled("ShadowCaster", false);

        return material;
    }

    private static Mesh CreateChipMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "Runtime Concrete Chip"
        };

        mesh.vertices = new[]
        {
            new Vector3(0f, 0.60f, 0f),
            new Vector3(-0.50f, -0.38f, 0.34f),
            new Vector3(0.50f, -0.38f, 0.34f),
            new Vector3(0f, -0.38f, -0.46f)
        };

        mesh.triangles = new[]
        {
            0, 1, 2,
            0, 2, 3,
            0, 3, 1,
            1, 3, 2
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "Runtime Bullet Hole Quad"
        };

        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f)
        };

        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };

        mesh.triangles = new[]
        {
            0, 2, 1,
            2, 3, 1
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private static Gradient CreateDustGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(
                    new Color(0.92f, 0.88f, 0.80f),
                    0f
                ),
                new GradientColorKey(
                    new Color(0.63f, 0.61f, 0.58f),
                    1f
                )
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.95f, 0.05f),
                new GradientAlphaKey(0.55f, 0.42f),
                new GradientAlphaKey(0f, 1f)
            }
        );

        return gradient;
    }

    private static Gradient CreateDebrisGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(
                    new Color(0.76f, 0.72f, 0.65f),
                    1f
                )
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.72f),
                new GradientAlphaKey(0f, 1f)
            }
        );

        return gradient;
    }

    private static Gradient CreateSparkGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(
                    new Color(1f, 0.42f, 0.04f),
                    0.58f
                ),
                new GradientColorKey(
                    new Color(0.36f, 0.025f, 0f),
                    1f
                )
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.82f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            }
        );

        return gradient;
    }

    private static void SetTextureIfPresent(
        Material material,
        string property,
        Texture texture
    )
    {
        if (material.HasProperty(property))
        {
            material.SetTexture(property, texture);
        }
    }

    private static void SetColorIfPresent(
        Material material,
        string property,
        Color color
    )
    {
        if (material.HasProperty(property))
        {
            material.SetColor(property, color);
        }
    }

    private static void SetFloatIfPresent(
        Material material,
        string property,
        float value
    )
    {
        if (material.HasProperty(property))
        {
            material.SetFloat(property, value);
        }
    }

    private void OnDestroy()
    {
        if (impactRoot != null)
        {
            DestroyRuntimeObject(impactRoot.gameObject);
            impactRoot = null;
        }

        DestroyRuntimeObject(dustMaterial);
        DestroyRuntimeObject(debrisMaterial);
        DestroyRuntimeObject(sparkMaterial);
        DestroyRuntimeObject(bulletHoleMaterial);
        DestroyRuntimeObject(chipMesh);
        DestroyRuntimeObject(quadMesh);
    }

    private static void DestroyRuntimeObject(Object runtimeObject)
    {
        if (runtimeObject != null)
        {
            Destroy(runtimeObject);
        }
    }
}
