using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Makes the held weapon feel like it has weight:
/// - look sway: the gun lags behind fast mouse turns, then catches up;
/// - walk bob: a gait-phase figure-eight while moving, plus a lean when strafing;
/// - sprint carry: the gun lowers and tilts while really sprinting, and comes straight back up
///   when you fire or reload.
/// Ported from the Western project (WesternPlayerGunplay's sway/bob maths and WesternSprintGunPose).
/// Put it on a holder object between the camera and the gun (Camera → WeaponHolder → Sidearm).
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponSway : MonoBehaviour
{
    [Header("References (found automatically)")]
    [SerializeField] Transform player;
    [SerializeField] CharacterController body;
    [Tooltip("Gun whose shots and reloads interrupt the sprint carry, and whose aim drives the sights pose.")]
    [SerializeField] Sidearm gun;

    [Header("Aim down sights")]
    [Tooltip("Holder position while fully aimed (sights on the screen centre).")]
    [SerializeField] Vector3 aimLocalPosition = new Vector3(0f, -0.075f, 0.34f);
    [Tooltip("Sway and bob left while fully aimed.")]
    [Range(0, 1)] [SerializeField] float aimSwayScale = .2f;

    [Header("Look sway")]
    [Range(0, 2)] [SerializeField] float swayIntensity = 1f;
    [Range(.01f, .2f)] [SerializeField] float lookSmoothing = .075f;
    [Range(.001f, .03f)] [SerializeField] float lookInertia = .009f;
    [Range(.5f, 5)] [SerializeField] float maximumLookSway = 2.5f;

    [Header("Walk bob")]
    [Tooltip("Speed treated as a normal walk (match FirstPersonController's walk speed).")]
    [SerializeField] float walkSpeed = 4f;
    [Tooltip("Stride length in metres for one full bob cycle.")]
    [SerializeField] float strideLength = 1.7f;
    [Range(.05f, .3f)] [SerializeField] float movementRiseSeconds = .12f;
    [Range(.05f, .4f)] [SerializeField] float movementSettleSeconds = .20f;
    [Tooltip("Bob and sway are reduced to this while reloading.")]
    [Range(0, 1)] [SerializeField] float reloadSwayScale = .4f;

    [Header("Sprint carry")]
    [SerializeField] Vector3 carryOffset = new Vector3(-0.01f, -0.045f, -0.03f);
    [SerializeField] Vector3 carryAngles = new Vector3(8f, -14f, 10f);
    [Min(0.01f)] [SerializeField] float lowerSeconds = 0.22f;
    [Min(0.01f)] [SerializeField] float returnSeconds = 0.16f;
    [Tooltip("After firing or reloading, stay raised this long before lowering again.")]
    [Min(0f)] [SerializeField] float actionHoldSeconds = 0.4f;

    Vector3 restPosition;
    Quaternion restRotation;
    Vector3 lastPosition;
    Quaternion lastAim;
    bool sampled;
    Vector2 lookOffset, lookVelocity;
    float movement, strafe, gaitPhase, sprintBlend;
    double holdUntil;
    InputAction sprintAction, moveAction;

    /// <summary>0..1, how lowered the gun is for sprinting.</summary>
    public float SprintBlend => sprintBlend;

    void Awake()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        if (body == null) body = GetComponentInParent<CharacterController>();
        if (player == null) player = body != null ? body.transform : transform.root;
        allGuns = GetComponentsInChildren<Sidearm>(true);
        slots = GetComponent<WeaponSlots>();
        if (gun == null && allGuns.Length > 0) gun = allGuns[0];
        sprintAction = InputSystem.actions.FindAction("Player/Sprint");
        moveAction = InputSystem.actions.FindAction("Player/Move");
    }

    Sidearm[] allGuns;
    WeaponSlots slots;
    /// <summary>The gun in hand (follows weapon switching).</summary>
    Sidearm Current => slots != null && slots.Active != null ? slots.Active : gun;

    void OnEnable()
    {
        sampled = false;
        foreach (Sidearm g in allGuns) { g.Fired += HoldRaised; g.DryFired += HoldRaised; g.ReloadStarted += HoldRaised; }
    }

    void OnDisable()
    {
        foreach (Sidearm g in allGuns) { g.Fired -= HoldRaised; g.DryFired -= HoldRaised; g.ReloadStarted -= HoldRaised; }
        transform.localPosition = restPosition;
        transform.localRotation = restRotation;
    }

    void HoldRaised()
    {
        holdUntil = Time.timeAsDouble + actionHoldSeconds;
        sprintBlend = 0f;
    }

    void LateUpdate()
    {
        float dt = Mathf.Clamp(Time.deltaTime, 0f, .1f);
        Vector3 position = player.position;
        Quaternion aim = transform.parent != null ? transform.parent.rotation : transform.rotation;

        if (!sampled || dt <= 0f)
        {
            lastPosition = position;
            lastAim = aim;
            sampled = true;
            return;
        }

        // --- movement (from real displacement, so pushing into a wall doesn't bob) ---
        Vector3 travel = position - lastPosition;
        travel.y = 0f;
        float distance = travel.magnitude;
        // Far more than a sprint step in one frame = teleport/respawn.
        if (distance > Mathf.Max(.5f, walkSpeed * 6f * dt))
        {
            // Teleport/respawn: don't swing the gun across the screen.
            distance = 0f;
            travel = Vector3.zero;
        }
        float speed = distance / dt;
        bool grounded = body == null || body.isGrounded;

        float wantedMovement = grounded ? Mathf.Clamp(speed / Mathf.Max(1f, walkSpeed), 0f, 1.5f) : 0f;
        float settle = wantedMovement > movement ? movementRiseSeconds : movementSettleSeconds;
        movement = Mathf.MoveTowards(movement, wantedMovement, dt / Mathf.Max(.01f, settle));
        Vector3 localTravel = player.InverseTransformDirection(travel);
        strafe = Mathf.Lerp(strafe, distance > .0001f ? localTravel.x / distance : 0f, 1f - Mathf.Exp(-14f * dt));
        gaitPhase = Mathf.Repeat(gaitPhase + distance * (Mathf.PI * 2f / Mathf.Max(.1f, strideLength)), Mathf.PI * 2f);

        // --- look sway (gun lags behind fast turns) ---
        Vector3 previousAngles = lastAim.eulerAngles, angles = aim.eulerAngles;
        float lookDt = Mathf.Max(.0001f, Time.unscaledDeltaTime);
        Vector2 angularSpeed = new Vector2(Mathf.DeltaAngle(previousAngles.x, angles.x),
            Mathf.DeltaAngle(previousAngles.y, angles.y)) / lookDt;
        Vector2 wantedLook = Vector2.ClampMagnitude(-angularSpeed * lookInertia, maximumLookSway);
        lookOffset = Vector2.SmoothDamp(lookOffset, wantedLook, ref lookVelocity, lookSmoothing, 100f,
            Mathf.Clamp(Time.unscaledDeltaTime, 0f, .1f));

        lastPosition = position;
        lastAim = aim;

        // --- sprint carry ---
        bool sprintHeld = sprintAction != null && sprintAction.IsPressed();
        bool moving = moveAction == null || moveAction.ReadValue<Vector2>().sqrMagnitude > .01f;
        bool reloading = Current != null && Current.IsReloading;
        bool wantsSprint = sprintHeld && moving && grounded && speed > walkSpeed * 1.04f &&
                           !reloading && Time.timeAsDouble >= holdUntil;
        sprintBlend = Mathf.MoveTowards(sprintBlend, wantsSprint ? 1f : 0f,
            dt / Mathf.Max(.01f, wantsSprint ? lowerSeconds : returnSeconds));
        float carry = sprintBlend * sprintBlend * (3f - 2f * sprintBlend);

        // --- present ---
        float aimT = Current != null ? Current.AimAmount : 0f;
        aimT = aimT * aimT * (3f - 2f * aimT);
        float scale = swayIntensity * (reloading ? reloadSwayScale : 1f) * Mathf.Lerp(1f, aimSwayScale, aimT);
        float motion = movement * (1f - carry); // the sprint carry replaces the walk bob
        float x = Mathf.Sin(gaitPhase), y = Mathf.Cos(gaitPhase * 2f);

        Vector3 swayPosition = new Vector3(
            lookOffset.y * .005f + x * .010f * motion,
            -lookOffset.x * .004f + y * .007f * motion,
            -.008f * motion) * scale;
        Quaternion swayRotation = Quaternion.Euler(new Vector3(
            lookOffset.x + y * .48f * motion,
            lookOffset.y + x * .32f * motion,
            lookOffset.y * .35f + (x * .45f - strafe * .7f) * motion) * scale);

        Vector3 basePosition = Vector3.Lerp(restPosition, aimLocalPosition, aimT);
        transform.localPosition = basePosition + restRotation * (swayPosition + carryOffset * carry);
        transform.localRotation = restRotation * swayRotation *
                                  Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(carryAngles), carry);
    }
}
