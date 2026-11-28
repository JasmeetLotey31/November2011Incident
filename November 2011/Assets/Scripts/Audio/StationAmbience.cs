using UnityEngine;

/// <summary>
/// The station breathing around you: a looping room tone (air handlers, mains hum) and, now and then,
/// the hull groaning or a sheet of metal ticking somewhere along the walls (placed in 3D, so you hear
/// where). When the keys cut the power the electrical hum sags out of the room tone.
/// </summary>
public sealed class StationAmbience : MonoBehaviour
{
    [SerializeField] AudioClip roomTone;
    [Range(0, 1)] [SerializeField] float roomVolume = .3f;
    [SerializeField] AudioClip[] groans;
    [SerializeField] AudioClip[] ticks;
    [Tooltip("Seconds between groans (random between).")]
    [SerializeField] Vector2 groanEvery = new Vector2(10f, 24f);
    [SerializeField] Vector2 tickEvery = new Vector2(3f, 8f);
    [Tooltip("The room these sounds come from (centre, half size).")]
    [SerializeField] Vector3 roomCentre = new Vector3(0f, 2f, 0f);
    [SerializeField] Vector3 roomHalfSize = new Vector3(11f, 2f, 9f);

    AudioSource bed;
    float nextGroan, nextTick;

    void Start()
    {
        bed = gameObject.AddComponent<AudioSource>();
        bed.clip = roomTone; bed.loop = true; bed.playOnAwake = false;
        bed.spatialBlend = 0f; bed.volume = roomVolume; bed.priority = 32;
        if (roomTone != null) bed.Play();
        nextGroan = Time.time + Random.Range(4f, groanEvery.x);
        nextTick = Time.time + Random.Range(1f, tickEvery.y);
    }

    void Update()
    {
        // Power cut: the hum dies and the air slows; it comes back with the lights.
        bool dark = EmergencyLight.IsBlackout;
        bed.volume = Mathf.MoveTowards(bed.volume, roomVolume * (dark ? .45f : 1f), Time.deltaTime * .5f);
        bed.pitch = Mathf.MoveTowards(bed.pitch, dark ? .9f : 1f, Time.deltaTime * .2f);

        if (Time.time >= nextGroan)
        {
            nextGroan = Time.time + Random.Range(groanEvery.x, groanEvery.y);
            PlayAt(groans, OnTheShell(), Random.Range(.55f, .9f), Random.Range(.85f, 1.1f), 6f, 45f);
        }
        if (Time.time >= nextTick)
        {
            nextTick = Time.time + Random.Range(tickEvery.x, tickEvery.y);
            PlayAt(ticks, OnTheShell(), Random.Range(.3f, .55f), Random.Range(.9f, 1.15f), 2f, 22f);
        }
    }

    /// <summary>A random point on the room's walls or ceiling.</summary>
    Vector3 OnTheShell()
    {
        Vector3 p = new Vector3(Random.Range(-1f, 1f), Random.Range(-.2f, 1f), Random.Range(-1f, 1f));
        int axis = Random.Range(0, 3);
        if (axis == 0) p.x = Mathf.Sign(p.x); else if (axis == 1) p.y = 1f; else p.z = Mathf.Sign(p.z);
        return roomCentre + Vector3.Scale(p, roomHalfSize);
    }

    static void PlayAt(AudioClip[] clips, Vector3 at, float volume, float pitch, float near, float far)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        var go = new GameObject("Station Sound");
        go.transform.position = at;
        var s = go.AddComponent<AudioSource>();
        s.clip = clip; s.volume = volume; s.pitch = pitch;
        s.spatialBlend = 1f; s.minDistance = near; s.maxDistance = far; s.rolloffMode = AudioRolloffMode.Logarithmic;
        s.dopplerLevel = 0f;
        s.Play();
        Destroy(go, clip.length / pitch + .1f);
    }
}
