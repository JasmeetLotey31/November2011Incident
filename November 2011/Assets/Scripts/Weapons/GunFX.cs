using UnityEngine;

/// <summary>
/// What makes the station's energy weapons feel like space guns (on the gun, next to Sidearm):
/// - GLOW: the cyan parts (energy cell, coils, emitter ring, sight dot) burn with the charge
///   left in the cell, flare on every shot, pulse while it recharges and go dark when it's empty.
///   A rotary gun's coils brighten as it spins up.
/// - BOLT: a short cyan streak from the muzzle to wherever the shot went.
/// - ZAP: a synthesized energy crack layered over the gun's own (real) shot sound.
/// - WHINE: a rotary gun's motor, rising with the spin.
/// </summary>
[RequireComponent(typeof(Sidearm))]
[DisallowMultipleComponent]
public sealed class GunFX : MonoBehaviour
{
    public enum Zap { None, Ion, Arc }

    [Header("Glow")]
    [Tooltip("Renderers with glowing parts, and which of their materials is the glow.")]
    [SerializeField] Renderer[] glowRenderers;
    [SerializeField] int glowMaterialIndex = 1;
    [SerializeField] Color glowColor = new Color(.35f, .95f, 1f);
    [SerializeField] float glowCharged = 1.1f;
    [SerializeField] float glowShot = 1.8f;

    [Tooltip("Small light at the cell that lights the gun itself, following the glow.")]
    [SerializeField] Light cellLight;
    [SerializeField] float cellLightIntensity = .9f;

    [Header("Bolt")]
    [SerializeField] LineRenderer bolt;
    [SerializeField] float boltSeconds = .05f;
    [SerializeField] float boltWidth = .035f;

    [Header("Sound")]
    [SerializeField] Zap zap = Zap.Ion;
    [Range(0, 1)] [SerializeField] float zapVolume = .5f;
    [Tooltip("Motor whine for rotary guns (follows the spin-up).")]
    [SerializeField] bool spinWhine;

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static AudioClip ionClip, arcClip, whineClip;
    Sidearm gun;
    MaterialPropertyBlock block;
    AudioSource zapSource, whineSource;
    float shotAt = -10f;
    Vector3 boltFrom, boltTo;

    void Awake()
    {
        gun = GetComponent<Sidearm>();
        block = new MaterialPropertyBlock();
        if (ionClip == null) ionClip = Sweep("Ion Zap", .12f, 2300f, 360f, .55f, .25f);
        if (arcClip == null) arcClip = Sweep("Arc Zap", .085f, 1150f, 150f, .7f, .45f);
        if (whineClip == null) whineClip = Whine();
        zapSource = Source("Zap", false);
        if (spinWhine) { whineSource = Source("Whine", true); whineSource.clip = whineClip; }
        if (bolt != null) bolt.enabled = false;
    }

    AudioSource Source(string name, bool loop)
    {
        var s = new GameObject(name).AddComponent<AudioSource>();
        s.transform.SetParent(transform, false);
        s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f; s.volume = loop ? 0f : 1f;
        return s;
    }

    void OnEnable() => gun.Fired += OnFired;

    void OnDisable()
    {
        gun.Fired -= OnFired;
        if (bolt != null) bolt.enabled = false;
        if (whineSource != null) whineSource.Stop();
    }

    void OnFired()
    {
        shotAt = Time.time;
        boltFrom = gun.Muzzle != null ? gun.Muzzle.position : transform.position;
        boltTo = gun.LastShotEnd;
        AudioClip clip = zap == Zap.Ion ? ionClip : zap == Zap.Arc ? arcClip : null;
        if (clip != null)
        {
            zapSource.pitch = Random.Range(.94f, 1.06f);
            zapSource.PlayOneShot(clip, zapVolume);
        }
    }

    void LateUpdate()
    {
        float now = Time.time;
        // Glow: the charge left, a flare per shot, a pulse while recharging, dark when empty.
        float charge = gun.MagazineSize > 0 ? gun.Loaded / (float)gun.MagazineSize : 0f;
        float level = gun.IsReloading ? .35f + .35f * Mathf.Sin(now * 12f) * Mathf.Sin(now * 12f) + gun.ReloadProgress * .5f
                    : gun.IsEmpty ? .08f
                    : Mathf.Lerp(.35f, 1f, charge) * glowCharged;
        level += glowShot * Mathf.Exp(-(now - shotAt) * 28f);
        level += gun.Spin01 * .6f;
        if (glowRenderers != null)
            foreach (Renderer r in glowRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block, glowMaterialIndex);
                block.SetColor(EmissionId, glowColor * level);
                r.SetPropertyBlock(block, glowMaterialIndex);
            }

        if (cellLight != null) cellLight.intensity = level * cellLightIntensity;

        // Bolt: a quick streak, thinning out.
        if (bolt != null)
        {
            float age = now - shotAt;
            bool on = age < boltSeconds;
            bolt.enabled = on;
            if (on)
            {
                Vector3 from = gun.Muzzle != null ? gun.Muzzle.position : boltFrom;
                Vector3 to = Vector3.Lerp(from, boltTo, .15f + .85f * Mathf.Clamp01(age / boltSeconds + .4f));
                bolt.SetPosition(0, Vector3.Lerp(from, boltTo, Mathf.Clamp01(age / boltSeconds) * .6f));
                bolt.SetPosition(1, to);
                float w = boltWidth * (1f - age / boltSeconds);
                bolt.startWidth = w; bolt.endWidth = w * .4f;
            }
        }

        // Rotary motor whine.
        if (whineSource != null)
        {
            float spin = gun.Spin01;
            if (spin > .01f && !whineSource.isPlaying) whineSource.Play();
            whineSource.volume = .28f * spin;
            whineSource.pitch = .55f + 1.1f * spin;
            if (spin <= .01f && whineSource.isPlaying) whineSource.Stop();
        }
    }

    // ---------------- synthesized sounds ----------------

    /// <summary>An energy crack: a falling pitch sweep, part square for bite, with a noise burst at the front.</summary>
    static AudioClip Sweep(string name, float seconds, float fromHz, float toHz, float square, float noise)
    {
        const int rate = 22050;
        int n = Mathf.RoundToInt(seconds * rate);
        var data = new float[n];
        var rnd = new System.Random(name.Length * 7919);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float hz = fromHz * Mathf.Pow(toHz / fromHz, t);
            phase += 2.0 * System.Math.PI * hz / rate;
            float s = Mathf.Sin((float)phase);
            s = Mathf.Lerp(s, Mathf.Sign(s), square);
            float burst = (float)(rnd.NextDouble() * 2 - 1) * noise * Mathf.Exp(-t * 18f);
            float env = Mathf.Min(1f, i / 40f) * Mathf.Exp(-t * 3.2f);
            data[i] = (s * .6f + burst) * env * .8f;
        }
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Seamless motor whine loop (whole cycles of each partial in half a second).</summary>
    static AudioClip Whine()
    {
        const int rate = 22050;
        int n = rate / 2;
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float s = Mathf.Sin(2f * Mathf.PI * 180f * t) * .5f + Mathf.Sin(2f * Mathf.PI * 360f * t) * .3f
                    + Mathf.Sin(2f * Mathf.PI * 720f * t) * .15f + Mathf.Sin(2f * Mathf.PI * 24f * t) * .1f;
            data[i] = s * .6f;
        }
        var clip = AudioClip.Create("Motor Whine", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
