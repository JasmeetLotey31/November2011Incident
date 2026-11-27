using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PINNED: a key slammed you to the floor and is on top of you, jaws snapping at your face.
/// Your hands are busy holding it off (no shooting, no moving).
/// - SHOVE: alternate A / D as fast as you can to push its head back. Too slow and the jaws
///   reach your face: it bites (damage, maybe a lasting injury) and keeps going.
/// - STAB: once you've shoved it back, [V] STAB! flashes for a moment. Hit it and you stab it
///   in the throat: big damage, it's thrown off stunned and kickable. Miss it and you're back
///   to shoving.
/// - Pinned too long and it tears a chunk out of you and lets go.
/// Started by <see cref="KeyFlyer"/> when a chomp lands from behind, from a rush, or while
/// you're down. Put it on the player root (PlayerEaten adds it if missing).
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public sealed class PlayerPinned : MonoBehaviour
{
    [Header("Struggle")]
    [Tooltip("How close the jaws start (0 = held off, 1 = at your face).")]
    [Range(0, 1)] [SerializeField] float startPressure = .45f;
    [Tooltip("Jaws close in by this much per second.")]
    [SerializeField] float pressureRise = .4f;
    [Tooltip("Each correct alternating press pushes the jaws back by this much...")]
    [SerializeField] float pressurePerPress = .06f;
    [Tooltip("...and fills the shove meter by this much.")]
    [SerializeField] float shovePerPress = .11f;
    [SerializeField] float shoveDecay = .18f;
    [Tooltip("Pressing V before the stab is ready lets the jaws in a little.")]
    [SerializeField] float earlyStabPenalty = .1f;

    [Header("Outcomes")]
    [SerializeField] float stabWindow = .7f;
    [SerializeField] float stabDamage = 70f;
    [SerializeField] float biteDamage = 12f;
    [Range(0, 1)] [SerializeField] float biteInjuryChance = .35f;
    [Tooltip("Jaws pushed back to here after a bite.")]
    [Range(0, 1)] [SerializeField] float pressureAfterBite = .55f;
    [SerializeField] float maxSeconds = 6f;
    [SerializeField] float tearDamage = 20f;
    [Tooltip("No new pin for this long after one ends.")]
    [SerializeField] float pinCooldown = 6f;

    public bool IsPinned => key != null;
    public KeyFlyer Key => key;
    /// <summary>0 = jaws held off, 1 = at your face (it bites).</summary>
    public float Pressure { get; private set; }
    /// <summary>0..1; full = stab chance.</summary>
    public float Shove { get; private set; }
    public bool StabReady => key != null && Time.time < stabUntil;
    public float StabWindow01 => StabReady ? Mathf.Clamp01((stabUntil - Time.time) / stabWindow) : 0f;
    /// <summary>The side to press next: 0 = A (left), 1 = D (right), -1 = either.</summary>
    public int NextSide { get; private set; } = -1;
    public float Elapsed01 => key != null ? Mathf.Clamp01((Time.time - startedAt) / maxSeconds) : 0f;
    public float LastBiteTime { get; private set; } = -10f;
    public float LastShoveTime { get; private set; } = -10f;
    public float LastStabTime { get; private set; } = -10f;

    public event Action Began;
    public event Action<bool> Ended; // true = you stabbed it off

    static float nextPinAllowed;

    Health health;
    PlayerEaten eaten;
    CharacterController body;
    CameraKick cameraKick;
    KeyFlyer key;
    Vector3 anchor;
    float startedAt, stabUntil, stickX;
    int kickCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => nextPinAllowed = 0f;

    void Awake()
    {
        health = GetComponent<Health>();
        eaten = GetComponent<PlayerEaten>();
        body = GetComponent<CharacterController>();
        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cameraKick = cam.GetComponent<CameraKick>();
    }

    void OnEnable() => health.Died += OnDied;
    void OnDisable() => health.Died -= OnDied;

    void OnDied(DamageInfo info) { if (key != null) End(false, false); }

    /// <summary>A key tries to pin you. False if you can't be pinned right now.</summary>
    public bool TryBegin(KeyFlyer by)
    {
        if (by == null || key != null || health.IsDead || Time.time < nextPinAllowed) return false;
        if (eaten != null && !eaten.CanBeGrabbed) return false;
        key = by;
        anchor = transform.position;
        startedAt = Time.time;
        Pressure = startPressure;
        Shove = 0f;
        stabUntil = 0f;
        NextSide = -1;
        Kick(2f, 3f);
        BloodVFX.Spray(transform.position + Vector3.up * .5f, Vector3.up, BloodVFX.HumanBlood, 1.2f);
        Began?.Invoke();
        return true;
    }

    /// <summary>One shove press (0 = A / left, 1 = D / right). Public so tests can drive it.</summary>
    public void PressSide(int side)
    {
        if (key == null || StabReady) return;
        if (NextSide != -1 && side != NextSide) return; // same side twice does nothing
        NextSide = 1 - side;
        Pressure = Mathf.Max(0f, Pressure - pressurePerPress);
        Shove = Mathf.Min(1f, Shove + shovePerPress);
        LastShoveTime = Time.time;
        Kick(.5f, .5f);
        if (Shove >= 1f)
        {
            stabUntil = Time.time + stabWindow;
            key.PinStabOpening();
        }
    }

    /// <summary>The stab press ([V]). Public so tests can drive it.</summary>
    public void PressStab()
    {
        if (key == null) return;
        if (!StabReady) { Pressure = Mathf.Min(1f, Pressure + earlyStabPenalty); return; }
        LastStabTime = Time.time;
        KeyFlyer k = key;
        End(true, false);
        k.TakeMelee(new DamageInfo
        {
            amount = stabDamage, point = k.MouthPosition, direction = (k.MouthPosition - transform.position).normalized,
            source = gameObject, melee = true, part = BodyPart.Throat, weakSpot = true,
        });
        BloodVFX.Spray(k.MouthPosition, Vector3.up, BloodVFX.KeyIchor, 3f);
        Kick(2f, -3f);
    }

    void Update()
    {
        if (key == null) return;
        if (key.IsDead || !key.IsPinning) { End(true, true); return; }

        ReadInput();
        if (key == null) return;

        float dt = Time.deltaTime;
        if (StabReady) return; // the jaws are held while you go for the stab
        if (stabUntil > 0f)
        {
            // Missed the stab window: it shakes you off and comes back down.
            stabUntil = 0f;
            Shove = .4f;
            Pressure = Mathf.Max(Pressure, .5f);
            key.PinStabMissed();
        }
        Shove = Mathf.Max(0f, Shove - shoveDecay * dt);
        Pressure += pressureRise * dt;
        if (Pressure >= 1f) Bite();
        if (key != null && Time.time - startedAt >= maxSeconds) Tear();
    }

    void ReadInput()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        Keyboard kb = Keyboard.current;
        Gamepad pad = Gamepad.current;
        if (kb != null)
        {
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) PressSide(0);
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) PressSide(1);
            if (kb.vKey.wasPressedThisFrame) PressStab();
        }
        if (pad != null && key != null)
        {
            // Flick the stick (or tap the shoulders) left/right; [X]/[Square] or right bumper to stab.
            float x = pad.leftStick.x.ReadValue();
            if (x < -.6f && stickX >= -.6f) PressSide(0);
            if (x > .6f && stickX <= .6f) PressSide(1);
            stickX = x;
            if (pad.leftShoulder.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame) PressSide(0);
            if (pad.rightShoulder.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame) PressSide(1);
            if (pad.buttonWest.wasPressedThisFrame) PressStab();
        }
    }

    void Bite()
    {
        LastBiteTime = Time.time;
        Pressure = pressureAfterBite;
        Shove = Mathf.Max(0f, Shove - .3f);
        key.PinBite();
        Kick(2f, 3f);
        if (eaten != null) eaten.Wound(biteDamage, key.gameObject, biteInjuryChance);
        else health.TakeDamage(new DamageInfo { amount = biteDamage, point = transform.position + Vector3.up, direction = Vector3.down, source = key.gameObject });
    }

    void Tear()
    {
        KeyFlyer k = key;
        k.PinBite();
        if (eaten != null) eaten.Wound(tearDamage, k.gameObject, 1f);
        if (key != null) End(false, false);
    }

    void End(bool won, bool silent)
    {
        KeyFlyer k = key;
        key = null;
        stabUntil = 0f;
        Shove = 0f;
        nextPinAllowed = Time.time + pinCooldown;
        if (!silent && k != null) k.PinReleased(won);
        // Still on the floor for a moment either way: roll up with [ALT].
        if (eaten != null && !health.IsDead) eaten.Knockdown(won ? .5f : 1f, Vector3.zero);
        Ended?.Invoke(won);
    }

    void LateUpdate()
    {
        // Held down: you don't go anywhere while it's on you (gravity still drops you to the floor).
        if (key == null || body == null || !body.enabled) return;
        Vector3 off = anchor - transform.position;
        off.y = 0f;
        if (off.sqrMagnitude > .0001f) body.Move(off);
    }

    void Kick(float amount, float degrees)
    {
        if (cameraKick != null) cameraKick.Play(amount, Mathf.Abs(degrees), ++kickCount);
    }
}
