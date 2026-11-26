using System.Collections;
using UnityEngine;

/// <summary>
/// An air vent the keys crawl out of (and flee back into). The grate rattles before one emerges,
/// which is your warning of where the next one is coming from.
/// </summary>
[DisallowMultipleComponent]
public sealed class KeyVent : MonoBehaviour
{
    [Tooltip("Point on the walkable floor in front of the vent where keys appear.")]
    [SerializeField] Transform exitPoint;
    [Tooltip("Grate that shakes when something comes through.")]
    [SerializeField] Transform grate;
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip rattleClip;
    [Tooltip("Seconds of rattling before the key comes out.")]
    [SerializeField] float warningSeconds = .9f;

    public Vector3 ExitPosition => exitPoint != null ? exitPoint.position : transform.position + transform.forward;
    public float WarningSeconds => warningSeconds;

    Vector3 grateRest;

    void Awake() { if (grate != null) grateRest = grate.localPosition; }

    /// <summary>Rattles the grate, then calls <paramref name="onOpen"/>.</summary>
    public void Open(System.Action onOpen) => StartCoroutine(OpenRoutine(onOpen));

    IEnumerator OpenRoutine(System.Action onOpen)
    {
        if (audioSource != null && rattleClip != null)
        {
            audioSource.pitch = Random.Range(.8f, 1.05f);
            audioSource.PlayOneShot(rattleClip);
        }
        for (float t = 0f; t < warningSeconds; t += Time.deltaTime)
        {
            if (grate != null) grate.localPosition = grateRest + Random.insideUnitSphere * .02f;
            yield return null;
        }
        if (grate != null) grate.localPosition = grateRest;
        onOpen?.Invoke();
    }

    /// <summary>Is this vent on screen for <paramref name="cam"/>? (the director prefers vents behind you)</summary>
    public bool IsInView(Camera cam)
    {
        if (cam == null) return false;
        Vector3 v = cam.WorldToViewportPoint(ExitPosition + Vector3.up);
        return v.z > 0f && v.x > -.1f && v.x < 1.1f && v.y > -.1f && v.y < 1.1f;
    }
}
