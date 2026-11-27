using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The player's side of being hunted by things that eat you:
/// - HIT: chomps, whips, dives and globs land through <see cref="Hit"/>; a dash in time goes through them.
/// - TONGUED: a key's tongue speared you and is yanking you into its mouth. Shoot the tongue,
///   or DASH to tear free.
/// - KNOCKED DOWN: a whip or a dive put you on the floor. You can still shoot; DASH to roll up.
///   While you're down (or nearly dead) a key can SWALLOW you.
/// - EATEN: you're inside the mouth. Shoot down the throat, or PRY it open with V.
///   Too slow and it bites a chunk out of you.
/// - INJURED: bites leave lasting injuries: ARM (shaky aim, slow reloads), LEG (limp),
///   SIDE (less max health). They stay for the rest of the run.
/// - BLEEDING: after any wound you bleed for a while, and the keys smell it (frenzy).
/// - PINNED: a key slammed you down and is on top of you (see <see cref="PlayerPinned"/>).
/// - SPLATTERED: gore across your eyes. SCREAMED AT: your aim shakes.
/// Put it on the player root.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(1001)] // LateUpdate after the look controller and CameraKick
public sealed class PlayerEaten : MonoBehaviour
{
    public enum Injury { Arm, Leg, Side }

    [SerializeField] float bleedSeconds = 6f;
    [SerializeField] float immunityAfterEscape = 1.5f;
    [Tooltip("Pry damage per V press while inside a mouth (it spits you out at the key's escape threshold).")]
    [SerializeField] float pryDamage = 35f;
    [SerializeField] float sideBiteMaxHealthLoss = 15f;
    [SerializeField] float spitOutPush = 6f;
    [Tooltip("At or below this health fraction a key can swallow you even standing up.")]
    [Range(0, 1)] [SerializeField] float executeBelow = .3f;
    [SerializeField] float downEyeDrop = .95f;
    [SerializeField] float downRoll = 28f;

    [Header("Audio")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip[] chewedClips;
    [SerializeField] AudioClip bittenClip;
    [SerializeField] AudioClip escapeClip;

    public bool IsPulled => puller != null;
    public PlayerPinned Pinned => pinned;
    public bool IsPinned => pinned != null && pinned.IsPinned;
    public bool IsEaten => eater != null;
    public KeyFlyer Eater => eater;
    public bool IsBleeding => Time.time < bleedingUntil;
    public IReadOnlyList<Injury> Injuries => injuries;
    public int Count(Injury i) { int n = 0; foreach (Injury x in injuries) if (x == i) n++; return n; }
    /// <summary>0..1 of the time inside the mouth before the bite (for the HUD).</summary>
    public float EatProgress01 => eater != null ? eater.EatTime01 : 0f;
    public float LastChewTime { get; private set; } = -10f;
    public float LastBittenTime { get; private set; } = -10f;
    public float LastHitTime { get; private set; } = -10f;
    public float LastSplatterTime { get; private set; } = -10f;
    public Injury LastInjury { get; private set; }

    /// <summary>Reload slowdown from arm bites.</summary>
    public float ArmPenalty => 1f + .45f * Count(Injury.Arm);
    /// <summary>Spread multiplier: arm bites, plus a shaking aim after a scream.</summary>
    public float SpreadPenalty => ArmPenalty * (IsScreamedAt ? 2.6f : 1f);
    /// <summary>Movement multiplier from leg bites.</summary>
    public float LegMultiplier => Mathf.Max(.5f, 1f - .18f * Count(Injury.Leg));
    public bool IsDown => Time.time < downUntil;
    public bool IsScreamedAt => Time.time < screamedUntil;
    /// <summary>0..1 how much gore is over your eyes.</summary>
    public float Splatter01 => Mathf.Clamp01((splatterUntil - Time.time) / 2.5f);
    /// <summary>Knocked down or nearly dead, and not already caught: a key can swallow you.</summary>
    public bool CanBeExecuted => !health.IsDead && !IsEaten && !IsPulled && !IsPinned && Time.time >= immuneUntil
                                 && (IsDown || health.Fraction <= executeBelow);
    /// <summary>Free to be slammed down and pinned (not caught, not mid-dash or mid-kick).</summary>
    public bool CanBeGrabbed => !health.IsDead && !IsEaten && !IsPulled && !IsPinned && Time.time >= immuneUntil
                                && (closeCombat == null || (!closeCombat.IsDodging && !closeCombat.IsGrabbed && !closeCombat.IsKicking));

    public event Action Pulled, Eaten, Escaped_, BittenEvent;

    Health health;
    CharacterController body;
    PlayerCloseCombat closeCombat;
    PlayerPinned pinned;
    CameraKick cameraKick;
    KeyFlyer puller, eater;
    readonly List<Injury> injuries = new List<Injury>();
    float bleedingUntil, immuneUntil, downUntil, downStarted, screamedUntil, splatterUntil, rollSide = 1f, downAmount;
    int kickCount;
    InputAction pryAction;
    Transform cam;
    Vector3 camBase;

    void Awake()
    {
        health = GetComponent<Health>();
        body = GetComponent<CharacterController>();
        closeCombat = GetComponent<PlayerCloseCombat>();
        pinned = GetComponent<PlayerPinned>();
        if (pinned == null) pinned = gameObject.AddComponent<PlayerPinned>();
        Camera view = GetComponentInChildren<Camera>();
        if (view != null) { cameraKick = view.GetComponent<CameraKick>(); cam = view.transform; camBase = cam.localPosition; }
        pryAction = new InputAction("Pry", InputActionType.Button);
        pryAction.AddBinding("<Keyboard>/v");
        pryAction.AddBinding("<Gamepad>/rightShoulder");
    }

    void OnEnable() { pryAction.Enable(); health.Damaged += OnDamaged; health.Died += OnDied; }
    void OnDisable() { pryAction.Disable(); health.Damaged -= OnDamaged; health.Died -= OnDied; }
    void OnDestroy() => pryAction.Dispose();

    void OnDamaged(DamageInfo info)
    {
        bleedingUntil = Time.time + bleedSeconds;
        BloodVFX.Spray(transform.position + Vector3.up * 1.2f, info.direction, BloodVFX.HumanBlood, .8f);
    }

    void OnDied(DamageInfo info)
    {
        puller = null;
        eater = null;
        downUntil = 0f;
    }

    // ---------------- hits ----------------

    /// <summary>A key's attack connects. Returns false if you dashed through it (or one has you pinned: the rest wait).</summary>
    /// <param name="knockback">Shove speed (m/s, fading over .25 s).</param>
    /// <param name="injuryChance">Chance it tears off a lasting injury.</param>
    /// <param name="knockdownSeconds">Puts you on the floor for this long (0 = stays standing).</param>
    public bool Hit(float damage, GameObject source, Vector3 direction, float knockback, float injuryChance, float knockdownSeconds)
    {
        if (health.IsDead || IsPinned || (closeCombat != null && closeCombat.IsDodging)) return false;
        LastHitTime = Time.time;
        Vector3 flat = new Vector3(direction.x, 0f, direction.z);
        if (flat.sqrMagnitude < .001f) flat = -transform.forward;
        flat.Normalize();
        if (injuryChance > 0f && UnityEngine.Random.value < injuryChance) AddInjury();
        if (cameraKick != null) cameraKick.Play(2f, 3f, ++kickCount);
        if (audioSource != null && bittenClip != null) audioSource.PlayOneShot(bittenClip, .8f);
        health.TakeDamage(new DamageInfo { amount = damage, point = transform.position + Vector3.up * 1.2f, direction = flat, source = source, knockback = knockback });
        if (knockback > 0f) StartCoroutine(PushRoutine(flat, knockback, .25f));
        if (knockdownSeconds > 0f) Knockdown(knockdownSeconds, flat);
        return true;
    }

    /// <summary>A bite that can't be dodged (the key is already on top of you).</summary>
    public void Wound(float damage, GameObject source, float injuryChance)
    {
        if (health.IsDead) return;
        LastHitTime = Time.time;
        if (injuryChance > 0f && UnityEngine.Random.value < injuryChance) AddInjury();
        if (cameraKick != null) cameraKick.Play(2f, 3f, ++kickCount);
        if (audioSource != null && bittenClip != null) audioSource.PlayOneShot(bittenClip, .9f);
        BloodVFX.Spray(transform.position + Vector3.up * .6f, Vector3.up + UnityEngine.Random.insideUnitSphere * .5f, BloodVFX.HumanBlood, 1.8f);
        health.TakeDamage(new DamageInfo { amount = damage, point = transform.position + Vector3.up * .8f, direction = Vector3.down, source = source });
    }

    public void Knockdown(float seconds, Vector3 fromDirection)
    {
        if (health.IsDead) return;
        if (!IsDown) downStarted = Time.time;
        downUntil = Mathf.Max(downUntil, Time.time + seconds);
        rollSide = Vector3.Dot(fromDirection, transform.right) >= 0f ? -1f : 1f;
        if (cameraKick != null) cameraKick.Play(2f, 3f, ++kickCount);
    }

    /// <summary>Gore in your face: you can barely see for a couple of seconds.</summary>
    public void Splattered()
    {
        splatterUntil = Time.time + 2.5f;
        LastSplatterTime = Time.time;
        if (cameraKick != null) cameraKick.Play(1.2f, 1.5f, ++kickCount);
    }

    /// <summary>A key screamed at you: your aim shakes.</summary>
    public void Screamed(float seconds)
    {
        screamedUntil = Mathf.Max(screamedUntil, Time.time + seconds);
        if (cameraKick != null) cameraKick.Play(1.5f, 1f, ++kickCount);
    }

    void AddInjury()
    {
        // Which chunk: arm and leg change how you fight, side lowers max health.
        Injury injury = (Injury)UnityEngine.Random.Range(0, 3);
        injuries.Add(injury);
        LastInjury = injury;
        LastBittenTime = Time.time;
        if (injury == Injury.Side) health.SetMax(Mathf.Max(40f, health.Max - sideBiteMaxHealthLoss));
        BloodVFX.Spray(transform.position + Vector3.up * 1.1f, Vector3.up + UnityEngine.Random.insideUnitSphere, BloodVFX.HumanBlood, 2.5f);
        BittenEvent?.Invoke();
    }

    // ---------------- tongue ----------------

    public bool BeginPull(KeyFlyer key)
    {
        if (IsPulled || IsEaten || IsPinned || health.IsDead || Time.time < immuneUntil) return false;
        if (closeCombat != null && closeCombat.IsDodging) return false; // dashed through it
        puller = key;
        downUntil = 0f; // yanked off the floor
        if (cameraKick != null) cameraKick.Play(1.6f, -2f, ++kickCount);
        Pulled?.Invoke();
        return true;
    }

    public bool IsPulledBy(KeyFlyer key) => puller == key && key != null;

    public void EndPull(bool intoMouth)
    {
        puller = null;
        if (!intoMouth) immuneUntil = Time.time + .5f;
    }

    // ---------------- mouth ----------------

    public bool BeginEaten(KeyFlyer key)
    {
        if (IsEaten || IsPinned || health.IsDead || Time.time < immuneUntil) return false;
        if (closeCombat != null && closeCombat.IsDodging) return false;
        puller = null;
        eater = key;
        if (cameraKick != null) cameraKick.Play(2f, 3f, ++kickCount);
        Eaten?.Invoke();
        return true;
    }

    public bool IsEatenBy(KeyFlyer key) => eater == key && key != null;

    /// <summary>A chew while you're inside the mouth.</summary>
    public void Chew(float damage, GameObject source)
    {
        LastChewTime = Time.time;
        if (cameraKick != null) cameraKick.Play(1.3f, UnityEngine.Random.Range(-2f, 2f), ++kickCount);
        if (audioSource != null && chewedClips != null && chewedClips.Length > 0)
            audioSource.PlayOneShot(chewedClips[UnityEngine.Random.Range(0, chewedClips.Length)], .8f);
        health.TakeDamage(new DamageInfo { amount = damage, point = transform.position + Vector3.up * 1.4f, direction = transform.forward, source = source });
    }

    /// <summary>It bit a chunk out of you and spat you out.</summary>
    public void Bitten(float damage, GameObject source)
    {
        KeyFlyer key = eater;
        eater = null;
        immuneUntil = Time.time + immunityAfterEscape;
        downUntil = 0f;
        AddInjury();
        if (audioSource != null && bittenClip != null) audioSource.PlayOneShot(bittenClip, 1f);
        BloodVFX.Spray(transform.position + Vector3.up * 1.1f, Vector3.up + UnityEngine.Random.insideUnitSphere, BloodVFX.HumanBlood, 3f);
        BloodVFX.Pool(transform.position, BloodVFX.HumanBlood, 1.4f);
        if (cameraKick != null) cameraKick.Play(2f, 3f, ++kickCount);
        health.TakeDamage(new DamageInfo { amount = damage, point = transform.position + Vector3.up * 1.2f, direction = transform.forward, source = source });
        Push(key);
    }

    /// <summary>You got out (pried it open, shot its throat, or it died).</summary>
    public void Escaped(KeyFlyer key)
    {
        if (eater != key && puller != key) return;
        eater = null;
        puller = null;
        immuneUntil = Time.time + immunityAfterEscape;
        downUntil = 0f;
        if (audioSource != null && escapeClip != null) audioSource.PlayOneShot(escapeClip, 1f);
        if (cameraKick != null) cameraKick.Play(1.2f, -1.5f, ++kickCount);
        Push(key);
        Escaped_?.Invoke();
    }

    void Push(KeyFlyer key)
    {
        if (body == null || key == null) return;
        Vector3 away = transform.position - key.transform.position;
        away.y = 0f;
        StartCoroutine(PushRoutine(away.sqrMagnitude > .01f ? away.normalized : -transform.forward, spitOutPush, .2f));
    }

    System.Collections.IEnumerator PushRoutine(Vector3 dir, float speed, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = 1f - t / seconds;
            if (body != null && body.enabled) body.Move(dir * (speed * k * 1.6f * Time.deltaTime));
            yield return null;
        }
    }

    void Update()
    {
        if (health.IsDead) return;

        // Dashing rolls you back onto your feet.
        if (IsDown && closeCombat != null && closeCombat.IsDodging && Time.time - downStarted > .3f) downUntil = Time.time;

        if (IsPulled)
        {
            // Dashing tears you off the tongue.
            if (closeCombat != null && closeCombat.IsDodging) { puller.TakeTongueTearDamage(); return; }
            Vector3 to = puller.MouthPosition - (transform.position + Vector3.up * 1.2f);
            if (to.magnitude <= 1.6f) { KeyFlyer k = puller; puller = null; k.ReeledIn(this); return; }
            if (body != null && body.enabled) body.Move(to.normalized * (puller.ReelSpeed * Time.deltaTime));
        }

        if (IsEaten && Cursor.lockState == CursorLockMode.Locked && pryAction.WasPressedThisFrame())
        {
            // Prying the jaws apart with the gun.
            KeyFlyer k = eater;
            if (cameraKick != null) cameraKick.Play(1f, -1f, ++kickCount);
            k.TakeMelee(new DamageInfo { amount = pryDamage, point = k.MouthPosition, direction = transform.forward, source = gameObject, melee = true, part = BodyPart.Throat });
        }
    }

    void LateUpdate()
    {
        if (cam == null) return;
        // On the floor: the eye line drops and rolls to one side (less while pinned, so you can see
        // the jaws). Screamed at: the view shakes.
        float want = (IsDown || IsPinned) && !health.IsDead ? 1f : 0f;
        float roll = IsPinned ? .3f : 1f;
        downAmount = Mathf.MoveTowards(downAmount, want, Time.deltaTime * (want > downAmount ? 7f : 2.8f));
        float e = downAmount * downAmount * (3f - 2f * downAmount);
        cam.localPosition = camBase + Vector3.down * (downEyeDrop * e);
        float shake = IsScreamedAt ? 1.4f : 0f;
        if (e > .001f || shake > 0f)
            cam.localRotation *= Quaternion.Euler(Mathf.Sin(Time.time * 23f) * shake, Mathf.Sin(Time.time * 19f + 1f) * shake,
                                                  rollSide * downRoll * roll * e +Mathf.Sin(Time.time * 17f) * shake);
    }
}
