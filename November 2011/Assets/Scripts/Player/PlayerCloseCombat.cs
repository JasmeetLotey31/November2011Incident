using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Close combat, Resident Evil 4 meets Doom:
/// - KICK (V / right bumper): when a key is staggered (shot in the face, shot in the leg, stuck in
///   the floor after a missed dive...), V is a ROUNDHOUSE: big damage to it and a sweep that knocks
///   down every key around you. Fast (a few hundred ms) so you keep moving.
/// - BASH (V with nothing staggered): a quick gun-butt to push a key off you.
/// - DASH (Left Alt / B): a quick burst in your move direction (backwards if standing still),
///   briefly invulnerable. Short cooldown, no stamina: use it to sidestep dives.
/// - GRABBED: a key latched on. Mash Interact (E) / Attack to break free; break free and it's
///   staggered, ready to kick.
/// Put it on the player root (with Health, PlayerVitals, FirstPersonController).
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public sealed class PlayerCloseCombat : MonoBehaviour
{
    [Header("Roundhouse kick (on a staggered key)")]
    [SerializeField] float kickRange = 2.8f;
    [SerializeField] float kickAngle = 120f;
    [SerializeField] float kickDamage = 60f;
    [Tooltip("Everything else within this radius is swept: damaged and knocked down.")]
    [SerializeField] float sweepRadius = 3.2f;
    [SerializeField] float sweepDamage = 25f;
    [SerializeField] float kickKnockback = 9f;
    [SerializeField] float kickSeconds = .3f;
    [SerializeField] float kickHitstop = .06f;

    [Header("Bash (nothing staggered)")]
    [SerializeField] float bashCooldown = .6f;
    [SerializeField] float bashRange = 2.2f;
    [SerializeField] float bashAngle = 80f;
    [SerializeField] float bashDamage = 15f;
    [SerializeField] float bashKnockback = 5f;

    [Header("Dash")]
    [SerializeField] float dashCooldown = .8f;
    [SerializeField] float dashDistance = 4f;
    [SerializeField] float dashSeconds = .18f;
    [SerializeField] float invulnerableSeconds = .28f;

    [Header("Grab")]
    [SerializeField] int pressesToBreakFree = 6;
    [SerializeField] float grabWindow = 1.8f;
    [SerializeField] float grabFailDamage = 20f;
    [SerializeField] float grabImmunitySeconds = 2f;

    [Header("Audio")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip kickClip;
    [SerializeField] AudioClip bashClip;
    [SerializeField] AudioClip dashClip;
    [SerializeField] AudioClip grabbedClip;
    [SerializeField] AudioClip breakFreeClip;

    public bool IsGrabbed => grabber != null;
    public bool IsDodging { get; private set; }
    public bool IsKicking { get; private set; }
    /// <summary>A staggered key in reach right now (the HUD shows "[V] KICK").</summary>
    public IKickable KickTarget { get; private set; }
    public float GrabProgress => pressesToBreakFree > 0 ? (float)presses / pressesToBreakFree : 0f;
    public float GrabTimeLeft01 => grabWindow > 0f ? Mathf.Clamp01(grabTimeLeft / grabWindow) : 0f;
    /// <summary>0..1 dash ready (HUD).</summary>
    public float DashReady01 => dashCooldown > 0f ? Mathf.Clamp01(1f - (nextDash - Time.time) / dashCooldown) : 1f;
    public float LastKickTime { get; private set; } = -10f;

    public event Action Kicked, Bashed, Dashed, GrabStarted;
    public event Action<bool> GrabEnded;

    Health health;
    CharacterController body;
    Camera view;
    CameraKick cameraKick;
    KeyTarget target;
    PlayerEaten eaten;
    InputAction meleeAction, dashAction, interactAction, attackAction, moveAction;
    float nextBash, nextDash, grabTimeLeft, grabImmuneUntil;
    KeyAlien grabber;
    int presses, kickCount;

    void Awake()
    {
        health = GetComponent<Health>();
        body = GetComponent<CharacterController>();
        target = GetComponent<KeyTarget>();
        eaten = GetComponent<PlayerEaten>();
        view = GetComponentInChildren<Camera>();
        if (view != null) cameraKick = view.GetComponent<CameraKick>();

        meleeAction = new InputAction("Melee", InputActionType.Button);
        meleeAction.AddBinding("<Keyboard>/v");
        meleeAction.AddBinding("<Gamepad>/rightShoulder");
        dashAction = new InputAction("Dash", InputActionType.Button);
        dashAction.AddBinding("<Keyboard>/leftAlt");
        dashAction.AddBinding("<Gamepad>/buttonEast");
        interactAction = InputSystem.actions.FindAction("Player/Interact");
        attackAction = InputSystem.actions.FindAction("Player/Attack");
        moveAction = InputSystem.actions.FindAction("Player/Move");
    }

    void OnEnable() { meleeAction.Enable(); dashAction.Enable(); health.Died += OnDied; }
    void OnDisable() { meleeAction.Disable(); dashAction.Disable(); health.Died -= OnDied; }
    void OnDestroy() { meleeAction.Dispose(); dashAction.Dispose(); }

    Sidearm Gun => target != null ? target.Gun : GetComponentInChildren<Sidearm>();

    void Update()
    {
        if (health.IsDead) return;
        if (IsGrabbed) { UpdateGrab(Time.deltaTime); return; }
        // Inside a mouth, V pries it open (PlayerEaten); pinned, V stabs (PlayerPinned). Dashing still works on a tongue.
        if (eaten != null && (eaten.IsEaten || eaten.IsPinned)) { KickTarget = null; return; }
        KickTarget = IsKicking ? null : FindKickable();
        if (IsKicking || Cursor.lockState != CursorLockMode.Locked) return;
        if (meleeAction.WasPressedThisFrame()) TryMelee();
        if (dashAction.WasPressedThisFrame()) TryDash();
    }

    // ---------------- kick / bash ----------------

    /// <summary>V: roundhouse a staggered key if one's in reach, otherwise a quick bash.</summary>
    public bool TryMelee()
    {
        if (IsGrabbed || IsKicking) return false;
        IKickable kickable = FindKickable();
        if (kickable != null) { StartCoroutine(Kick(kickable)); return true; }
        if (Time.time < nextBash) return false;
        nextBash = Time.time + bashCooldown;
        Bash();
        return true;
    }

    IKickable FindKickable()
    {
        if (view == null) return null;
        Vector3 forward = Flat(view.transform.forward).normalized;
        IKickable best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (IKickable k in Kickables.All)
        {
            if (k == null || !k.IsAlive || !k.IsKickable) continue;
            Vector3 to = Flat(k.Root.position - transform.position);
            float d = to.magnitude;
            if (d > kickRange || Vector3.Angle(forward, to) > kickAngle * .5f) continue;
            if (d < bestDistance) { bestDistance = d; best = k; }
        }
        return best;
    }

    IEnumerator Kick(IKickable main)
    {
        IsKicking = true;
        LastKickTime = Time.time;
        bool wasInvulnerable = health.invulnerable;
        health.invulnerable = true;
        Play(kickClip);
        Gun?.PlayBash();

        // Snap to face it and lunge in a step.
        Vector3 to = Flat(main.Root.position - transform.position);
        if (to.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(to);
        float step = Mathf.Max(0f, to.magnitude - 1.3f);
        for (float t = 0f; t < .08f; t += Time.deltaTime)
        {
            if (body != null) body.Move(to.normalized * (step / .08f * Time.deltaTime));
            yield return null;
        }

        // Impact: the roundhouse lands, everything nearby gets swept.
        main.TakeMelee(new DamageInfo
        {
            amount = kickDamage, point = main.Root.position + Vector3.up, direction = to.normalized,
            source = gameObject, knockback = kickKnockback, melee = true, part = BodyPart.Body,
        });
        foreach (IKickable k in Kickables.All.ToArraySafe())
        {
            if (k == null || k == main || !k.IsAlive) continue;
            Vector3 away = Flat(k.Root.position - transform.position);
            if (away.magnitude > sweepRadius) continue;
            k.TakeMelee(new DamageInfo
            {
                amount = sweepDamage, point = k.Root.position + Vector3.up, direction = away.normalized,
                source = gameObject, knockback = kickKnockback * .7f, melee = true, part = BodyPart.Leg,
            });
        }
        if (cameraKick != null) cameraKick.Play(1.8f, -2.2f, ++kickCount);
        Noise.Emit(transform.position, 10f, gameObject);
        StartCoroutine(Hitstop(kickHitstop));
        Kicked?.Invoke();

        yield return new WaitForSeconds(Mathf.Max(0f, kickSeconds - .08f));
        health.invulnerable = wasInvulnerable;
        IsKicking = false;
    }

    void Bash()
    {
        Play(bashClip);
        Gun?.PlayBash();
        if (cameraKick != null) cameraKick.Play(.9f, -1.2f, ++kickCount);
        Vector3 forward = Flat(view != null ? view.transform.forward : transform.forward).normalized;
        foreach (IKickable k in Kickables.All.ToArraySafe())
        {
            if (k == null || !k.IsAlive) continue;
            Vector3 to = Flat(k.Root.position - transform.position);
            if (to.magnitude > bashRange || Vector3.Angle(forward, to) > bashAngle * .5f) continue;
            k.TakeMelee(new DamageInfo
            {
                amount = bashDamage, point = k.Root.position + Vector3.up, direction = to.normalized,
                source = gameObject, knockback = bashKnockback, melee = true, part = BodyPart.Body,
            });
        }
        Noise.Emit(transform.position, 8f, gameObject);
        Bashed?.Invoke();
    }

    IEnumerator Hitstop(float seconds)
    {
        float before = Time.timeScale;
        if (before <= 0f || seconds <= 0f) yield break;
        Time.timeScale = .05f * before;
        yield return new WaitForSecondsRealtime(seconds);
        Time.timeScale = before;
    }

    // ---------------- dash ----------------

    public bool TryDash()
    {
        if (Time.time < nextDash || IsGrabbed || IsDodging || body == null) return false;
        nextDash = Time.time + dashCooldown;
        Vector2 input = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        Vector3 direction = input.sqrMagnitude > .05f
            ? (transform.right * input.x + transform.forward * input.y).normalized
            : -transform.forward;
        StartCoroutine(Dash(direction));
        Play(dashClip);
        if (cameraKick != null) cameraKick.Play(.5f, -.6f, ++kickCount);
        Dashed?.Invoke();
        return true;
    }

    IEnumerator Dash(Vector3 direction)
    {
        IsDodging = true;
        bool wasInvulnerable = health.invulnerable;
        health.invulnerable = true;
        float speed = dashDistance / Mathf.Max(.05f, dashSeconds);
        for (float t = 0f; t < dashSeconds; t += Time.deltaTime)
        {
            body.Move(direction * (speed * Time.deltaTime));
            yield return null;
        }
        IsDodging = false;
        yield return new WaitForSeconds(Mathf.Max(0f, invulnerableSeconds - dashSeconds));
        health.invulnerable = wasInvulnerable;
    }

    // ---------------- grab ----------------

    public bool BeginGrab(KeyAlien key)
    {
        if (IsGrabbed || IsDodging || IsKicking || health.IsDead || health.invulnerable || key == null) return false;
        if (Time.time < grabImmuneUntil) return false;
        grabber = key;
        presses = 0;
        grabTimeLeft = grabWindow;
        Play(grabbedClip);
        if (cameraKick != null) cameraKick.Play(2f, 3f, ++kickCount);
        GrabStarted?.Invoke();
        return true;
    }

    void UpdateGrab(float dt)
    {
        grabTimeLeft -= dt;
        bool pressed = (interactAction != null && interactAction.WasPressedThisFrame()) ||
                       (attackAction != null && attackAction.WasPressedThisFrame()) ||
                       meleeAction.WasPressedThisFrame();
        if (pressed)
        {
            presses++;
            if (cameraKick != null) cameraKick.Play(.6f, UnityEngine.Random.Range(-1.5f, 1.5f), ++kickCount);
        }
        if (presses >= pressesToBreakFree) EndGrab(true);
        else if (grabTimeLeft <= 0f)
        {
            KeyAlien key = grabber;
            EndGrab(false);
            health.TakeDamage(new DamageInfo
            {
                amount = grabFailDamage,
                point = transform.position + Vector3.up * 1.4f,
                direction = key != null ? Flat(transform.position - key.transform.position).normalized : -transform.forward,
                source = key != null ? key.gameObject : null,
            });
        }
    }

    void EndGrab(bool brokeFree)
    {
        KeyAlien key = grabber;
        grabber = null;
        grabImmuneUntil = Time.time + grabImmunitySeconds;
        if (key != null) key.ReleaseGrab(brokeFree, Flat(key.transform.position - transform.position).normalized);
        if (brokeFree)
        {
            Play(breakFreeClip);
            if (cameraKick != null) cameraKick.Play(1.2f, -1.5f, ++kickCount);
            Gun?.PlayBash();
        }
        GrabEnded?.Invoke(brokeFree);
    }

    public void ForceEndGrab()
    {
        if (!IsGrabbed) return;
        grabber = null;
        grabImmuneUntil = Time.time + grabImmunitySeconds;
        GrabEnded?.Invoke(true);
    }

    public void ApplyDifficulty(float newGrabFailDamage, int newPresses)
    {
        grabFailDamage = newGrabFailDamage;
        pressesToBreakFree = newPresses;
    }

    void OnDied(DamageInfo info) => ForceEndGrab();

    void Play(AudioClip clip)
    {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}

static class KickableListExtensions
{
    /// <summary>Copy for safe iteration (a kick can kill keys, which unregister mid-loop).</summary>
    public static IKickable[] ToArraySafe(this System.Collections.Generic.IReadOnlyList<IKickable> list)
    {
        var copy = new IKickable[list.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = list[i];
        return copy;
    }
}
