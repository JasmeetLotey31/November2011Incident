using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The first gun: a maintenance nail gun. Weak and slow on purpose.
/// - 2 nails jam a loose key; a nail that hits a key in front of a wall pins it (longer jam).
/// - 6-nail magazine, slow strip reload, quiet "thunk" (small noise radius).
/// - Nails drop over distance and stay stuck where they land; Interact pulls them back out.
/// Put the NailGun prefab under the player camera. Attack fires, R / left bumper reloads,
/// Interact (E) collects nail strips and stuck nails.
/// </summary>
public class NailGun : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Camera the gun aims through (crosshair = screen centre). Defaults to Camera.main.")]
    [SerializeField] Camera aimCamera;
    [Tooltip("Where nails spawn.")]
    [SerializeField] Transform muzzle;
    [SerializeField] NailProjectile nailPrefab;
    [Tooltip("Visual model that kicks on fire and dips while reloading.")]
    [SerializeField] Transform model;

    [Header("Ammo")]
    [SerializeField] int magazineSize = 6;
    [SerializeField] int startingLoaded = 6;
    [SerializeField] int startingReserve = 12;
    [SerializeField] int maxReserve = 36;
    [SerializeField] float reloadTime = 2.5f;

    [Header("Firing")]
    [SerializeField] float fireInterval = 0.35f;
    [SerializeField] float muzzleSpeed = 45f;
    [Tooltip("Bend meter filled per nail. Loose keys have a meter of 1, so 0.5 = 2 nails to jam.")]
    [SerializeField] float bendPerNail = 0.5f;
    [Tooltip("Distance from where the nail enters the target to a wall behind it, for the nail to pin it. " +
             "Covers the target's thickness plus a small gap.")]
    [SerializeField] float pinReach = 1.25f;
    [SerializeField] float maxAimDistance = 100f;
    [SerializeField] LayerMask hitMask = ~0;
    [Tooltip("How far the keys hear the shot. Low: it's a thunk, not a bang.")]
    [SerializeField] float fireNoiseRadius = 8f;

    [Header("Interact")]
    [SerializeField] float interactRange = 2.5f;

    [Header("Recoil (ported rifle recoil, lighter)")]
    [Range(0, 2)] [SerializeField] float recoilAmount = .7f;
    [SerializeField] float recoilKickBack = .045f;
    [SerializeField] float recoilLift = .01f;
    [SerializeField] float recoilRiseDegrees = 4f;
    [SerializeField] float recoilSettleSeconds = .2f;
    [Tooltip("Recoil pivots around this point in the gun's local space (the grip).")]
    [SerializeField] Vector3 recoilPivot = new Vector3(0f, -.11f, -.07f);

    [Header("Camera kick")]
    [Range(0, 2)] [SerializeField] float cameraKickAmount = .6f;
    [SerializeField] float cameraKickDegrees = 1.1f;

    [Header("Reload motion")]
    [SerializeField] float reloadDipDistance = 0.12f;
    [SerializeField] float reloadDipPitch = 25f;

    [Header("Audio")]
    [SerializeField] AudioSource audioSource;
    [Tooltip("One is picked at random per shot.")]
    [SerializeField] AudioClip[] fireClips;
    [SerializeField] AudioClip dryFireClip;
    [Tooltip("Reload cues: open at the start, a strip seating mid-way, close at the end.")]
    [SerializeField] AudioClip reloadOpenClip;
    [SerializeField] AudioClip[] reloadSeatClips;
    [SerializeField] AudioClip reloadCloseClip;
    [SerializeField] AudioClip pickupClip;
    [Tooltip("Random pitch variation so repeated shots don't sound identical.")]
    [Range(0, .2f)] [SerializeField] float pitchVariation = .06f;

    public int Loaded { get; private set; }
    public int Reserve { get; private set; }
    public int MagazineSize => magazineSize;
    public bool IsReloading { get; private set; }
    /// <summary>0..1 while reloading.</summary>
    public float ReloadProgress { get; private set; }
    /// <summary>Raised whenever Loaded or Reserve changes (for the HUD).</summary>
    public event Action AmmoChanged;
    public event Action Fired;
    public event Action DryFired;
    public event Action ReloadStarted;

    InputAction attackAction, interactAction, reloadAction;
    float nextFireTime;
    bool cursorWasLocked;
    Vector3 modelRestPosition;
    Quaternion modelRestRotation;
    readonly ViewmodelRecoil recoil = new ViewmodelRecoil();
    CameraKick cameraKick;
    int shotCount;

    void Awake()
    {
        if (aimCamera == null) aimCamera = Camera.main;
        if (aimCamera != null)
        {
            cameraKick = aimCamera.GetComponent<CameraKick>();
            if (cameraKick == null) cameraKick = aimCamera.gameObject.AddComponent<CameraKick>();
        }
        if (model != null)
        {
            modelRestPosition = model.localPosition;
            modelRestRotation = model.localRotation;
        }

        Loaded = Mathf.Clamp(startingLoaded, 0, magazineSize);
        Reserve = Mathf.Clamp(startingReserve, 0, maxReserve);

        attackAction = InputSystem.actions.FindAction("Player/Attack", throwIfNotFound: true);
        interactAction = InputSystem.actions.FindAction("Player/Interact", throwIfNotFound: true);
        // Reload isn't in the shared input asset yet, so the gun owns its binding.
        reloadAction = new InputAction("Reload", InputActionType.Button);
        reloadAction.AddBinding("<Keyboard>/r");
        reloadAction.AddBinding("<Gamepad>/leftShoulder");
    }

    void OnEnable() => reloadAction.Enable();

    void OnDisable()
    {
        reloadAction.Disable();
        recoil.Reset();
        // A reload interrupted by switching away doesn't finish.
        if (IsReloading)
        {
            StopAllCoroutines();
            IsReloading = false;
            ReloadProgress = 0f;
        }
    }

    void OnDestroy() => reloadAction.Dispose();

    void Update()
    {
        // Ignore the click that recaptures the mouse after Escape.
        bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
        bool canAct = cursorLocked && cursorWasLocked;

        if (canAct)
        {
            if (attackAction.WasPressedThisFrame()) TryFire();
            if (reloadAction.WasPressedThisFrame()) TryReload();
            if (interactAction.WasPressedThisFrame()) TryCollectNails();
        }

        cursorWasLocked = cursorLocked;
    }

    void LateUpdate()
    {
        recoil.Update(Time.timeAsDouble);
        AnimateModel();
    }

    void TryFire()
    {
        if (IsReloading || Time.time < nextFireTime) return;

        if (Loaded <= 0)
        {
            Play(dryFireClip);
            nextFireTime = Time.time + fireInterval;
            DryFired?.Invoke();
            return;
        }

        nextFireTime = Time.time + fireInterval;
        Loaded--;
        AmmoChanged?.Invoke();

        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Vector3 direction = AimDirection(origin);

        if (nailPrefab != null)
        {
            NailProjectile nail = Instantiate(nailPrefab, origin, Quaternion.LookRotation(direction));
            nail.Launch(direction * muzzleSpeed, bendPerNail, pinReach, hitMask, transform.root);
        }

        shotCount++;
        if (aimCamera != null)
            recoil.Play(transform, aimCamera.transform, Time.timeAsDouble, recoilAmount, recoilKickBack,
                recoilLift, recoilRiseDegrees, recoilSettleSeconds, recoilPivot, shotCount);
        if (cameraKick != null) cameraKick.Play(cameraKickAmount, cameraKickDegrees, shotCount);
        Play(PickRandom(fireClips));
        Noise.Emit(origin, fireNoiseRadius, transform.root.gameObject);
        Fired?.Invoke();
    }

    /// <summary>From the muzzle toward whatever is under the crosshair.</summary>
    Vector3 AimDirection(Vector3 origin)
    {
        if (aimCamera == null) return transform.forward;

        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 target = Physics.Raycast(ray, out RaycastHit hit, maxAimDistance, hitMask, QueryTriggerInteraction.Ignore)
            ? hit.point
            : ray.GetPoint(maxAimDistance);

        Vector3 toTarget = target - origin;
        // Point-blank: aiming from the muzzle would angle sharply, so shoot straight down the camera ray.
        return toTarget.sqrMagnitude < 1f ? ray.direction : toTarget.normalized;
    }

    void TryReload()
    {
        if (IsReloading || Loaded >= magazineSize || Reserve <= 0) return;
        StartCoroutine(Reload());
    }

    IEnumerator Reload()
    {
        IsReloading = true;
        ReloadProgress = 0f;
        recoil.Reset();
        ReloadStarted?.Invoke();
        Play(reloadOpenClip);
        bool seated = false, closed = false;

        for (float t = 0f; t < reloadTime; t += Time.deltaTime)
        {
            ReloadProgress = t / reloadTime;
            // The strip slides in at the bottom of the dip, the magazine shuts near the end.
            if (!seated && ReloadProgress >= .45f) { seated = true; Play(PickRandom(reloadSeatClips)); }
            if (!closed && ReloadProgress >= .85f) { closed = true; Play(reloadCloseClip); }
            yield return null;
        }

        int moved = Mathf.Min(magazineSize - Loaded, Reserve);
        Loaded += moved;
        Reserve -= moved;
        IsReloading = false;
        ReloadProgress = 0f;
        AmmoChanged?.Invoke();
    }

    /// <summary>Adds nails to the reserve, up to the cap. Returns how many were taken.</summary>
    public int AddReserve(int nails)
    {
        int taken = Mathf.Clamp(nails, 0, maxReserve - Reserve);
        if (taken > 0)
        {
            Reserve += taken;
            AmmoChanged?.Invoke();
        }
        return taken;
    }

    void TryCollectNails()
    {
        NailPickup pickup = LookedAtPickup();
        if (pickup != null && pickup.TryCollect(this))
            Play(pickupClip);
    }

    /// <summary>The nail strip or stuck nail under the crosshair within reach, or null (also used by the HUD prompt).</summary>
    public NailPickup LookedAtPickup()
    {
        if (aimCamera == null) return null;
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        // Collide with triggers: stuck nails use a small trigger collider so they don't block movement.
        if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, ~0, QueryTriggerInteraction.Collide)) return null;
        NailPickup pickup = hit.collider.GetComponentInParent<NailPickup>();
        return pickup != null && pickup.IsCollectable ? pickup : null;
    }

    public bool ReserveFull => Reserve >= maxReserve;

    /// <summary>Reload dip on the model. Recoil moves the whole gun (this transform) separately.</summary>
    void AnimateModel()
    {
        if (model == null) return;

        // Dip down and tilt during the reload, easing in and out.
        float dip = IsReloading ? Mathf.Sin(ReloadProgress * Mathf.PI) : 0f;
        model.localPosition = modelRestPosition + Vector3.down * (dip * reloadDipDistance);
        model.localRotation = modelRestRotation * Quaternion.Euler(dip * reloadDipPitch, 0f, 0f);
    }

    static AudioClip PickRandom(AudioClip[] clips) =>
        clips == null || clips.Length == 0 ? null : clips[UnityEngine.Random.Range(0, clips.Length)];

    void Play(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        audioSource.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
        audioSource.PlayOneShot(clip);
    }
}
