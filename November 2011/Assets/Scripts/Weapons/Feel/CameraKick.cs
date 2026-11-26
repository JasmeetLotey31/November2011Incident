using UnityEngine;

/// <summary>
/// Short additive camera kick when a gun fires: a quick pitch bump plus a tiny vibration.
/// Ported from the Western project's RifleCameraKick, which was a Cinemachine extension. This
/// project has no Cinemachine, so it layers the same rotation on the camera in LateUpdate,
/// after FirstPersonController has set the look pitch for the frame. It never changes where
/// the player is aiming next frame: the controller rewrites the pitch every Update.
/// Put it on the player camera (the nail gun adds it automatically if missing).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class CameraKick : MonoBehaviour
{
    [Tooltip("Scales every kick (0 turns camera kick off, e.g. a comfort setting).")]
    [Range(0, 2)] [SerializeField] float intensity = 1f;

    double startedAt = double.NegativeInfinity;
    float strength, pitch, side;
    bool active;
    // What we added last frame, so it can be removed if nothing overwrote the camera since.
    Quaternion lastKick = Quaternion.identity;
    Quaternion rotationAfterKick;
    bool kickApplied;

    /// <param name="amount">Strength (0–2).</param>
    /// <param name="degrees">Pitch bump in degrees (0–3).</param>
    /// <param name="shot">Shot index; alternates the side wobble.</param>
    public void Play(float amount, float degrees, int shot)
    {
        startedAt = Time.timeAsDouble - .008d;
        strength = Mathf.Clamp(amount, 0, 2) * intensity;
        pitch = Mathf.Clamp(degrees, 0, 3);
        side = shot % 2 == 0 ? -1f : 1f;
        active = strength > 0f;
    }

    void LateUpdate()
    {
        // The look controller normally rewrites the camera every frame. If it didn't (cursor
        // unlocked, controller disabled), take last frame's kick off so kicks never stack.
        if (kickApplied && transform.localRotation == rotationAfterKick)
            transform.localRotation *= Quaternion.Inverse(lastKick);
        kickApplied = false;

        if (!active) return;
        float age = (float)(Time.timeAsDouble - startedAt);
        if (age < 0 || age >= .23f) { active = false; return; }

        float bump = ViewmodelRecoil.Envelope(age, .016f, .214f) * strength;
        float vibration = ViewmodelRecoil.Envelope(age, .009f, .09f) * strength;
        lastKick = Quaternion.Euler(
            -pitch * bump + Mathf.Sin(age * 157f) * .025f * vibration,
            side * (.055f * bump + Mathf.Sin(age * 173f) * .07f * vibration),
            side * Mathf.Sin(age * 139f) * .045f * vibration);
        transform.localRotation *= lastKick;
        rotationAfterKick = transform.localRotation;
        kickApplied = true;
    }
}
