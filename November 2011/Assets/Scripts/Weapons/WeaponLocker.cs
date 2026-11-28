using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One of the armory's small-arms lockers. Station security still logs every kill; earn enough
/// points and the locker opens for you (<see cref="PlayerBuy"/> does the buying).
/// - Look at it and hold [E]: the terminal beeps, the grille swings open, you take the gun.
/// - Already carry that gun? The locker refills its ammo at <see cref="AmmoCost"/>.
/// - Can't afford it: the terminal buzzes and flashes red.
/// The gun on display disappears while you take it and is restocked a moment later.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponLocker : MonoBehaviour
{
    [Tooltip("The gun's display name (Sidearm.DisplayName), e.g. SHOTGUN.")]
    [SerializeField] string weaponName = "SHOTGUN";
    [SerializeField] string lockerLabel = "SMALL ARMS 02";
    [SerializeField] int cost = 750;
    [SerializeField] int ammoCost = 375;

    [Header("Parts")]
    [Tooltip("Hinge of the grille door (rotates around its Y axis).")]
    [SerializeField] Transform door;
    [SerializeField] float doorOpenDegrees = -105f;
    [SerializeField] GameObject display;
    [SerializeField] Text screenTop, screenName, screenPrice;
    [SerializeField] Renderer screenGlow;
    [SerializeField] Light lamp;
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip openClip, closeClip;

    public static readonly List<WeaponLocker> All = new List<WeaponLocker>();
    public string WeaponName => weaponName;
    public string Label => lockerLabel;
    public int Cost => cost;
    public int AmmoCost => ammoCost;

    static readonly Color Amber = new Color(1f, .62f, .2f), Denied = new Color(1f, .16f, .1f), Approved = new Color(.55f, 1f, .45f);
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static AudioClip beepClip, buzzClip;
    MaterialPropertyBlock block;
    float openUntil = -1f, flashUntil = -1f, doorAngle, lampBase;
    Color flashColor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void Awake()
    {
        block = new MaterialPropertyBlock();
        if (lamp != null) lampBase = lamp.intensity;
        RefreshScreen();
        if (beepClip == null) beepClip = Tone("Locker Beep", new[] { 1320f, 0f, 1760f }, .055f, .25f, true);
        if (buzzClip == null) buzzClip = Tone("Locker Buzz", new[] { 110f }, .32f, .3f, true);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    void OnValidate() => RefreshScreen();

    void RefreshScreen()
    {
        if (screenTop != null) screenTop.text = lockerLabel;
        if (screenName != null) screenName.text = ScreenName(weaponName);
        if (screenPrice != null) screenPrice.text = cost.ToString();
    }

    /// <summary>The terminal shows the last word (CARBINE, REPEATER): big enough to read at PS1 resolution.</summary>
    public static string ScreenName(string gun) => gun.Contains(" ") ? gun.Substring(gun.LastIndexOf(' ') + 1) : gun;

    /// <summary>Bought: open up, hand the gun over (or the ammo), restock in a moment.</summary>
    public void Purchased(bool ammo)
    {
        openUntil = Time.time + 1.8f;
        Flash(Approved, .9f);
        Play(beepClip, .6f);
        Play(openClip, 1f);
        if (!ammo && display != null) display.SetActive(false);
    }

    /// <summary>Not enough points.</summary>
    public void Refused()
    {
        Flash(Denied, .6f);
        Play(buzzClip, .5f);
    }

    void Flash(Color c, float seconds) { flashColor = c; flashUntil = Time.time + seconds; }

    void Update()
    {
        // Grille door swings open, then shuts with a clank; the gun is back on its hooks after.
        bool open = Time.time < openUntil;
        float want = open ? doorOpenDegrees : 0f;
        float before = doorAngle;
        doorAngle = Mathf.MoveTowards(doorAngle, want, (open ? 420f : 260f) * Time.deltaTime);
        if (door != null) door.localRotation = Quaternion.Euler(0f, doorAngle, 0f);
        if (!open && before != 0f && doorAngle == 0f)
        {
            Play(closeClip, .8f);
            if (display != null) display.SetActive(true);
        }

        // Terminal: steady amber, flashing green when approved and red when refused.
        bool flashing = Time.time < flashUntil;
        Color c = flashing && Mathf.Repeat(Time.time * 8f, 1f) < .6f ? flashColor : Amber;
        if (screenName != null) screenName.color = c;
        if (screenPrice != null) screenPrice.color = c;
        if (screenTop != null) screenTop.color = c * .8f;
        if (screenGlow != null)
        {
            if (block == null) block = new MaterialPropertyBlock();       // after a script reload mid-play
            screenGlow.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);                               // tints the painted white letters
            block.SetColor(EmissionId, c * (flashing ? .25f : .04f));     // a faint glow on the dark CRT
            screenGlow.SetPropertyBlock(block);
        }
        // The lamp hums: a slow, uneven flicker (the station's on emergency power).
        if (lamp != null)
            lamp.intensity = lampBase * (.85f + .15f * Mathf.PerlinNoise(Time.time * 3f, transform.position.x));
    }

    void Play(AudioClip clip, float volume)
    {
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, volume);
    }

    /// <summary>Square-wave terminal tones (0 Hz = a gap), made once at runtime.</summary>
    static AudioClip Tone(string name, float[] notes, float noteSeconds, float volume, bool square)
    {
        const int rate = 22050;
        int perNote = Mathf.RoundToInt(noteSeconds * rate);
        var data = new float[perNote * notes.Length];
        for (int n = 0; n < notes.Length; n++)
            for (int i = 0; i < perNote; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Min(1f, i / 60f) * Mathf.Min(1f, (perNote - i) / 300f);
                float s = notes[n] <= 0f ? 0f : Mathf.Sin(2f * Mathf.PI * notes[n] * t);
                if (square) s = Mathf.Sign(s) * .6f + s * .4f;
                data[n * perNote + i] = s * env * volume;
            }
        var clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
