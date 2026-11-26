using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes shooting a key physical:
/// - body hits snap off a piece (a key tooth, a fang, a claw) that flies away with the shot;
/// - a face shot bursts one of its glowing eyes (sparks, the eye goes dark);
/// - on death the whole key falls apart into clattering brass pieces.
/// Pieces are real physics debris that clean themselves up after a while.
/// Put it on the KeyAlien root; assign the parts that may snap off and the eye renderers.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class KeyBreakable : MonoBehaviour
{
    [Tooltip("Pieces that can snap off when the body is hit (teeth, fangs, claws).")]
    [SerializeField] Transform[] snappableParts;
    [Tooltip("Eye renderers; a face shot bursts one.")]
    [SerializeField] Renderer[] eyes;
    [Tooltip("Everything visual that should fall apart on death (usually the Visual root).")]
    [SerializeField] Transform visualRoot;
    [Tooltip("Head: pops off whole on a face kill.")]
    [SerializeField] Transform head;
    [Range(0, 1)] [SerializeField] float snapChance = .6f;
    [SerializeField] float shotForce = 4f;
    [SerializeField] float deathForce = 3.5f;
    [SerializeField] float debrisLifetime = 10f;
    [SerializeField] AudioClip[] clatterClips;

    Health health;
    readonly List<Transform> remaining = new List<Transform>();
    readonly List<Renderer> liveEyes = new List<Renderer>();

    /// <summary>Eyes still glowing (a key with no eyes left is half blind).</summary>
    public int EyesLeft => liveEyes.Count;

    void Awake()
    {
        health = GetComponent<Health>();
        if (snappableParts != null) foreach (Transform t in snappableParts) if (t != null) remaining.Add(t);
        if (eyes != null) foreach (Renderer r in eyes) if (r != null) liveEyes.Add(r);
    }

    void OnEnable() { health.Damaged += OnDamaged; health.Died += OnDied; }
    void OnDisable() { health.Damaged -= OnDamaged; health.Died -= OnDied; }

    void OnDamaged(DamageInfo info)
    {
        if (health.IsDead) return; // death handles everything
        if (info.weakSpot) BurstEye(info);
        else if (remaining.Count > 0 && Random.value < snapChance)
        {
            // Snap the piece nearest to where the bullet hit.
            Transform nearest = null;
            float best = float.PositiveInfinity;
            foreach (Transform t in remaining)
            {
                float d = (t.position - info.point).sqrMagnitude;
                if (d < best) { best = d; nearest = t; }
            }
            remaining.Remove(nearest);
            Detach(nearest, info.direction * shotForce + Random.insideUnitSphere * 1.5f + Vector3.up * 1.5f);
            Clatter(info.point, .7f);
        }
    }

    void BurstEye(DamageInfo info)
    {
        if (liveEyes.Count == 0) return;
        Renderer eye = liveEyes[0];
        float best = float.PositiveInfinity;
        foreach (Renderer r in liveEyes)
        {
            float d = (r.transform.position - info.point).sqrMagnitude;
            if (d < best) { best = d; eye = r; }
        }
        liveEyes.Remove(eye);
        eye.enabled = false;
        // A burst of sparks right at the eye.
        ImpactVFX.PlaySparks(eye.transform.position, -info.direction, 2f);
        Clatter(eye.transform.position, .9f);
    }

    void OnDied(DamageInfo info)
    {
        if (visualRoot == null) return;
        // Killed through the face (shot or finisher): the head pops off whole, spinning up and away.
        if (info.weakSpot && head != null)
        {
            Detach(head, info.direction * 3f + Vector3.up * 5.5f + Random.insideUnitSphere);
            Rigidbody rb = head.GetComponent<Rigidbody>();
            if (rb != null) rb.angularVelocity = Random.onUnitSphere * 22f;
            ImpactVFX.PlaySparks(head.position, -info.direction, 2.5f);
        }
        // Every rendered piece becomes loose debris, blown away from the shot.
        Vector3 centre = visualRoot.position + Vector3.up * 1f;
        var pieces = new List<Renderer>(visualRoot.GetComponentsInChildren<Renderer>());
        foreach (Renderer r in pieces)
        {
            if (r == null || !r.enabled) continue;
            Vector3 away = (r.transform.position - centre).normalized;
            Detach(r.transform, info.direction * deathForce + away * 2f + Vector3.up * 1.2f + Random.insideUnitSphere);
        }
        Clatter(centre, 1f);
        Invoke(nameof(SecondClatter), .35f);
    }

    void SecondClatter() => Clatter(transform.position, .8f);

    void Detach(Transform part, Vector3 velocity)
    {
        if (part == null) return;
        part.SetParent(null, true);
        Vector3 scale = part.lossyScale;
        part.localScale = scale;
        // Debris isn't shootable: drop any hitbox (which requires its collider) before the colliders.
        foreach (Hitbox h in part.GetComponentsInChildren<Hitbox>()) DestroyImmediate(h);
        foreach (Collider c in part.GetComponentsInChildren<Collider>()) Destroy(c);
        var col = part.gameObject.AddComponent<BoxCollider>();
        if (part.GetComponent<Renderer>() != null) col.size = Vector3.one; // a scaled primitive
        else
        {
            // A group (e.g. the whole head): fit the box around its pieces.
            Bounds b = new Bounds(part.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in part.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            Vector3 s = part.lossyScale;
            col.center = part.InverseTransformPoint(b.center);
            col.size = any ? new Vector3(b.size.x / Mathf.Max(.001f, s.x), b.size.y / Mathf.Max(.001f, s.y), b.size.z / Mathf.Max(.001f, s.z)) : Vector3.one * .3f;
        }
        // Debris bounces off the level but never shoves the player around.
        KeyTarget player = KeyTarget.Current;
        if (player != null && player.Body != null) Physics.IgnoreCollision(col, player.Body);
        var rb = part.gameObject.AddComponent<Rigidbody>();
        rb.mass = .4f;
        rb.linearDamping = .4f;
        rb.angularDamping = .3f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = velocity;
        rb.angularVelocity = Random.insideUnitSphere * 12f;
        part.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); // don't block shots or sight
        Destroy(part.gameObject, debrisLifetime + Random.value * 2f);
    }

    void Clatter(Vector3 point, float volume)
    {
        if (clatterClips == null || clatterClips.Length == 0) return;
        AudioSource.PlayClipAtPoint(clatterClips[Random.Range(0, clatterClips.Length)], point, volume);
    }
}
