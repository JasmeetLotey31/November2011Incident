using UnityEngine;

/// <summary>
/// Boots on the deck plates: a step every stride while you move on the ground (longer, louder
/// strides when sprinting), a heavier one when you land. Quiet enough to sit under the fight.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] AudioClip[] steps;
    [Tooltip("Metres between steps walking / sprinting.")]
    [SerializeField] float stride = 1.6f;
    [SerializeField] float sprintStride = 2.3f;
    [Tooltip("Above this speed (m/s) it counts as sprinting.")]
    [SerializeField] float sprintSpeed = 5.5f;
    [Range(0, 1)] [SerializeField] float volume = .35f;

    CharacterController controller;
    AudioSource source;
    float travelled;
    bool wasGrounded = true;
    int last = -1;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false; source.spatialBlend = 0f; source.priority = 64;
    }

    void Update()
    {
        if (!controller.isGrounded) { wasGrounded = false; return; }
        Vector3 v = controller.velocity; v.y = 0f;
        float speed = v.magnitude;
        if (!wasGrounded) { wasGrounded = true; Step(1.3f); travelled = 0f; return; }   // landing
        if (speed < .3f) { travelled = stride * .6f; return; }                           // the first step comes quickly
        travelled += speed * Time.deltaTime;
        float s = speed > sprintSpeed ? sprintStride : stride;
        if (travelled >= s) { travelled -= s; Step(speed > sprintSpeed ? 1.15f : .85f); }
    }

    void Step(float weight)
    {
        if (steps == null || steps.Length == 0) return;
        int i = Random.Range(0, steps.Length);
        if (i == last && steps.Length > 1) i = (i + 1) % steps.Length;      // never the same boot twice
        last = i;
        source.pitch = Random.Range(.92f, 1.08f);
        source.PlayOneShot(steps[i], volume * weight);
    }
}
