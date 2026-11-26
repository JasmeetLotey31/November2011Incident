using UnityEngine;

/// <summary>
/// Nails the player can collect with Interact: a strip of nails from a toolbox, or a single
/// nail pulled back out of a wall. The <see cref="NailGun"/> looks for these when you press Interact.
/// </summary>
public class NailPickup : MonoBehaviour
{
    [SerializeField] int nails = 6;
    [Tooltip("Collider the player aims at to collect. On stuck nails it's a small trigger, off while flying.")]
    [SerializeField] Collider interactCollider;

    bool collectable = true;

    public int Nails => nails;
    public bool IsCollectable => collectable && nails > 0;

    public void SetCollectable(bool value)
    {
        collectable = value;
        if (interactCollider != null) interactCollider.enabled = value;
    }

    /// <summary>Gives as many nails as the gun can hold. Returns false if it took none.</summary>
    public bool TryCollect(NailGun gun)
    {
        if (!IsCollectable || gun == null) return false;
        int taken = gun.AddReserve(nails);
        if (taken <= 0) return false;

        nails -= taken;
        if (nails <= 0) Destroy(gameObject);
        return true;
    }
}
