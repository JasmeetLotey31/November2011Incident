using System.Collections;
using UnityEngine;

/// <summary>
/// The station's own voice through the fight: the alarm klaxon when a wave comes, the all-clear chime
/// when it's beaten, radio squelch round every radio line, and the power dying in a blackout.
/// Listens to the KeyHordeDirector.
/// </summary>
public sealed class WaveAudio : MonoBehaviour
{
    [SerializeField] AudioClip klaxon;
    [SerializeField] AudioClip allClear;
    [SerializeField] AudioClip radioIn;
    [SerializeField] AudioClip radioOut;
    [SerializeField] AudioClip powerDown;
    [Range(0, 1)] [SerializeField] float klaxonVolume = .5f;
    [Range(0, 1)] [SerializeField] float chimeVolume = .45f;
    [Range(0, 1)] [SerializeField] float radioVolume = .4f;
    [Range(0, 1)] [SerializeField] float powerVolume = .8f;
    [Tooltip("How long a radio line stays up (matches the HUD), when the squelch closes.")]
    [SerializeField] float radioSeconds = 4.6f;

    KeyHordeDirector director;
    AudioSource source;
    Coroutine closing;

    void Start()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false; source.spatialBlend = 0f; source.priority = 16;
        director = Object.FindAnyObjectByType<KeyHordeDirector>();
        if (director == null) return;
        director.WaveStarted += OnWave;
        director.WaveCleared += OnCleared;
        director.Radio += OnRadio;
        director.BlackoutStarted += OnBlackout;
    }

    void OnDestroy()
    {
        if (director == null) return;
        director.WaveStarted -= OnWave;
        director.WaveCleared -= OnCleared;
        director.Radio -= OnRadio;
        director.BlackoutStarted -= OnBlackout;
    }

    void OnWave(int wave) => Play(klaxon, klaxonVolume);
    void OnCleared(int wave) => Play(allClear, chimeVolume);
    void OnBlackout() => Play(powerDown, powerVolume);

    void OnRadio(string line)
    {
        Play(radioIn, radioVolume);
        if (closing != null) StopCoroutine(closing);
        closing = StartCoroutine(CloseRadio());
    }

    IEnumerator CloseRadio()
    {
        yield return new WaitForSeconds(radioSeconds);
        Play(radioOut, radioVolume);
        closing = null;
    }

    void Play(AudioClip clip, float volume)
    {
        if (clip != null && source != null) source.PlayOneShot(clip, volume);
    }
}
