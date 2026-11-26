using System.Collections;
using UnityEngine;

/// <summary>
/// Hit feel for the player's own shots:
/// - hit: a dull impact tick;
/// - weak spot (a key's face): a sharp, higher crack;
/// - kill: a heavy clack, a short hitstop and a camera punch.
/// Put it on the gun. The HUD hit marker listens to the same event.
/// </summary>
[DisallowMultipleComponent]
public sealed class HitFeedback : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip hitClip;
    [SerializeField] AudioClip weakSpotClip;
    [SerializeField] AudioClip killClip;
    [Range(0, 1)] [SerializeField] float volume = .9f;

    [Header("Kill")]
    [Tooltip("Real seconds the game nearly freezes on a kill.")]
    [SerializeField] float killHitstop = .045f;
    [SerializeField] float weakSpotKillHitstop = .07f;
    [Range(0, .5f)] [SerializeField] float hitstopTimeScale = .05f;
    [SerializeField] float killPunchAmount = 1.1f;
    [SerializeField] float killPunchDegrees = 1.4f;

    CameraKick cameraKick;
    GameObject owner;
    Coroutine hitstopRoutine;
    float hitstopEndsAt, timeScaleBefore = 1f;
    int punchCount;

    void Awake()
    {
        owner = transform.root.gameObject;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        Camera cam = Camera.main;
        if (cam != null) cameraKick = cam.GetComponent<CameraKick>();
    }

    void OnEnable() => CombatFeedback.Damaged += OnDamaged;

    void OnDisable()
    {
        CombatFeedback.Damaged -= OnDamaged;
        if (hitstopRoutine != null) { StopCoroutine(hitstopRoutine); hitstopRoutine = null; Time.timeScale = timeScaleBefore; }
    }

    void OnDamaged(DamageInfo info, DamageOutcome outcome, Health target)
    {
        if (info.source != owner) return;

        if (outcome == DamageOutcome.Killed)
        {
            Play(killClip, info.weakSpot ? .85f : 1f, 1f);
            Hitstop(info.weakSpot ? weakSpotKillHitstop : killHitstop);
            if (cameraKick != null) cameraKick.Play(killPunchAmount, killPunchDegrees, ++punchCount);
        }
        else if (info.weakSpot) Play(weakSpotClip, 1.25f, 1f);
        else Play(hitClip, 1f, .8f);
    }

    void Play(AudioClip clip, float pitch, float volumeScale)
    {
        if (clip == null || audioSource == null) return;
        float previous = audioSource.pitch;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume * volumeScale);
        audioSource.pitch = previous;
    }

    void Hitstop(float seconds)
    {
        if (seconds <= 0f) return;
        float end = Time.unscaledTime + seconds;
        if (hitstopRoutine != null) { hitstopEndsAt = Mathf.Max(hitstopEndsAt, end); return; }
        timeScaleBefore = Time.timeScale;
        if (timeScaleBefore <= 0f) return;
        hitstopEndsAt = end;
        hitstopRoutine = StartCoroutine(HitstopRoutine());
    }

    IEnumerator HitstopRoutine()
    {
        Time.timeScale = hitstopTimeScale * timeScaleBefore;
        while (Time.unscaledTime < hitstopEndsAt) yield return null;
        hitstopRoutine = null;
        Time.timeScale = timeScaleBefore;
    }
}
