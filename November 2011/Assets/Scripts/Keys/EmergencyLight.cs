using UnityEngine;

/// <summary>
/// Red emergency light: a slow alarm pulse, plus a broken flicker that sometimes drops it out.
/// Put on a Light. Each one gets its own timing so the room never pulses in sync.
/// </summary>
[RequireComponent(typeof(Light))]
public sealed class EmergencyLight : MonoBehaviour
{
    [SerializeField] float pulseSpeed = 1.6f;
    [Range(0, 1)] [SerializeField] float pulseDepth = .55f;
    [Tooltip("0 = steady, 1 = badly broken.")]
    [Range(0, 1)] [SerializeField] float brokenness = .25f;

    /// <summary>All station lights go (nearly) dark until this time: the keys cut the power.</summary>
    public static float BlackoutUntil { get; private set; } = -1f;
    public static bool IsBlackout => Time.time < BlackoutUntil;

    /// <summary>Kill every emergency light for <paramref name="seconds"/> (they stutter back on after).</summary>
    public static void Blackout(float seconds) => BlackoutUntil = Mathf.Max(BlackoutUntil, Time.time + seconds);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => BlackoutUntil = -1f;

    Light lightSource;
    float baseIntensity, offset, outUntil, blackoutFade = 1f;

    void Awake()
    {
        lightSource = GetComponent<Light>();
        baseIntensity = lightSource.intensity;
        offset = Random.value * 10f;
    }

    void Update()
    {
        float t = Time.time + offset;
        float pulse = 1f - pulseDepth * (.5f + .5f * Mathf.Sin(t * pulseSpeed * Mathf.PI * 2f));
        if (Time.time < outUntil) pulse *= .05f;
        else if (Random.value < brokenness * Time.deltaTime * 2f) outUntil = Time.time + Random.Range(.05f, .4f);
        float buzz = 1f + (Mathf.PerlinNoise(t * 20f, offset) - .5f) * brokenness * .6f;

        // Blackout: snap off, then stutter back on unevenly when it ends.
        blackoutFade = IsBlackout ? 0f : Mathf.MoveTowards(blackoutFade, 1f, Time.deltaTime * Random.Range(.5f, 3f));
        float power = IsBlackout ? .04f : (Random.value < blackoutFade ? 1f : .1f);
        lightSource.intensity = baseIntensity * pulse * buzz * power;
    }
}
