using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Something the keys can eat: a dead crew member, or a dead key. A key that feeds on it heals and
/// grows. The body shrinks as it's eaten and leaves only blood. Only one key feeds on it at a time.
/// </summary>
public sealed class Corpse : MonoBehaviour
{
    [SerializeField] float meat = 100f;
    [Tooltip("What shrinks as it's eaten (defaults to this object).")]
    [SerializeField] Transform visual;
    [SerializeField] bool keyIchor;

    public static readonly List<Corpse> All = new List<Corpse>();

    public float Meat { get; private set; }
    public bool IsGone => Meat <= 0f;
    public KeyFlyer Feeder { get; private set; }
    public Vector3 FeedPoint => transform.position + Vector3.up * .3f;
    public Color BloodColor => keyIchor ? BloodVFX.KeyIchor : BloodVFX.HumanBlood;

    float startMeat;
    Vector3 startScale;
    float nextSpray;

    public void Init(float amount, Transform shrinkTarget, bool ichor)
    {
        meat = amount;
        visual = shrinkTarget;
        keyIchor = ichor;
        Meat = startMeat = amount;
        if (visual != null) startScale = visual.localScale;
    }

    void Awake()
    {
        if (visual == null) visual = transform;
        Meat = startMeat = meat;
        startScale = visual.localScale;
    }

    [Tooltip("Lies in a pool of its own blood from the start (crew bodies).")]
    [SerializeField] bool poolOnStart;

    void Start()
    {
        if (poolOnStart) BloodVFX.Pool(transform.position, BloodColor, 1.8f);
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public bool TryClaim(KeyFlyer key)
    {
        if (IsGone || (Feeder != null && Feeder != key && !Feeder.IsDead)) return false;
        Feeder = key;
        return true;
    }

    public void Release(KeyFlyer key) { if (Feeder == key) Feeder = null; }

    /// <summary>A key takes a bite. Returns how much it actually got.</summary>
    public float Bite(float amount)
    {
        if (IsGone) return 0f;
        float eaten = Mathf.Min(Meat, amount);
        Meat -= eaten;
        if (visual != null) visual.localScale = startScale * Mathf.Lerp(.25f, 1f, Meat / Mathf.Max(1f, startMeat));
        if (Time.time >= nextSpray)
        {
            nextSpray = Time.time + .35f;
            BloodVFX.Spray(FeedPoint, Vector3.up + Random.insideUnitSphere, BloodColor, .7f);
        }
        if (IsGone)
        {
            BloodVFX.Pool(transform.position, BloodColor, 1.6f);
            Feeder = null;
            if (visual != null) visual.gameObject.SetActive(false);
            All.Remove(this);
        }
        return eaten;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();
}
