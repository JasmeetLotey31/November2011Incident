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

/// <summary>Anything weapons can bend. Keys implement this through <see cref="Bendable"/>.</summary>
public interface IBendable
{
    void ApplyBend(BendHit hit);
}
