using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Hit points for anything that can be hurt (the player, the keys). Damage arrives through
/// <see cref="Hitbox"/> colliders, which apply weak-spot multipliers first.
/// </summary>
[DisallowMultipleComponent]
public sealed class Health : MonoBehaviour
{
    [SerializeField] float maxHealth = 100f;
    [Tooltip("Ignore all damage (cutscenes, spawning in).")]
    public bool invulnerable;

    [Header("Events")]
    public UnityEvent onDied;

    public float Max => maxHealth;
    public float Current { get; private set; }
    public float Fraction => maxHealth > 0f ? Current / maxHealth : 0f;
    public bool IsDead { get; private set; }
    public DamageInfo LastDamage { get; private set; }

    public event Action<DamageInfo> Damaged;
    public event Action<DamageInfo> Died;
    public event Action Healed;

    void Awake() => Current = maxHealth;

    public DamageOutcome TakeDamage(DamageInfo info)
    {
        if (IsDead || invulnerable || info.amount <= 0f) return DamageOutcome.None;

        Current = Mathf.Max(0f, Current - info.amount);
        LastDamage = info;
        Damaged?.Invoke(info);

        if (Current > 0f) return DamageOutcome.Hit;
        IsDead = true;
        Died?.Invoke(info);
        onDied.Invoke();
        return DamageOutcome.Killed;
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Current = Mathf.Min(maxHealth, Current + amount);
        Healed?.Invoke();
    }

    /// <summary>Changes max health (a bite taking a chunk out of you, a key growing from feeding).</summary>
    public void SetMax(float newMax, bool keepFraction = false)
    {
        float fraction = Fraction;
        maxHealth = Mathf.Max(1f, newMax);
        Current = keepFraction ? maxHealth * fraction : Mathf.Min(Current, maxHealth);
        Healed?.Invoke();
    }

    /// <summary>Sets health directly, e.g. a key returning from a vent partly healed. Doesn't revive the dead.</summary>
    public void SetCurrent(float value)
    {
        if (IsDead) return;
        Current = Mathf.Clamp(value, 1f, maxHealth);
        Healed?.Invoke();
    }

    /// <summary>Back to full and alive (respawn / restart).</summary>
    public void ResetHealth()
    {
        IsDead = false;
        Current = maxHealth;
        Healed?.Invoke();
    }
}
