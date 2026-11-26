using System;
using UnityEngine;

/// <summary>Where a hit landed. Keys react differently to each (Resident Evil style).</summary>
public enum BodyPart { Body, Head, Leg, Arm, Tongue, Throat }

/// <summary>One hit of damage: how much, where, from what direction, and which body part.</summary>
public struct DamageInfo
{
    public float amount;
    public Vector3 point;
    public Vector3 direction;
    public Vector3 normal;
    public GameObject source;
    /// <summary>Hit a weak spot (a key's face/eyes). Already multiplied into <see cref="amount"/>.</summary>
    public bool weakSpot;
    public BodyPart part;
    /// <summary>Extra shove from the weapon (shotgun blasts, kicks). 0 = just the normal flinch.</summary>
    public float knockback;
    /// <summary>A melee kick (counts for bigger loot on a kill).</summary>
    public bool melee;
}

/// <summary>Something the player can kick while it's staggered (a dazed walker, a key stuck in the floor).</summary>
public interface IKickable
{
    /// <summary>Staggered right now: a kick lands as a big roundhouse.</summary>
    bool IsKickable { get; }
    bool IsAlive { get; }
    Transform Root { get; }
    /// <summary>Hit by a kick or bash: damage plus a push of <paramref name="knockback"/> m/s.</summary>
    void TakeMelee(DamageInfo info);
}

/// <summary>Everything kickable that's alive in the scene.</summary>
public static class Kickables
{
    static readonly System.Collections.Generic.List<IKickable> all = new System.Collections.Generic.List<IKickable>();
    public static System.Collections.Generic.IReadOnlyList<IKickable> All => all;
    public static void Register(IKickable k) { if (!all.Contains(k)) all.Add(k); }
    public static void Unregister(IKickable k) => all.Remove(k);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();
}

public enum DamageOutcome
{
    /// <summary>Ignored (already dead, invulnerable).</summary>
    None,
    Hit,
    Killed,
}

/// <summary>
/// Every successful hit on anything with <see cref="Health"/>, for feedback (hit marker, hit sounds,
/// hitstop). Listeners filter by <c>info.source</c> to react only to their own shots.
/// </summary>
public static class CombatFeedback
{
    public static event Action<DamageInfo, DamageOutcome, Health> Damaged;

    public static void Report(DamageInfo info, DamageOutcome outcome, Health target) =>
        Damaged?.Invoke(info, outcome, target);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Damaged = null;
}
