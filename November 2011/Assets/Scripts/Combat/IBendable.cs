using System;
using UnityEngine;

/// <summary>
/// One hit against something that can be bent and jammed (the keys, test dummies).
/// Every weapon (wrench, nail gun, shotgun) reports its hits this way.
/// </summary>
public struct BendHit
{
    /// <summary>How much of the target's bend meter this hit fills (1 = a full meter).</summary>
    public float amount;
    public Vector3 point;
    public Vector3 direction;
    /// <summary>True when the hit pins the target against a wall (nail gun).</summary>
    public bool pinned;
    /// <summary>Where on the wall the target is pinned. Only meaningful when <see cref="pinned"/> is true.</summary>
    public Vector3 pinPoint;
    public GameObject source;
}

/// <summary>What a hit did, so feedback can scale with it.</summary>
public enum BendOutcome
{
    /// <summary>Hit landed but changed nothing (e.g. already jammed and not pinned).</summary>
    None,
    /// <summary>Filled the bend meter but didn't jam.</summary>
    Bent,
    /// <summary>This hit jammed the target.</summary>
    Jammed,
    /// <summary>This hit pinned the target to a wall (jammed it, or pinned an already-jammed one).</summary>
    Pinned,
}

/// <summary>Anything weapons can bend. Keys implement this through <see cref="Bendable"/>.</summary>
public interface IBendable
{
    BendOutcome ApplyBend(BendHit hit);
    /// <summary>Where projectiles that hit it should stay attached (follows any bend/lean animation).</summary>
    Transform AttachPoint { get; }
}

/// <summary>
/// Every hit on a bendable target, for feedback (hit marker, hit sounds, hitstop, camera punch).
/// Listeners filter by <c>hit.source</c> to react only to their own shots.
/// </summary>
public static class CombatFeedback
{
    public static event Action<BendHit, BendOutcome, IBendable> TargetHit;

    public static void Report(BendHit hit, BendOutcome outcome, IBendable target) =>
        TargetHit?.Invoke(hit, outcome, target);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => TargetHit = null;
}
