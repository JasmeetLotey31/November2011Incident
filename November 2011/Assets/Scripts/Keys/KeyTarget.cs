using UnityEngine;

/// <summary>
/// Marks the player as what the keys hunt, and gives them what they need to judge you:
/// where you are, where you're looking, whether your flashlight is on, and whether your gun is
/// reloading or empty (their cue to rush). Put it on the player root.
/// </summary>
[DisallowMultipleComponent]
public sealed class KeyTarget : MonoBehaviour
{
    public static KeyTarget Current { get; private set; }

    [SerializeField] Camera view;
    [SerializeField] Sidearm gun;

    public Health Health { get; private set; }
    public Camera View => view;
    /// <summary>The gun in the player's hands right now.</summary>
    public Sidearm Gun => slots != null && slots.Active != null ? slots.Active : gun;
    WeaponSlots slots;
    public CharacterController Body { get; private set; }

    /// <summary>Head height, where the keys aim their eyes.</summary>
    public Vector3 EyePosition => view != null ? view.transform.position : transform.position + Vector3.up * 1.6f;
    public Vector3 Position => transform.position;
    public Vector3 Velocity => Body != null ? Body.velocity : Vector3.zero;
    public bool IsDead => Health != null && Health.IsDead;
    public bool FlashlightOn => Flashlight.Player != null && Flashlight.Player.IsOn;
    /// <summary>Can't shoot right now (reloading, empty, or a key has hold of you): the moment the pack rushes.</summary>
    public bool IsVulnerable => (Gun != null && (Gun.IsReloading || Gun.IsEmpty)) || (closeCombat != null && closeCombat.IsGrabbed);

    PlayerCloseCombat closeCombat;

    void Awake()
    {
        Health = GetComponent<Health>();
        closeCombat = GetComponent<PlayerCloseCombat>();
        Body = GetComponent<CharacterController>();
        if (view == null) view = GetComponentInChildren<Camera>();
        slots = GetComponentInChildren<WeaponSlots>(true);
        if (gun == null) gun = GetComponentInChildren<Sidearm>(true);
    }

    void OnEnable() => Current = this;
    void OnDisable() { if (Current == this) Current = null; }

    /// <summary>True if the player is aiming almost straight at <paramref name="point"/> (within <paramref name="degrees"/>).</summary>
    public bool IsAimingAt(Vector3 point, float degrees)
    {
        if (view == null) return false;
        Vector3 to = point - view.transform.position;
        return Vector3.Angle(view.transform.forward, to) < degrees;
    }
}
