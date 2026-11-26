using UnityEngine;

/// <summary>
/// Shows the bend meter on the target itself instead of a health bar:
/// - every hit kicks a springy wobble away from the shot;
/// - the target leans further away as its meter fills (and straightens as it decays);
/// - jammed: it slumps over and goes still;
/// - pinned: it shudders against the wall for a moment, then stops.
/// Rotates a visual pivot (at the target's base) so colliders, AI and nav stay untouched.
/// Set the Bendable's Attach Point to the same pivot so stuck nails bend with it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Bendable))]
public sealed class BendReaction : MonoBehaviour
{
    [Tooltip("Child that holds the visuals, placed at the target's base. It is rotated; nothing else is.")]
    [SerializeField] Transform pivot;

    [Header("Lean")]
    [Tooltip("Lean in degrees at a nearly full bend meter.")]
    [SerializeField] float maxBendLean = 16f;
    [Tooltip("Lean in degrees while jammed (slumped).")]
    [SerializeField] float jammedLean = 38f;
    [SerializeField] float leanSmoothTime = .08f;
    [SerializeField] float recoverSmoothTime = .35f;

    [Header("Hit wobble (spring)")]
    [SerializeField] float wobblePerHit = 14f;
    [SerializeField] float wobbleStiffness = 220f;
    [SerializeField] float wobbleDamping = 11f;

    [Header("Pin shudder")]
    [SerializeField] float pinShudderDegrees = 5f;
    [SerializeField] float pinShudderSeconds = .5f;
    [SerializeField] float pinShudderFrequency = 38f;

    Bendable bendable;
    Vector3 restLocalPosition;
    Quaternion restLocalRotation;
    Vector3 leanAxisWorld = Vector3.right; // axis the target tips over (perpendicular to the last shot)
    float lean, leanVelocity;
    float wobble, wobbleVelocity;
    float pinShudderStartedAt = float.NegativeInfinity;

    void Awake()
    {
        bendable = GetComponent<Bendable>();
        if (pivot == null && transform.childCount > 0) pivot = transform.GetChild(0);
        if (pivot != null)
        {
            restLocalPosition = pivot.localPosition;
            restLocalRotation = pivot.localRotation;
        }
    }

    void OnEnable()
    {
        bendable.Hit += OnHit;
        bendable.Recovered += OnRecovered;
    }

    void OnDisable()
    {
        bendable.Hit -= OnHit;
        bendable.Recovered -= OnRecovered;
        if (pivot != null)
        {
            pivot.localPosition = restLocalPosition;
            pivot.localRotation = restLocalRotation;
        }
    }

    void OnHit(BendHit hit, BendOutcome outcome)
    {
        // Tip away from the shooter: rotate around the horizontal axis perpendicular to the shot.
        Vector3 flat = Vector3.ProjectOnPlane(hit.direction, Vector3.up);
        if (flat.sqrMagnitude > .0001f)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, flat.normalized);
            // Keep the existing lean direction while already leaning far, so it doesn't snap sideways.
            leanAxisWorld = lean > 5f ? Vector3.Slerp(leanAxisWorld, axis, .35f).normalized : axis;
        }

        float kick = outcome == BendOutcome.Pinned ? 1.6f : outcome == BendOutcome.Jammed ? 1.3f : 1f;
        wobbleVelocity += wobblePerHit * kick * 12f;
        if (outcome == BendOutcome.Pinned) pinShudderStartedAt = Time.time;
    }

    void OnRecovered()
    {
        pinShudderStartedAt = float.NegativeInfinity;
        // A little shake as it pulls itself back together.
        wobbleVelocity -= wobblePerHit * 6f;
    }

    void LateUpdate()
    {
        if (pivot == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float targetLean = bendable.IsJammed ? jammedLean : bendable.Bend01 * maxBendLean;
        float smooth = targetLean >= lean ? leanSmoothTime : recoverSmoothTime;
        lean = Mathf.SmoothDamp(lean, targetLean, ref leanVelocity, smooth, Mathf.Infinity, dt);

        // Damped spring back to 0 (semi-implicit Euler, stable at normal frame rates).
        float step = Mathf.Min(dt, 1f / 30f);
        wobbleVelocity += (-wobbleStiffness * wobble - wobbleDamping * wobbleVelocity) * step;
        wobble += wobbleVelocity * step;

        float shudder = 0f;
        float shudderAge = Time.time - pinShudderStartedAt;
        if (bendable.IsPinned && shudderAge >= 0f && shudderAge < pinShudderSeconds)
        {
            float fade = 1f - shudderAge / pinShudderSeconds;
            shudder = Mathf.Sin(shudderAge * pinShudderFrequency * Mathf.PI * 2f) * pinShudderDegrees * fade * fade;
        }

        // While pinned the wall holds it: less slump, no free wobble, just the shudder.
        float pinnedHold = bendable.IsPinned ? .45f : 1f;
        float angle = lean * pinnedHold + (bendable.IsPinned ? 0f : wobble) + shudder;

        Vector3 axisLocal = transform.InverseTransformDirection(leanAxisWorld);
        Quaternion tilt = Quaternion.AngleAxis(angle, axisLocal);
        // Rotate around the pivot's rest position (the base), so it tips rather than spins.
        pivot.localPosition = restLocalPosition;
        pivot.localRotation = tilt * restLocalRotation;
    }
}
