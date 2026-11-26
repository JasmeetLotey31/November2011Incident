using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Helmet flashlight (F / D-pad up). You need it to see in the dark station, but keys spot a
/// lit player from much further away: the survival trade-off. Flickers when you're badly hurt.
/// Put it on a Spot Light under the camera.
/// </summary>
[RequireComponent(typeof(Light))]
public sealed class Flashlight : MonoBehaviour
{
    [SerializeField] bool startOn = true;
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip toggleClip;
    [Tooltip("Optional: flicker when this health is low.")]
    [SerializeField] Health playerHealth;
    [Range(0, 1)] [SerializeField] float flickerBelowHealth = .35f;

    /// <summary>The flashlight the keys check when deciding how far they can see you.</summary>
    public static Flashlight Player { get; private set; }

    public bool IsOn => lightSource.enabled;

    Light lightSource;
    float baseIntensity;
    InputAction toggleAction;

    void Awake()
    {
        lightSource = GetComponent<Light>();
        baseIntensity = lightSource.intensity;
        lightSource.enabled = startOn;
        if (playerHealth == null) playerHealth = GetComponentInParent<Health>();
        toggleAction = new InputAction("Flashlight", InputActionType.Button);
        toggleAction.AddBinding("<Keyboard>/f");
        toggleAction.AddBinding("<Gamepad>/dpad/up");
    }

    void OnEnable() { Player = this; toggleAction.Enable(); }
    void OnDisable() { if (Player == this) Player = null; toggleAction.Disable(); }
    void OnDestroy() => toggleAction.Dispose();

    void Update()
    {
        if (toggleAction.WasPressedThisFrame())
        {
            lightSource.enabled = !lightSource.enabled;
            if (audioSource != null && toggleClip != null) audioSource.PlayOneShot(toggleClip, .6f);
        }
        if (!lightSource.enabled) return;

        float intensity = baseIntensity;
        if (playerHealth != null && playerHealth.Fraction < flickerBelowHealth && !playerHealth.IsDead)
        {
            // Hurt: the light stutters, worse the lower your health.
            float severity = 1f - playerHealth.Fraction / flickerBelowHealth;
            float n = Mathf.PerlinNoise(Time.time * 14f, 0f);
            if (n < .25f * severity) intensity *= .15f;
        }
        lightSource.intensity = intensity;
    }
}
