using UnityEngine;

/// <summary>
/// A damageable part (head, leg, arm, body). Put it on a collider under something with
/// <see cref="Health"/>. Weapons call <see cref="Hit"/>; the multiplier and body part are applied
/// here, so the creature can react to where it was shot (head: dazed, leg: drops to a knee...).
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class Hitbox : MonoBehaviour
{
    [Tooltip("Damage multiplier for this part (e.g. 2 for a key's face).")]
    [SerializeField] float multiplier = 1f;
    [Tooltip("Counts as a weak spot for feedback (red hit marker, special sound).")]
    [SerializeField] bool weakSpot;
    [SerializeField] BodyPart part = BodyPart.Body;
    [SerializeField] Health owner;

    public Health Owner => owner != null ? owner : owner = GetComponentInParent<Health>();
    public bool IsWeakSpot => weakSpot;
    public BodyPart Part => part;

    /// <summary>Applies damage to the owner and reports it for hit feedback.</summary>
    public DamageOutcome Hit(DamageInfo info)
    {
        Health target = Owner;
        if (target == null) return DamageOutcome.None;
        info.amount *= multiplier;
        info.weakSpot = weakSpot;
        info.part = part;
        DamageOutcome outcome = target.TakeDamage(info);
        if (outcome != DamageOutcome.None) CombatFeedback.Report(info, outcome, target);
        return outcome;
    }
}
