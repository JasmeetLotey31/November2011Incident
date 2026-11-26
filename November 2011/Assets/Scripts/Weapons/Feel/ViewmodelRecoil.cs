using UnityEngine;

/// <summary>
/// Short, camera-relative recoil on a held weapon: kick back, muzzle rise around a virtual
/// shoulder/grip pivot, alternating left/right per shot. Evaluated from absolute time, so it looks
/// the same at 30, 60 or 144 fps and never accumulates.
/// Ported from the Western project's RifleRecoilMotion (plain C#, call Play then Update each frame).
/// </summary>
public sealed class ViewmodelRecoil
{
    Transform weapon;
    Vector3 restPosition, translation, pivotOffset;
    Quaternion restRotation, cameraInParent;
    double startedAt;
    float recovery, rise, side, strength;

    public bool IsActive { get; private set; }

    /// <param name="amount">Overall strength (0–2).</param>
    /// <param name="back">Kick-back distance in metres.</param>
    /// <param name="up">Lift in metres.</param>
    /// <param name="degrees">Muzzle rise in degrees.</param>
    /// <param name="settle">Seconds to settle back to rest.</param>
    /// <param name="pivot">Pivot point in the weapon's local space (grip/shoulder), scaled by the weapon's scale.</param>
    /// <param name="shot">Shot index; odd/even shots kick to opposite sides.</param>
    public void Play(Transform root, Transform camera, double now, float amount, float back, float up,
        float degrees, float settle, Vector3 pivot, int shot)
    {
        Reset();
        if (root == null || camera == null || amount <= 0) return;
        weapon = root;
        restPosition = root.localPosition;
        restRotation = root.localRotation;
        strength = Mathf.Clamp(amount, 0, 2);
        recovery = Mathf.Clamp(settle, .12f, .6f);
        rise = Mathf.Clamp(degrees, 0, 9);
        side = shot % 2 == 0 ? -1f : 1f;

        Transform parent = root.parent;
        cameraInParent = Quaternion.Inverse(parent != null ? parent.rotation : Quaternion.identity) * camera.rotation;
        Vector3 offset = camera.TransformDirection(
            new Vector3(side * .0015f, Mathf.Clamp(up, 0, .03f), -Mathf.Clamp(back, 0, .08f)) * strength);
        translation = parent != null ? parent.InverseTransformVector(offset) : offset;
        // Rotate the whole gun around the pivot (behind/below the receiver) instead of its own centre.
        pivotOffset = restRotation * Vector3.Scale(root.localScale, pivot);
        startedAt = now - .008d;
        IsActive = true;
        Update(now);
    }

    public void Update(double now)
    {
        if (!IsActive) return;
        if (weapon == null) { IsActive = false; return; }

        float age = (float)(now - startedAt);
        if (float.IsNaN(age) || age < 0 || age >= .052f + recovery) { Reset(); return; }

        float kick = Envelope(age, .024f, recovery * .75f);
        float lift = Envelope(age, .052f, recovery);
        Quaternion q = cameraInParent
                       * Quaternion.Euler(-rise * lift * strength, side * .12f * lift * strength, side * .35f * lift * strength)
                       * Quaternion.Inverse(cameraInParent);
        weapon.localPosition = restPosition + translation * kick + pivotOffset - q * pivotOffset;
        weapon.localRotation = q * restRotation;
    }

    /// <summary>0→1→0 pulse: cubic ease-out attack, cubic decay. Same shape at any frame rate.</summary>
    public static float Envelope(float age, float attack, float settle)
    {
        if (float.IsNaN(age) || float.IsInfinity(age) || age <= 0) return 0;
        attack = Mathf.Max(.001f, attack);
        settle = Mathf.Max(.001f, settle);
        if (age < attack)
        {
            float t = age / attack;
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }
        return Mathf.Pow(1f - Mathf.Clamp01((age - attack) / settle), 3f);
    }

    public void Reset()
    {
        if (IsActive && weapon != null)
        {
            weapon.localPosition = restPosition;
            weapon.localRotation = restRotation;
        }
        IsActive = false;
    }
}
