using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// The player's side of getting hurt and dying:
/// - a key's lunge jolts the camera and scratches your leg: you're slowed for a few seconds
///   (the "leg scratched" moment from the black box);
/// - health slowly comes back after a while without being hit, but only part of the way;
/// - on death: controls stop, the black box HUD shows SIGNAL LOST, R restarts.
/// Also combines movement slowdowns (leg scratch × aiming) into the controller's speed.
/// Put it on the player root next to Health and FirstPersonController.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public sealed class PlayerVitals : MonoBehaviour
{
    [SerializeField] float legScratchSlow = .55f;
    [SerializeField] float legScratchSeconds = 2.5f;
    [SerializeField] float regenDelay = 6f;
    [SerializeField] float regenPerSecond = 4f;
    [Tooltip("Regeneration stops at this fraction of max health.")]
    [Range(0, 1)] [SerializeField] float regenCap = .6f;
    [SerializeField] float hurtKickAmount = 2f;
    [SerializeField] float hurtKickDegrees = 3f;

    [Header("Audio")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip[] hurtClips;
    [SerializeField] AudioClip deathClip;

    public Health Health { get; private set; }
    public float LegScratchRemaining { get; private set; }
    public float LastHurtTime { get; private set; } = -999f;
    public bool IsDead => Health.IsDead;

    public event Action<DamageInfo> Hurt;
    public event Action Died;

    FirstPersonController motor;
    KeyTarget keyTarget;
    Sidearm gun => keyTarget != null ? keyTarget.Gun : GetComponentInChildren<Sidearm>();
    CameraKick cameraKick;
    PlayerCloseCombat closeCombat;
    PlayerEaten eaten;
    int hurtCount;
    Vector3 spawnPosition;

    [Tooltip("Safety net: below this height the player is put back at the spawn point.")]
    [SerializeField] float killPlaneY = -20f;

    void Awake()
    {
        Health = GetComponent<Health>();
        closeCombat = GetComponent<PlayerCloseCombat>();
        eaten = GetComponent<PlayerEaten>();
        spawnPosition = transform.position;
        motor = GetComponent<FirstPersonController>();
        keyTarget = GetComponent<KeyTarget>();
        Camera cam = GetComponentInChildren<Camera>();
        if (cam != null) cameraKick = cam.GetComponent<CameraKick>();
    }

    public void ApplyDifficulty(float newRegenCap, float newRegenDelay)
    {
        regenCap = newRegenCap;
        regenDelay = newRegenDelay;
    }

    void OnEnable() { Health.Damaged += OnDamaged; Health.Died += OnDied; }
    void OnDisable() { Health.Damaged -= OnDamaged; Health.Died -= OnDied; }

    void Update()
    {
        if (Health.IsDead)
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            if ((keyboard != null && keyboard.rKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame))
                Restart();
            return;
        }

        if (transform.position.y < killPlaneY)
        {
            // Fell out of the level somehow: put the player back rather than falling forever.
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = spawnPosition;
            if (cc != null) cc.enabled = true;
            Debug.LogWarning("PlayerVitals: player fell out of the level and was returned to the spawn point.", this);
        }

        LegScratchRemaining = Mathf.Max(0f, LegScratchRemaining - Time.deltaTime);
        if (motor != null)
        {
            float leg = LegScratchRemaining > 0f ? legScratchSlow : 1f;
            Sidearm g = gun;
            float aim = g != null ? Mathf.Lerp(1f, g.AimMoveMultiplier, g.AimAmount) : 1f;
            // Pinned by a key, or busy twisting one's head off.
            float held = closeCombat != null && (closeCombat.IsGrabbed || closeCombat.IsKicking) ? 0f : 1f;
            if (eaten != null && (eaten.IsEaten || eaten.IsPulled || eaten.IsDown || eaten.IsPinned)) held = 0f;
            if (eaten != null) leg *= eaten.LegMultiplier;
            motor.SpeedMultiplier = leg * aim * held;
        }

        if (Time.time - LastHurtTime > regenDelay && Health.Fraction < regenCap)
            Health.Heal(Mathf.Min(regenPerSecond * Time.deltaTime, Health.Max * regenCap - Health.Current));
    }

    void OnDamaged(DamageInfo info)
    {
        LastHurtTime = Time.time;
        LegScratchRemaining = legScratchSeconds;
        if (cameraKick != null) cameraKick.Play(hurtKickAmount, hurtKickDegrees, ++hurtCount);
        if (audioSource != null && hurtClips != null && hurtClips.Length > 0)
            audioSource.PlayOneShot(hurtClips[UnityEngine.Random.Range(0, hurtClips.Length)]);
        Hurt?.Invoke(info);
    }

    void OnDied(DamageInfo info)
    {
        if (motor != null) { motor.SpeedMultiplier = 0f; motor.enabled = false; }
        var slots = GetComponentInChildren<WeaponSlots>();
        if (slots != null) slots.enabled = false;
        if (gun != null) gun.enabled = false;
        if (audioSource != null && deathClip != null) audioSource.PlayOneShot(deathClip);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Died?.Invoke();
    }

    void Restart()
    {
        Time.timeScale = 1f;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0)
        {
            SceneManager.LoadScene(scene.buildIndex);
            return;
        }
#if UNITY_EDITOR
        // Not in Build Settings (e.g. a test scene): reload it from its asset path in the Editor.
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
    }
}
