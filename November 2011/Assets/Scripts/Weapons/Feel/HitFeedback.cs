using System.Collections;
using UnityEngine;

/// <summary>
/// Hit feel for the player's own shots, scaled by what the hit did:
/// - Bent: a small metal "tink" that rises in pitch as the target's bend meter fills;
/// - Jammed: a heavier clack, a short hitstop and a bigger camera punch;
/// - Pinned: the heaviest version of all three.
/// Put it on the gun (same object as NailGun). The HUD hit marker listens to the same event.
/// </summary>
[DisallowMultipleComponent]
public sealed class HitFeedback : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip bentClip;
    [SerializeField] AudioClip jammedClip;
    [SerializeField] AudioClip pinnedClip;
    [Range(0, 1)] [SerializeField] float volume = .9f;
    [Tooltip("Pitch of the 'bent' tink at an empty and an almost-full meter.")]
    [SerializeField] Vector2 bentPitchRange = new Vector2(1f, 1.35f);
    [SerializeField] float jammedPitch = .9f;
    [SerializeField] float pinnedPitch = .7f;

    [Header("Hitstop (real seconds the game nearly freezes)")]
    [SerializeField] float jamHitstop = .05f;
    [SerializeField] float pinHitstop = .08f;
    [Range(0, .5f)] [SerializeField] float hitstopTimeScale = .03f;

    [Header("Camera punch")]
    [SerializeField] float jamPunchAmount = 1.2f;
    [SerializeField] float jamPunchDegrees = 1.8f;
    [SerializeField] float pinPunchAmount = 1.6f;
    [SerializeField] float pinPunchDegrees = 2.6f;

    CameraKick cameraKick;
    GameObject owner;
    Coroutine hitstopRoutine;
    float hitstopEndsAt;
    float timeScaleBeforeHitstop = 1f;
    int punchCount;

    void Awake()
    {
        owner = transform.root.gameObject;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        Camera cam = Camera.main;
        if (cam != null) cameraKick = cam.GetComponent<CameraKick>();
    }

    void OnEnable() => CombatFeedback.TargetHit += OnTargetHit;

    void OnDisable()
    {
        CombatFeedback.TargetHit -= OnTargetHit;
        EndHitstop();
    }

    void OnTargetHit(BendHit hit, BendOutcome outcome, IBendable target)
    {
        if (hit.source != owner) return;

        switch (outcome)
        {
            case BendOutcome.Bent:
            case BendOutcome.None:
                float fill = target is Bendable b ? b.Bend01 : 0f;
                Play(bentClip, Mathf.Lerp(bentPitchRange.x, bentPitchRange.y, fill), outcome == BendOutcome.None ? .6f : 1f);
                break;
            case BendOutcome.Jammed:
                Play(jammedClip, jammedPitch, 1f);
                Hitstop(jamHitstop);
                Punch(jamPunchAmount, jamPunchDegrees);
                break;
            case BendOutcome.Pinned:
                Play(pinnedClip, pinnedPitch, 1f);
                Play(jammedClip, jammedPitch * .8f, .7f); // layered: clack + deep thunk
                Hitstop(pinHitstop);
                Punch(pinPunchAmount, pinPunchDegrees);
                break;
        }
    }

    void Play(AudioClip clip, float pitch, float volumeScale)
    {
        if (clip == null || audioSource == null) return;
        // PlayOneShot uses the source's pitch at the time of the call.
        float previous = audioSource.pitch;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume * volumeScale);
        audioSource.pitch = previous;
    }

    void Punch(float amount, float degrees)
    {
        if (cameraKick != null) cameraKick.Play(amount, degrees, ++punchCount);
    }

    void Hitstop(float seconds)
    {
        if (seconds <= 0f) return;
        float end = Time.unscaledTime + seconds;
        if (hitstopRoutine != null)
        {
            hitstopEndsAt = Mathf.Max(hitstopEndsAt, end); // extend, don't stack
            return;
        }
        timeScaleBeforeHitstop = Time.timeScale;
        if (timeScaleBeforeHitstop <= 0f) return; // paused: leave it alone
        hitstopEndsAt = end;
        hitstopRoutine = StartCoroutine(HitstopRoutine());
    }

    IEnumerator HitstopRoutine()
    {
        Time.timeScale = hitstopTimeScale * timeScaleBeforeHitstop;
        while (Time.unscaledTime < hitstopEndsAt)
            yield return null;
        hitstopRoutine = null;
        Time.timeScale = timeScaleBeforeHitstop;
    }

    /// <summary>Cuts a running hitstop short (e.g. the gun is disabled mid-freeze).</summary>
    void EndHitstop()
    {
        if (hitstopRoutine == null) return;
        StopCoroutine(hitstopRoutine);
        hitstopRoutine = null;
        Time.timeScale = timeScaleBeforeHitstop;
    }
}
