using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A fired nail. Flies with a little drop, bends whatever it hits, and pins the target if there is
/// a wall right behind it. It then stays stuck where it landed and can be pulled back out
/// (its <see cref="NailPickup"/> turns on once it's stuck).
/// </summary>
public class NailProjectile : MonoBehaviour
{
    [Tooltip("Downward acceleration in m/s². Lower than real gravity so the drop only shows at medium range.")]
    [SerializeField] float gravity = 6f;
    [SerializeField] float lifetime = 3f;
    [Tooltip("How far the nail sinks into what it hits.")]
    [SerializeField] float embedDepth = 0.03f;
    [Tooltip("Noise radius of the nail hitting something (the gun itself is louder).")]
    [SerializeField] float impactNoiseRadius = 4f;
    [Tooltip("Enabled once the nail is stuck so the player can pull it back out.")]
    [SerializeField] NailPickup pickup;
    [Tooltip("One is picked at random when the nail lands.")]
    [SerializeField] AudioClip[] impactClips;
    [Range(0, 1)] [SerializeField] float impactVolume = .6f;

    /// <summary>Oldest stuck nails are cleaned up past this count.</summary>
    const int MaxStuckNails = 60;
    static readonly Queue<NailProjectile> stuckNails = new Queue<NailProjectile>();
    static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    Vector3 velocity;
    float bendAmount;
    float pinReach;
    LayerMask hitMask;
    Transform owner;
    float age;
    bool stuck;

    public bool IsStuck => stuck;

    void Awake()
    {
        if (pickup != null) pickup.SetCollectable(false);
    }

    /// <param name="owner">Root of whoever fired it; the nail passes through its colliders.</param>
    public void Launch(Vector3 velocity, float bendAmount, float pinReach, LayerMask hitMask, Transform owner)
    {
        this.velocity = velocity;
        this.bendAmount = bendAmount;
        this.pinReach = pinReach;
        this.hitMask = hitMask;
        this.owner = owner;
        transform.rotation = Quaternion.LookRotation(velocity);
    }

    void Update()
    {
        if (stuck) return;

        age += Time.deltaTime;
        if (age > lifetime)
        {
            Destroy(gameObject);
            return;
        }

        velocity += Vector3.down * (gravity * Time.deltaTime);
        Vector3 step = velocity * Time.deltaTime;

        // Ray from the old position to the new one, so fast nails can't skip through thin walls.
        if (CastFirst(transform.position, step, owner, null, out RaycastHit hit))
            Impact(hit);
        else
        {
            transform.position += step;
            if (velocity.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(velocity);
        }
    }

    void Impact(RaycastHit hit)
    {
        Vector3 direction = velocity.normalized;
        IBendable target = hit.collider.GetComponentInParent<IBendable>();
        Transform stickTo = null;

        if (target != null)
        {
            Transform targetRoot = ((Component)target).transform;
            var bend = new BendHit
            {
                amount = bendAmount,
                point = hit.point,
                direction = direction,
                source = owner != null ? owner.gameObject : null,
            };
            // A wall right behind the target means the nail goes through and pins it there.
            if (CastFirst(hit.point, direction * pinReach, owner, targetRoot, out RaycastHit wall) &&
                wall.collider.GetComponentInParent<IBendable>() == null)
            {
                bend.pinned = true;
                bend.pinPoint = wall.point;
            }
            target.ApplyBend(bend);
            stickTo = targetRoot; // follow the target when it moves
        }

        Stick(hit.point + direction * embedDepth, direction, stickTo);
        NailImpactVFX.Play(hit, target != null);
        if (impactClips != null && impactClips.Length > 0)
        {
            AudioClip clip = impactClips[Random.Range(0, impactClips.Length)];
            if (clip != null) AudioSource.PlayClipAtPoint(clip, hit.point, impactVolume);
        }
        Noise.Emit(hit.point, impactNoiseRadius, gameObject);
    }

    void Stick(Vector3 position, Vector3 direction, Transform parent)
    {
        stuck = true;
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        // Only parent to moving targets; parenting to scaled level geometry would skew the nail.
        if (parent != null) transform.SetParent(parent, true);
        if (pickup != null) pickup.SetCollectable(true);

        stuckNails.Enqueue(this);
        while (stuckNails.Count > MaxStuckNails)
        {
            NailProjectile oldest = stuckNails.Dequeue();
            if (oldest != null) Destroy(oldest.gameObject);
        }
    }

    /// <summary>Closest solid hit along <paramref name="ray"/>, skipping the shooter and (optionally) one target.</summary>
    bool CastFirst(Vector3 origin, Vector3 ray, Transform skipA, Transform skipB, out RaycastHit best)
    {
        best = default;
        float distance = ray.magnitude;
        if (distance < 0.0001f) return false;

        int count = Physics.RaycastNonAlloc(origin, ray / distance, hitBuffer, distance, hitMask,
            QueryTriggerInteraction.Ignore);
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Transform t = hitBuffer[i].collider.transform;
            if (skipA != null && t.IsChildOf(skipA)) continue;
            if (skipB != null && t.IsChildOf(skipB)) continue;
            if (hitBuffer[i].distance < bestDistance)
            {
                bestDistance = hitBuffer[i].distance;
                best = hitBuffer[i];
            }
        }
        return bestDistance < float.PositiveInfinity;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => stuckNails.Clear();
}
