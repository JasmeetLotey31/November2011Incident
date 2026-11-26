using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// CaseOh's sidearm from the armory: survival shooting, not a power fantasy.
/// - Hitscan, 8 rounds, scarce spare ammo. Body shots take 4 to drop a key; a face shot bursts an
///   eye and leaves it nearly dead.
/// - Loud: every shot is heard far away and pulls more keys toward you.
/// - Spread grows when you move, sprint or fire fast; aiming down sights (right mouse / left
///   trigger) tightens it but slows you.
/// - Reloading takes time and the keys know it: they rush you while you reload.
/// Attack fires, R / left bumper reloads, right mouse / left trigger aims, Interact picks up ammo.
/// </summary>
[DisallowMultipleComponent]
public sealed class Sidearm : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Camera aimCamera;
    [SerializeField] Transform muzzle;
    [Tooltip("Visual model: dips for reloads.")]
    [SerializeField] Transform model;
    [Tooltip("Brief light at the muzzle on each shot (lights up a dark room).")]
    [SerializeField] Light muzzleLight;
    [Tooltip("Flash mesh at the muzzle, shown for a frame or two per shot.")]
    [SerializeField] Renderer muzzleFlash;

    [Header("Ammo")]
    [SerializeField] int magazineSize = 8;
    [SerializeField] int startingReserve = 16;
    [SerializeField] int maxReserve = 48;
    [Tooltip("Reload with rounds still in the gun.")]
    [SerializeField] float tacticalReloadTime = 1.9f;
    [Tooltip("Reload from empty (slide locked back, slower).")]
    [SerializeField] float emptyReloadTime = 2.5f;

    [Header("Identity")]
    [SerializeField] string displayName = "PISTOL";

    [Header("Pellets (shotgun)")]
    [Tooltip("Rays per shot. 1 = pistol; 8 = shotgun.")]
    [Min(1)] [SerializeField] int pellets = 1;
    [Tooltip("Extra spread for pellets around the aim point (degrees).")]
    [SerializeField] float pelletSpread = 0f;
    [Tooltip("Knockback (m/s) on the first pellet that hits each target. 0 = none (pistol).")]
    [SerializeField] float knockback = 0f;

    [Header("Laser sight (while aiming)")]
    [SerializeField] LineRenderer laser;
    [SerializeField] Transform laserDot;

    [Header("Damage")]
    [Tooltip("Body hit. Keys have 110 health: 4 body shots; a face shot (x3) bursts an eye and leaves them nearly dead.")]
    [SerializeField] float damage = 34f;
    [SerializeField] float range = 80f;
    [SerializeField] LayerMask hitMask = ~0;
    [SerializeField] float fireInterval = .2f;
    [Tooltip("How far the keys hear a shot. Gunfire is loud.")]
    [SerializeField] float noiseRadius = 32f;

    [Header("Spread (degrees, cone half-angle)")]
    [SerializeField] float baseSpread = .6f;
    [SerializeField] float moveSpread = 1.6f;
    [SerializeField] float sprintSpread = 4f;
    [Tooltip("Added per shot, recovers over time: spamming gets inaccurate.")]
    [SerializeField] float spreadPerShot = 1.1f;
    [SerializeField] float maxShotBloom = 4f;
    [SerializeField] float bloomRecoveryPerSecond = 6f;
    [Range(0, 1)] [SerializeField] float aimSpreadMultiplier = .3f;

    [Header("Aim down sights")]
    [SerializeField] float hipFov = 70f;
    [SerializeField] float aimFov = 55f;
    [SerializeField] float aimBlendSeconds = .14f;
    [Range(0, 1)] [SerializeField] float aimMoveSpeedMultiplier = .65f;

    [Header("Recoil")]
    [Range(0, 2)] [SerializeField] float recoilAmount = 1f;
    [SerializeField] float recoilKickBack = .05f;
    [SerializeField] float recoilLift = .012f;
    [SerializeField] float recoilRiseDegrees = 6f;
    [SerializeField] float recoilSettleSeconds = .22f;
    [SerializeField] Vector3 recoilPivot = new Vector3(0f, -.09f, -.05f);
    [Range(0, 2)] [SerializeField] float cameraKickAmount = 1f;
    [SerializeField] float cameraKickDegrees = 1.6f;

    [Header("Interact")]
    [SerializeField] float interactRange = 2.5f;

    [Header("Audio")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip[] fireClips;
    [SerializeField] AudioClip dryFireClip;
    [SerializeField] AudioClip reloadStartClip;
    [SerializeField] AudioClip reloadInsertClip;
    [SerializeField] AudioClip reloadEndClip;
    [SerializeField] AudioClip pickupClip;
    [Tooltip("The player shouting \"RELOADING!\" (like the black box).")]
    [SerializeField] AudioClip reloadCallout;
    [Range(0, 1)] [SerializeField] float reloadCalloutChance = .7f;
    [Range(0, .2f)] [SerializeField] float pitchVariation = .05f;
    [Tooltip("Base pitch of the gunshot (below 1 = deeper, e.g. a shotgun boom).")]
    [SerializeField] float firePitch = 1f;

    public int Loaded { get; private set; }
    public int Reserve { get; private set; }
    public int MagazineSize => magazineSize;
    public bool IsReloading { get; private set; }
    public float ReloadProgress { get; private set; }
    public bool IsEmpty => Loaded <= 0;
    public bool ReserveFull => Reserve >= maxReserve;
    /// <summary>0..1 how far into aim-down-sights.</summary>
    public float AimAmount { get; private set; }
    /// <summary>Current spread half-angle in degrees (the HUD crosshair opens with it).</summary>
    public float CurrentSpread { get; private set; }
    /// <summary>Movement speed multiplier at full aim (PlayerVitals applies it).</summary>
    public float AimMoveMultiplier => aimMoveSpeedMultiplier;

    public event Action AmmoChanged, Fired, DryFired, ReloadStarted, ReloadFinished;

    InputAction attackAction, interactAction, sprintAction, moveAction, reloadAction, aimAction;
    float nextFireTime, shotBloom;
    bool cursorWasLocked;
    Vector3 modelRestPosition;
    Quaternion modelRestRotation;
    readonly ViewmodelRecoil recoil = new ViewmodelRecoil();
    CameraKick cameraKick;
    FirstPersonController motor;
    CharacterController body;
    PlayerCloseCombat closeCombat;
    PlayerEaten injuries;
    float bashStartedAt = -10f;
    const float BashSeconds = .28f;
    int shotCount;
    float flashUntil;
    static readonly RaycastHit[] hits = new RaycastHit[16];

    void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (aimCamera != null)
        {
            cameraKick = aimCamera.GetComponent<CameraKick>();
            if (cameraKick == null) cameraKick = aimCamera.gameObject.AddComponent<CameraKick>();
            hipFov = aimCamera.fieldOfView;
        }
        motor = GetComponentInParent<FirstPersonController>();
        body = GetComponentInParent<CharacterController>();
        closeCombat = GetComponentInParent<PlayerCloseCombat>();
        injuries = GetComponentInParent<PlayerEaten>();
        if (model != null)
        {
            modelRestPosition = model.localPosition;
            modelRestRotation = model.localRotation;
        }
        Loaded = magazineSize;
        Reserve = Mathf.Clamp(startingReserve, 0, maxReserve);

        attackAction = InputSystem.actions.FindAction("Player/Attack", throwIfNotFound: true);
        interactAction = InputSystem.actions.FindAction("Player/Interact", throwIfNotFound: true);
        sprintAction = InputSystem.actions.FindAction("Player/Sprint");
        moveAction = InputSystem.actions.FindAction("Player/Move");
        // Reload and aim aren't in the shared input asset yet, so the gun owns these bindings.
        reloadAction = new InputAction("Reload", InputActionType.Button);
        reloadAction.AddBinding("<Keyboard>/r");
        reloadAction.AddBinding("<Gamepad>/leftShoulder");
        aimAction = new InputAction("Aim", InputActionType.Button);
        aimAction.AddBinding("<Mouse>/rightButton");
        aimAction.AddBinding("<Gamepad>/leftTrigger");
        SetFlash(false);
    }

    void OnEnable() { reloadAction.Enable(); aimAction.Enable(); }

    void OnDisable()
    {
        reloadAction.Disable();
        aimAction.Disable();
        recoil.Reset();
        SetFlash(false);
        if (IsReloading) { StopAllCoroutines(); IsReloading = false; ReloadProgress = 0f; }
        if (aimCamera != null) aimCamera.fieldOfView = hipFov;
    }

    void OnDestroy() { reloadAction.Dispose(); aimAction.Dispose(); }

    void Update()
    {
        bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
        bool canAct = cursorLocked && cursorWasLocked; // ignore the click that recaptures the mouse
        cursorWasLocked = cursorLocked;
        // A key has hold of you, or you're mid-bash: no shooting.
        if (closeCombat != null && (closeCombat.IsGrabbed || closeCombat.IsKicking)) canAct = false;
        if (Time.time - bashStartedAt < BashSeconds) canAct = false;

        bool wantsAim = canAct && aimAction.IsPressed() && !IsSprinting();
        AimAmount = Mathf.MoveTowards(AimAmount, wantsAim ? 1f : 0f, Time.deltaTime / Mathf.Max(.01f, aimBlendSeconds));
        if (aimCamera != null) aimCamera.fieldOfView = Mathf.Lerp(hipFov, aimFov, Smooth(AimAmount));

        shotBloom = Mathf.MoveTowards(shotBloom, 0f, bloomRecoveryPerSecond * Time.deltaTime);
        CurrentSpread = ComputeSpread();

        if (canAct)
        {
            if (attackAction.WasPressedThisFrame()) TryFire();
            if (reloadAction.WasPressedThisFrame()) TryReload();
            if (interactAction.WasPressedThisFrame()) TryPickup();
        }
    }

    void LateUpdate()
    {
        recoil.Update(Time.timeAsDouble);
        if (Time.time >= flashUntil) SetFlash(false);
        AnimateModel();
        UpdateLaser();
    }

    public string DisplayName => displayName;

    /// <summary>Resident Evil laser sight: a red beam and dot while aiming down sights.</summary>
    void UpdateLaser()
    {
        if (laser == null) return;
        bool on = AimAmount > .5f && !IsReloading && aimCamera != null;
        laser.enabled = on;
        if (laserDot != null) laserDot.gameObject.SetActive(on);
        if (!on) return;
        Ray aim = aimCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        Vector3 end = CastShot(aim, out RaycastHit hit) ? hit.point : aim.GetPoint(range);
        Vector3 start = muzzle != null ? muzzle.position : transform.position;
        laser.SetPosition(0, start);
        laser.SetPosition(1, end);
        if (laserDot != null)
        {
            laserDot.position = end - aim.direction * .02f;
            laserDot.rotation = Quaternion.LookRotation(aim.direction);
            float size = Mathf.Clamp(Vector3.Distance(aim.origin, end) * .012f, .02f, .25f);
            laserDot.localScale = Vector3.one * size;
        }
    }

    bool IsSprinting()
    {
        bool moving = moveAction != null && moveAction.ReadValue<Vector2>().sqrMagnitude > .01f;
        return moving && sprintAction != null && sprintAction.IsPressed();
    }

    float ComputeSpread()
    {
        float speed = body != null ? new Vector3(body.velocity.x, 0f, body.velocity.z).magnitude : 0f;
        float movement = Mathf.Clamp01(speed / 4f);
        float spread = baseSpread + moveSpread * movement + (IsSprinting() ? sprintSpread : 0f) + shotBloom;
        return spread * Mathf.Lerp(1f, aimSpreadMultiplier, AimAmount) * (injuries != null ? injuries.SpreadPenalty : 1f);
    }

    void TryFire()
    {
        if (IsReloading || Time.time < nextFireTime) return;
        nextFireTime = Time.time + fireInterval;

        if (Loaded <= 0)
        {
            Play(dryFireClip, 1f);
            DryFired?.Invoke();
            return;
        }

        Loaded--;
        shotCount++;
        AmmoChanged?.Invoke();

        Ray aim = aimCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        Vector3 centre = SpreadDirection(aim.direction, CurrentSpread);
        shotBloom = Mathf.Min(maxShotBloom, shotBloom + spreadPerShot);
        var knocked = new System.Collections.Generic.HashSet<Health>();

        for (int p = 0; p < pellets; p++)
        {
            Vector3 dir = pellets > 1 ? SpreadDirection(centre, pelletSpread) : centre;
            Ray shot = new Ray(aim.origin, dir);
            if (!CastShot(shot, out RaycastHit hit)) continue;
            Hitbox hitbox = hit.collider.GetComponent<Hitbox>();
            var info = new DamageInfo
            {
                amount = damage,
                point = hit.point,
                direction = shot.direction,
                normal = hit.normal,
                source = transform.root.gameObject,
            };
            // A shotgun blast knocks each target down once, not once per pellet.
            if (knockback > 0f && hitbox != null && hitbox.Owner != null && knocked.Add(hitbox.Owner))
                info.knockback = knockback;
            DamageOutcome outcome = hitbox != null ? hitbox.Hit(info) : DamageOutcome.None;
            if (p < 3 || hitbox != null) ImpactVFX.Play(hit, hitbox != null);
            if (outcome == DamageOutcome.None && hitbox == null && p == 0)
                Noise.Emit(hit.point, 6f, gameObject); // ricochet/impact
        }

        Vector3 muzzlePos = muzzle != null ? muzzle.position : transform.position;
        recoil.Play(transform, aimCamera.transform, Time.timeAsDouble,
            recoilAmount * Mathf.Lerp(1f, .6f, AimAmount), recoilKickBack, recoilLift,
            recoilRiseDegrees, recoilSettleSeconds, recoilPivot, shotCount);
        if (cameraKick != null) cameraKick.Play(cameraKickAmount * Mathf.Lerp(1f, .7f, AimAmount), cameraKickDegrees, shotCount);
        SetFlash(true);
        flashUntil = Time.time + .045f;
        PlayPitched(PickRandom(fireClips), firePitch);
        Noise.Emit(muzzlePos, noiseRadius, transform.root.gameObject);
        Fired?.Invoke();

        if (Loaded == 0 && Reserve > 0) StartCoroutine(AutoReloadSoon());
    }

    IEnumerator AutoReloadSoon()
    {
        // Empty: start reloading on its own after a beat, like slamming in a new mag in a panic.
        yield return new WaitForSeconds(.35f);
        TryReload();
    }

    /// <summary>
    /// Nearest solid hit or hitbox, ignoring the player's own colliders. Hitboxes are triggers
    /// (so creatures can't physically shove the player), every other trigger is ignored.
    /// </summary>
    bool CastShot(Ray ray, out RaycastHit best)
    {
        best = default;
        int count = Physics.RaycastNonAlloc(ray, hits, range, hitMask, QueryTriggerInteraction.Collide);
        float bestDistance = float.PositiveInfinity;
        Transform self = transform.root;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(self)) continue;
            if (hits[i].collider.isTrigger && hits[i].collider.GetComponent<Hitbox>() == null) continue;
            if (hits[i].distance < bestDistance) { bestDistance = hits[i].distance; best = hits[i]; }
        }
        if (bestDistance == float.PositiveInfinity) return false;

        // A weak spot just behind the first hit on the same creature wins: a shot into an open
        // mouth reaches the throat instead of stopping at the outside of the head.
        Hitbox first = best.collider.GetComponent<Hitbox>();
        if (first != null && !first.IsWeakSpot && first.Owner != null)
        {
            float weakDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Hitbox h = hits[i].collider.GetComponent<Hitbox>();
                if (h == null || !h.IsWeakSpot || h.Owner != first.Owner) continue;
                if (hits[i].distance - bestDistance > 1.6f || hits[i].distance >= weakDistance) continue;
                weakDistance = hits[i].distance;
                best = hits[i];
            }
        }
        return true;
    }

    static Vector3 SpreadDirection(Vector3 forward, float degrees)
    {
        if (degrees <= 0f) return forward;
        // Uniform-ish disc sample biased toward the centre: most shots land near the crosshair.
        Vector2 disc = UnityEngine.Random.insideUnitCircle * UnityEngine.Random.value;
        float radius = Mathf.Tan(degrees * Mathf.Deg2Rad);
        Vector3 right = Vector3.Cross(Mathf.Abs(forward.y) > .99f ? Vector3.forward : Vector3.up, forward).normalized;
        Vector3 up = Vector3.Cross(forward, right);
        return (forward + (right * disc.x + up * disc.y) * radius).normalized;
    }

    void TryReload()
    {
        if (IsReloading || Loaded >= magazineSize || Reserve <= 0) return;
        StartCoroutine(Reload((Loaded == 0 ? emptyReloadTime : tacticalReloadTime) * (injuries != null ? 1f + (injuries.ArmPenalty - 1f) * .6f : 1f)));
    }

    IEnumerator Reload(float duration)
    {
        IsReloading = true;
        ReloadProgress = 0f;
        recoil.Reset();
        ReloadStarted?.Invoke();
        // The keys hear the mag drop.
        Noise.Emit(transform.position, 10f, transform.root.gameObject);
        Play(reloadStartClip, 1f);
        if (reloadCallout != null && audioSource != null && UnityEngine.Random.value < reloadCalloutChance)
        {
            audioSource.pitch = 1f;
            audioSource.PlayOneShot(reloadCallout, 1f);
        }
        bool inserted = false;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            ReloadProgress = t / duration;
            if (!inserted && ReloadProgress >= .55f) { inserted = true; Play(reloadInsertClip, 1f); }
            yield return null;
        }
        Play(reloadEndClip, 1f);
        int moved = Mathf.Min(magazineSize - Loaded, Reserve);
        Loaded += moved;
        Reserve -= moved;
        IsReloading = false;
        ReloadProgress = 0f;
        AmmoChanged?.Invoke();
        ReloadFinished?.Invoke();
    }

    /// <summary>Set the spare rounds directly (difficulty start values).</summary>
    public void SetReserve(int rounds)
    {
        Reserve = Mathf.Clamp(rounds, 0, maxReserve);
        AmmoChanged?.Invoke();
    }

    public int AddReserve(int rounds)
    {
        int taken = Mathf.Clamp(rounds, 0, maxReserve - Reserve);
        if (taken > 0) { Reserve += taken; AmmoChanged?.Invoke(); }
        return taken;
    }

    void TryPickup()
    {
        AmmoPickup pickup = LookedAtPickup();
        if (pickup != null && pickup.TryCollect(this)) Play(pickupClip, 1f);
    }

    /// <summary>Ammo under the crosshair within reach (also used for the HUD prompt).</summary>
    public AmmoPickup LookedAtPickup()
    {
        if (aimCamera == null) return null;
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, ~0, QueryTriggerInteraction.Collide)) return null;
        AmmoPickup pickup = hit.collider.GetComponentInParent<AmmoPickup>();
        return pickup != null && pickup.Rounds > 0 ? pickup : null;
    }

    /// <summary>Gun-butt bash (shove / breaking free): thrust forward and tilt.</summary>
    public void PlayBash() => bashStartedAt = Time.time;

    void AnimateModel()
    {
        if (model == null) return;
        float dip = IsReloading ? Mathf.Sin(ReloadProgress * Mathf.PI) : 0f;
        float bashT = Mathf.Clamp01((Time.time - bashStartedAt) / BashSeconds);
        float bash = bashT < 1f ? Mathf.Sin(bashT * Mathf.PI) : 0f;
        model.localPosition = modelRestPosition + Vector3.down * (dip * .1f) + new Vector3(-.05f, .02f, .12f) * bash;
        model.localRotation = modelRestRotation * Quaternion.Euler(dip * 30f - bash * 35f, bash * -25f, dip * -25f + bash * 40f);
    }

    void SetFlash(bool on)
    {
        if (muzzleLight != null) muzzleLight.enabled = on;
        if (muzzleFlash != null)
        {
            muzzleFlash.enabled = on;
            if (on) muzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
        }
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip == null || audioSource == null) return;
        audioSource.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
        audioSource.PlayOneShot(clip, volume);
    }

    void PlayPitched(AudioClip clip, float basePitch)
    {
        if (clip == null || audioSource == null) return;
        audioSource.pitch = basePitch * (1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation));
        audioSource.PlayOneShot(clip, 1f);
    }

    static AudioClip PickRandom(AudioClip[] clips) =>
        clips == null || clips.Length == 0 ? null : clips[UnityEngine.Random.Range(0, clips.Length)];

    static float Smooth(float t) => t * t * (3f - 2f * t);

    void OnApplicationFocus(bool focus) { if (!focus) cursorWasLocked = false; }
}
